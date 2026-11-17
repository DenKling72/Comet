-- ============================================================================
-- Звёздная Академия — образовательная игра для Германа
-- Шаг 1: Схема базы данных PostgreSQL 16
--
-- Принципы проектирования:
--  1. Экстенсивность: новые предметы/темы/типы вопросов добавляются ДАННЫМИ,
--     а не кодом. Всё специфичное живёт в jsonb (metadata / answer_rules / effect).
--  2. Гибрид закрытых и открытых вопросов:
--     - closed-вопросы (тест с вариантами) -> строки в таблице answers;
--     - open-вопросы (ввод текста/числа)   -> правильный ответ в questions.correct_answer.
--  3. Персонажи и магазин вынесены в справочники (characters, shop_items),
--     чтобы лор можно было расширять без изменения схемы.
-- ============================================================================

BEGIN;

-- ----------------------------------------------------------------------------
-- Расширения
-- ----------------------------------------------------------------------------
CREATE EXTENSION IF NOT EXISTS pgcrypto;  -- на будущее: gen_random_uuid(), хэши

-- ----------------------------------------------------------------------------
-- Пользователи (дети). Без паролей на MVP — вход по имени + PIN (опционально).
-- ----------------------------------------------------------------------------
CREATE TABLE users (
    id              INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username        VARCHAR(50)  NOT NULL UNIQUE,        -- детское «космическое имя» (логин)
    display_name    VARCHAR(100) NOT NULL,               -- как обращаемся к ребёнку
    pin_hash        VARCHAR(255),                        -- хэш PIN-кода (может быть NULL)
    star_dust       INT          NOT NULL DEFAULT 0      -- «Звёздная пыль» — игровая валюта
                          CHECK (star_dust >= 0),
    ship_level      INT          NOT NULL DEFAULT 1,     -- уровень корабля (прогресс улучшений)
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    last_login_at   TIMESTAMPTZ
);

-- ----------------------------------------------------------------------------
-- Персонажи-помощники (лор игры). Справочник — расширяемый.
-- ----------------------------------------------------------------------------
CREATE TABLE characters (
    id            INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code          VARCHAR(50) NOT NULL UNIQUE,   -- 'captain_comet', 'robo_byte', 'alien_zorp'
    name          VARCHAR(100) NOT NULL,         -- 'Капитан Комета'
    role          VARCHAR(100),                  -- 'Главный герой', 'Персонаж математики'...
    description   TEXT,                          -- короткая легенда для ребёнка
    avatar_url    TEXT,                          -- ссылка на спрайт/картинку
    greeting_tpls JSONB NOT NULL DEFAULT '[]'    -- варианты приветствий («Погнали, кадет!»...)
);

-- ----------------------------------------------------------------------------
-- Справочник предметов (Математика, Русский язык, Английский язык, ...)
-- ----------------------------------------------------------------------------
CREATE TABLE subjects (
    id            INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code          VARCHAR(50)  NOT NULL UNIQUE,          -- 'math', 'ru', 'en'
    title         VARCHAR(100) NOT NULL,                 -- 'Математика'
    description   TEXT,
    icon          VARCHAR(50),                           -- имя иконки на фронте ('rocket', 'book'...)
    character_id  INT REFERENCES characters(id) ON DELETE SET NULL, -- кто «ведёт» предмет
    sort_order    INT          NOT NULL DEFAULT 0,       -- порядок отображения ребёнку
    is_active     BOOLEAN      NOT NULL DEFAULT TRUE,    -- можно «выключить» предмет без удаления
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now()
);

COMMENT ON TABLE subjects IS 'Предметы. Новый предмет = новая строка, код приложения менять не нужно.';

-- ----------------------------------------------------------------------------
-- Темы внутри предмета (Сложение, Умножение, Приоритет операций, Словарные слова).
-- parent_id позволяет строить дерево тем («Математика -> Арифметика -> Умножение»).
-- ----------------------------------------------------------------------------
CREATE TABLE topics (
    id          INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    subject_id  INT NOT NULL REFERENCES subjects(id) ON DELETE CASCADE,
    parent_id   INT REFERENCES topics(id) ON DELETE SET NULL,
    code        VARCHAR(50) NOT NULL UNIQUE,      -- 'addition_table', 'multiplication_table', ...
    title       VARCHAR(150) NOT NULL,
    description TEXT,
    min_age     SMALLINT,                         -- с какого возраста доступна
    sort_order  INT NOT NULL DEFAULT 0,
    is_active   BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE INDEX idx_topics_subject ON topics(subject_id);

-- ----------------------------------------------------------------------------
-- Правила/подсказки (кнопка «Правило/Хинт»).
-- kind: 'rule' — краткое правило + ссылка на статью; 'hint' — маленькая подсказка.
-- ----------------------------------------------------------------------------
CREATE TABLE hints (
    id           INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    topic_id     INT NOT NULL REFERENCES topics(id) ON DELETE CASCADE,
    kind         VARCHAR(20) NOT NULL DEFAULT 'rule'
                 CHECK (kind IN ('hint', 'rule')),
    short_text   TEXT NOT NULL,                   -- «Сначала умножаем, потом складываем»
    article_url  TEXT,                            -- ссылка на подробную статью с примерами
    character_id INT REFERENCES characters(id) ON DELETE SET NULL, -- кто произносит правило
    language     VARCHAR(10) NOT NULL DEFAULT 'ru', -- i18n на будущее
    sort_order   INT NOT NULL DEFAULT 0,
    is_active    BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE INDEX idx_hints_topic ON hints(topic_id);

-- ----------------------------------------------------------------------------
-- Вопросы. Универсальная таблица: question_type определяет, как интерпретировать
-- metadata и где искать правильный ответ.
--
-- Типы (question_type):
--   'single_choice' — один правильный вариант (варианты в answers.is_correct)
--   'multi_choice'  — несколько правильных вариантов
--   'text_input'    — открытый текстовый ответ (correct_answer)
--   'number_input'  — числовой ответ (correct_answer, сравнение числами)
--   'translation'   — перевод слова (EN->RU и т.п.), correct_answer + синонимы в answer_rules
--   'ordering'      — расставь по порядку (например, приоритет действий) — данные в metadata
--
-- Генерация вопросов: в metadata можно хранить шаблон {"a":2,"b":9,"op":"+"},
-- и бэкенд сможет генерировать бесконечные примеры по таблице сложения.
-- ----------------------------------------------------------------------------
CREATE TABLE questions (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    topic_id       INT  NOT NULL REFERENCES topics(id) ON DELETE CASCADE,
    question_type  VARCHAR(30) NOT NULL
                   CHECK (question_type IN
                       ('single_choice','multi_choice','text_input',
                        'number_input','translation','ordering')),
    prompt         TEXT NOT NULL,                          -- текст вопроса: «7 × 8 = ?»
    metadata       JSONB NOT NULL DEFAULT '{}'::jsonb,     -- спец. данные типа вопроса
    correct_answer TEXT,                                   -- для открытых типов; для choice = NULL
    answer_rules   JSONB NOT NULL DEFAULT '{}'::jsonb,     -- допуски проверки:
                                                            -- {"case_insensitive":true,"accept":["Москва"]}
    difficulty     SMALLINT NOT NULL DEFAULT 1 CHECK (difficulty BETWEEN 1 AND 5),
    explanation    TEXT,                                   -- разбор ответа после проверки
    points         SMALLINT NOT NULL DEFAULT 10,           -- звёздная пыль за верный ответ
    is_active      BOOLEAN NOT NULL DEFAULT TRUE,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_questions_topic ON questions(topic_id) WHERE is_active;
CREATE INDEX idx_questions_meta_gin ON questions USING gin (metadata);

-- ----------------------------------------------------------------------------
-- Варианты ответов для закрытых вопросов (single/multi choice).
-- Для открытых вопросов строк здесь нет — в этом и гибкость схемы.
-- ----------------------------------------------------------------------------
CREATE TABLE answers (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    question_id BIGINT NOT NULL REFERENCES questions(id) ON DELETE CASCADE,
    text        TEXT NOT NULL,                -- содержимое варианта («54», «apple»...)
    is_correct  BOOLEAN NOT NULL DEFAULT FALSE,
    feedback    TEXT,                         -- что сказать, если выбрал именно этот вариант
    sort_order  INT NOT NULL DEFAULT 0,
    extra       JSONB NOT NULL DEFAULT '{}'   -- картинка варианта, аудио-произношение и т.д.
);

CREATE INDEX idx_answers_question ON answers(question_id);

-- ----------------------------------------------------------------------------
-- Журнал попыток (user_progress): из него считается прогресс, статистика и
-- начисление «звёздной пыли». Храним сырые события — агрегаты пересчитываются.
-- ----------------------------------------------------------------------------
CREATE TABLE user_progress (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id       INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    topic_id      INT NOT NULL REFERENCES topics(id) ON DELETE CASCADE,
    question_id   BIGINT REFERENCES questions(id) ON DELETE SET NULL,
    is_correct    BOOLEAN NOT NULL,
    answer_given  TEXT,                        -- что реально ввёл/выбрал ребёнок
    time_spent_ms INT,                         -- скорость ответа (для адаптивности на будущее)
    stars_earned  SMALLINT NOT NULL DEFAULT 0, -- сколько пыли принёс этот ответ
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_progress_user_topic ON user_progress(user_id, topic_id);
CREATE INDEX idx_progress_created ON user_progress(created_at);

-- ----------------------------------------------------------------------------
-- Магазин: улучшения корабля и аксессуары для персонажей.
-- effect — jsonb, чтобы не плодить колонки под каждый бонус:
--   {"type":"ship_upgrade","level":2}
--   {"type":"accessory","slot":"hat","sprite":"captain_cap"}
--   {"type":"boost","param":"hint_free_count","value":3}
-- ----------------------------------------------------------------------------
CREATE TABLE shop_items (
    id                INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code              VARCHAR(50) NOT NULL UNIQUE,
    name              VARCHAR(100) NOT NULL,          -- «Турбо-двигатель», «Кепка капитана»
    description       TEXT,
    image_url         TEXT,
    cost_dust         INT NOT NULL CHECK (cost_dust >= 0),
    item_kind         VARCHAR(20) NOT NULL DEFAULT 'accessory'
                      CHECK (item_kind IN ('ship_upgrade','accessory','boost')),
    target_character_id INT REFERENCES characters(id) ON DELETE SET NULL, -- аксессуар кому
    effect            JSONB NOT NULL DEFAULT '{}'::jsonb,
    requires_level    INT NOT NULL DEFAULT 1,      -- минимальный уровень корабля
    is_active         BOOLEAN NOT NULL DEFAULT TRUE
);

-- ----------------------------------------------------------------------------
-- Инвентарь: что купил ребёнок. Уникальность = нельзя купить дважды.
-- ----------------------------------------------------------------------------
CREATE TABLE user_inventory (
    id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id      INT  NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    item_id      INT  NOT NULL REFERENCES shop_items(id) ON DELETE CASCADE,
    is_equipped  BOOLEAN NOT NULL DEFAULT FALSE, -- надет ли аксессуар сейчас
    purchased_at TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT uq_user_item UNIQUE (user_id, item_id)
);

CREATE INDEX idx_inventory_user ON user_inventory(user_id);

-- ============================================================================
-- НАЧАЛЬНОЕ НАПОЛНЕНИЕ (seed): лор, предметы, темы, правила, примеры вопросов
-- ============================================================================

INSERT INTO characters (code, name, role, description, greeting_tpls) VALUES
('captain_comet', 'Капитан Комета', 'Главный герой',
 'Храбрая командирша космической академии. Даёт квесты и следит, чтобы кадет не скучал.',
 '["Привет, кадет! Сегодня тренируемся на орбите знаний!","Звёзды ждут твоих ответов!"]'),
('robo_byte', 'Робо-Байт', 'Персонаж математики',
 'Милый робот с калькулятором вместо сердца. Обожает числа и задачки.',
 '["Бип-буп! Загружаю задачку!","Мои шестерёнки ждут правильного ответа!"]'),
('alien_zorp', 'Инопланетянин Зорп', 'Персонаж языков',
 'Забавный пришелец с универсальным переводчиком. Учил русский 300 световых лет.',
 '["Глот-глот! Переведи меня на земной!","На моей планете все знают словарные слова!"]');

-- Предметы
INSERT INTO subjects (code, title, description, icon, character_id, sort_order) VALUES
('math', 'Математика',       'Сложение, умножение и порядок действий', 'rocket',
 (SELECT id FROM characters WHERE code='robo_byte'),  1),
('ru',   'Русский язык',     'Словарные слова и правила',              'book',
 (SELECT id FROM characters WHERE code='alien_zorp'), 2),
('en',   'Английский язык',  'Словарные слова: перевод',               'globe',
 (SELECT id FROM characters WHERE code='alien_zorp'), 3);

-- Темы
INSERT INTO topics (subject_id, code, title, sort_order) VALUES
((SELECT id FROM subjects WHERE code='math'), 'addition_table',       'Таблица сложения',          1),
((SELECT id FROM subjects WHERE code='math'), 'multiplication_table', 'Таблица умножения',         2),
((SELECT id FROM subjects WHERE code='math'), 'order_of_operations',  'Приоритет операций',        3),
((SELECT id FROM subjects WHERE code='ru'),   'dictionary_words_ru',  'Словарные слова',           1),
((SELECT id FROM subjects WHERE code='en'),   'dictionary_words_en',  'Словарные слова (перевод)', 1);

-- Правила/хинты (контент кнопки «Правило/Хинт»)
INSERT INTO hints (topic_id, kind, short_text, article_url, character_id) VALUES
((SELECT id FROM topics WHERE code='addition_table'), 'rule',
 'Сложение — это когда числа «складывают в одну кучку». От перестановки сумма не меняется: 3+5 = 5+3.',
 'https://interneturok.ru/lesson/matematika/1-klass/schenie',
 (SELECT id FROM characters WHERE code='robo_byte')),
((SELECT id FROM topics WHERE code='multiplication_table'), 'rule',
 'Умножение — это сложение одинаковых чисел несколько раз: 4×3 = 4+4+4 = 12.',
 'https://interneturok.ru/lesson/matematika/2-klass/umnozhenie',
 (SELECT id FROM characters WHERE code='robo_byte')),
((SELECT id FROM topics WHERE code='order_of_operations'), 'rule',
 'Сначала выполняем действия в скобках, потом умножение и деление, и только затем сложение и вычитание.',
 'https://interneturok.ru/lesson/matematika/3-klass/poryadok-deystviy',
 (SELECT id FROM characters WHERE code='robo_byte')),
((SELECT id FROM topics WHERE code='order_of_operations'), 'hint',
 'Подсказка: найди в примере скобки — они всегда самые главные!', NULL,
 (SELECT id FROM characters WHERE code='captain_comet')),
((SELECT id FROM topics WHERE code='dictionary_words_ru'), 'rule',
 'Словарные слова нельзя проверить правилом — их нужно запомнить. Разбивай слово на части и проговаривай.',
 'https://interneturok.ru/lesson/russkiy-yazyk/2-klass/slovarnye-slova',
 (SELECT id FROM characters WHERE code='alien_zorp')),
((SELECT id FROM topics WHERE code='dictionary_words_en'), 'rule',
 'Учи слова парами: слово + картинка или перевод. Повторяй новые слова на следующий день.',
 'https://interneturok.ru/lesson/angliyskiy-yazyk',
 (SELECT id FROM characters WHERE code='alien_zorp'));

-- --- Математика: таблица сложения -------------------------------------------
INSERT INTO questions (topic_id, question_type, prompt, metadata, correct_answer, difficulty, explanation) VALUES
((SELECT id FROM topics WHERE code='addition_table'), 'number_input', '7 + 8 = ?',
 '{"a":7,"b":8,"op":"+"}', '15', 1, '7 + 8 = 15. Можно округлить: 7+3=10, ещё 5 = 15.'),
((SELECT id FROM topics WHERE code='addition_table'), 'single_choice', 'Сколько будет 6 + 9?',
 '{}', NULL, 1, '6 + 9 = 15');

INSERT INTO answers (question_id, text, is_correct, sort_order)
SELECT q.id, v.txt, v.ok, v.n
FROM questions q,
     (VALUES ('13', FALSE, 1), ('14', FALSE, 2), ('15', TRUE, 3), ('16', FALSE, 4))
       AS v(txt, ok, n)
WHERE q.prompt = 'Сколько будет 6 + 9?';

-- --- Математика: таблица умножения ------------------------------------------
INSERT INTO questions (topic_id, question_type, prompt, metadata, correct_answer, difficulty) VALUES
((SELECT id FROM topics WHERE code='multiplication_table'), 'number_input', '7 × 8 = ?',
 '{"a":7,"b":8,"op":"*"}', '56', 2),
((SELECT id FROM topics WHERE code='multiplication_table'), 'number_input', '9 × 6 = ?',
 '{"a":9,"b":6,"op":"*"}', '54', 2);

-- --- Математика: приоритет операций -----------------------------------------
INSERT INTO questions (topic_id, question_type, prompt, metadata, correct_answer, difficulty, explanation) VALUES
((SELECT id FROM topics WHERE code='order_of_operations'), 'number_input', '3 + 4 × 2 = ?',
 '{}', '11', 3, 'Сначала 4×2=8, потом 3+8=11.'),
((SELECT id FROM topics WHERE code='order_of_operations'), 'number_input', '(3 + 4) × 2 = ?',
 '{}', '14', 3, 'Сначала скобки: 3+4=7, потом 7×2=14.');

-- --- Русский язык: словарные слова ------------------------------------------
INSERT INTO questions (topic_id, question_type, prompt, metadata, correct_answer, answer_rules, difficulty) VALUES
((SELECT id FROM topics WHERE code='dictionary_words_ru'), 'text_input',
 'Впиши пропущенную букву: м_сква (город)',
 '{"word":"москва","missing":"о"}', 'о',
 '{"case_insensitive":true,"accept":["о","О"]}', 1),
((SELECT id FROM topics WHERE code='dictionary_words_ru'), 'single_choice',
 'Какое слово написано верно?', '{}', NULL, '{}', 1);

INSERT INTO answers (question_id, text, is_correct, sort_order)
SELECT q.id, v.txt, v.ok, v.n
FROM questions q,
     (VALUES ('ракета', TRUE, 1), ('рокета', FALSE, 2), ('рукета', FALSE, 3))
       AS v(txt, ok, n)
WHERE q.prompt = 'Какое слово написано верно?';

-- --- Английский язык: перевод ------------------------------------------------
INSERT INTO questions (topic_id, question_type, prompt, metadata, correct_answer, answer_rules, difficulty) VALUES
((SELECT id FROM topics WHERE code='dictionary_words_en'), 'translation', 'Переведи: apple',
 '{"lang_from":"en","lang_to":"ru"}', 'яблоко',
 '{"case_insensitive":true,"accept":["яблоко","Яблоко"]}', 1),
((SELECT id FROM topics WHERE code='dictionary_words_en'), 'single_choice',
 'Что означает слово "dog"?', '{}', NULL, '{}', 1);

INSERT INTO answers (question_id, text, is_correct, sort_order)
SELECT q.id, v.txt, v.ok, v.n
FROM questions q,
     (VALUES ('кошка', FALSE, 1), ('собака', TRUE, 2), ('птица', FALSE, 3), ('рыба', FALSE, 4))
       AS v(txt, ok, n)
WHERE q.prompt = 'Что означает слово "dog"?';

-- --- Магазин -----------------------------------------------------------------
INSERT INTO shop_items (code, name, description, cost_dust, item_kind, target_character_id, effect, requires_level) VALUES
('engine_warp1', 'Варп-двигатель Mk.I', 'Корабль стал быстрее! +1 уровень корабля.', 100, 'ship_upgrade', NULL,
 '{"type":"ship_upgrade","level":2}', 1),
('hat_captain',  'Кепка капитана',      'Самая крутая кепка на орбите (для Капитана Кометы).', 50, 'accessory',
 (SELECT id FROM characters WHERE code='captain_comet'),
 '{"type":"accessory","slot":"hat","sprite":"captain_cap"}', 1),
('antenna_zorp', 'Блестящая антенна',   'Антенна для Инопланетянина Зорпа. Глот-глот, красиво!', 40, 'accessory',
 (SELECT id FROM characters WHERE code='alien_zorp'),
 '{"type":"accessory","slot":"head","sprite":"antenna"}', 1),
('oil_robo',     'Смазка «Супер-Блеск»','Робо-Байт будет блестеть на солнце.', 30, 'accessory',
 (SELECT id FROM characters WHERE code='robo_byte'),
 '{"type":"accessory","slot":"body","sprite":"shine_oil"}', 1),
('boost_hint3',  '3 бесплатных хинта',  'Подсказки от Капитана Кометы бесплатно.', 60, 'boost', NULL,
 '{"type":"boost","param":"hint_free_count","value":3}', 1);

COMMIT;