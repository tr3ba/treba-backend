# Troubleshooting

## obj/bin у Git

Проблема:

У Git були закомічені файли obj/bin з локальними NuGet paths.

Через це dotnet ef не працював на іншому ПК.

Рішення:

Видалили tracked obj/bin і оновили .gitignore.

Після цього:

dotnet restore -> SUCCESS
dotnet build -> SUCCESS
dotnet ef -> SUCCESS


## Docker Desktop не запущений

Помилка:

failed to connect to dockerDesktopLinuxEngine

Рішення:

Запустити Docker Desktop.


## Контейнер падає при запуску

Помилка:

Connection string 'DefaultConnection' was not found.

Рішення:

Передати ConnectionStrings__DefaultConnection через environment variable або env-file.


## PostgreSQL

Для перевірки таблиць потрібно спочатку зайти в psql.

Потім виконати:

\dt


## Smoke test

Для перевірки backend:

curl http://localhost:5000/WeatherForecast

або локально через Docker:

curl http://localhost:5050/WeatherForecast
