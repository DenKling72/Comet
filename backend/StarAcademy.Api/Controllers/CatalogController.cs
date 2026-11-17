// ============================================================================
// CatalogController — справочники: предметы, темы, персонажи.
//
// GET /api/subjects               -> список активных предметов (+персонаж-ведущий)
// GET /api/subjects/{code}/topics -> темы предмета (+число вопросов)
// GET /api/characters             -> карточки персонажей (лор для экранов игры)
//
// Это «читалки»: они показывают, что БД подключена и seed-данные из
// db/schema.sql доходят до клиента. На этом же можно тренировать Swagger.
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StarAcademy.Api.Data;
using StarAcademy.Api.Dtos;
using StarAcademy.Api.Models;
using System.Text.Json;

namespace StarAcademy.Api.Controllers;

[ApiController]
[Route("api")]                       // общий префикс всех маршрутов контроллера
public class CatalogController : ControllerBase
{
    private readonly StarAcademyDbContext _db;

    // DI: ASP.NET Core сам создаст DbContext и передаст сюда (в Program.cs
    // зарегистрировано AddDbContext). Никогда не пишем `new StarAcademyDbContext()`.
    public CatalogController(StarAcademyDbContext db) => _db = db;

    /// <summary>Список предметов с ведущими персонажами.</summary>
    [HttpGet("subjects")]
    [ProducesResponseType(typeof(List<SubjectDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSubjects()
    {
        var subjects = await _db.Subjects
            .Where(s => s.IsActive)
            .Include(s => s.Character)                    // JOIN с characters
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        var dtos = subjects.Select(s => new SubjectDto(
            s.Id, s.Code, s.Title, s.Description, s.Icon, ToDto(s.Character)
        )).ToList();

        return Ok(dtos);
    }

    /// <summary>Темы предмета по его коду (math / ru / en).</summary>
    [HttpGet("subjects/{code}/topics")]
    public async Task<IActionResult> GetTopics(string code)
    {
        var subject = await _db.Subjects
            .Include(s => s.Topics.Where(t => t.IsActive))     // фильтр на уровне JOIN
            .ThenInclude(t => t.Questions.Where(q => q.IsActive))
            .FirstOrDefaultAsync(s => s.Code == code && s.IsActive);

        if (subject is null)
            return NotFound(new ErrorResponse($"Предмет '{code}' не найден"));

        var dtos = subject.Topics
            .OrderBy(t => t.SortOrder)
            .Select(t => new TopicDto(
                t.Id, t.Code, t.Title, t.Description, t.SubjectId,
                t.Questions.Count(q => q.IsActive)))           // бейдж «12 задач» на фронте
            .ToList();

        return Ok(dtos);
    }

    /// <summary>Карточки персонажей — для экрана знакомства и реплик в игре.</summary>
    [HttpGet("characters")]
    public async Task<IActionResult> GetCharacters()
    {
        var chars = await _db.Characters.OrderBy(c => c.Id).ToListAsync();

        var dtos = chars.Select(c => new CharacterCardDto(
            c.Code, c.Name, c.Role, c.Description, c.AvatarUrl,
            ParseGreetings(c.GreetingTpls))).ToList();

        return Ok(dtos);
    }

    // ---------- приватные хелперы маппинга сущность -> DTO ----------

    /// <summary>
    /// jsonb-колонка приходит из EF как строка ('["привет","погнали"]').
    /// Парсим её в string[]. Если JSON битый — отдаём пустой массив, а не падаем:
    /// справочник не должен ронять весь API.
    /// </summary>
    private static string[] ParseGreetings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static CharacterDto? ToDto(Character? c) => c is null ? null :
        new CharacterDto(c.Id, c.Code, c.Name, c.Role, c.Description, c.AvatarUrl,
                         ParseGreetings(c.GreetingTpls));
}