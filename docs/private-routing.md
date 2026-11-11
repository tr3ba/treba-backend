# Private routing for the inspected TREBA EC2

Scope: DEVOPS-HTTP-01 and SEC-32. This changes container routing, not application code, images, database schema, credentials, or media storage.

## Expected topology

- Caddy publishes TCP 80 and 443 and retains its existing networks and persistent data/config volumes.
- Frontend is on `treba-edge` and `treba-app`, with no published host port.
- Backend is on `treba-app`, with no published host port.
- These are ordinary bridge networks with outbound access; they are not Docker `--internal` networks.
- Frontend uses `TREBA_API_URL=http://treba-backend:5000`. Public browser API URLs remain unchanged.

## First migration

Coordinate this window with both repositories: do not allow an old deployment script to replace a container while migration is running. The old scripts recreate public bindings and omit the private networks. Deploy the reviewed private-routing scripts before the next normal release. No application S3 or Next.js streaming changes are required for this topology change.

The network preparation must already be complete. The inspected server has `treba-edge` and `treba-app` with all required container connections.

Copy `scripts/server/migrate-private-routing.py` to the inspected EC2. First run:

```bash
sudo python3 migrate-private-routing.py
```

This inspects only and must print `READ_ONLY_CHECK_OK`. It refuses a different hostname, unexpected networks, upstreams, mounts, or stopped required containers.

Then apply the exact reviewed file during the coordinated window:

```bash
sudo python3 migrate-private-routing.py --apply
```

The script uses the exact locally running image IDs, copies the existing environment and HostConfig (including mounts and restart policy), changes only required network/port settings and the frontend internal API origin, and preserves the existing Caddyfile inode. It saves an owner-only Caddyfile backup and retains the old stopped containers.

It keeps existing login/admin iframe headers, the API/Telegram path split and legacy admin redirects. Explicit HTTP redirects work with the inspected `auto_https disable_redirects` setting and preserve the URI.

On failure it removes only identity-labelled migration containers, restores original names/configuration and starts originals. Do not manually start a legacy frontend while Caddy owns port 80. A later manual rollback must reverse the topology as a coordinated operation; the retained old container is not independently safe to start.

## Acceptance

The script checks private application HTTP routes, the frontend-to-backend connection, public HTTPS routes through local TLS, both HTTP hosts with query preservation, iframe headers, Telegram JavaScript MIME types and absence of application host-port bindings.

After local checks, verify the AWS security-group rule for inbound TCP 80 and test external HTTP 308 / HTTPS 200. Existing TCP 443 access must remain. Do not open application ports 3000/5000.

Run one normal frontend and backend deployment using the reviewed scripts. Verify private memberships and absence of published application ports again. Test the real signed-in user/admin flows. The migration helper's read-only re-run verifies the private topology and current HTTP controls.

## Local validation completed 11 November 2026

- Five focused Python boundary checks passed.
- A labelled isolated Docker fixture passed a forced failure after cutover, restoration of the original container IDs/Caddyfile, a successful cutover and Caddy restart.
- The complete frontend deployment script passed with real Docker containers and synthetic HTTP fixtures. ECR authentication/pull alone were replaced with local test shims; no AWS requests were made.
- A deliberately failed image check exercised frontend rollback to the preceding private container.
- Existing frontend rollback-retention tests: three passed.
- Bash syntax checks passed for both deployment scripts.
- Independent read-only candidate review reported no concrete material bypass or regression for the inspected topology.

These are local fixture results. They do not prove EC2 rollout, authenticated application behavior, or a normal backend ECR/SSM deployment. Those remain acceptance steps.

The legacy `telegram-mini-app.Caddyfile.example` is intentionally unchanged because the image-publishing smoke test transforms its upstream strings. The live migration and deploy scripts implement private routing without changing that build-time fixture.
