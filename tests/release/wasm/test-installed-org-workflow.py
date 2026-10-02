#!/usr/bin/env python3
"""Check the actual read-only workflow before tools or installed tests launch."""
import pathlib

root = pathlib.Path(__file__).resolve().parents[3]
text = (root / ".github/workflows/wasm-installed-org.yml").read_text()

def validate(candidate):
    assert "on:\n  workflow_dispatch:\n" in candidate and "  push:" not in candidate
    assert "packages: read" in candidate and candidate.count("    runs-on:") == 1
    assert all(value not in candidate for value in ("packages: write", "contents: write", "id-token:", "dotnet pack", "nuget push", "gh release", "git tag"))
    assert "PUBLISHED_SOURCE: ea015cbf884b01754bc6615476241450907b6b24" in candidate
    assert "WASM_RELEASE_BASE_URL: https://github.com/FS-GG/FS.GG.Game/releases/download/wasm/v0.1.1" in candidate
    assert "WASM_PACKAGE_FEED: https://nuget.pkg.github.com/FS-GG/index.json" in candidate
    assert "975166b2cf53b17c2e5f55b1e020266f4cba78c9d2257d27a751f25deb58c2b5" in candidate
    assert "923173219374de6c2f5adc62c930042e18abc17125b0e32ba8d66c57266430d2" in candidate
    assert 'git diff --exit-code "$PUBLISHED_SOURCE" HEAD -- tests/Wasm.PackageConsumer sdk/wasm scripts/wasm-release/release_manifest.py .config/dotnet-tools.json global.json' in candidate
    assert "NuGetPackageSourceCredentials_wasm: Username=token;Password=${{ secrets.GITHUB_TOKEN }};ValidAuthenticationTypes=Basic" in candidate
    assert "scripts/verify-wasm-package-consumer.sh --feed-only" in candidate
    steps = ["Static installed qualification preflight", "Bind qualification source and immutable published receipts", "Install exact consumer tools", "Qualify fresh org feed installed consumer"]
    positions = [candidate.index(value) for value in steps]
    assert positions == sorted(positions)
    assert "fetch-depth: 0" in candidate and "ref:" not in candidate

validate(text)
for broken in (
    text.replace("packages: read", "packages: write"),
    text.replace("--feed-only", "--custody artifacts/wasm-release"),
    text.replace("PUBLISHED_SOURCE: ea015cbf884b01754bc6615476241450907b6b24", "PUBLISHED_SOURCE: main"),
    text.replace("975166b2cf53b17c2e5f55b1e020266f4cba78c9d2257d27a751f25deb58c2b5", "0" * 64),
    text.replace("          fetch-depth: 0", "          fetch-depth: 0\n          ref: wasm/v0.1.1"),
):
    try:
        validate(broken)
    except (AssertionError, ValueError):
        pass
    else:
        raise SystemExit("known-bad installed qualification route accepted")
print("wasm-installed-org-preflight: manual=pass read-only=pass published-receipts=bound qualifier-source=current")
