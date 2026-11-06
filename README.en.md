<div align="center">

# Treba Backend

### Marketplace API · Data · Authentication

[Українська](README.md) · **English**

<img src="https://skillicons.dev/icons?i=cs,dotnet,postgres,docker&theme=dark" height="44" alt="C#, .NET, PostgreSQL and Docker">

<br>

![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-10-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![EF Core](https://img.shields.io/badge/EF_Core-10-6C3CA4?style=flat-square&logo=dotnet&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18-4169E1?style=flat-square&logo=postgresql&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-Containers-2496ED?style=flat-square&logo=docker&logoColor=white)

[![Source](https://img.shields.io/badge/SOURCE-FF6E2A?style=for-the-badge&logo=github&logoColor=white)](https://github.com/tr3ba/treba-backend)
[![Frontend](https://img.shields.io/badge/FRONTEND-002AFF?style=for-the-badge&logo=nextdotjs&logoColor=white)](https://github.com/tr3ba/treba-frontend)
[![Project](https://img.shields.io/badge/PROJECT-002AFF?style=for-the-badge&logo=github&logoColor=white)](https://github.com/tr3ba/.github/blob/main/profile/README.md)

</div>

[Overview](#overview) · [Stack](#stack) · [Run locally](#run-locally) · [Checks](#checks) · [Delivery](#docker-and-delivery)

## Overview

Treba Backend is the API for the marketplace. It serves the catalog, accounts, sellers, shops, carts, orders, reviews, support, and administrative operations. The administrative interface itself runs in the [frontend at /admin](https://github.com/tr3ba/treba-frontend/pull/50).

| Marketplace | Accounts and operations |
|---|---|
| Products, categories, brands, variants, and images | Registration with email/SMS verification |
| Sellers, shops, and seller applications | Login, refresh/logout, and 2FA checks |
| Cart, wishlist, orders, reviews, and support | Password recovery, external sign-in, and access rules |
| Product import and notifications | Delivery integrations and Telegram Mini App |

External integrations need their own configuration. A successful API startup alone does not exercise email, SMS, OAuth, delivery, or Telegram flows.

<a id="stack"></a>
## Stack

| Component | Version or configuration |
|---|---|
| Platform | ASP.NET Core / .NET 10, <code>net10.0</code> |
| EF Core Design/Tools | <code>10.0.11</code> |
| PostgreSQL provider | <code>Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3</code> |
| JWT Bearer | <code>10.0.12</code> |
| Password hashing | <code>BCrypt.Net-Next 4.2.0</code> |
| Swagger | <code>Swashbuckle.AspNetCore 10.2.3</code> |
| Local and CI database | <code>postgres:18-alpine</code> |

[WebApi project](https://github.com/tr3ba/treba-backend/blob/main/src/Rozetka-clone/WebApi/WebApi.csproj) · [Infrastructure project](https://github.com/tr3ba/treba-backend/blob/main/src/Rozetka-clone/Infrastructure/Infrastructure.csproj) · [Dockerfile](https://github.com/tr3ba/treba-backend/blob/main/docker/Dockerfile.backend)

## Solution layout

~~~text
src/Rozetka-clone/
├── Application/       application services and use cases
├── Contracts/         shared contracts
├── Domain/            domain entities
├── Infrastructure/    EF Core, persistence, integrations
├── WebApi/            HTTP API and configuration
└── WebApi.Tests/      tests
~~~

The solution file is <code>src/Rozetka-clone/Rozetka-clone.sln</code>. <code>Shared/</code> contains helpers used by WebApi.

<a id="run-locally"></a>
## Run locally

Run these commands from the root of <code>tr3ba/treba-backend</code>. You need .NET SDK 10, PostgreSQL, and Development configuration.

| .NET setting | Purpose |
|---|---|
| <code>ConnectionStrings:DefaultConnection</code> | Connection to a separate local PostgreSQL database |
| <code>Jwt:SecretKey</code> | At least 32 UTF-8 bytes |
| <code>Jwt:Issuer</code>, <code>Jwt:Audience</code> | Token issuer and audience |
| <code>Security:EncryptionKey</code> | Base64 value for exactly 32 bytes |
| <code>Security:OtpHashKey</code> | Base64 value for at least 32 bytes |
| <code>ApplyMigrations</code> | Whether startup applies EF migrations |

Environment variable names use <code>__</code> in place of <code>:</code>, for example <code>ConnectionStrings__DefaultConnection</code>. Use .NET User Secrets or a local <code>appsettings.Development.json</code> based on the [example](https://github.com/tr3ba/treba-backend/blob/main/src/Rozetka-clone/WebApi/appsettings.Development.example.json). The root <code>.env.example</code> belongs to Compose; <code>dotnet run</code> does not automatically load it.

~~~powershell
dotnet restore src/Rozetka-clone/Rozetka-clone.sln
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/Rozetka-clone/WebApi/WebApi.csproj --no-launch-profile -- --urls http://localhost:5000
~~~

Swagger is enabled in Development. The two health endpoints check different things:

| Request | What it checks |
|---|---|
| <code>GET /ping</code> | The API process responds; no database connection is required |
| <code>GET /health</code> | PostgreSQL responds to <code>CanConnectAsync</code>; this does not test every schema or user flow |

<a id="checks"></a>
## Build and checks

~~~powershell
dotnet build src/Rozetka-clone/Rozetka-clone.sln --configuration Release
dotnet test src/Rozetka-clone/WebApi.Tests/WebApi.Tests.csproj --configuration Release
dotnet list src/Rozetka-clone/Rozetka-clone.sln package --vulnerable --include-transitive
~~~

PostgreSQL integration tests need <code>TREBA_TEST_POSTGRES</code> pointing at a separate test database. Without it, some tests are skipped. The [Backend Build workflow](https://github.com/tr3ba/treba-backend/blob/main/.github/workflows/build-backend.yml) starts PostgreSQL 18 and supplies this setting.

<a id="docker-and-delivery"></a>
## Docker and delivery

~~~powershell
docker build -f docker/Dockerfile.backend -t treba-backend:local .
~~~

Compose includes PostgreSQL, backend, and frontend, with the frontend built from a neighboring <code>../treba-frontend</code> checkout. The separate <code>k8s/</code> directory contains local Kubernetes Stage 5 manifests.

~~~text
Backend Build and tests
          ↓
Docker image and /ping checks → Amazon ECR
          ↓
AWS Systems Manager → candidate container → EC2 checks
~~~

The [deployment workflow](https://github.com/tr3ba/treba-backend/blob/main/.github/workflows/deploy-backend.yml) selects an image by commit SHA, checks the candidate container, and keeps the previous container for rollback. Rolling back a container does not roll back database migrations.

<details>
<summary><strong>Before a non-Development deployment</strong></summary>

- <code>MockPaymentProvider</code> is registered only in Development. A non-Development environment needs an <code>IPaymentProvider</code> implementation before payment flows can work.
- <code>IDeliveryProvider</code> is bound to <code>MockDeliveryProvider</code> in this setup. Nova Poshta integration is described separately in the [delivery documentation](https://github.com/tr3ba/treba-backend/blob/main/docs/delivery-notifications.md).

Provider registration is in [DependencyInjection.cs](https://github.com/tr3ba/treba-backend/blob/main/src/Rozetka-clone/Infrastructure/DependencyInjection.cs).

</details>

## More documentation

[Admin workspace migration](https://github.com/tr3ba/treba-backend/blob/main/docs/admin-frontend-migration.md) · [Registration verification](https://github.com/tr3ba/treba-backend/blob/main/docs/registration-verification.md) · [OAuth](https://github.com/tr3ba/treba-backend/blob/main/docs/external-authentication.md) · [Delivery](https://github.com/tr3ba/treba-backend/blob/main/docs/delivery-notifications.md) · [Seller import](https://github.com/tr3ba/treba-backend/blob/main/docs/seller-product-import.md)

[Frontend README](https://github.com/tr3ba/treba-frontend/blob/main/README.en.md) · [Treba history](https://github.com/tr3ba/.github/blob/main/docs/history/project-history.en.md) · [Team](https://github.com/tr3ba/.github/blob/main/docs/team/README.en.md)
