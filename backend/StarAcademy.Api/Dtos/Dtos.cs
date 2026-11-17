// ============================================================================
// DTO (Data Transfer Objects) — «витрина» API.
//
// ВАЖНАЯ ИДЕЯ: наружу (в JSON) мы НИКОГДА не отдаём сущности EF Core целиком.
// Причины:
//   1. Безопасность: в User есть PinHash — он не должен улетать клиенту.
//      В Question есть CorrectAnswer и IsCorrect у Answer — ребёнок сможет
//      «подсмотреть» правильные ответы через DevTools. Поэтому для теста
//      делаем отдельный «безопасный» DTO без этих полей.
//   2. Стабильность контракта: изменим структуру БД — а JSON-формат фронта
//      останется прежним (между DTO и сущностями пишем маппинг вручную).
//
// record vs class: record — неизменяемый тип с приятным синтаксисом, идеален
// для «кусочков данных», которые только передаём.
// ============================================================================

namespace StarAcademy.Api.Dtos;

// ---------- Общие ----------

/// <summary>Ответ ошибки в едином формате.</summary>
public record ErrorResponse(string Error);

// ---------- Предметы / темы ----------

/// <summary>Предмет на экране выбора («Математика», иконка, ведущий персонаж).</summary>
public record SubjectDto(int Id, string Code, string Title, string? Description,
                         string? Icon, CharacterDto? Guide);

/// <summary>Персонаж (лор). Приветствия парсим из jsonb в список.</summary>
public record CharacterDto(int Id, string Code, string Name, string? Role,
                           string? Description, string? AvatarUrl, string[] Greetings);

/// <summary>Тема внутри предмета + количество доступных вопросов (для бейджа).</summary>
public record TopicDto(int Id, string Code, string Title, string? Description,
                       int SubjectId, int QuestionCount);

// ---------- Персонажи ----------

/// <summary>Карточка персонажа для экрана знакомства/диалогов.</summary>
public record CharacterCardDto(string Code, string Name, string? Role,
                               string? Description, string? AvatarUrl,
                               string[] Greetings);

// ---------- Тест (Шаг 3 будет расширять эти DTO) ----------

/// <summary>
/// БЕЗОПАСНЫЙ вопрос для клиента: ни correct_answer, ни is_correct у вариантов!
/// metadata отдаём как объект, чтобы фронт мог рендерить спец-типы (ordering и т.п.).
/// </summary>
public record QuestionForTestDto(
    long Id,
    string Type,                 // single_choice | number_input | translation | ...
    string Prompt,
    System.Text.Json.JsonElement Metadata,
    short Difficulty,
    short Points,
    QuestionOptionDto[] Options  // пустой массив для открытых вопросов
);

/// <summary>Вариант ответа закрытого вопроса (без флага правильности).</summary>
public record QuestionOptionDto(long Id, string Text);

/// <summary>Запрос: начать тест по теме.</summary>
public record StartTestRequest(int UserId, string TopicCode, int Count = 5);

/// <summary>Ответ: сгенерированный набор вопросов + реплика персонажа.</summary>
public record TestStartDto(string TopicCode, string TopicTitle,
                           CharacterDto Guide, QuestionForTestDto[] Questions);

/// <summary>Запрос проверки одного ответа.</summary>
public record SubmitAnswerRequest(
    int UserId,
    long QuestionId,
    string AnswerGiven,          // текст/число или id выбранного варианта
    int TimeSpentMs = 0
);

/// <summary>Результат проверки: правда ли, сколько пыли дали, что сказать.</summary>
public record SubmitAnswerResultDto(
    bool IsCorrect,
    int StarsEarned,
    int TotalStarDust,           // баланс после начисления — фронт обновит HUD
    string? CorrectAnswer,       // показываем ТОЛЬКО после ответа
    string? Explanation,         // разбор от персонажа
    string FeedbackPhrase        // короткая реплика («Бип-буп! Точно!»)
);

// ---------- Хинты / правила ----------

/// <summary>Контент кнопки «Правило/Хинт».</summary>
public record HintDto(string Kind, string ShortText, string? ArticleUrl,
                      string? SpeakerCharacter);

// ---------- Магазин ----------

/// <summary>Товар в магазине (эффект отдаём как jsonb-объект).</summary>
public record ShopItemDto(int Id, string Code, string Name, string? Description,
                          string? ImageUrl, int CostDust, string ItemKind,
                          int RequiresLevel, bool Owned);

/// <summary>Результат покупки.</summary>
public record PurchaseResultDto(bool Success, string Message,
                                int TotalStarDust, int ShipLevel);