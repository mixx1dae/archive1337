# archive1337: розумний захищений архіватор (проєкт 2)

C#, .NET 10. Архів складається з томів: заголовок 41 байт (відкритий), зашифроване навантаження (AES-256-CTR) і тег HMAC-SHA256.
Блоки стискаються (Гаффман + LZ77), розкладаються по томах задачею про рюкзак, шифруються й підписуються.

## Вимоги

- .NET 10 SDK (`dotnet --version` має показувати 10.x)
- для скриптів у `tools/`: Python 3 і `pip install cryptography` (необов'язково)

## Збірка і тести

```
dotnet build
dotnet test tests/SA.Tests
```

Після `dotnet test` усі тести мають пройти. Тести, що перевіряють справжні еталони викладача, шукають папку `reference/` (у корені репозиторію або в `tests/data/reference/`); без неї вони позначаються як пропущені (Inconclusive).

Звіт про покриття (як у CI):

```
dotnet test tests/SA.Tests --collect:"XPlat Code Coverage" --results-directory ./coverage
```

## Дані викладача

| Що | Куди покласти | Для чого |
|---|---|---|
| `team_config.json` | корінь репозиторію (вже є) | параметри команди: розмір тому, блока, пріоритети, Q, пароль |
| `team/reference/` | `reference/` | еталонні й пошкоджені архіви для тестів |
| `team/corpus/` | `corpus/` | корпус для заміру Q |
| `team/train/`, `team/valid/` | локально, не в git (≈ 4 МБ) | навчання класифікатора |

## Швидкий старт: створити архів і розпакувати

```
dotnet run --project src/SA.Cli -- --config team_config.json pack   out  algo2026 corpus/f0000.txt corpus/f0001.py
dotnet run --project src/SA.Cli -- --config team_config.json unpack out2 algo2026 out/archive.001.arc
```

`out2` має містити ті самі файли, байт у байт. Пароль `algo2026` береться з `team_config.json`.

## Вимір якості (Q) і таблиці звіту

```
dotnet run --project src/SA.Cli -- --config team_config.json measure  corpus algo2026 [dp|greedy|sequential]
dotnet run --project src/SA.Cli -- --config team_config.json ablation corpus algo2026
```

`measure` друкує B_orig, B_arc, час, S, T, Q і порівняння з опорним пакувальником викладача.
`ablation` друкує таблицю «вплив кожного блока» у Markdown.

## Перевірка цілісності

```
dotnet run --project src/SA.Cli -- --config team_config.json attack-integrity 300 algo2026 out/archive.001.arc
```

Змінює байти тому й перевіряє, що розархіватор усе відхиляє (прийнято має бути 0).

## Структура

| Проєкт | Зона | Зміст |
|---|---|---|
| `SA.Core` | спільне | `IBlockCompressor`, `IFileClassifier`, `ArchiveException` (коди помилок), `TeamConfig` |
| `SA.Compression` | П1 | біти, відстані LZ77, канонічний Гаффман, декодер блока |
| `SA.Format` | П2 | заголовок тому, навантаження, читання томів, CRC32 |
| `SA.Crypto` | П2 | PBKDF2, AES-256-CTR, HMAC-SHA256 |
| `SA.Packing` | П2 | рюкзак (ДП, жадібний, послідовний), пакування в томи, збирач архіву |
| `SA.Analysis` | П3 | ознаки, перцептрон, предиктор |
| `SA.Optimization` | П3 | генетичний алгоритм |
| `SA.Attack` | П2, П3 | перевірка цілісності, атака на слабкий шифр |
| `SA.Benchmark` | усі | метрика Q, вимір на корпусі, таблиця впливу, пристосованість для ГА |
| `SA.Cli` | усі | командний рядок |

## Документи

- `docs/P2_EXPLAINED.md`: як працює формат, захист і рюкзак (для тих, хто починає з нуля).
- `docs/REPORT_P2_checkpoint2.md`, `docs/REPORT_P2_checkpoint3.md`: звіти зони П2.
- `docs/WEEK12_CROSS_TEST.md`: інструкція крос-тесту з іншою командою.
- `docs/DEFENSE_P2.md`: підготовка до захисту (E1, E2, E3).
- `docs/META-USAGE_P2.md`: шаблон декларації використання ШІ.
- `tools/ref_decoder.py`: незалежний декодер архіву (Python) для звірки.
- `tools/p2_experiments.py`: відтворює числа звітів.

## Правила

- Гілки `feature/*` або `pN/*`, у `main` лише через PR з ревʼю.
- zlib, lzma, `System.IO.Compression` у тракті заборонені (лише як еталон для порівняння, з позначкою).
- Згенерований код декларується у `META-USAGE.md`.
