<div align="center">

# ⚙️ Treba Backend

### ASP.NET Core · .NET 10 · PostgreSQL · AWS

Backend API and application services for the **Treba Marketplace**.

<br>

[![Organization](https://img.shields.io/badge/TREBA-002AFF?style=for-the-badge&logo=github&logoColor=white)](https://github.com/tr3ba)
[![Live Demo](https://img.shields.io/badge/LIVE_DEMO-FF6E2A?style=for-the-badge&logo=googlechrome&logoColor=white)](http://52.209.28.30/)
[![Frontend](https://img.shields.io/badge/FRONTEND-002AFF?style=for-the-badge&logo=nextdotjs&logoColor=white)](https://github.com/tr3ba/treba-frontend)

<br><br>

![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-API-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-4169E1?style=flat-square&logo=postgresql&logoColor=white)
![EF Core](https://img.shields.io/badge/EF_Core-10-512BD4?style=flat-square)
![Docker](https://img.shields.io/badge/Docker-2496ED?style=flat-square&logo=docker&logoColor=white)
![GitHub Actions](https://img.shields.io/badge/GitHub_Actions-2088FF?style=flat-square&logo=githubactions&logoColor=white)
![AWS](https://img.shields.io/badge/AWS-FF9900?style=flat-square&logo=amazonwebservices&logoColor=white)

</div>

---

## 👋 About

**Treba Backend** is the server-side part of the Treba Marketplace.

The solution provides the marketplace API, authentication and authorization,
user management, catalog operations, cart functionality, administration endpoints
and PostgreSQL database access.

The backend is built with **ASP.NET Core on .NET 10** and uses
**Entity Framework Core with Npgsql** for PostgreSQL.

---

## 🧩 Technology Stack

| Area | Technology |
|---|---|
| Runtime | **.NET 10** |
| Web API | **ASP.NET Core** |
| ORM | **Entity Framework Core 10** |
| PostgreSQL provider | **Npgsql 10.0.3** |
| Database | **PostgreSQL** |
| Authentication | **JWT Bearer** |
| Password security | **BCrypt** |
| API documentation | **Swagger / Swashbuckle** |
| Containers | **Docker** |
| CI/CD | **GitHub Actions** |
| Cloud | **AWS ECR · EC2 · Systems Manager** |

---

## 🏗️ Solution Architecture

The backend follows a layered structure:

```text
Rozetka-clone
│
├── Application
├── Contracts
├── Domain
├── Infrastructure
├── WebApi
└── AdminPanel
```

### Application

Contains application-level services and use-case logic.

### Contracts

Contains shared contracts and communication models.

### Domain

Contains the core domain model.

### Infrastructure

Contains database and infrastructure integrations, including
**Entity Framework Core**, **Npgsql** and PostgreSQL access.

### WebApi

Main ASP.NET Core HTTP API.

### AdminPanel

Administrative part of the backend solution.

---

## 🔌 API Areas

The current backend contains endpoints and services for marketplace areas including:

- Products
- Categories
- Brands
- Product variants
- Catalog
- Cart
- Sellers
- Stores
- Users
- Administration
- Authentication
- Security / 2FA

---

## 🔐 Authentication & Security

The backend implements authentication functionality including:

- User registration
- Login
- JWT authentication
- Refresh token flow
- Logout
- Two-factor authentication checks
- Email-based security configuration
- Authenticator-based 2FA configuration

Runtime security configuration is separated from source code and is expected
to be provided through environment/runtime configuration.

---

## 🗄️ Database

Treba Backend uses **PostgreSQL** through:

```text
ASP.NET Core
      ↓
Entity Framework Core
      ↓
Npgsql
      ↓
PostgreSQL
```

The application contains EF Core migrations and uses `UseNpgsql`
in the infrastructure configuration.

For the AWS environment, PostgreSQL is hosted in **Amazon RDS**.

---

## ❤️ Health Endpoints

The backend exposes separate health endpoints:

### `/ping`

Basic application availability check.

```text
GET /ping
```

Used to confirm that the backend process is responding.

### `/health`

Database-aware application health check.

```text
GET /health
```

Used to verify both the backend service and PostgreSQL connectivity.

---

## 📖 API Documentation

Swagger is configured for the **Development** environment.

Production environments intentionally do not expose Swagger by default.

---

## 🐳 Docker

The repository contains a dedicated backend Dockerfile:

```text
docker/Dockerfile.backend
```

The image builds and runs the application on **.NET 10**.

The repository also contains a Docker Compose configuration for local
multi-service development with:

```text
PostgreSQL
Backend
Frontend
```

> **Development note:** the Compose configuration and `.env.example`
> are currently being aligned with the latest backend runtime configuration.
> Some legacy configuration names and an older health-check route still need cleanup.

---

## 🚀 CI/CD

The backend repository contains several GitHub Actions workflows.

### Build & Validation

The backend workflow performs:

```text
Restore
   ↓
Build
   ↓
NuGet vulnerability check
   ↓
Tests
```

Build validation runs for the main development branches and pull requests.

### Docker Image Publishing

The Docker publishing workflow:

```text
Source
   ↓
Docker Build
   ↓
Container Start
   ↓
/ping Smoke Check
   ↓
Amazon ECR
```

Images are published with version/SHA-oriented tags and `latest`.

### Deployment

Deployment uses:

```text
GitHub Actions
      ↓
Amazon ECR
      ↓
AWS Systems Manager
      ↓
Amazon EC2
```

AWS Systems Manager is used to execute deployment commands on EC2.

### Releases

A dedicated backend release workflow exists for tags matching:

```text
v*
```

---

## ☁️ AWS Deployment

The backend delivery path is designed around the following AWS services:

| Service | Purpose |
|---|---|
| Amazon ECR | Docker image registry |
| Amazon EC2 | Application runtime |
| Amazon RDS | PostgreSQL database |
| AWS Systems Manager | Remote deployment |
| AWS IAM | Access control |
| AWS Secrets Manager | Runtime secrets |
| Amazon CloudWatch | Monitoring |

---

## 📊 Current Project Status

<div align="center">

![API](https://img.shields.io/badge/Backend_API-Implemented-002AFF?style=flat-square)
![Database](https://img.shields.io/badge/PostgreSQL-Integrated-002AFF?style=flat-square)
![Docker](https://img.shields.io/badge/Docker-Configured-002AFF?style=flat-square)
![CI](https://img.shields.io/badge/CI%2FCD-Configured-FF6E2A?style=flat-square)
![AWS](https://img.shields.io/badge/AWS_Deployment-Configured-FF6E2A?style=flat-square)

</div>

The backend API and PostgreSQL integration are implemented.

The frontend application is maintained separately and its integration with
backend-powered catalog, authentication and cart flows is still being completed.

---

## ⚠️ Configuration Notes

The current repository still contains a few development configuration items
that should be cleaned up:

- Docker Compose contains legacy JWT configuration naming
- `.env.example` requires alignment with the latest required security settings
- Docker Compose still references the legacy `/WeatherForecast` health check
- current health checks are `/ping` and `/health`

These items do not change the current application architecture,
but should be updated before presenting Docker Compose as a fully reproducible local environment.

---

## 📦 Related Repository

### 🎨 Treba Frontend

Next.js marketplace frontend:

[![Open Frontend](https://img.shields.io/badge/OPEN_FRONTEND-002AFF?style=for-the-badge&logo=github&logoColor=white)](https://github.com/tr3ba/treba-frontend)

---

<div align="center">

## Treba Marketplace

**Build · Automate · Deploy · Monitor · Improve**

<br>

[![Treba Organization](https://img.shields.io/badge/TREBA_ORGANIZATION-002AFF?style=for-the-badge&logo=github&logoColor=white)](https://github.com/tr3ba)

</div>
