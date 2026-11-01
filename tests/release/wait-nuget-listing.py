#!/usr/bin/env python3
"""Wait until nuget.org LISTS every released package version, before a public consumer restores (FS.GG.Game#764).

Readback fetches each .nupkg from the flat container, and that blob can be served minutes before the flat
container's version list (`<id>/index.json`) names the version. NuGet restore resolves versions from that list, so
a public consumer that restores in the gap fails with NU1102 although the package is published (0.18.0, release run
41324010304). This polls the same list restore reads, for every package id in the release, until each names the
version, within a bound. It reads the exact URL a restore client requests (no cache-busting), because that response is
what the consumer that follows will see.

usage: wait-nuget-listing.py --version <v> [--attempts N] [--interval S] (--packages <dir> | <id>...)
exit:  0 every id lists the version; 4 the bound elapsed (each unlisted id is named); 2 usage.
"""
import argparse
import json
from pathlib import Path
import sys
import time
import urllib.error
import urllib.request

FLAT_CONTAINER = "https://api.nuget.org/v3-flatcontainer"


def fetch_versions(package_id):
    """The versions nuget.org's flat container lists for `package_id`; None while unreadable or absent."""
    url = f"{FLAT_CONTAINER}/{package_id.lower()}/index.json"
    request = urllib.request.Request(url, headers={"User-Agent": "FS-GG-Game-release-listing/1"})
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            document = json.load(response)
    except (urllib.error.URLError, TimeoutError, ValueError):
        return None
    versions = document.get("versions") if isinstance(document, dict) else None
    return versions if isinstance(versions, list) else None


def wait_for_listing(ids, version, fetch=None, sleep=None, attempts=60, interval=15.0, log=print):
    """Poll until every id lists `version` (flat-container versions are lower-case); return the ids still unlisted."""
    fetch = fetch or fetch_versions
    sleep = sleep or time.sleep
    wanted = version.lower()
    pending = list(dict.fromkeys(ids))
    for attempt in range(1, attempts + 1):
        still = []
        for package_id in pending:
            versions = fetch(package_id)
            if versions is None or wanted not in {str(v).lower() for v in versions}:
                still.append(package_id)
        pending = still
        if not pending:
            log(f"nuget.org lists {version} for every released id (attempt {attempt}/{attempts})")
            return []
        if attempt < attempts:
            log(f"Waiting for nuget.org to list {version} for {', '.join(pending)} ({attempt}/{attempts})")
            sleep(interval)
    return pending


def package_ids(directory, version):
    suffix = f".{version}.nupkg"
    ids = sorted(path.name[: -len(suffix)] for path in Path(directory).glob(f"*{suffix}")
                 if not path.name.endswith(".symbols.nupkg"))
    if not ids:
        raise SystemExit(f"no {suffix} packages in {directory}")
    return ids


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--version", required=True)
    parser.add_argument("--attempts", type=int, default=60)
    parser.add_argument("--interval", type=float, default=15.0)
    parser.add_argument("--packages", type=Path)
    parser.add_argument("ids", nargs="*")
    args = parser.parse_args(argv)
    if bool(args.packages) == bool(args.ids) or args.attempts < 1 or args.interval < 0:
        parser.error("give --packages <dir> or package ids (not both), with --attempts >= 1 and --interval >= 0")
    ids = package_ids(args.packages, args.version) if args.packages else args.ids
    unlisted = wait_for_listing(ids, args.version, attempts=args.attempts, interval=args.interval)
    if unlisted:
        print(f"::error::nuget.org did not list {args.version} for {', '.join(unlisted)} within "
              f"{args.attempts} attempts {args.interval:g}s apart; the public consumer would fail with NU1102",
              file=sys.stderr)
        return 4
    return 0


if __name__ == "__main__":
    sys.exit(main())
