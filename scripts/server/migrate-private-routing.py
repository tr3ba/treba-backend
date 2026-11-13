#!/usr/bin/env python3
"""One-time migration of the inspected EC2 to private application routing.

No image pull, database migration, application upgrade or secret output.
Default: inspect only. Apply explicitly with sudo python3 ... --apply.
"""
import argparse
import copy
import datetime
import http.client
import json
import os
from pathlib import Path
import signal
import socket
import subprocess
import time
from urllib.parse import quote

EXPECTED_HOST = "ip-172-31-24-4"
CADDYFILE = Path("/opt/treba-caddy/Caddyfile")
NAMES = ("treba-backend", "treba-frontend", "treba-caddy")
NETWORKS = {"treba-backend": ("treba-app",),
            "treba-frontend": ("treba-edge", "treba-app")}
HTTP_MARKER = "# TREBA managed HTTP redirects"
REDIRECT = """
# TREBA managed HTTP redirects
http://treba.duckdns.org, http://admin.treba.duckdns.org {
    redir https://{host}{uri} 308
}
"""


class UnixHTTP(http.client.HTTPConnection):
    def connect(self):
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.sock.settimeout(self.timeout)
        self.sock.connect("/var/run/docker.sock")


class Engine:
    def request(self, method, path, data=None):
        connection = UnixHTTP("localhost", timeout=45)
        try:
            body = None if data is None else json.dumps(data)
            connection.request(method, path, body, {"Content-Type": "application/json"})
            response = connection.getresponse()
            raw = response.read()
            if response.status >= 300 and response.status != 304:
                # Engine errors can contain environment values; never echo their bodies.
                raise RuntimeError("Docker operation failed: " + str(response.status))
            return json.loads(raw) if raw else None
        finally:
            connection.close()

    def inspect(self, name):
        return self.request("GET", "/containers/" + quote(name, safe="") + "/json")

    def action(self, name, action, parameters=""):
        return self.request("POST", "/containers/" + quote(name, safe="") + "/" + action + parameters)


def command(*args):
    result = subprocess.run(args, capture_output=True, text=True, timeout=60)
    if result.returncode:
        raise RuntimeError("Validation command failed: " + args[0])
    return result.stdout.strip()


def published(item):
    return any((item["HostConfig"].get("PortBindings") or {}).values())


def routed_source(source):
    pairs = (("host.docker.internal:5000", "treba-backend:5000"),
             ("host.docker.internal:80", "treba-frontend:3000"))
    for old, new in pairs:
        if source.count(old) == 1 and source.count(new) == 0:
            source = source.replace(old, new)
        elif source.count(old) == 0 and source.count(new) == 1:
            pass
        else:
            raise RuntimeError("Caddy upstream differs from the inspected configuration")
    if HTTP_MARKER not in source:
        if "http://treba.duckdns.org" in source or "http://admin.treba.duckdns.org" in source:
            raise RuntimeError("Existing HTTP site requires individual review")
        source += REDIRECT
    elif source.count(HTTP_MARKER) != 1 or REDIRECT.strip() not in source:
        raise RuntimeError("Managed HTTP redirect differs from the reviewed version")
    return source


def payload(item, name, networks):
    keys = ("Hostname", "Domainname", "User", "ExposedPorts", "Tty", "OpenStdin",
            "StdinOnce", "Env", "Cmd", "Healthcheck", "Image", "Volumes",
            "WorkingDir", "Entrypoint", "Labels", "StopSignal", "StopTimeout")
    result = {key: copy.deepcopy(item["Config"][key])
              for key in keys if key in item["Config"]}
    result["Image"] = item["Image"]  # Exact locally running image ID, never a mutable tag.
    result["HostConfig"] = copy.deepcopy(item["HostConfig"])
    result["HostConfig"]["NetworkMode"] = networks[0]
    result["HostConfig"]["AutoRemove"] = False
    result["Labels"] = dict(result.get("Labels") or {})
    result["Labels"]["com.treba.private-routing"] = "2026-11-11"
    if name != "treba-caddy":
        result["HostConfig"]["PortBindings"] = {}
        result["HostConfig"]["PublishAllPorts"] = False
    else:
        bindings = result["HostConfig"].get("PortBindings") or {}
        bindings["80/tcp"] = [{"HostIp": "", "HostPort": "80"}]
        result["HostConfig"]["PortBindings"] = bindings
    if name == "treba-frontend":
        result["Env"] = [value for value in result.get("Env") or []
                         if not value.startswith("TREBA_API_URL=")]
        result["Env"].append("TREBA_API_URL=http://treba-backend:5000")
    return result


def endpoint(name, network):
    return {} if network == "bridge" else {"Aliases": [name]}


def prerequisites(engine):
    if socket.gethostname() != EXPECTED_HOST:
        raise RuntimeError("Unexpected host; this script is for the inspected EC2 only")
    live = {name: engine.inspect(name) for name in NAMES}
    for name, item in live.items():
        if not item["State"]["Running"]:
            raise RuntimeError("A required live container is not running: " + name)
    for network in ("treba-edge", "treba-app"):
        item = engine.request("GET", "/networks/" + network)
        if item["Driver"] != "bridge" or item.get("Internal"):
            raise RuntimeError("A required network has an unexpected configuration")
    for name, networks in NETWORKS.items():
        if not set(networks).issubset(live[name]["NetworkSettings"]["Networks"]):
            raise RuntimeError("Run the private-network preparation first")
    caddy = live["treba-caddy"]
    if not {"treba-edge", "treba-app"}.issubset(caddy["NetworkSettings"]["Networks"]):
        raise RuntimeError("Caddy is missing a required network")
    mounts = {item["Destination"]: item for item in caddy["Mounts"]}
    if mounts.get("/etc/caddy/Caddyfile", {}).get("Source") != str(CADDYFILE):
        raise RuntimeError("Unexpected Caddyfile mount")
    for destination, volume in (("/data", "treba_caddy_data"), ("/config", "treba_caddy_config")):
        if mounts.get(destination, {}).get("Name") != volume:
            raise RuntimeError("Unexpected Caddy persistent volume")
    # Preserve all mount definitions instead of recreating containers with guessed paths.
    for item in live.values():
        if item["Mounts"] and not (item["HostConfig"].get("Binds") or item["HostConfig"].get("Mounts")):
            raise RuntimeError("An anonymous mount needs individual review before migration")
    source = CADDYFILE.read_text(encoding="utf-8")
    planned = routed_source(source)
    return live, source, planned


def wait_http(origin, path, expected, resolve=None):
    for attempt in range(35):
        args = ["curl", "--silent", "--show-error", "--max-time", "3",
                "--output", "/dev/null", "--write-out", "%{http_code}"]
        if resolve:
            args += ["--resolve", resolve]
        try:
            if command(*args, origin + path) == str(expected):
                return
        except (RuntimeError, subprocess.TimeoutExpired):
            pass
        time.sleep(1)
    raise RuntimeError("HTTP check failed: " + path)


def private_origin(engine, name, network, port):
    address = engine.inspect(name)["NetworkSettings"]["Networks"][network]["IPAddress"]
    if not address:
        raise RuntimeError("Missing private address")
    return "http://" + address + ":" + str(port)


def verify(engine):
    for name in ("treba-backend", "treba-frontend"):
        item = engine.inspect(name)
        if published(item) or "bridge" in item["NetworkSettings"]["Networks"]:
            raise RuntimeError("An application container retained a public or default-bridge path")
    backend = private_origin(engine, "treba-backend", "treba-app", 5000)
    frontend = private_origin(engine, "treba-frontend", "treba-edge", 3000)
    wait_http(backend, "/ping", 200)
    wait_http(frontend, "/", 200)
    command("docker", "exec", "treba-frontend", "node", "-e",
            'if(process.env.TREBA_API_URL!=="http://treba-backend:5000")process.exit(1);'
            'fetch(process.env.TREBA_API_URL+"/ping",{signal:AbortSignal.timeout(5000)})'
            '.then(r=>{if(!r.ok)process.exit(1)}).catch(()=>process.exit(1))')
    domain = "treba.duckdns.org"
    resolve = domain + ":443:127.0.0.1"
    for path, status in (("/", 200), ("/login", 200), ("/admin", 200),
                         ("/api/v1/catalog?size=1", 200), ("/telegram/", 200),
                         ("/shop-api/session", 401), ("/admin-api/session", 401)):
        wait_http("https://" + domain, path, status, resolve)
    for host in (domain, "admin.treba.duckdns.org"):
        for path in ("/login?next=%2Faccount", "/api/v1/catalog?size=1"):
            output = command("curl", "--silent", "--show-error", "--max-time", "5",
                             "--resolve", host + ":80:127.0.0.1", "--output", "/dev/null",
                             "--write-out", "%{http_code}\n%{redirect_url}", "http://" + host + path)
            if output.splitlines() != ["308", "https://" + host + path]:
                raise RuntimeError("HTTP redirect did not preserve the original URI")
    for path in ("/login", "/admin"):
        headers = command("curl", "--silent", "--show-error", "--max-time", "5",
                          "--resolve", resolve, "--output", "/dev/null", "--dump-header", "-",
                          "https://" + domain + path).lower()
        if "frame-ancestors 'none'" not in headers or "x-frame-options: deny" not in headers:
            raise RuntimeError("Existing iframe protection was not preserved")
    for path in ("/telegram/app.js", "/telegram/ui.js", "/js/editor-drafts.js"):
        content_type = command("curl", "--silent", "--show-error", "--fail",
                               "--max-time", "8", "--resolve", resolve,
                               "--output", "/dev/null", "--write-out", "%{content_type}",
                               "https://" + domain + path)
        if content_type.split(";", 1)[0] not in ("application/javascript", "text/javascript"):
            raise RuntimeError("A Telegram JavaScript route returned an unexpected MIME type")


def apply(engine, live, source, planned):
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%d%H%M%S%f")
    previous = {name: name + "-before-private-" + stamp for name in NAMES}
    backup = CADDYFILE.with_name(CADDYFILE.name + ".before-private-" + stamp)
    # Exclusive backup, owner-only permissions; never replace the bind-mounted inode.
    fd = os.open(backup, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as handle:
        handle.write(source)
    print("Prepared owner-only Caddyfile backup:", backup, flush=True)
    try:
        CADDYFILE.write_text(planned, encoding="utf-8")
        command("docker", "exec", "treba-caddy", "caddy", "validate",
                "--config", "/etc/caddy/Caddyfile", "--adapter", "caddyfile")
        for name in reversed(NAMES):
            engine.action(name, "stop", "?t=30")
        for name in NAMES:
            engine.action(name, "rename", "?name=" + quote(previous[name], safe=""))
        for name in NAMES:
            networks = list(NETWORKS.get(name) or live[name]["NetworkSettings"]["Networks"])
            data = payload(live[name], name, networks)
            data["Labels"]["com.treba.routing-migration"] = stamp
            data["NetworkingConfig"] = {"EndpointsConfig": {networks[0]: endpoint(name, networks[0])}}
            item = engine.request("POST", "/containers/create?name=" + name, data)
            for network in networks[1:]:
                engine.request("POST", "/networks/" + network + "/connect",
                               {"Container": item["Id"], "EndpointConfig": endpoint(name, network)})
            engine.action(name, "start")
            if name == "treba-backend":
                wait_http(private_origin(engine, name, "treba-app", 5000), "/ping", 200)
            elif name == "treba-frontend":
                wait_http(private_origin(engine, name, "treba-edge", 3000), "/", 200)
        verify(engine)
    except BaseException:
        for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            signal.signal(sig, signal.SIG_IGN)
        errors = []
        for name in reversed(NAMES):
            try:
                current = engine.inspect(name)
            except Exception:
                current = None
            if current is not None and current["Id"] != live[name]["Id"]:
                if current["Config"].get("Labels", {}).get("com.treba.routing-migration") != stamp:
                    errors.append("unexpected container " + name)
                    continue
                try:
                    engine.request("DELETE", "/containers/" + name + "?force=true")
                except Exception:
                    errors.append("remove " + name)
        try:
            CADDYFILE.write_text(source, encoding="utf-8")
        except Exception:
            errors.append("restore Caddyfile")
        for name in NAMES:
            try:
                old = engine.inspect(previous[name])
            except Exception:
                old = None
            if old is not None and old["Id"] == live[name]["Id"]:
                try:
                    engine.action(previous[name], "rename", "?name=" + name)
                except Exception:
                    errors.append("rename " + name)
        for name in NAMES:
            try:
                current = engine.inspect(name)
                if current["Id"] != live[name]["Id"]:
                    raise RuntimeError("Unexpected container identity")
                engine.action(name, "start")
            except Exception:
                errors.append("start " + name)
        if errors:
            raise RuntimeError("ROLLBACK NEEDS ATTENTION: " + ", ".join(errors) + "; backup=" + str(backup))
        raise RuntimeError("Migration failed; original containers and Caddyfile restored. Backup=" + str(backup))
    print("PRIVATE_ROUTING_OK")
    print("Caddyfile backup:", backup)
    for name in NAMES:
        print("Retained stopped container:", previous[name])
    print("Application images and data preserved. No database migration or image pull performed.")
    print("External HTTP still requires the reviewed AWS port-80 security-group rule.")
    print("Use the patched deploy scripts before the next frontend/backend release.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="Apply the inspected coordinated migration")
    args = parser.parse_args()
    engine = Engine()
    live, source, planned = prerequisites(engine)
    if not any(published(live[name]) for name in ("treba-backend", "treba-frontend")) and source == planned:
        verify(engine)
        print("ALREADY_PRIVATE_AND_VERIFIED")
        return
    print("Inspected host:", EXPECTED_HOST)
    for name in NAMES:
        print(name, "image_id=" + live[name]["Image"], "application_publication=" + str(published(live[name])))
    print("Plan: Caddy owns 80/443; frontend uses edge+app; backend uses app; no application host ports.")
    if not args.apply:
        print("READ_ONLY_CHECK_OK. To apply, rerun this exact script with --apply.")
        return
    if os.geteuid() != 0:
        raise RuntimeError("Apply requires root")
    def interrupted(signum, frame):
        raise RuntimeError("Migration interrupted")
    for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(sig, interrupted)
    apply(engine, live, source, planned)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        raise SystemExit(str(error))
