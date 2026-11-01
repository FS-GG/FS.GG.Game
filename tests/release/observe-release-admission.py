#!/usr/bin/env python3
"""Bounded GET observations; no package or policy writes, no credential readbacks."""
import argparse
import base64
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import urllib.error
import urllib.request
from urllib.parse import urlsplit
import xml.etree.ElementTree as ET

PACKAGES = ("FS.GG.Game.Core", "FS.GG.Game.Render", "FS.GG.Game.Harness", "FS.GG.Game.Physics.Box2D", "FS.GG.Game.Render.Browser")
NEW_PACKAGES = {"FS.GG.Game.Render.Browser"}
ROOT = Path(__file__).resolve().parents[2]
UNVERIFIABLE_STATUS = 400
UNVERIFIABLE_WARNING = ("NuGet verify-key answered HTTP 400, so package authority is unverifiable through this route, not "
                        "refused; see FS-GG Atlas github/nuget-trusted-publishing-policy-scope. release.yml pushes the new "
                        "identity first and alone on each feed, so an actual refusal publishes nothing more there.")


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, newurl):
        redirected = super().redirect_request(request, response, code, message, headers, newurl)
        if redirected is not None and urlsplit(request.full_url).netloc != urlsplit(newurl).netloc:
            redirected.remove_header("Authorization")
            redirected.remove_header("X-nuget-apikey")
        return redirected


def observe(url, headers=None):
    request = urllib.request.Request(url, headers={"User-Agent": "FS-GG-Game-release-admission/1", **(headers or {})}, method="GET")
    try:
        with urllib.request.build_opener(SafeRedirect()).open(request, timeout=15) as response:
            # Discard response bodies. Credential-bearing endpoints never become artifacts.
            return response.status
    except urllib.error.HTTPError as error:
        return error.code
    except (urllib.error.URLError, TimeoutError):
        return 0


def collect(mode, version):
    rows = []
    if mode == "registries":
        token = os.environ["FEED_TOKEN"]
        authorization = "Basic " + base64.b64encode(("fs-gg:" + token).encode()).decode()
        headers = {"Authorization": authorization}
        sentinel = observe("https://nuget.pkg.github.com/FS-GG/download/fs.gg.game.core/0.17.0/fs.gg.game.core.0.17.0.nupkg", headers)
        failed = sentinel != 200
        for package in PACKAGES:
            lower = package.lower()
            for feed, prefix, auth in (
                ("github", "https://nuget.pkg.github.com/FS-GG/download", headers),
                ("nuget", "https://api.nuget.org/v3-flatcontainer", None),
            ):
                status = observe(f"{prefix}/{lower}/{version}/{lower}.{version}.nupkg", auth)
                rows.append({"package": package, "feed": feed, "httpStatus": status,
                             "observation": "absent-to-this-principal" if status == 404 else "collision" if status == 200 else "unknown"})
                failed |= status != 404
        return {"githubPredecessorReadHttpStatus": sentinel, "packages": rows,
                "limits": "GET access does not prove new-package creation authority or visibility of unrelated private packages."}, failed
    key = os.environ["NUGET_API_KEY"]
    headers = {"X-NuGet-ApiKey": key}
    failed = False
    unverifiable = []
    for package in PACKAGES:
        # No version asks for the existing registration; a missing identity is 404 before scope evaluation.
        status = observe(f"https://www.nuget.org/api/v2/verifykey/{package}", headers)
        existing = package not in NEW_PACKAGES
        if status == UNVERIFIABLE_STATUS:
            # NuGet answers verify-key with HTTP 400 for every id under the current trusted-publishing temporary key
            # (FS-GG Atlas github/nuget-trusted-publishing-policy-scope; Game 0.18.0 admission run 41323090013).
            # It says nothing either way about push authority, so it is recorded, warned about and not a refusal.
            # Every other unexpected answer still refuses: 401/403/404 for an existing id, a non-404 for a new one.
            authority = "unverifiable"
            unverifiable.append(package)
        elif existing:
            authority = "existing-package-push" if status == 200 else "refused"
            failed |= status != 200
        else:
            authority = "unproven" if status == 404 else "refused"
            failed |= status != 404
        rows.append({"package": package, "httpStatus": status, "authority": authority})
    report = {"oidcExchange": "completed", "packages": rows,
              "newAdapterCreationScope": "not-exposed-and-unproven",
              "limits": "OIDC exchange returns no scopes. Verify-key 200 covers existing identity; missing identity 404 does not evaluate creation scope; HTTP 400 is unverifiable. This is not full publication authorization."}
    if unverifiable:
        report["unverifiable"] = unverifiable
        report["warning"] = UNVERIFIABLE_WARNING
    return report, failed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument("--registries", type=Path)
    modes.add_argument("--authority", type=Path)
    args = parser.parse_args()
    version = ET.parse(ROOT / "Directory.Build.local.props").findtext(".//Version")
    if version != "0.18.0":
        raise SystemExit("This observation is scoped to the coherent 0.18.0 release.")
    mode = "registries" if args.registries else "authority"
    output = args.registries or args.authority
    report, failed = collect(mode, version)
    if report.get("warning"):
        print(f"::warning title=verify-key unverifiable::{report['warning']} Unverifiable ids: {', '.join(report['unverifiable'])}.")
    report.update(version=version, observedUtc=datetime.now(timezone.utc).isoformat(),
                  sourceCommit=os.environ.get("GITHUB_SHA"), runId=os.environ.get("GITHUB_RUN_ID"))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n")
    print(f"{mode}: {'unexpected or unavailable observation' if failed else 'expected limited observations'}; see sanitized artifact")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
