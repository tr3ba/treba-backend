# Deployment

Treba Backend розгортається в AWS у Docker-контейнері.

## AWS Services

Для backend infrastructure використовуються:

- Amazon EC2
- Amazon ECR
- Amazon RDS for PostgreSQL
- AWS Systems Manager (SSM)
- AWS IAM
- AWS Secrets Manager
- Security Groups

## Deployment Architecture

GitHub → GitHub Actions → Docker Build → Amazon ECR → AWS Systems Manager → Amazon EC2 → Treba Backend → Amazon RDS PostgreSQL

## CI/CD

Deployment автоматизований через GitHub Actions.

Після змін у `main` backend проходить build та перевірки.

Після успішної збірки створюється Docker image та публікується в Amazon ECR.

Deployment workflow використовує AWS Systems Manager для виконання команд на EC2.

EC2 завантажує Docker image з ECR та запускає backend container.

## Runtime

Backend працює в Docker-контейнері.

Backend port:

`5000`

Runtime:

- ASP.NET Core
- .NET 10

## Health Checks

Для перевірки доступності backend використовується:

`GET /ping`

Очікувана відповідь:

```json
{
  "status": "ok"
}
```

## Telegram deployment

Telegram Mini App is served by WebApi at `/telegram/`. The inspected EC2 uses Caddy
(`treba-caddy`, `/opt/treba-caddy/Caddyfile`); that path must join the existing API
matcher and keep the request path unchanged. The storefront continues at `/`.

The deployment workflow pins the image to the source commit SHA and invokes
`scripts/server/deploy-telegram-backend.sh` over the existing SSM channel. It checks
the actual database migrations and bot token using `--check-telegram-deployment`,
then tests a temporary loopback container before replacing the running API.
The previous container and Caddy configuration are retained for rollback.
Before switching HTTP, deployment applies the explicitly allowlisted, backwards-compatible
operational migrations through `--migrate-admin-operations`. It verifies database ownership,
the migration history, CRUD privileges and real schema reads, then checks the protected
operational routes on both the candidate and public API. An unavailable migration or route
stops deployment while the previous container is retained. If the runtime account cannot
apply DDL, the database owner must apply the reviewed SQL before retrying. Other pending
migrations require a separately reviewed update. See [admin operations](admin-operations.md).

Server bot settings stay in the existing deployment environment. Never commit or
print the bot token. See [Telegram handoff](telegram-server-handoff.md) for settings,
database prerequisites, checks and the existing separate AdminPanel deployment.
