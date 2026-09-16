# CI/CD

У проєкті використовується GitHub Actions.

Основні workflow:
 build-backend.yml
 docker-publish.yml
 deploy-backend.yml
 release-backend.yml

Pipeline:

Push / Pull Request
 Restore
 Build
 Tests
 NuGet vulnerability scan
 Docker build
 Smoke test
 Push image в AWS ECR
 Deploy на AWS EC2
 HTTP перевірка

Для тестів використовується xUnit.

Docker image перевіряється через endpoint:

GET /WeatherForecast

Release workflow запускається по тегах виду:

v0.1.0

Тестовий release v0.1.0 був успішно створений і завантажений в AWS ECR.
