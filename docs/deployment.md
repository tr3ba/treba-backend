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
