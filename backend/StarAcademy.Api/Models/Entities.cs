// ============================================================================
// Звёздная Академия — EF Core модели (сущности БД)
// Файл: Models/Entities.cs
//
// Как это связано со схемой PostgreSQL из db/schema.sql:
//  - Каждой таблице соответствует класс. Имена таблиц зададим в DbContext
//    через ToTable("snake_case") или атрибутом [Table].
//  - Колонки jsonb мапятся на строку (string) + ValueComparer, либо на
//    сильные типы через конвертер (пока храним как JSON-строку — проще для старта).
//  - Навигационные свойства (List<...>, ссылки) — это «мосты» между таблицами,
//    EF Core сам подставит нужные JOIN'ы и внешние ключи.
// ============================================================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StarAcademy.Api.Models;

/// <summary>Ребёнок-игрок. Таблица users.</summary>
[Table("users")]
public class User
{
    [Key]
    public int Id { get; set; }

    /// <summary>Логин («космическое имя»), уникален.</summary>
    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    /// <summary>Имя, к которому обращаемся в игре.</summary>
    [Required, MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Хэш PIN-кода. NULL — вход без PIN (MVP).</summary>
    public string? PinHash { get; set; }

    /// <summary>«Звёздная пыль» — игровая валюта за правильные ответы.</summary>
    public int StarDust { get; set; }

    /// <summary>Уровень корабля (растёт через улучшения из магазина).</summary>
    public int ShipLevel { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    // Навигации: один пользователь — много попыток и предметов инвентаря.
    public ICollection<UserProgress> Progress { get; set; } = new List<UserProgress>();
    public ICollection<UserInventoryItem> Inventory { get; set; } = new List<UserInventoryItem>();
}

/// <summary>Персонаж-помощник (лор). Таблица characters.</summary>
[Table("characters")]
public class Character
{
    [Key]
    public int Id { get; set; }

    /// <summary>Машинный код: captain_comet / robo_byte / alien_zorp.</summary>
    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Role { get; set; }

    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }

    /// <summary>JSON-массив приветствий: ["Бип-буп! Загружаю задачку!", ...].</summary>
    public string GreetingTpls { get; set; } = "[]"; // jsonb -> string

    public ICollection<Subject> Subjects { get; set; } = new List<Subject>();
}

/// <summary>Предмет (Математика, Русский, Английский). Таблица subjects.</summary>
[Table("subjects")]
public class Subject
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;   // 'math', 'ru', 'en'

    [Required, MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Icon { get; set; }                  // имя иконки на фронте

    /// <summary>Персонаж, который «ведёт» предмет.</summary>
    public int? CharacterId { get; set; }
    public Character? Character { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Topic> Topics { get; set; } = new List<Topic>();
}

/// <summary>Тема внутри предмета. Таблица topics. parent_id — дерево тем.</summary>
[Table("topics")]
public class Topic
{
    [Key]
    public int Id { get; set; }

    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;

    /// <summary>Родительская тема (nullable — верхний уровень).</summary>
    public int? ParentId { get; set; }
    public Topic? Parent { get; set; }
    public ICollection<Topic> Children { get; set; } = new List<Topic>();

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;   // 'multiplication_table' и т.д.

    [Required, MaxLength(150)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }
    public short? MinAge { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Question> Questions { get; set; } = new List<Question>();
    public ICollection<Hint> Hints { get; set; } = new List<Hint>();
}

/// <summary>Правило/подсказка для кнопки «Правило/Хинт». Таблица hints.</summary>
[Table("hints")]
public class Hint
{
    [Key]
    public int Id { get; set; }

    public int TopicId { get; set; }
    public Topic Topic { get; set; } = null!;

    /// <summary>'rule' — правило + статья; 'hint' — короткая подсказка.</summary>
    [Required, MaxLength(20)]
    public string Kind { get; set; } = "rule";

    /// <summary>Краткий текст: «Сначала умножаем, потом складываем».</summary>
    [Required]
    public string ShortText { get; set; } = string.Empty;

    /// <summary>Ссылка на подробную статью с примерами.</summary>
    public string? ArticleUrl { get; set; }

    /// <summary>Кто из персонажей «произносит» правило.</summary>
    public int? CharacterId { get; set; }
    public Character? Character { get; set; }

    [MaxLength(10)]
    public string Language { get; set; } = "ru";

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Вопрос. Универсальная таблица questions — тип определяет поведение фронта
/// и способ проверки. Специфика типов живёт в Metadata (jsonb).
/// </summary>
[Table("questions")]
public class Question
{
    [Key]
    public long Id { get; set; }

    public int TopicId { get; set; }
    public Topic Topic { get; set; } = null!;

    /// <summary>См. enum QuestionType — храним строкой для расширяемости БД.</summary>
    [Required, MaxLength(30)]
    public string QuestionType { get; set; } = QuestionTypes.NumberInput;

    /// <summary>Текст вопроса: «7 × 8 = ?».</summary>
    [Required]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// jsonb со спецификой типа:
    ///  number_input: {"a":7,"b":8,"op":"*"} — шаблон для генератора вопросов;
    ///  translation : {"lang_from":"en","lang_to":"ru"};
    ///  ordering    : {"items":[...], "correct_order":[...]}.
    /// </summary>
    public string Metadata { get; set; } = "{}";

    /// <summary>Правильный ответ для открытых типов. Для choice — NULL.</summary>
    public string? CorrectAnswer { get; set; }

    /// <summary>Правила проверки: {"case_insensitive":true,"accept":["Москва"]}.</summary>
    public string AnswerRules { get; set; } = "{}";

    /// <summary>Сложность 1..5 — используется генератором тестов.</summary>
    public short Difficulty { get; set; } = 1;

    /// <summary>Разбор ответа, показываем после проверки.</summary>
    public string? Explanation { get; set; }

    /// <summary>Сколько звёздной пыли даёт верный ответ.</summary>
    public short Points { get; set; } = 10;

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Варианты ответа (только для закрытых типов).</summary>
    public ICollection<Answer> Answers { get; set; } = new List<Answer>();
}

/// <summary>Строки-константы типов вопросов (расширяются без enum-миграций).</summary>
public static class QuestionTypes
{
    public const string SingleChoice = "single_choice";
    public const string MultiChoice  = "multi_choice";
    public const string TextInput    = "text_input";
    public const string NumberInput  = "number_input";
    public const string Translation  = "translation";
    public const string Ordering     = "ordering";
}

/// <summary>Вариант ответа закрытого вопроса. Таблица answers.</summary>
[Table("answers")]
public class Answer
{
    [Key]
    public long Id { get; set; }

    public long QuestionId { get; set; }
    public Question Question { get; set; } = null!;

    [Required]
    public string Text { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }

    /// <summary>Персональная реакция на этот вариант («Почти! Посчитай ещё раз»).</summary>
    public string? Feedback { get; set; }

    public int SortOrder { get; set; }

    /// <summary>jsonb: картинка варианта, аудио произношения и т.п.</summary>
    public string Extra { get; set; } = "{}";
}

/// <summary>Журнал ответов ребёнка. Таблица user_progress.</summary>
[Table("user_progress")]
public class UserProgress
{
    [Key]
    public long Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int TopicId { get; set; }
    public Topic Topic { get; set; } = null!;

    /// <summary>NULL допустим — событие могло относиться к сгенерированному вопросу.</summary>
    public long? QuestionId { get; set; }
    public Question? Question { get; set; }

    public bool IsCorrect { get; set; }

    /// <summary>Что реально ответил ребёнок (для статистики ошибок).</summary>
    public string? AnswerGiven { get; set; }

    /// <summary>Скорость ответа в мс — база для адаптивной сложности.</summary>
    public int? TimeSpentMs { get; set; }

    /// <summary>Сколько пыли принёс этот ответ.</summary>
    public short StarsEarned { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Товар магазина (улучшения корабля, аксессуары). Таблица shop_items.</summary>
[Table("shop_items")]
public class ShopItem
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>Цена в звёздной пыли.</summary>
    public int CostDust { get; set; }

    /// <summary>'ship_upgrade' | 'accessory' | 'boost'.</summary>
    [Required, MaxLength(20)]
    public string ItemKind { get; set; } = "accessory";

    /// <summary>Для аксессуаров — какому персонажу надевается.</summary>
    public int? TargetCharacterId { get; set; }
    public Character? TargetCharacter { get; set; }

    /// <summary>jsonb-эффект: {"type":"boost","param":"hint_free_count","value":3}.</summary>
    public string Effect { get; set; } = "{}";

    /// <summary>Минимальный уровень корабля для покупки.</summary>
    public int RequiresLevel { get; set; } = 1;

    public bool IsActive { get; set; } = true;
}

/// <summary>Инвентарь игрока. Таблица user_inventory.</summary>
[Table("user_inventory")]
public class UserInventoryItem
{
    [Key]
    public long Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int ItemId { get; set; }
    public ShopItem Item { get; set; } = null!;

    /// <summary>Надет ли аксессуар прямо сейчас.</summary>
    public bool IsEquipped { get; set; }

    public DateTimeOffset PurchasedAt { get; set; } = DateTimeOffset.UtcNow;
}