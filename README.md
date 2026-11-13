# archive1337

Розумний захищений архіватор (проєкт 2). Мова: C#, .NET 10.

## Збірка і тести

```
dotnet build
dotnet test tests/SA.Tests
```

Звіт про покриття (як у CI):

```
dotnet test tests/SA.Tests --collect:"XPlat Code Coverage" --results-directory ./coverage
```

## Структура

| Проєкт | Зона | Зміст |
| --- | --- | --- |
| SA.Core | спільне | `IBlockCompressor`, `ArchiveException` |
| SA.Compression | П1 | біти, відстані LZ77, Гаффман, LZ77 |
| SA.Format | П2 | контейнер, заголовок тому, таблиця файлів |
| SA.Packing | П2 | рюкзак (ДП і жадібний), пакування в томи |
| SA.Crypto | П2 | PBKDF2, AES-256-CTR, HMAC-SHA256 |
| SA.Analysis | П3 | ознаки, перцептрон, предиктор |
| SA.Optimization | П3 | генетичний алгоритм |
| SA.Attack | П3 | атака на слабкий шифр |
| SA.Benchmark | усі | вимірювання Q |
| SA.Cli | усі | командний рядок |

## Правила

- Гілки `feature/*` або `pN/*`, у `main` лише через PR з ревʼю.
- zlib, lzma, `System.IO.Compression` у тракті заборонені (лише як еталон для порівняння).
- Згенерований код декларується в META-USAGE.

## Дані викладача

Із `out_p2_reference_T01.zip`:

- `team_config.json` лежить у корені репозиторію (справжній файл від викладача);
- папку `team/reference` покладіть у `reference/` або `tests/data/reference/`: тести знаходять її самі
  (без неї відповідні тести позначаються як Inconclusive);
- папку `team/corpus` покладіть у `corpus/` для замірів.

`train/` і `valid/` (близько 4 МБ) у git краще не додавати, а тримати локально.

## Командний рядок

```
dotnet run --project src/SA.Cli -- --config team_config.json pack    out  algo2026 file1 file2
dotnet run --project src/SA.Cli -- --config team_config.json unpack  out2 algo2026 out/archive.001.arc ...
dotnet run --project src/SA.Cli -- --config team_config.json measure corpus algo2026 [dp|greedy]
```

`measure` виводить B_orig, B_arc, час, S, T, Q і порівняння коефіцієнта стиснення з опорним пакувальником
(128968 байтів на 329903, коефіцієнт 2.558).
