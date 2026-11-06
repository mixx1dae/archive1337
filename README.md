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
