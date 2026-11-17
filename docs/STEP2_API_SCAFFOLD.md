# Шаг 2 — Каркас ASP.NET Core Web API

## Что сделано в этой итерации

1. **Файл проекта** `StarAcademy.Api.csproj`: .NET 8 (LTS), пакеты
   `Npgsql.EntityFrameworkCore.PostgreSQL` (EF Core ↔ PostgreSQL) и
   `Swashbuckle.AspNetCore` (Swagger UI).
2. **Program.cs** — сборка приложения:
   - регистрация `StarAcademyDbContext` с подключением к PostgreSQL;
   - контроллеры, Swagger, CORS (фронтенд будет на другом порту);
   - endpoint `/health` — проверка «API жива + БД доступна».
3. **Middleware/GlobalExceptionHandler.cs** — все необработанные исключения
   превращаются в аккуратный JSON `500`, сервер не «падает» молча.
4. **Dtos/Dtos.cs** — контракты API (record-типы). Ключевой принцип:
   клиент **никогда** не получает поля `correct_answer` / `is_correct` / `pin_hash`.
5. **Controllers/CatalogController.cs** — первые эндпоинты-«читалки»:
   - `GET /api/subjects` — предметы с персонажами-ведущими;
   - `GET /api/subjects/{code}/topics` — темы + число активных вопросов;
   - `GET /api/characters` — карточки лора (Комета, Байт, Зорп).
6. **.gitignore**, `appsettings.json`, `appsettings.Development.example.json`,
   `Properties/launchSettings.json`.

## Как запустить локально

```bash
# 1. Один раз: создать БД и загрузить схему+seed из Шага 1
sudo -u postgres psql -c "CREATE DATABASE star_academy;"
sudo -u postgres psql -d star_academy -f db/schema.sql

# 2. Настроить пароль: скопировать пример и вписать свой
cp backend/StarAcademy.Api/appsettings.Development.example.json \
   backend/StarAcademy.Api/appsettings.Development.json
nano backend/StarAcademy.Api/appsettings.Development.json   # Password=...

# 3. Запустить API (из корня репозитория)
cd backend/StarAcademy.Api
dotnet run