"""Focused private-routing checks; integration requires the labelled isolated Docker fixture."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).parents[1] / "scripts/server/migrate-private-routing.py"
spec = importlib.util.spec_from_file_location("private_routing", SCRIPT)
routing = importlib.util.module_from_spec(spec)
spec.loader.exec_module(routing)


def fixture():
    return {
        "Id": "container-original",
        "Image": "sha256:running-image",
        "Config": {"Image": "mutable:tag", "Env": ["KEEP=synthetic", "TREBA_API_URL=http://old"],
                   "Labels": {"existing": "keep"}, "Cmd": ["node", "app.js"]},
        "HostConfig": {"PortBindings": {"3000/tcp": [{"HostIp": "", "HostPort": "80"}]},
                       "PublishAllPorts": True, "Binds": ["uploads:/app/uploads"],
                       "RestartPolicy": {"Name": "unless-stopped"}},
    }


class BoundaryTests(unittest.TestCase):
    def test_recreation_keeps_exact_image_secrets_mounts_and_restart_policy(self):
        original = fixture()
        before = copy.deepcopy(original)
        body = routing.payload(original, "treba-frontend", ("treba-edge", "treba-app"))
        self.assertEqual(original, before)
        self.assertEqual(body["Image"], original["Image"])
        self.assertEqual(body["HostConfig"]["Binds"], original["HostConfig"]["Binds"])
        self.assertEqual(body["HostConfig"]["RestartPolicy"], original["HostConfig"]["RestartPolicy"])
        self.assertIn("KEEP=synthetic", body["Env"])
        self.assertEqual([v for v in body["Env"] if v.startswith("TREBA_API_URL=")],
                         ["TREBA_API_URL=http://treba-backend:5000"])
        self.assertFalse(body["HostConfig"]["PortBindings"])
        self.assertFalse(body["HostConfig"]["PublishAllPorts"])

    def test_backend_and_frontend_never_republish_inherited_ports(self):
        for name in ("treba-backend", "treba-frontend"):
            body = routing.payload(fixture(), name, ("treba-app",))
            self.assertFalse(body["HostConfig"]["PortBindings"])
            self.assertFalse(body["HostConfig"]["PublishAllPorts"])

    def test_unknown_or_duplicate_upstreams_fail_before_editing(self):
        for source in ("reverse_proxy unexpected:80",
                       "host.docker.internal:5000 host.docker.internal:80 host.docker.internal:80"):
            with self.assertRaises(RuntimeError):
                routing.routed_source(source)

    def test_existing_http_site_requires_review(self):
        source = "http://treba.duckdns.org { respond 200 }\nhost.docker.internal:5000 host.docker.internal:80"
        with self.assertRaises(RuntimeError):
            routing.routed_source(source)

    def test_conversion_is_idempotent_and_keeps_headers_admin_routes_and_uri(self):
        source = ("frame-ancestors 'none'\nX-Frame-Options DENY\n"
                  "redir https://treba.duckdns.org/admin{uri} 302\n"
                  "host.docker.internal:5000 host.docker.internal:80")
        result = routing.routed_source(source)
        self.assertEqual(routing.routed_source(result), result)
        self.assertIn("frame-ancestors 'none'", result)
        self.assertIn("https://treba.duckdns.org/admin{uri} 302", result)
        self.assertIn("redir https://{host}{uri} 308", result)
        self.assertEqual(result.count(routing.HTTP_MARKER), 1)


@unittest.skipUnless(os.environ.get("TREBA_PRIVATE_ROUTING_INTEGRATION") == "1",
                     "requires the isolated, labelled Docker fixture")
class DockerIntegration(unittest.TestCase):
    def test_failed_cutover_restores_originals_then_success_preserves_state(self):
        engine = routing.Engine()
        old = {name: engine.inspect(name) for name in routing.NAMES}
        # Never let this test target unlabelled application containers.
        for item in old.values():
            self.assertEqual(item["Config"].get("Labels", {}).get("com.treba.routing-fixture"), "true")
        with patch.object(routing, "EXPECTED_HOST", socket.gethostname()):
            live, source, planned = routing.prerequisites(engine)
            real_verify = routing.verify

            def fail_after_real_checks(client):
                real_verify(client)
                raise RuntimeError("Synthetic post-cutover failure")

            with patch.object(routing, "verify", fail_after_real_checks):
                with self.assertRaisesRegex(RuntimeError, "original containers and Caddyfile restored"):
                    routing.apply(engine, live, source, planned)
            self.assertEqual(routing.CADDYFILE.read_text(), source)
            for name in routing.NAMES:
                self.assertEqual(engine.inspect(name)["Id"], old[name]["Id"])
                self.assertTrue(engine.inspect(name)["State"]["Running"])
            routing.wait_http("https://treba.duckdns.org", "/", 200,
                              "treba.duckdns.org:443:127.0.0.1")
            live, source, planned = routing.prerequisites(engine)
            routing.apply(engine, live, source, planned)
            real_verify(engine)
            for name in routing.NAMES:
                current = engine.inspect(name)
                self.assertEqual(current["Image"], old[name]["Image"])
                self.assertEqual(current["HostConfig"]["Binds"], old[name]["HostConfig"]["Binds"])
                self.assertEqual(current["HostConfig"]["RestartPolicy"], old[name]["HostConfig"]["RestartPolicy"])
            self.assertFalse(routing.published(engine.inspect("treba-backend")))
            self.assertFalse(routing.published(engine.inspect("treba-frontend")))
            self.assertIn("308", routing.command(
                "curl", "--silent", "--max-time", "5", "--resolve",
                "treba.duckdns.org:80:127.0.0.1", "--output", "/dev/null",
                "--write-out", "%{http_code}", "http://treba.duckdns.org/login?x=1"))
            # Restart proves the recreated configuration, rather than only a live reload.
            engine.action("treba-caddy", "restart", "?t=10")
            real_verify(engine)


if __name__ == "__main__":
    unittest.main()
