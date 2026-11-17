// ============================================================================
// StarAcademyDbContext — подключение EF Core к PostgreSQL (Npgsql-провайдер)
//
// Ключевые моменты:
//  1. Имена таблиц заданы атрибутом [Table("snake_case")] в Models/Entities.cs,
//     поэтому модель явно совпадает с db/schema.sql.
//  2. Колонки jsonb мапятся на string: Npgsql умеет писать string в jsonb,
//     если указать column type "jsonb".
//  3. Мы работаем по схеме «database-first»: SQL-скрипт создаёт БД, а миграции
//     EF на старте НЕ делаем. Позже можно перейти на миграции командой
//     `dotnet ef migrations add Initial`.
// ============================================================================

using Microsoft.EntityFrameworkCore;
using StarAcademy.Api.Models;

namespace StarAcademy.Api.Data;

public class StarAcademyDbContext : DbContext
{
    // Конструктор принимает настройки (строка подключения и провайдер),
    // ASP.NET Core внедрит их через DI-контейнер в Program.cs.
    public StarAcademyDbContext(DbContextOptions<StarAcademyDbContext> options)
        : base(options) { }

    // По одному DbSet на каждую таблицу — через них делаем запросы к БД.
    public DbSet<User> Users => Set<User>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<Hint> Hints => Set<Hint>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Answer> Answers => Set<Answer>();
    public DbSet<UserProgress> UserProgress => Set<UserProgress>();
    public DbSet<ShopItem> ShopItems => Set<ShopItem>();
    public DbSet<UserInventoryItem> UserInventory => Set<UserInventoryItem>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        // --- jsonb-колонки: говорим PostgreSQL, что это именно jsonb ----------
        mb.Entity<Character>().Property(c => c.GreetingTpls).HasColumnType("jsonb");
        mb.Entity<Question>().Property(q => q.Metadata).HasColumnType("jsonb");
        mb.Entity<Question>().Property(q => q.AnswerRules).HasColumnType("jsonb");
        mb.Entity<Answer>().Property(a => a.Extra).HasColumnType("jsonb");
        mb.Entity<ShopItem>().Property(s => s.Effect).HasColumnType("jsonb");

        // --- уникальные индексы -----------------------------------------------
        mb.Entity<User>().HasIndex(u => u.Username).IsUnique();
        mb.Entity<Character>().HasIndex(c => c.Code).IsUnique();
        mb.Entity<Subject>().HasIndex(s => s.Code).IsUnique();
        mb.Entity<Topic>().HasIndex(t => t.Code).IsUnique();
        mb.Entity<ShopItem>().HasIndex(s => s.Code).IsUnique();
        mb.Entity<UserInventoryItem>()
          .HasIndex(i => new { i.UserId, i.ItemId }).IsUnique(); // купить дважды нельзя

        // --- связи (FK) со стратегиями удаления -------------------------------
        // Cascade: удалили тему — удалились её вопросы и подсказки.
        mb.Entity<Question>()
          .HasOne(q => q.Topic).WithMany(t => t.Questions)
          .HasForeignKey(q => q.TopicId).OnDelete(DeleteBehavior.Cascade);

        mb.Entity<Answer>()
          .HasOne(a => a.Question).WithMany(q => q.Answers)
          .HasForeignKey(a => a.QuestionId).OnDelete(DeleteBehavior.Cascade);

        mb.Entity<Hint>()
          .HasOne(h => h.Topic).WithMany(t => t.Hints)
          .HasForeignKey(h => h.TopicId).OnDelete(DeleteBehavior.Cascade);

        // SetNull: удалили вопрос — история ответов останется (question_id = NULL).
        mb.Entity<UserProgress>()
          .HasOne(p => p.Question).WithMany()
          .HasForeignKey(p => p.QuestionId).OnDelete(DeleteBehavior.SetNull);

        // Тема-родитель: удалили — дети останутся, parent_id станет NULL.
        mb.Entity<Topic>()
          .HasOne(t => t.Parent).WithMany(t => t.Children)
          .HasForeignKey(t => t.ParentId).OnDelete(DeleteBehavior.SetNull);

        // Предмет <-> персонаж: удалили персонажа — предмет живёт дальше.
        mb.Entity<Subject>()
          .HasOne(s => s.Character).WithMany(c => c.Subjects)
          .HasForeignKey(s => s.CharacterId).OnDelete(DeleteBehavior.SetNull);

        // Магазин <-> инвентарь.
        mb.Entity<UserInventoryItem>()
          .HasOne(i => i.User).WithMany(u => u.Inventory)
          .HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);

        mb.Entity<UserInventoryItem>()
          .HasOne(i => i.Item).WithMany()
          .HasForeignKey(i => i.ItemId).OnDelete(DeleteBehavior.Cascade);

        // Аксессуар -> персонаж-владелец.
        mb.Entity<ShopItem>()
          .HasOne(s => s.TargetCharacter).WithMany()
          .HasForeignKey(s => s.TargetCharacterId).OnDelete(DeleteBehavior.SetNull);

        // Хинт -> персонаж, который его произносит.
        mb.Entity<Hint>()
          .HasOne(h => h.Character).WithMany()
          .HasForeignKey(h => h.CharacterId).OnDelete(DeleteBehavior.SetNull);

        // CHECK difficulty BETWEEN 1 AND 5 (в schema.sql уже есть; дублируем для
        // будущих миграций EF):
        mb.Entity<Question>().ToTable(t =>
            t.HasCheckConstraint("ck_questions_difficulty", "\"difficulty\" BETWEEN 1 AND 5"));
    }
}