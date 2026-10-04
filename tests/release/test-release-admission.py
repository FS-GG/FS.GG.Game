#!/usr/bin/env python3
"""Offline checks of admission boundaries and meaningful provider response controls."""
import importlib.util
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
import urllib.request

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("admission", Path(__file__).with_name("observe-release-admission.py"))
admission = importlib.util.module_from_spec(spec)
spec.loader.exec_module(admission)


class AdmissionTests(unittest.TestCase):
    def test_expected_registry_absence_and_authenticated_predecessor(self):
        with patch.dict(admission.os.environ, FEED_TOKEN="offline-placeholder"), patch.object(admission, "observe", side_effect=[200] + [404] * 8):
            report, failed = admission.collect("registries", "0.17.0")
        self.assertFalse(failed)
        self.assertEqual(len(report["packages"]), 8)

    def test_registry_collision_unauthorized_and_unavailable_refuse(self):
        for status in (200, 401, 403, 0):
            with self.subTest(status=status), patch.dict(admission.os.environ, FEED_TOKEN="offline-placeholder"), patch.object(admission, "observe", side_effect=[200, status] + [404] * 7):
                self.assertTrue(admission.collect("registries", "0.17.0")[1])

    def test_predecessor_404_does_not_prove_registry_authentication(self):
        with patch.dict(admission.os.environ, FEED_TOKEN="offline-placeholder"), patch.object(admission, "observe", return_value=404):
            self.assertTrue(admission.collect("registries", "0.17.0")[1])

    def test_oidc_success_cannot_prove_adapter_creation_scope(self):
        with patch.dict(admission.os.environ, NUGET_API_KEY="offline-placeholder"), patch.object(admission, "observe", side_effect=[200, 200, 200, 404]):
            report, failed = admission.collect("authority", "0.17.0")
        self.assertFalse(failed)
        self.assertEqual(report["newAdapterCreationScope"], "not-exposed-and-unproven")
        self.assertEqual(report["packages"][-1]["authority"], "unproven")

    def test_foreign_redirect_does_not_forward_credentials(self):
        request = urllib.request.Request("https://www.nuget.org/api/v2/verifykey/example", headers={"X-NuGet-ApiKey": "offline-placeholder", "Authorization": "offline-placeholder"})
        redirected = admission.SafeRedirect().redirect_request(request, None, 302, "Found", {}, "https://other.example/result")
        self.assertNotIn("Authorization", redirected.headers)
        self.assertNotIn("X-nuget-apikey", redirected.headers)

    def test_existing_package_authority_failure_refuses(self):
        with patch.dict(admission.os.environ, NUGET_API_KEY="offline-placeholder"), patch.object(admission, "observe", side_effect=[403, 200, 200, 404]):
            self.assertTrue(admission.collect("authority", "0.17.0")[1])

    def test_workflow_observation_cannot_enter_publisher(self):
        text = (ROOT / ".github/workflows/release.yml").read_text()
        excluded = "&& (github.event_name != 'workflow_dispatch' || inputs.admission_only != true)"
        self.assertEqual(text.count(excluded), 2)
        job = text.split("\n  admission:\n", 1)[1]
        self.assertIn("github.event_name == 'workflow_dispatch' && inputs.admission_only == true", job)
        for effect in ("dotnet", "nuget push", "source_run_id }}'", "GenerateTrustedPublisherPolicy"):
            self.assertNotIn(effect, job)
        self.assertIn('test -z "$SOURCE_RUN_ID"', job)
        self.assertNotIn("ref:", job)


if __name__ == "__main__":
    unittest.main()
