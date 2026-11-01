#!/usr/bin/env python3
"""Bind a read-only installed qualification to an immutable published release."""
import hashlib
import sys
sys.dont_write_bytecode = True
import importlib.util
import json
import os
import pathlib
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[2]
REPO = "FS-GG/FS.GG.Game"
# The route is derived from observed state, never re-pinned per release: the selected
# version is eng/wasm-shared/version.props, the publication identity is the immutable
# tag, the native release assets and the published manifest (verify-identity), and the
# published tree is read from Git. Only a release that version.props currently selects
# can be qualified; a historical release is refused (its evidence stays verifiable at
# the revision and run that produced it).
#
# Installed-consumer input closure. Everything the read-only installed run restores,
# builds or executes as a *consumer* of the published artifacts must be byte-identical
# between the published source P and the accepted qualifier Q. Qualifier tooling
# (binder, evidence collector, workflows, toolchain scripts, docs) may change between P
# and Q and is recorded in the audit; unrelated product development is not a consumer
# input and does not require a binder change.
CONSUMER_INPUTS = (
    'eng/wasm-shared/',
    'src/Wasm.Contracts/',
    'src/Wasm.Browser/',
    'sdk/wasm/',
    'tests/Wasm.PackageConsumer/',
    'tests/Wasm.Supervisor.Compatibility/',
)


def tracked_version():
    return ET.parse(ROOT / "eng/wasm-shared/version.props").findtext(".//WasmSharedVersion")


def release_manifest_module():
    spec = importlib.util.spec_from_file_location("release_manifest", ROOT / "scripts/wasm-release/release_manifest.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def checked_inputs(environment, tracked_version):
    values = {key: environment[key] for key in ("RELEASE_VERSION", "PUBLISHED_SOURCE", "QUALIFIER_SOURCE", "MANIFEST_SHA256", "SDK_SHA256")}
    assert re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", values["RELEASE_VERSION"]), "invalid stable version"
    assert values["RELEASE_VERSION"] == tracked_version, f"historical release {values['RELEASE_VERSION']} is not the current route (version.props selects {tracked_version})"
    for key in ("PUBLISHED_SOURCE", "QUALIFIER_SOURCE"):
        assert re.fullmatch(r"[0-9a-f]{40}", values[key]), "source must be an immutable commit"
    for key in ("MANIFEST_SHA256", "SDK_SHA256"):
        assert re.fullmatch(r"[0-9a-f]{64}", values[key]), "receipt must be an exact SHA-256"
    for key, length in (("ACCEPTED_EXECUTOR",40),("PROMOTION_BINDING_SHA256",64)):
        values[key] = environment[key]
        assert re.fullmatch(r"[0-9a-f]{%d}" % length, values[key]), "invalid explicit executor/transaction binding"
    assert values["QUALIFIER_SOURCE"] == values["ACCEPTED_EXECUTOR"], "selected current qualifier required"
    assert not environment.get("EXECUTION_BINDING") and not environment.get("CANONICAL_QUALIFICATION_AUDIT"), "historical overlays cannot authorize current qualification"
    return values


def validate_release(release, values):
    version = values["RELEASE_VERSION"]
    assert version == tracked_version(), "only the release version.props selects can qualify"
    tag = f"wasm/v{version}"
    assert release["tag_name"] == tag and not release["draft"] and not release["prerelease"]
    assert release["published_at"], "release is unpublished"
    assets = {item["name"]: item for item in release["assets"]}
    assert len(assets) == len(release["assets"]), "duplicate release assets"
    selected = {}
    for name, expected in (("release-manifest.json", values["MANIFEST_SHA256"]), (f"fsgg-wasm-sdk-{version}.tar.gz", values["SDK_SHA256"])):
        item = assets[name]
        assert item["browser_download_url"] == f"https://github.com/{REPO}/releases/download/{tag}/{name}", "asset identity mismatch"
        assert item.get("digest") == "sha256:" + expected, "native release asset digest mismatch"
        selected[name] = item["browser_download_url"]
    sha = values["PROMOTION_BINDING_SHA256"]
    assert re.fullmatch(r"[0-9a-f]{64}", sha), "invalid promotion record hash"
    row = assets["promotion-binding.json"]
    assert row.get("digest") == "sha256:" + sha, "native transaction digest mismatch"
    assert row["browser_download_url"] == f"https://github.com/{REPO}/releases/download/{tag}/promotion-binding.json"
    selected["promotion-binding.json"] = row["browser_download_url"]
    return selected


def git(*args):
    return subprocess.check_output(["git", "-C", str(ROOT), *args], text=True, timeout=30).strip()


def bind_published_identity(manifest, source, tracked, source_tree):
    """Authenticate the published manifest against the observed release state.

    The manifest is the authority for the published identity: it must pass
    release_manifest verify-identity against the tag-resolved source, be the release
    that version.props currently selects, and name the tree Git resolves for that source.
    """
    module = release_manifest_module()
    try:
        module.verify_identity(manifest, source)
    except SystemExit as error:
        raise AssertionError(f"manifest identity refused: {error}") from None
    assert manifest["version"] == tracked, f"manifest version {manifest['version']} differs from version.props {tracked}"
    assert manifest["source"] == source, "manifest source mismatch"
    assert re.fullmatch(r"[0-9a-f]{40}", str(manifest["tree"])) and manifest["tree"] == source_tree, "manifest tree differs from the published source tree"
    return source_tree


def source_audit(source, qualifier):
    assert re.fullmatch(r'[0-9a-f]{40}', source), 'wrong immutable publication subject'
    try:
        tree = git('rev-parse', '--verify', source+'^{tree}')
    except subprocess.CalledProcessError:
        raise AssertionError('published source is not a commit of this repository') from None
    assert subprocess.run(['git','-C',str(ROOT),'merge-base','--is-ancestor',source,qualifier], timeout=30).returncode == 0, 'qualifier does not descend from the published source'
    raw = subprocess.check_output(['git','-C',str(ROOT),'diff','--raw','--no-abbrev','--no-renames','-z',source,qualifier], timeout=60)
    chunks = raw.decode().split('\0'); rows=[]
    def fingerprint(mode, blob):
        if set(blob) == {'0'}: return None
        body = subprocess.check_output(['git','-C',str(ROOT),'cat-file','blob',blob], timeout=30) if mode != '160000' else b''
        return {'blob':blob,'mode':mode,'sha256':hashlib.sha256(body).hexdigest(),'bytes':len(body)}
    for i in range(0,len(chunks)-1,2):
        mode_old,mode_new,old_blob,new_blob,status=chunks[i].split()
        path=chunks[i+1]
        assert not path.startswith(CONSUMER_INPUTS), 'installed consumer input differs from the published source: '+path
        rows.append({'path':path,'status':status,'old':fingerprint(mode_old[1:],old_blob),'new':fingerprint(mode_new,new_blob)})
    return {'schema':'fsgg.wasm.installed-inputs/v1','publishedSource':source,'publishedTree':tree,
            'qualifierSource':qualifier,'qualifierTree':git('rev-parse',qualifier+'^{tree}'),'consumerInputs':list(CONSUMER_INPUTS),'changes':rows,
            'consumerInputsNote':'every path under consumerInputs is byte-identical to P; other changed paths are qualifier tooling or unrelated development, recorded here; published packages/SDK remain P inputs, evidence collector/binder are selected Q inputs'}


def bind_executor(source, executor, version):
    assert version == tracked_version() and executor == os.environ['ACCEPTED_EXECUTOR'] == os.environ['QUALIFIER_SOURCE'] == os.environ['GITHUB_SHA'] == os.environ['GITHUB_WORKFLOW_SHA'] == git('rev-parse','HEAD'), 'wrong actual installed qualifier/executor'
    assert os.environ['GITHUB_REPOSITORY'] == REPO and os.environ['GITHUB_REF'] == 'refs/heads/main' and os.environ['GITHUB_EVENT_NAME'] == 'workflow_dispatch' and os.environ['GITHUB_RUN_ATTEMPT'] == '1', 'foreign/rerun installed execution'
    assert os.environ['GITHUB_WORKFLOW_REF'] == REPO+'/.github/workflows/wasm-installed-org.yml@refs/heads/main', 'foreign installed workflow'
    assert os.environ['GITHUB_JOB'] == 'installed' and re.fullmatch('[1-9][0-9]*',os.environ['GITHUB_RUN_ID']), 'wrong installed native run/job'
    assert os.environ['WASM_PACKAGE_FEED'] in ('https://api.nuget.org/v3/index.json','https://nuget.pkg.github.com/FS-GG/index.json'), 'foreign installed feed'
    assert not git('status','--porcelain'), 'dirty installed qualifier'
    audit=source_audit(source, executor)
    audit['execution']={'run':int(os.environ['GITHUB_RUN_ID']),'attempt':1,'job':'installed','workflowRef':os.environ['GITHUB_WORKFLOW_REF'],'feed':os.environ['WASM_PACKAGE_FEED']}
    return audit


def bind_qualification_inputs(source, qualifier_source, version, release_binding=None):
    assert not os.environ.get('CANONICAL_QUALIFICATION_AUDIT') and not os.environ.get('EXECUTION_BINDING'), 'historical audit/overlay cannot authorize current qualification'
    return bind_executor(source,qualifier_source,version)


def bind_promotion_execution(promotion, transaction, values, release, receipts, published_tree):
    subject=promotion.PublicationSubject(values['PUBLISHED_SOURCE'], published_tree, values['RELEASE_VERSION'])
    reviewed=promotion.validate_tuple(transaction['preparation'],subject=subject)
    assert promotion.execution_input() is None
    assert reviewed['binding']['producer'] == reviewed['binding']['executor'] == values['PUBLISHED_SOURCE']
    native=promotion.Native()
    assert native.checked(f'repos/{REPO}/git/ref/heads/main')['object']['sha'] == values['QUALIFIER_SOURCE'], 'protected qualifier moved'
    # Authenticate the original successful qualification, artifact and admission
    # against P, not against this read-only installed executor Q.
    run_id=reviewed['binding']['runId']; attempt=reviewed['binding']['runAttempt']
    run=native.checked(f'repos/{REPO}/actions/runs/{run_id}/attempts/{attempt}')
    jobs=native.pages(f'repos/{REPO}/actions/runs/{run_id}/attempts/{attempt}/jobs','jobs')
    artifact=native.checked(f'repos/{REPO}/actions/artifacts/{reviewed["artifactId"]}')
    promotion.validate_run(reviewed,run,jobs,artifact)
    (receipts / "published-native.json").write_text(json.dumps({"release":release,"qualificationRun":run,"qualificationJobs":jobs,"qualificationArtifact":artifact},sort_keys=True,indent=2)+"\n")
    _,actual=promotion.release_state(native,subject=subject)
    assert actual==release, 'native tag/list/by-ID published release disagreement'
    original=promotion.verify_transaction(native,release,reviewed,subject=subject)
    published=native.checked(f'repos/{REPO}/actions/runs/{original["firstPromotionRun"]}/attempts/{original["firstPromotionAttempt"]}')
    assert published['status']=='completed' and published['conclusion']=='success' and published['run_attempt']==1, 'original publication incomplete/failed/rerun'
    publication_jobs=native.pages(f'repos/{REPO}/actions/runs/{original["firstPromotionRun"]}/attempts/{original["firstPromotionAttempt"]}/jobs','jobs')
    for name in ('preflight','admission','begin','org','public','assets','complete'):
        matches=[row for row in publication_jobs if row['name']==name]
        assert len(matches)==1 and matches[0]['conclusion']=='success', 'original publication stage absent/failed'
    (receipts / 'published-native.json').write_text(json.dumps({'release':release,'qualificationRun':run,'qualificationJobs':jobs,'qualificationArtifact':artifact,'publicationRun':published,'publicationJobs':publication_jobs},sort_keys=True,indent=2)+'\n')
    return reviewed


def main():
    tracked = tracked_version()
    values = checked_inputs(os.environ, tracked)
    assert not git("status", "--porcelain"), "installed qualifier must be a clean accepted executor"
    assert git("rev-parse", "HEAD") == values["QUALIFIER_SOURCE"] == os.environ["GITHUB_SHA"], "wrong qualifier checkout"
    source = values["PUBLISHED_SOURCE"]
    tag = "wasm/v" + values["RELEASE_VERSION"]
    assert git("rev-parse", f"refs/tags/{tag}^{{commit}}") == source, "tag does not name published source"
    subprocess.run(["git", "-C", str(ROOT), "merge-base", "--is-ancestor", source, "origin/main"], check=True)
    # Read-only qualification authenticates immutable published P separately from Q.
    spec=importlib.util.spec_from_file_location('promotion',ROOT/'scripts/wasm-release/promotion.py')
    promotion=importlib.util.module_from_spec(spec);spec.loader.exec_module(promotion)
    release=promotion.Native().checked(f"repos/{REPO}/releases/tags/wasm%2Fv{values['RELEASE_VERSION']}")
    selected = validate_release(release, values)
    receipts = pathlib.Path(os.environ["RUNNER_TEMP"]) / "wasm-installed-receipts"
    receipts.mkdir()
    for name, url in selected.items():
        destination = receipts / name
        subprocess.run(["curl", "--max-time", "30", "--max-filesize", "536870912", "--fail", "--location", "--proto", "=https", "--tlsv1.2", url, "--output", str(destination)], check=True)
        expected = values["MANIFEST_SHA256"] if name == "release-manifest.json" else values["SDK_SHA256"] if name.endswith(".tar.gz") else values["PROMOTION_BINDING_SHA256"]
        assert hashlib.sha256(destination.read_bytes()).hexdigest() == expected, "downloaded release bytes mismatch"
    manifest = json.loads((receipts / "release-manifest.json").read_text())
    published_tree = bind_published_identity(manifest, source, tracked, git("rev-parse", f"{source}^{{tree}}"))
    assert manifest["version"] == values["RELEASE_VERSION"]
    sdk = next(row for row in manifest["artifacts"] if row["file"] == f"fsgg-wasm-sdk-{values['RELEASE_VERSION']}.tar.gz")
    assert sdk["sha256"] == values["SDK_SHA256"]
    release_binding = {"manifestSha256": values["MANIFEST_SHA256"], "sdkSha256": values["SDK_SHA256"],
                       "packages": [{"name": row["file"], "sha256": row["sha256"]} for row in manifest["artifacts"] if row["file"].endswith(".nupkg")]}
    spec = importlib.util.spec_from_file_location("promotion", ROOT / "scripts/wasm-release/promotion.py")
    promotion = importlib.util.module_from_spec(spec); spec.loader.exec_module(promotion)
    transaction = json.loads((receipts / "promotion-binding.json").read_text())
    reviewed = promotion.validate_tuple(transaction["preparation"], subject=promotion.PublicationSubject(source, published_tree, values["RELEASE_VERSION"]))
    bound = reviewed["binding"]
    assert transaction["schema"] == "fsgg.wasm.promotion/v1" and transaction["producer"] == source and transaction["tag"] == tag
    bind_promotion_execution(promotion,transaction,values,release,receipts,published_tree)
    assert bound["manifestSha256"] == values["MANIFEST_SHA256"]
    assert bound["archives"][sdk["file"]] == values["SDK_SHA256"]
    assert {row["file"]:row["sha256"] for row in manifest["artifacts"]} == bound["archives"]
    input_audit = bind_qualification_inputs(source, values["QUALIFIER_SOURCE"], values["RELEASE_VERSION"], release_binding)
    (receipts / "installed-input-audit.json").write_text(json.dumps(input_audit, sort_keys=True, indent=2)+"\n")
    print("wasm-installed-input-audit: " + json.dumps(input_audit, sort_keys=True))
    print(f"wasm-installed-binding: qualifier={values['QUALIFIER_SOURCE']} published={source} tag={tag} native-assets=verified manifest={values['MANIFEST_SHA256']} sdk={values['SDK_SHA256']}")


if __name__ == "__main__":
    main()
