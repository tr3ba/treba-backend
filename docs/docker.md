# Docker

Backend запускається в Docker.

Використовується Dockerfile:

docker/Dockerfile.backend

Docker image збирається командою:

docker build -f docker/Dockerfile.backend -t treba-backend:test .

Backend працює на порту:

5000

Для перевірки контейнера використовується endpoint:

GET /WeatherForecast

Також у проєкті є .dockerignore, щоб у Docker build не потрапляли зайві файли типу:

bin
obj
.git
.env
logs

Docker images зберігаються в AWS ECR.

Основний repository:

treba-backend

Для release також використовуються теги типу:

v0.1.0
