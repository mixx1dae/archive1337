# archive1337: розумний захищений архіватор (проєкт 2)

C#, .NET 10. Архів складається з томів: заголовок 41 байт (відкритий), зашифроване навантаження (AES-256-CTR) і тег HMAC-SHA256.
Блоки стискаються (LZ77 + канонічний Гаффман), розкладаються по томах задачею про рюкзак, шифруються й підписуються.
Тип кожного файлу визначає перцептрон, параметри кодека підібрані генетичним алгоритмом.
Рівень складності: базовий (ДП-розбір LZ77, ДП-розбиття на блоки й предиктор байта не реалізовано).

## Вимоги

- .NET 10 SDK (`dotnet --version` має показувати 10.x)
- для скриптів у `tools/`: Python 3 і `pip install cryptography` (необов'язково)

## Збірка і тести

```
dotnet build -c Release
dotnet test tests/SA.Tests
```

Усі тести мають пройти (168). Тести, що перевіряють справжні еталони викладача, шукають папку `reference/` (у корені репозиторію або в `tests/data/reference/`); без неї вони позначаються як пропущені (Inconclusive).

Звіт про покриття (як у CI):

```
dotnet test tests/SA.Tests --collect:"XPlat Code Coverage" --results-directory ./coverage
```

Для вимірювання часу використовуйте збірку Release (`-c Release`): у Debug час сильно шумить.

## Дані викладача

| Що | Де лежить | Для чого |
|---|---|---|
| `team_config.json` | корінь репозиторію | параметри команди: розмір тому, блока, пріоритети, Q, пароль |
| `reference/` | корінь | еталонні й пошкоджені архіви для тестів |
| `corpus/` | корінь | корпус для заміру Q |
| `train/`, `valid/` | корінь (у кожній `labels.csv`) | навчання і вибір класифікатора |

`holdout/` (дає викладач наприкінці) використовується лише один раз для фінальної оцінки: на ньому не навчаємося й не підбираємо параметри.

## Швидкий старт: створити архів і розпакувати

```
dotnet run --project src/SA.Cli -- --config team_config.json pack   out  algo2026 corpus/f0000.txt corpus/f0001.py
dotnet run --project src/SA.Cli -- --config team_config.json unpack out2 algo2026 out/archive.001.arc
```

`out2` має містити ті самі файли, байт у байт (у PowerShell: `fc /b out2\f0000.txt corpus\f0000.txt`). Пароль `algo2026` береться з `team_config.json`.

## Вимір якості (Q) і таблиці звіту

```
dotnet run -c Release --project src/SA.Cli -- --config team_config.json measure  corpus algo2026 [dp|greedy|sequential]
dotnet run -c Release --project src/SA.Cli -- --config team_config.json --model perceptron.json ablation corpus algo2026 > docs/ablation.md
dotnet run -c Release --project tools/SA.CodecBench -- bench corpus 16384 3 5 128 > docs/codecbench.md
```

`measure` друкує B_orig, B_arc, час, S, T, Q і порівняння з опорним пакувальником викладача.
`ablation` друкує таблицю «вплив кожного блока» у Markdown.
`CodecBench` порівнює жадібний і лінивий розбір LZ77 по файлах (аргументи: каталог, розмір блока, повторів, `MinMatch`, `MaxChain`).

### Поточні результати (`corpus/`: 21 файл, 329 903 Б)

| Конфігурація | B_arc, Б | Коеф. стиснення | Q |
|---|---:|---:|---:|
| `CodecOptions()` за замовчуванням | 129 044 | 2,557 | 0,7995 |
| `CodecOptions.Tuned` (`MinMatch` 5, `MaxChain` 128), використовується в CLI | 127 920 | 2,579 | ≈ 0,800 |
| опорний пакувальник викладача | 128 968 | 2,558 | n/a |

macro-F1 перцептрона на `valid`: 0,9711 (помилка лише клас 6 → клас 5, 3 файли з 15). Усі файли корпусу відновлюються побайтово. Час і Q залежать від машини, розміри ні. Докладно в `docs/SmartArchiver_report.docx`.

## Навчання класифікатора і генетичний алгоритм

```
dotnet run -c Release --project src/SA.Cli -- --config team_config.json train train valid perceptron.json
dotnet run -c Release --project src/SA.Cli -- --config team_config.json --model perceptron.json evolve corpus algo2026 12 10 evolution_log.csv 5
```

`train` навчає перцептрон за протоколом із `team_config.json`, друкує macro-F1 і матрицю помилок на `valid`, зберігає ваги.
`evolve` запускає ГА (популяція, покоління, журнал, кількість повторів) і порівнює його з випадковим пошуком з тим самим бюджетом обчислень Q. Пише `evolution_log*.csv`, криві збіжності `*_convergence.csv` і `best_genes.json`.

Додаткові прапорці для `pack`, `unpack`, `measure`, `ablation`:

- `--model perceptron.json`: ваги класифікатора (без них евристичний запасний класифікатор);
- `--genes best_genes.json`: гени ГА (без них береться `CodecOptions.Tuned`).

## Перевірка цілісності

```
dotnet run --project src/SA.Cli -- --config team_config.json attack-integrity 300 algo2026 out/archive.001.arc
```

Змінює байти тому й перевіряє, що розархіватор усе відхиляє (прийнято має бути 0).

## Незалежна звірка (Python)

```
python tools/ref_decoder.py algo2026 reference/ref_01_single_file.arc
python tools/p2_experiments.py . all
```

`ref_decoder.py` розпаковує архів незалежною реалізацією (аргументи: пароль і томи). `p2_experiments.py` відтворює експерименти зони П2 (аргументи: каталог з `corpus/` і `reference/`, потім назва експерименту або `all`). Він використовує DEFLATE як замінник кодера в частинах про розміри, і такі числа позначаються як порівняльні.

## Структура

| Проєкт | Зона | Зміст |
|---|---|---|
| `SA.Core` | спільне | `IBlockCompressor`, `IFileClassifier`, `ArchiveException` (коди помилок), `TeamConfig` |
| `SA.Compression` | П1 | біти, LZ77 (жадібний і лінивий розбір), канонічний Гаффман, кодер і декодер блока, `BlockCodecCompressor` |
| `SA.Format` | П2 | заголовок тому, навантаження, читання томів, CRC32 |
| `SA.Crypto` | П2 | PBKDF2, AES-256-CTR, HMAC-SHA256 |
| `SA.Packing` | П2 | рюкзак (ДП, жадібний, послідовний), пакування в томи, збирач архіву |
| `SA.Analysis` | П3 | ознаки файлу, перцептрон (навчання, macro-F1, збереження ваг) |
| `SA.Optimization` | П3 | генетичний алгоритм (хромосома: вікно, мін. збіг, поріг ентропії) |
| `SA.Attack` | П2, П3 | перевірка цілісності, атака на слабкий шифр |
| `SA.Benchmark` | усі | метрика Q, вимір на корпусі, таблиця впливу, пристосованість для ГА |
| `SA.Cli` | усі | командний рядок: `pack`, `unpack`, `measure`, `ablation`, `attack-integrity`, `train`, `evolve` |
| `tools/SA.CodecBench` | П1 | порівняння жадібного й лінивого розбору |

## Документи

- `docs/META-USAGE.md`: декларація використання ШІ по зонах П1, П2, П3.
- `docs/ablation.md`, `docs/codecbench.md`, `docs/codecbench_default.md`: результати вимірювань для звіту.
- `FORMAT_SPEC_P2.md`, `CHANNEL_SPEC_P2.md`: специфікації формату й каналу.
