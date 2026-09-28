#!/usr/bin/env python3
from __future__ import annotations

import base64
import hashlib
import importlib.util
import json
import pathlib
import unittest
from unittest.mock import patch


ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "v2_ci_ordinary_observe", ROOT / "tools/v2-ci-ordinary-observe.py"
)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class GameObservationSourceTests(unittest.TestCase):
    def test_tools_match_recorded_repaired_source_digests(self):
        policy = json.loads((ROOT / "policy/v2-ci-ordinary-settlement.json").read_text())
        expected = {
            "tools/v2-ci-ordinary-observe.py": policy["sourceImplementation"]["observerSha256"],
            "tools/v2-ci-ordinary-qualification.py":
                policy["sourceImplementation"]["qualificationSha256"],
        }
        for relative, digest in expected.items():
            self.assertEqual(digest, hashlib.sha256((ROOT / relative).read_bytes()).hexdigest())

    def test_game_profile_is_fixed_to_exact_checks_and_producers(self):
        policy = json.loads((ROOT / "policy/v2-ci-ordinary-settlement.json").read_text())
        profile = MODULE.QUALIFICATION.source_profile(policy, "game-v1")
        self.assertEqual("FS-GG/FS.GG.Game", profile["repository"])
        self.assertEqual(1290990429, profile["repositoryId"])
        self.assertEqual(15368, profile["requiredCheckAppId"])
        self.assertEqual({'Full test suite (dotnet test, headless) (ubuntu-latest)', 'Deterministic gate (locked restore + build) (ubuntu-latest)'},
                         set(profile["requiredChecks"]))
        self.assertEqual(set(policy["qualification"]["requiredGateChecks"]),
                         set(profile["requiredGateChecks"]))
        self.assertEqual(profile["requiredChecks"], policy["qualification"]["requiredChecks"])
        self.assertEqual(profile["checkProducers"], policy["qualification"]["checkProducers"])
        self.assertEqual(
            {
                "Surface baseline drift (readiness/surface-baselines)": 308011589,
                "Build-config drift check (shared-build-config)": 308011589,
                "Lock-range coherence (project refs track declared versions) / lock-ranges": 308011589,
                "Skill-manifest drift (template/skill-manifest)": 308011589,
                "Dangling skill refs (template/product-skills)": 308011589,
                "Skill-refs gate tests (scripts/check-skill-refs.sh)": 308011589,
                "Skill-refs sweep tests (.github/workflows/skill-refs-sweep.yml)": 308011589,
                "Test-harness selftest (scripts/lib/test-harness.sh)": 308011589,
                "Shell lint (actionlint + shellcheck over every run: block, and over the repo's own scripts)": 308011589,
                "Markdown fsharp blocks typecheck (skills + TestSpecs)": 308011589,
                "Scaffold drift (_scaffold.fs == published template geometry)": 308011589,
                "kit / coordination-kit": 308011587,
                "Deterministic gate (locked restore + build) (ubuntu-latest)": 308011589,
                "Deterministic gate (locked restore + build) (windows-latest)": 308011589,
                "Full test suite (dotnet test, headless) (ubuntu-latest)": 308011589,
                "Full test suite (dotnet test, headless) (windows-latest)": 308011589,
                "Determinism & property invariants (constraint face) (ubuntu-latest)": 308011589,
                "Determinism & property invariants (constraint face) (windows-latest)": 308011589,
                "materialize / receiver-validate": 316870217,
            },
            {name: producer["workflowId"]
             for name, producer in profile["checkProducers"].items()},
        )
        with patch.object(MODULE.QUALIFICATION, "read_json", return_value=policy):
            with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "no rehearsal activation"):
                MODULE.observe({"FSGG_V2_SOURCE_PROFILE": "game-v1"}, rehearsal=True)

    def test_game_local_policy_is_admitted_before_runtime_event_fences(self):
        policy = json.loads((ROOT / "policy/v2-ci-ordinary-settlement.json").read_text())
        env = {
            "FSGG_V2_SOURCE_PROFILE": "game-v1",
            "GITHUB_SHA": "a" * 40,
            "GITHUB_REPOSITORY": "FS-GG/FS.GG.Game",
            "GITHUB_EVENT_NAME": "pull_request",
            "GITHUB_REF": "refs/heads/main",
        }
        repository = {
            "id": 1290990429,
            "full_name": "FS-GG/FS.GG.Game",
            "default_branch": "main",
        }
        with patch.object(MODULE.QUALIFICATION, "read_json", return_value=policy), \
                patch.object(MODULE, "api", return_value=repository):
            with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal,
                                        "pinned protected-main event"):
                MODULE.observe(env)

    def test_current_authority_reads_game_policy_and_workflow_from_one_main_revision(self):
        policy = json.loads((ROOT / "policy/v2-ci-ordinary-settlement.json").read_text())
        revision = "a" * 40

        def api(path):
            if path == "repos/FS-GG/FS.GG.Game/git/ref/heads/main":
                return {"object": {"sha": revision}}
            prefix = "repos/FS-GG/FS.GG.Game/contents/"
            if path.startswith(prefix) and path.endswith("?ref=" + revision):
                relative = path[len(prefix):].split("?ref=", 1)[0]
                return {
                    "encoding": "base64",
                    "content": base64.b64encode((ROOT / relative).read_bytes()).decode(),
                }
            raise AssertionError(path)

        with patch.object(MODULE, "api", side_effect=api):
            MODULE.current_authority("FS-GG/FS.GG.Game", policy)

    def test_workflow_is_hard_disabled_and_has_no_credential_or_package_surface(self):
        workflow = (ROOT / ".github/workflows/v2-ci-ordinary-settlement.yml").read_text()
        self.assertIn("  push:\n    branches: [main]", workflow)
        self.assertIn("    if: ${{ false }}", workflow)
        self.assertIn("FSGG_V2_SOURCE_PROFILE: game-v1", workflow)
        self.assertIn("persist-credentials: false", workflow)
        self.assertIn("python3 tools/v2-ci-ordinary-observe.py produce", workflow)
        for forbidden in (
            "secrets.", "environment:", "ordinary-settlement execute", "PACKAGE_VERSION",
            "PACKAGE_SHA256", "setup-dotnet", "global.json", "workflow_dispatch:",
            "repository_dispatch:", "pull_request:", "pull_request_target:",
        ):
            self.assertNotIn(forbidden, workflow)

    def test_policy_anchor_environment_and_unresolved_package_are_bounded(self):
        policy = json.loads((ROOT / "policy/v2-ci-ordinary-settlement.json").read_text())
        anchor = json.loads((ROOT / "policy/v2-ci-ordinary-settlement-anchor.json").read_text())
        self.assertEqual("v2-ci-i1-ordinary-settlement-v1", policy["policyId"])
        self.assertEqual(policy["policyId"], anchor["policyId"])
        self.assertEqual("source-qualified-not-installed", policy["status"])
        self.assertFalse(policy["credentialJob"]["installed"])
        observation = policy["credentialJob"]["liveObservation"]
        self.assertEqual(22921368593, observation["environmentId"])
        self.assertEqual(61289298, observation["branchPolicyId"])
        self.assertEqual("main", observation["customBranchPolicy"])
        self.assertEqual(0, observation["secretCount"])
        self.assertEqual("unresolved-game-profile-release", policy["packagePin"]["status"])
        self.assertIsNone(policy["packagePin"]["version"])
        self.assertIsNone(policy["packagePin"]["sha256"])
        self.assertFalse(policy["packagePin"]["servedPackageVerified"])
        self.assertEqual([], policy["credentialInventory"])
        self.assertEqual(5064713, anchor["writer"]["appId"])
        self.assertEqual(164553252, anchor["writer"]["installationId"])
        self.assertEqual(1351660651, anchor["writer"]["repositoryId"])


if __name__ == "__main__":
    unittest.main()
