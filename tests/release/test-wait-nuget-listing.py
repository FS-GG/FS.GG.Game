#!/usr/bin/env python3
"""Offline checks of the nuget.org listing wait (FS.GG.Game#764). No provider is contacted."""
import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True

spec = importlib.util.spec_from_file_location("listing", Path(__file__).with_name("wait-nuget-listing.py"))
listing = importlib.util.module_from_spec(spec)
spec.loader.exec_module(listing)

IDS = ["FS.GG.Game.Core", "FS.GG.Game.Render", "FS.GG.Game.Render.Browser"]


class Feed:
    """A flat container whose index for each id gains the version after `lag` reads of that id."""

    def __init__(self, lag, version="0.19.0", never=()):
        self.lag, self.version, self.never, self.reads = lag, version, set(never), {}

    def __call__(self, package_id):
        self.reads[package_id] = self.reads.get(package_id, 0) + 1
        listed = ["0.17.0", "0.18.0"]
        if package_id not in self.never and self.reads[package_id] > self.lag.get(package_id, 0):
            listed.append(self.version)
        return listed


class ListingWaitTests(unittest.TestCase):
    def wait(self, feed, attempts=10, version="0.19.0"):
        sleeps = []
        unlisted = listing.wait_for_listing(IDS, version, fetch=feed, sleep=sleeps.append, attempts=attempts,
                                            interval=15, log=lambda _: None)
        return unlisted, sleeps

    def test_listing_lag_is_waited_out_per_id(self):
        # The 0.18.0 shape: blobs served, the list lags, one id longer than the others.
        feed = Feed({"FS.GG.Game.Core": 2, "FS.GG.Game.Render.Browser": 4})
        unlisted, sleeps = self.wait(feed)
        self.assertEqual(unlisted, [])
        self.assertEqual(sleeps, [15] * 4)
        self.assertEqual(feed.reads["FS.GG.Game.Render"], 1, "a listed id is not read again")

    def test_already_listed_needs_no_wait(self):
        unlisted, sleeps = self.wait(Feed({}))
        self.assertEqual((unlisted, sleeps), ([], []))

    def test_bound_elapses_and_names_each_unlisted_id(self):
        unlisted, sleeps = self.wait(Feed({}, never={"FS.GG.Game.Render.Browser"}), attempts=5)
        self.assertEqual(unlisted, ["FS.GG.Game.Render.Browser"])
        self.assertEqual(len(sleeps), 4, "no sleep after the last attempt")

    def test_unreadable_index_counts_as_unlisted_not_listed(self):
        reads = iter([None, None, ["0.19.0"]])
        unlisted = listing.wait_for_listing(["FS.GG.Game.Core"], "0.19.0", fetch=lambda _: next(reads),
                                               sleep=lambda _: None, attempts=3, log=lambda _: None)
        self.assertEqual(unlisted, [])

    def test_another_version_does_not_satisfy_the_wait(self):
        unlisted, _ = self.wait(Feed({}, version="0.19.1"), attempts=2, version="0.19.0")
        self.assertEqual(unlisted, IDS)

    def test_version_compare_is_case_insensitive_like_the_flat_container(self):
        unlisted, _ = self.wait(Feed({}, version="1.0.0-rc.1"), attempts=1, version="1.0.0-RC.1")
        self.assertEqual(unlisted, [])

    def test_cli_reads_ids_from_the_release_packages_and_fails_on_the_bound(self):
        with tempfile.TemporaryDirectory() as directory:
            for name in ("FS.GG.Game.Core.0.19.0.nupkg", "FS.GG.Game.Render.Browser.0.19.0.nupkg",
                         "FS.GG.Game.Core.0.19.0.symbols.nupkg", "FS.GG.Game.Core.0.18.0.nupkg"):
                Path(directory, name).touch()
            self.assertEqual(listing.package_ids(directory, "0.19.0"), ["FS.GG.Game.Core", "FS.GG.Game.Render.Browser"])
            with patch.object(listing, "fetch_versions", return_value=["0.18.0"]), \
                 patch.object(listing.time, "sleep", lambda _: None):
                code = listing.main(["--version", "0.19.0", "--packages", directory, "--attempts", "2", "--interval", "0"])
            self.assertEqual(code, 4)
            with patch.object(listing, "fetch_versions", return_value=["0.19.0"]):
                code = listing.main(["--version", "0.19.0", "--packages", directory, "--attempts", "1"])
            self.assertEqual(code, 0)

    def test_restore_url_is_the_uncached_flat_container_index(self):
        seen = []

        class Response:
            def __enter__(self): return self
            def __exit__(self, *_): return False
            def read(self, *_): return b'{"versions":["0.19.0"]}'

        with patch.object(listing.urllib.request, "urlopen", lambda request, timeout: seen.append(request.full_url) or Response()):
            self.assertEqual(listing.fetch_versions("FS.GG.Game.Render.Browser"), ["0.19.0"])
        self.assertEqual(seen, ["https://api.nuget.org/v3-flatcontainer/fs.gg.game.render.browser/index.json"])


if __name__ == "__main__":
    unittest.main()
