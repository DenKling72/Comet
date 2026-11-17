// ============================================================================
// GlobalExceptionHandler — middleware обработки исключений
//
// Идея: ни один необработанный exception не должен «уронить» запрос с пустым
// 500 или HTML-страницей. Ребёнок (и мы через Swagger) всегда получат понятный
// JSON вида: { "error": "..." }.
//
// Как это работает:
//   Middleware оборачивает ВСЮ оставшуюся часть конвейера (_next) в try/catch.
//   Если ниже по конвейеру что-то бросило исключение — мы его ловим здесь
//   и формируем ответ сами.
// ============================================================================

using System.Text.Json;

namespace StarAcademy.Api.Middleware;

public class GlobalExceptionHandler
{
    private readonly RequestDelegate _next;     // «остальной конвейер»
    private readonly ILogger<GlobalExceptionHandler> _logger;

    // ILogger внедряется DI-контейнером автоматически — логи пишем в консоль.
    public GlobalExceptionHandler(RequestDelegate next, ILogger<GlobalExceptionHandler> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);   // передаём запрос дальше по конвейеру
        }
        catch (Exception ex)
        {
            // Полную информацию — только в лог сервера (клиенту её не показываем).
            _logger.LogError(ex, "Необработанное исключение на {Path}", context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";

            var payload = new
            {
                error = "Что-то пошло не так на борту корабля. Капитан Комета уже чинит!",
                detail = ex.InnerException?.Message ?? ex.Message, // на время разработки
                path = context.Request.Path.ToString()
            };

            await context.Response.WriteAsync(
                JsonSerializer.Serialize(payload,
                    new JsonSerializerOptions { WriteIndented = false }));
        }
    }
}