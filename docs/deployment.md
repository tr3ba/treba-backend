# Deployment

Backend розгортається в AWS.

Використовуються сервіси:

EC2
ECR
RDS PostgreSQL
IAM
Security Groups
Secrets Manager
SSM

Схема:

GitHub
GitHub Actions
AWS ECR
AWS EC2
Docker
ASP.NET Backend
AWS RDS PostgreSQL

Deployment виконується автоматично через GitHub Actions.

Після успішної збірки Docker image завантажується в ECR.

Потім EC2 через AWS SSM завантажує новий image і запускає контейнер.

Backend працює на порту:

5000

Після deployment виконується перевірка:

GET /WeatherForecast

Для environment variables на EC2 використовується файл:

/home/ubuntu/treba-backend.env

База даних:

PostgreSQL

Database:

treba_dev
