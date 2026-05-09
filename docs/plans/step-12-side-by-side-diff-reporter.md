# Step 12 — `SideBySideDiffReporter` (two-column diff)

## Context

`Structura.Reporting` после Step 11 содержит три репортёра:

- `SimpleReporter` — плоский список `/path: old → new`.
- `ConsoleDiffReporter` — git-ish per-change «mini-hunk».
- `UnifiedDiffReporter` — unified diff в стиле Claude Code: банер, gutter, `-`/`+` сигилы, ANSI-фоны, inline-highlight, эллипсис между хунками.

Четвёртый вид — **side-by-side** (два столбца: слева OLD, справа NEW) — удобен, когда нужно
сравнивать «кадр в кадр» близко расположенные значения, не переводя взгляд между двумя строками
unified-вывода. Цветовая схема и логика хунков переиспользуются из Unified.

## Scope

**In scope:**
- Новый `SideBySideDiffReporter` (public static) рядом с тремя существующими.
- `SideBySideDiffOptions` (public sealed record).
- Pairing-слой `SideBySideRowBuilder` поверх существующего `DiffHunkBuilder`.
- Per-row рендерер `SideBySideRowRenderer`.
- Извлечение общего банера в `Internal/DiffBanner.cs` (используется и Unified, и SBS).
- Рефакторинг `DiffLine`: одиночный `LineNumber` → `OldLineNumber + NewLineNumber`. Сборка
  обоих полей в `DiffHunkBuilder`. `DiffLineRenderer` (Unified) переключается на нужное по `Kind`
  — байт-в-байт прежнее поведение Unified.
- Расширение демо в `Program.cs`: после `=== Diff (UnifiedDiffReporter) ===` добавить
  `=== Diff (SideBySideDiffReporter) ===` для всех сэмплов (order/blrwbl/library.xml/library.json).
- Unit + integration + color-tests.

**Out of scope:**
- Word-level diff внутри ячейки (Myers/LCS). Inline highlight использует точные span-координаты
  из `DocumentChange`, как и Unified.
- Soft-wrap длинного контента. Только truncation.
- Подсветка пустой ячейки тильдой/точками или dim-фоном. Пустая клетка = просто пробелы.
- Custom themes / 16-color fallback.
- Multi-document отчёт.
- Удаление или `[Obsolete]` пометка существующих репортёров.

## Coding Principles

- **Переиспользование, не дублирование.** `DiffHunkBuilder` уже корректно строит хунки,
  context-границы, inline highlights — SBS строит свою репрезентацию **на основе** его выхода,
  не дублируя алгоритм. Палитра — `AnsiPalette` без новых констант.
- **SRP.** Три новых внутренних слоя, каждый с одной ответственностью: `SideBySideRowBuilder`
  (pairing), `SideBySideRowRenderer` (одна строка вывода), `DiffBanner` (общий банер).
- **Без feature-flags.** В `SideBySideDiffOptions` ровно три опции (`ContextLines`,
  `InlineHighlight`, `TotalWidth`). Остальное захардкожено.
- **No nested calls in arguments.** Промежуточные значения — именованные локальные
  переменные (правило C#-style проекта).
- **Параллель Unified API.** `Print(doc)` → `Console.Out` с цветом на TTY; `Print(doc, writer)`
  и `Print(doc, writer, options)` всегда plain. Полная симметрия.

## Critical files

**Изменяются:**
- `src/Structura.Reporting/Internal/DiffLine.cs` — заменить `int LineNumber` на пару
  `int OldLineNumber, int NewLineNumber`.
- `src/Structura.Reporting/Internal/DiffHunkBuilder.cs` — заполнять оба поля по правилам §5.
- `src/Structura.Reporting/Internal/DiffLineRenderer.cs` — выбирать номер по `Kind` (см. §5).
- `src/Structura.Reporting/UnifiedDiffReporter.cs` — `WriteBanner(...)` удаляется, на его месте
  вызов `DiffBanner.Write(...)`. `maxLineNumber` подсчёт обновлён (берёт `max(Old, New)`).
- `src/Structura/Program.cs` — после каждого блока `UnifiedDiffReporter.Print(...)` добавить
  блок `=== Diff (SideBySideDiffReporter) ===` + вызов SBS.

**Создаются:**
- `src/Structura.Reporting/SideBySideDiffReporter.cs` — public static entry-point.
- `src/Structura.Reporting/SideBySideDiffOptions.cs` — public sealed record.
- `src/Structura.Reporting/Internal/SideBySideRow.cs` — `(DiffLine? Left, DiffLine? Right)`
  record struct.
- `src/Structura.Reporting/Internal/SideBySideRowBuilder.cs` — internal, плоский
  `IReadOnlyList<DiffLine>` → `IReadOnlyList<SideBySideRow>`.
- `src/Structura.Reporting/Internal/SideBySideRowRenderer.cs` — internal, рендерит одну строку.
- `src/Structura.Reporting/Internal/DiffBanner.cs` — internal, общий банер для Unified и SBS.
- `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterTests.cs` — plain-output тесты.
- `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterColorTests.cs` — ANSI-тесты.
- `tests/Structura.IntegrationTests/Reporting/OrderSampleJsonSideBySideDiffTests.cs` —
  integration на `order.sample.json`.

**Эталон, читать не трогая:**
- `src/Structura.Reporting/UnifiedDiffReporter.cs` — структура `RenderTo`, `Print`-перегрузки.
- `src/Structura.Reporting/Internal/DiffLineRenderer.cs` — паттерн рендеринга с inline-highlight,
  логика клипа диапазонов внутри строки.
- `src/Structura.Reporting/Internal/AnsiPalette.cs` — палитра целиком переиспользуется.
- `tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs` + `FakeStructuraDocument` —
  тестовая инфраструктура.

## Design

### 1. API surface

```csharp
namespace Structura.Reporting;

public static class SideBySideDiffReporter
{
    public static void Print(IStructuraDocument document);
    public static void Print(IStructuraDocument document, TextWriter writer);
    public static void Print(IStructuraDocument document, TextWriter writer, SideBySideDiffOptions options);
}

public sealed record SideBySideDiffOptions
{
    public int ContextLines { get; init; } = 3;
    public bool InlineHighlight { get; init; } = true;

    /// <summary>
    /// Total output width (both columns + gutters + separator). When <c>null</c>:
    /// <see cref="Console.WindowWidth"/> if available, otherwise 160.
    /// </summary>
    public int? TotalWidth { get; init; } = null;
}
```

Поведение цвета — паритет с `UnifiedDiffReporter`:
- `Print(doc)` → `Console.Out`, цвет только если `!Console.IsOutputRedirected`.
- `Print(doc, writer)` и `Print(doc, writer, options)` — всегда plain.

### 2. Refactor: `DiffLine.LineNumber` → `OldLineNumber + NewLineNumber`

```csharp
internal readonly record struct DiffLine(
    DiffLineKind Kind,
    int OldLineNumber,   // 0 если N/A
    int NewLineNumber,   // 0 если N/A
    string Content,
    IReadOnlyList<ColumnRange> InlineHighlights);
```

Заполнение в `DiffHunkBuilder.EmitHunk`:

| `Kind` | `OldLineNumber` | `NewLineNumber` |
|---|---|---|
| `Context` (pre/post/inter-change) | OLD-номер строки в `oldLines` (1-based) | NEW-номер той же строки после применения cumulative delta |
| `Removed` | `i + 1` (OLD) | `0` |
| `Added` | `0` | `i + 1` (NEW) |
| `HunkSeparator` | `0` | `0` |

`DiffLineRenderer` (Unified) выбирает номер для gutter:
```csharp
int gutterValue = line.Kind switch
{
    DiffLineKind.Removed => line.OldLineNumber,
    _                    => line.NewLineNumber, // Context, Added
};
```
Поведение Unified — байт-в-байт прежнее (для Context unified всегда показывал NEW, что совпадает).

`UnifiedDiffReporter.maxLineNumber` обновляется:
```csharp
int candidate = Math.Max(line.OldLineNumber, line.NewLineNumber);
if (candidate > maxLineNumber) { maxLineNumber = candidate; }
```

### 3. Banner extraction → `Internal/DiffBanner.cs`

Метод `WriteBanner` из `UnifiedDiffReporter` целиком переезжает в новый `DiffBanner`:

```csharp
internal static class DiffBanner
{
    public static void Write(
        TextWriter writer,
        string documentName,
        int additions,
        int removals,
        bool useColor,
        bool useUnicode);
}
```

Поведение **не меняется** — существующий тест `Print_DocumentNameFromFake_AppearsInBanner`
из Unified-набора по-прежнему проходит; SBS использует ту же реализацию. Память про
`"Patched"` wording соблюдена — формулировки не трогаем.

### 4. Layout

```
● Patched(order.sample.json)
  └ Patched order.sample.json with 8 additions and 8 removals

   5     "shipped": false,                        │   5     "shipped": false,
   6 -   "is_priority": true,                     │   6 +   "is_priority": false,
   7 -   "version": 7,                            │   7 +   "version": 42,
   8     "ts": "2025-01-01"                       │   8     "ts": "2025-01-01"
                  …                               │                  …
  14         "first_name": "John",                │  14         "first_name": "Ivan",
```

Формат одной row-строки:
```
{leftCell}{separator}{rightCell}
```
где
- `{leftCell}` = `{OldNum:>W} {sigil} {leftContent:Wcol}` (с возможной заливкой row-bg).
- `{rightCell}` = `{NewNum:>W} {sigil} {rightContent:Wcol}`.
- `{separator}` = ` │ ` (Unicode) или ` | ` (ASCII fallback) **строго 3 символа**, на TTY обёрнут
  `AnsiPalette.Dim` / `DimOff`.
- `W` = ширина gutter = число цифр в `max(OldLineNumber, NewLineNumber)` по всему набору
  собранных строк.
- `Wcol` = `(TotalWidth - 2*(W+3) - 3) / 2`. Целочисленно вниз; если результат < 1 — clamp до 1
  (см. §10).

**Сигил после номера** (паритет с Unified): `' ' '-' ' '` или `' ' '+' ' '` или ` ' ' ' ` для
context.

### 5. Pairing rules: `IReadOnlyList<DiffLine>` → `IReadOnlyList<SideBySideRow>`

```csharp
internal readonly record struct SideBySideRow(DiffLine? Left, DiffLine? Right);
```

`null` → пустая клетка (просто пробелы шириной `W+3+Wcol`, без bg, без gutter-номера).

Алгоритм `SideBySideRowBuilder.Build(IReadOnlyList<DiffLine> lines)`:

```
foreach line in lines (sequential walk with i):
    case HunkSeparator:
        rows.Add(new SideBySideRow(line, line))   // оба сайда — separator
        i++

    case Context:
        rows.Add(new SideBySideRow(line, line))   // одно и то же содержимое; OldNum слева, NewNum справа
        i++

    case Removed:
        // собрать соседний run Removed
        var rem = []; while (lines[i].Kind == Removed) { rem.Add(lines[i]); i++ }
        // и сразу же соседний run Added (если есть)
        var add = []; while (i < n && lines[i].Kind == Added) { add.Add(lines[i]); i++ }
        // top-aligned pairing
        int max = Math.Max(rem.Count, add.Count);
        for (int j = 0; j < max; j++) {
            DiffLine? l = j < rem.Count ? rem[j] : null;
            DiffLine? r = j < add.Count ? add[j] : null;
            rows.Add(new SideBySideRow(l, r));
        }

    case Added:
        // пустой run Removed перед Added (бывает если правка только добавляет строки) — обрабатывается симметрично:
        // тот же блок что выше, но с пустым rem.
        var add = []; while (lines[i].Kind == Added) { add.Add(lines[i]); i++ }
        for (j ...) rows.Add(new SideBySideRow(null, add[j]))
```

(Реализация — единая ветка с case `Removed`/`Added` через look-ahead. См. псевдокод выше.)

**Важно:** `DiffHunkBuilder` уже эмитит run'ы в порядке `Removed*` затем `Added*` для одного
изменения. Builder не нуждается в re-sort.

### 6. Cell rendering (`SideBySideRowRenderer`)

Для одной `SideBySideRow`:

1. **Render left cell** — функция `RenderCell(DiffLine? line, int colWidth, int gutterWidth, bool useColor, bool useUnicode, bool isLeftSide)`.
2. **Render separator** — ` │ ` (Unicode) / ` | ` (ASCII), Dim на TTY.
3. **Render right cell** — `RenderCell(...)` с `isLeftSide: false`.

`RenderCell` для `null` (пустая клетка):
- Возвращает `' '.Repeat(gutterWidth + 3 + colWidth)`. Без bg.

`RenderCell` для `Kind == HunkSeparator`:
- Возвращает строку с `…` (или `...`) **по центру** колонки (включая gutter-зону): полная
  ширина клетки = `width = gutterWidth + 3 + colWidth`, длина glyph'а
  `glyphLen = useUnicode ? 1 : 3`, отступ слева `leftPad = (width - glyphLen) / 2` пробелов,
  затем glyph, затем `(width - glyphLen - leftPad)` пробелов. На plain — без обёртки. На TTY — `Dim`.

`RenderCell` для `Kind == Context`:
- Gutter: `OldLineNumber` (для левой стороны) или `NewLineNumber` (для правой), padded до
  `gutterWidth`.
- Sigil: `' '`.
- Content: truncated до `colWidth` (см. §7).
- Без row-bg. На TTY — `Dim` обёртка вокруг `gutter + sigil`, content plain.

`RenderCell` для `Kind == Removed` или `Added`:
- Gutter: соответствующий номер (Removed → OldLineNumber на левой стороне; Added → NewLineNumber
  на правой стороне). Левая сторона для `Added` или правая для `Removed` не возникает по
  построению pairing'а — такие случаи рендерятся через `null`.
- Sigil: `-` или `+`.
- Content: truncated + inline-highlight ranges клипованы до видимой части.
- На TTY: row-bg (`BgRemovedRow` / `BgAddedRow`) **заливает всю клетку** включая хвост-padding
  до правого края колонки. Поверх row-bg рисуются inline-highlight диапазоны
  (`BgRemovedHi` / `BgAddedHi` + `Bold`), затем возврат к row-bg для остатка.

ANSI-структура клетки на TTY (для `Removed` примера):
```
{BgRemovedRow}{FgRemovedSigil}{gutter} {sigil}{FgDefault} {content with optional highlights}{padding spaces}{BgDefault}
```
(Отличие от Unified: `padding spaces` до края колонки растягивает row-bg.)

### 7. Truncation + highlight clipping

`Truncate(string content, int colWidth, bool useUnicode)`:
- Если `content.Length <= colWidth`: возвращается контент + `' '.Repeat(colWidth - content.Length)`.
- Если `content.Length > colWidth`:
    - Берём первые `colWidth - 1` символов + индикатор `…` (Unicode) / `>` (ASCII).
    - Итоговая длина ровно `colWidth`.

Highlight clipping:
- Усечение происходит **до** разворачивания highlight-диапазонов в ANSI escape.
- Каждый `ColumnRange { Start, Length }` клипуется:
    - `clippedStart = Math.Min(Start, visibleContentLength)`.
    - `clippedEnd = Math.Min(Start + Length, visibleContentLength)`, где `visibleContentLength =
      colWidth - 1` если усечено, иначе `min(content.Length, colWidth)`.
    - Если `clippedEnd <= clippedStart` — диапазон отбрасывается.
- Индикатор (`…` / `>`) **не подсвечивается** (вне highlight bg).

### 8. ANSI палитра (без новых констант)

Используется `AnsiPalette` целиком как есть:
- `BgRemovedRow` — фон Removed клетки, заливает всю колонку.
- `BgAddedRow` — фон Added клетки, заливает всю колонку.
- `BgRemovedHi` — inline highlight на Removed.
- `BgAddedHi` — inline highlight на Added.
- `FgRemovedSigil`, `FgAddedSigil` — на gutter+sigil изменённых строк.
- `Dim` / `DimOff` — на gutter+sigil context-строк, на разделителе ` │ `, на hunk-separator
  `…`, и в банере (через `DiffBanner`).
- `Bold` / `BoldOff` — на inline-highlight контенте и в банере.
- `BgDefault` — сброс bg в конце клетки.
- `FgDefault` — сброс fg.

### 9. Hunk separator row

Когда `DiffHunkBuilder` эмитит `DiffLineKind.HunkSeparator`, pairing-builder создаёт
`SideBySideRow(line, line)`. Рендерер каждой клетки выводит `…` по центру **полной клетки**
(`gutterWidth + 3 + colWidth`):

```
                  …                              │                  …
```

ASCII fallback (`useUnicode == false`): `...` вместо `…` (см. формулу центрирования в §6 —
`glyphLen = 3` для ASCII).

### 10. Width calculation и edge cases

```csharp
int totalWidth = options.TotalWidth ?? GetConsoleWindowWidthSafe();   // helper с try/catch
int gutterWidth = ComputeGutterWidth(lines);                          // max digits over Old/New
int minTotal = 2 * (gutterWidth + 3) + 3 + 2;                         // 2 cells × prefix + sep + 1ch content × 2
if (totalWidth < minTotal) { totalWidth = minTotal; }                 // clamp
int colWidth = (totalWidth - 2 * (gutterWidth + 3) - 3) / 2;
// colWidth >= 1 гарантирован clamp'ом выше
```

`GetConsoleWindowWidthSafe`:
```csharp
private static int GetConsoleWindowWidthSafe()
{
    try
    {
        int width = Console.WindowWidth;
        return width > 0 ? width : 160;
    }
    catch (IOException)            { return 160; }
    catch (PlatformNotSupportedException) { return 160; }
}
```

Edge cases:
- `Changes.Count == 0` → плоское `(no changes)\n` без банера и без тела (паритет с
  `UnifiedDiffReporter`, `SimpleReporter`, `ConsoleDiffReporter`).
- Single-line файл → колонки по 1 строке, без separator-рядов.
- Multi-line replacement, `N != M` → top-aligned pairing с `null`-padding снизу (§5).
- Two changes touching the same line → одна `Removed`-строка, одна `Added`-строка, парные
  inline-highlight диапазоны рисуются в каждой клетке отдельно (паритет с Unified).
- Файл без trailing `\n` → как в Unified, нормализации нет.

### 11. Демо в `Program.cs`

После каждого блока `UnifiedDiffReporter.Print(...)` добавляется парный SBS-блок:

```csharp
Console.WriteLine("=== Diff (UnifiedDiffReporter) ===");
UnifiedDiffReporter.Print(order);
Console.WriteLine();

Console.WriteLine("=== Diff (SideBySideDiffReporter) ===");
SideBySideDiffReporter.Print(order);
Console.WriteLine();
```

Аналогично для `waybill`, `library`, `libraryDoc`. Имена файлов уже идут в банер автоматически
через `IStructuraDocument.DocumentName` (Step 11 plumbing).

## Tests

### Unit (plain) — `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterTests.cs`

Все на `FakeStructuraDocument` через `StringWriter` (no ANSI). Опции — explicit `TotalWidth`
для детерминизма.

1. `Print_NoChanges_WritesNoChanges` — пустой `Changes` → точно `"(no changes)\n"`.
2. `Print_SingleChange_PairedRow` — одно изменение: левая клетка — `-`-строка с OLD-номером,
   правая — `+`-строка с NEW-номером, разделитель ` │ `, обе usercase колонки.
3. `Print_BannerSameAsUnified` — сравнить банер с банером Unified на тех же входах
   (через тот же `FakeStructuraDocument`); должны совпадать байт-в-байт.
4. `Print_TwoNearbyChanges_OneHunk` — две правки в радиусе `2*ContextLines` → один блок без
   `…`-разделителя.
5. `Print_TwoFarChanges_TwoHunksSeparatedByEllipsis` — расстояние > `2*ContextLines` →
   между ними строка `… │ …`.
6. `Print_MultiLineReplacement_TopAlignedPadding` — `N=2, M=4` → 4 строки, последние 2 имеют
   пустую левую клетку.
7. `Print_AddedOnly_LeftSideEmpty` — правка только добавляет строки → пустая левая колонка.
8. `Print_RemovedOnly_RightSideEmpty` — правка только удаляет → пустая правая.
9. `Print_ContextLineNumberingDivergesAfterPriorHunk` — после multi-line replacement с N≠M в
   первом хунке номера context-строк во втором хунке слева ≠ справа.
10. `Print_ContentLongerThanColumn_TruncatedWithEllipsis` — контент длиннее `colWidth` →
    усечён, последний символ `…`.
11. `Print_ContentTruncated_HighlightClippedToVisible` — inline-highlight частично за
    точкой усечения → клипован до видимой части.
12. `Print_ContextLinesZero_NoContext` — `Options { ContextLines = 0 }` → нет context-строк.
13. `Print_HunkSeparator_EllipsisCenteredInBothCells` — `…` центрирован по полной ширине
    клетки.
14. `Print_StringWriter_NoAnsiEscapes` — output не содержит `\x1b`.
15. `Print_NullDocument_Throws` / `Print_NullWriter_Throws` (в одном `Theory` или двух
    методах).
16. `Print_ChangeAtFileStart_TruncatesPreContext`.
17. `Print_ChangeAtFileEnd_TruncatesPostContext`.

### Unit (color) — `tests/Structura.UnitTests/Reporting/SideBySideDiffReporterColorTests.cs`

Через internal-API доступ (InternalsVisibleTo уже на `Structura.UnitTests` через
`Directory.Build.targets`) или через internal-overload `RenderTo(... bool useColor)`.

1. `Print_ColorEnabled_RemovedCellHasRowBg` — `\x1b[48;5;52m` присутствует в левой клетке,
   `\x1b[48;5;22m` — в правой.
2. `Print_ColorEnabled_RowBgFillsToColumnEdge` — между концом контента и `\x1b[49m` стоит
   достаточно пробелов чтобы заполнить колонку (длина строки между `48;5;52m` и `49m` =
   `gutterWidth + 3 + colWidth + tail-padding`).
3. `Print_ColorEnabled_InlineHighlightOff_NoHighlightEscape` — `Options { InlineHighlight =
   false }` → нет `\x1b[48;5;124m` / `\x1b[48;5;34m`.
4. `Print_ColorEnabled_SeparatorIsDimmed` — ` │ ` обёрнут `\x1b[2m...\x1b[22m`.
5. `Print_ColorEnabled_HunkSeparatorIsDimmed` — `…` обёрнут Dim.

### Integration — `tests/Structura.IntegrationTests/Reporting/OrderSampleJsonSideBySideDiffTests.cs`

1. `SideBySideDiffReporter_NoMutation_PrintsNoChanges` — реальный `order.sample.json` без
   мутаций → `(no changes)`.
2. `SideBySideDiffReporter_RealMutations_BannerAndExpectedRows` — current Program.cs-флоу
   (8 правок, см. step-11 spec). Asserts: банер `Patched order.sample.json with 8 additions
   and 8 removals`; в выводе встречается ` │ `; найдена строка с `-   "version": 7,` слева
   и `+   "version": 42,` справа.
3. `SideBySideDiffReporter_DocumentName_FromSourceFileName` — `text.ParseJson<OrderSampleJson>()`
   → банер `Patched(order.sample.json)`.

Существующие тесты Unified не модифицируются. Тест-список Unified пополняется одним
**регрессионным** тестом:

- `tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs` — добавить
  `Print_AfterDiffLineRefactor_OutputUnchanged` или просто убедиться что существующие тесты
  зелёные (refactor — байт-в-байт).

## Step decomposition (commit cadence)

Пять последовательных коммитов; на каждом сборка зелёная.

1. **Step 12 foundation — `DiffLine` refactor.** Заменить `LineNumber` на `OldLineNumber +
   NewLineNumber`. Обновить `DiffHunkBuilder` (заполнять оба поля), `DiffLineRenderer`
   (выбирать по `Kind`), `UnifiedDiffReporter.maxLineNumber` (брать `Math.Max`). Существующие
   тесты Unified — все зелёные, поведение Unified байт-в-байт прежнее.

2. **Step 12 banner extraction.** Перенести `WriteBanner` из `UnifiedDiffReporter` в новый
   `Internal/DiffBanner.cs`. `UnifiedDiffReporter` вызывает `DiffBanner.Write(...)`. Сборка
   и тесты зелёные.

3. **Step 12 main chunk — `SideBySideDiffReporter` + Options + helpers.** Создать
   `SideBySideDiffReporter`, `SideBySideDiffOptions`, `SideBySideRow`, `SideBySideRowBuilder`,
   `SideBySideRowRenderer`. Полный layout, все фичи. Без правок `Program.cs`. Без новых
   тестов.

4. **Step 12 tests.** Unit (plain) + Unit (color) + integration по списку выше.

5. **Step 12 demo.** В `Program.cs` после каждого `UnifiedDiffReporter.Print(...)` добавить
   парный SBS-блок (4 сэмпла).

## Risks & open questions

- **Width detection на CI.** На CI обычно `Console.WindowWidth` бросает или возвращает 0.
  Помогает try/catch + fallback 160 (§10). Тесты передают явный `TotalWidth`, не зависят
  от среды.
- **Длинные JSON-строки в order.sample.json.** На стандартных терминалах (≤200 cols) колонки
  будут ~80 символов; строки вроде `"first_name": "John"` укладываются. Truncation
  активируется на коротких терминалах — это ожидаемо.
- **Multi-line replacement highlight.** В SBS используется тот же `InlineHighlights` массив,
  что в Unified — на promiscious-строках уже отрабатывает `ColumnRange(0, contentLength)`
  (вся строка highlighted). Поведение визуально консистентно с Unified.
- **`DiffLine` refactor — байт-в-байт паритет с Unified.** Опасное место: для context-строк
  `LineNumber` ранее был NEW, для Removed — OLD. `DiffLineRenderer` после refactor должен
  выбрать тот же номер. Проверка — все Unified-тесты остаются зелёными без модификаций.

## Verification

End-to-end:

1. **`dotnet build Structura.slnx`** — собирается без новых warning'ов.

2. **`dotnet test`** — все существующие тесты зелёные. Новые тесты (unit + color +
   integration) зелёные.

3. **`dotnet run --project src/Structura/Structura.csproj`** — демо печатает на каждый
   сэмпл (`order`, `blrwbl`, `library.xml`, `library.json`):
    - `=== Modified <FORMAT> ===` + текст
    - `=== Diff (UnifiedDiffReporter) ===` + unified-вывод (без изменений после refactor)
    - `=== Diff (SideBySideDiffReporter) ===` + двухколоночный вывод с банером, разделителем
      ` │ ` (на TTY — dim), цветными клетками изменений, inline-highlight'ом.

4. **Спот-проверка цвета.** `dotnet run` в Windows Terminal / iTerm2 / gnome-terminal:
    - Левая колонка `-`-строк на тёмно-красном фоне до правого края колонки.
    - Правая колонка `+`-строк на тёмно-зелёном фоне до правого края колонки.
    - Изменённый подсегмент на каждой стороне ярче-фоном.
    - Banner идентичен Unified-варианту.
    - При `dotnet run > out.txt` — никаких ANSI escape-кодов в файле.

5. **Acceptance criterion.** На сэмпле `order.sample.json` после демо-мутаций
   `SideBySideDiffReporter` выводит:
    - Тот же банер что Unified (`Patched order.sample.json with 8 additions and 8 removals`).
    - Хунки в порядке возрастания позиции в исходнике.
    - На каждой changed-строке: левая клетка с `-` и OLD-номером; правая клетка с `+` и
      NEW-номером, разделитель ` │ `.
    - Inline highlight на изменённых литералах в обеих клетках.
