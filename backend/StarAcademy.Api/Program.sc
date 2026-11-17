// ============================================================================
// Program.cs — точка входа ASP.NET Core Web API (minimal hosting, .NET 8)
//
// Здесь собираем «конвейер» приложения:
//   1. DI-контейнер (сервисы: DbContext, CORS, Swagger, контроллеры)
//   2. HTTP-конвейер (middleware: обработчик ошибок -> CORS -> маршрутизация)
//
// Порядок middleware ВАЖЕН: сначала регистрируются те, что обрабатывают
// запрос раньше (исключения ловят всё, что ниже).
// ============================================================================

using Microsoft.EntityFrameworkCore;
using StarAcademy.Api.Data;
using StarAcademy.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1. СЕРВИСЫ (внедряются в конструкторы контроллеров через DI)
// ---------------------------------------------------------------------------

// PostgreSQL через EF Core. Строка подключения берётся из appsettings.json
// (секция ConnectionStrings:Default). Pooling — пул соединений, стандарт для API.
builder.Services.AddDbContext<StarAcademyDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Default"),
        npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3)));

// Контроллеры (Controllers/*.cs) + формат дат в JSON как ISO-8601.
builder.Services.AddControllers();

// Swagger: интерактивная документация API по адресу /swagger — удобно тестировать
// эндпоинты прямо из браузера на этапе разработки.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new()
    {
        Title = "Star Academy API",
        Version = "v1",
        Description = "Звёздная Академия — API образовательной игры"
    });
});

// CORS: фронтенд (PWA) будет жить на другом порту/домене (5173), чем API (5000).
// Браузер заблокирует кросс-доменные fetch-запросы, если не разрешить явно.
const string CorsPolicy = "AllowFrontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins(
                "http://localhost:5173",   // Vite/простой статический сервер фронта
                "http://localhost:3000",
                "http://127.0.0.1:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// 2. HTTP-КОНВЕЙЕР (middleware)
// ---------------------------------------------------------------------------

// Наш обработчик исключений: превращает падения в аккуратный JSON 500.
app.UseMiddleware<GlobalExceptionHandler>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();                       // отдаёт swagger.json
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "Star Academy API v1"));
}

// В продакшене за HTTPS-терминацию отвечает реверс-прокси (nginx), поэтому
// UseHttpsRedirection подключаем только не в Development.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors(CorsPolicy);
app.MapControllers();

// Health-check: «я жива» + проверка, что БД доступна. Полезно для мониторинга
// и деплоя (Docker healthcheck, Render/Fly.io).
app.MapGet("/health", async (StarAcademyDbContext db) =>
{
    try
    {
        // Простейший запрос к БД: если PostgreSQL недоступен — поймам исключение.
        var subjects = await db.Subjects.CountAsync();
        return Results.Ok(new { status = "ok", subjects, time = DateTimeOffset.UtcNow });
    }
    catch (Exception ex)
    {
        return Results.Problem($"База недоступна: {ex.Message}");
    }
}).WithTags("System");

app.Run();