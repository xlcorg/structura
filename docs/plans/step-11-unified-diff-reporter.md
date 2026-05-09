# Step 11 — `UnifiedDiffReporter` (Claude-Code-style unified diff)

## Context

`Structura.Reporting` сегодня содержит два потребителя `IStructuraDocument.Changes`:

- `SimpleReporter` — плоский список `/path: old → new` с заголовком `N change(s):`.
- `ConsoleDiffReporter` — git-ish per-change «mini-hunk» без контекста: `@@ /path (line N) @@` + одна `-`-строка + одна `+`-строка. Каждая мутация рендерится отдельным хунком, без объединения соседних.

Третий вид — **unified diff** в стиле, который рендерит Claude Code в своих tool-результатах: двухстрочный баннер, gutter с номерами строк, `-`/`+` сигилы после номера, цветные **фоны** строк (тёмно-красный / тёмно-зелёный), inline-highlight ярче-фоном на изменённом подсегменте, `…` между удалёнными хунками. Этот вид удобен, когда хочется «прочитать патч глазами», а не разглядывать список путей.

Шаг закрывает гэп: добавляем `UnifiedDiffReporter` рядом с двумя существующими (оба остаются), плюс минимальное расширение runtime для имени документа в баннере.

## Scope

**In scope:**
- Новый репортёр `UnifiedDiffReporter` в проекте `Structura.Reporting`.
- `UnifiedDiffOptions` record с двумя опциями (`ContextLines`, `InlineHighlight`).
- Расширение `IStructuraDocument` свойством `DocumentName : string?` и сквозной плумбинг через `StructuraDocumentContext`, ParseJson/ParseXml extensions, generated parser.
- Обновление `src/Structura/Program.cs`: убрать вызовы `SimpleReporter`/`ConsoleDiffReporter`, оставить только `UnifiedDiffReporter`.
- Unit + integration тесты.

**Out of scope:**
- JSON/XML syntax-highlighting содержимого строк (purple ключи, цветные строки/числа). Решено — v2-фича. Никакого hook'а в Options сейчас не закладываем; добавим, когда понадобится.
- Удаление или `[Obsolete]` пометка `SimpleReporter` / `ConsoleDiffReporter`. Оба остаются как публичные классы (только из demo убираются вызовы).
- Word-level diff внутри строки (Myers/LCS). Inline highlight использует точные span-координаты из `DocumentChange`.
- Темы цветов (light/dark, кастомные палитры). Хардкод 256-color ANSI.
- Multi-document отчёт (несколько `IStructuraDocument` в одном выводе).
- Truncation длинных строк, fallback на 16-color, fallback на не-Unicode терминалы за исключением одного простого подавления символов баннера (см. §«Glyph fallback»).

## Coding Principles

- **SRP / KISS.** Ровно три слоя: entry-point `UnifiedDiffReporter`, group-builder `DiffHunkBuilder`, line-renderer `DiffLineRenderer`. Никакой «общей абстракции репортёров» не вводим — три репортёра делают три разные вещи и могут жить параллельно.
- **Без LCS.** Алгоритм формирования хунков работает строго по `IReadOnlyList<DocumentChange>` + `OriginalText` + `CurrentText`. Это оправдано философией CLAUDE.md («reporters operate on the same change-set the patcher consumes, not by diffing strings post-hoc»).
- **Без feature-flags.** Опции в `UnifiedDiffOptions` управляют только тем, что явно перечислено (`ContextLines`, `InlineHighlight`). Остальное — захардкожено.
- **Параллель с существующими репортёрами.** API: `Print(doc)` рендерит в `Console.Out` с цветом, `Print(doc, writer)` без цвета (всегда), третья перегрузка с `UnifiedDiffOptions`. То же поведение, что у `ConsoleDiffReporter`.
- **No nested calls in arguments.** Промежуточные значения именованным локалам (правило C#-style проекта).

## Critical files

**Изменяются:**
- `src/Structura.Runtime/IStructuraDocument.cs` — добавить `string DocumentName { get; }` (non-null).
- `src/Structura.Runtime/StructuraDocumentContext.cs` — добавить второй параметр конструктора `string documentName` (required, non-null), хранить, экспонировать.
- `src/Structura.Runtime/StructuraJsonExtensions.cs` — **сигнатура `ParseJson<T>(this string json)` не меняется**, никаких overload'ов с именем.
- `src/Structura.Runtime/StructuraXmlExtensions.cs` — то же для XML.
- `src/Structura.Runtime/Json/IStructuraJsonDocument.cs` (и XML-аналог) — добавить static-abstract `SourceFileName { get; }`. Сигнатура `ParseFromJson(string json)` не меняется.
- `src/Structura.Generator/JsonModelEmitter.cs` и `XmlModelEmitter.cs` — generated тип получает `public const string SourceFileName = "<sample-file-name>";`. Generated `ParseFromJson(json)` создаёт `new StructuraDocumentContext(json, SourceFileName)`.
- `src/Structura/Program.cs` — заменить блоки `SimpleReporter.Print(...)` / `ConsoleDiffReporter.Print(...)` на `UnifiedDiffReporter.Print(...)`. Сигнатуры `ParseJson<T>()` / `ParseXml<T>()` не меняются — `DocumentName` появляется в баннере автоматически.

**Создаются:**
- `src/Structura.Reporting/UnifiedDiffReporter.cs` — entry-point, public static.
- `src/Structura.Reporting/UnifiedDiffOptions.cs` — public sealed record.
- `src/Structura.Reporting/Internal/DiffHunkBuilder.cs` — internal, group changes → list of `DiffHunk`.
- `src/Structura.Reporting/Internal/DiffLineRenderer.cs` — internal, форматирует одну строку (gutter + sigil + content + ANSI).
- `src/Structura.Reporting/Internal/AnsiPalette.cs` — internal static, набор escape-констант (red-bg, green-bg, bold, dim, reset).
- `tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs` — ~13 тестов (см. §Tests).
- `tests/Structura.IntegrationTests/Reporting/OrderSampleJsonReportingTests.cs` — расширить новыми тестами (или вынести unified-блок в отдельный файл `OrderSampleJsonUnifiedDiffTests.cs`).

**Эталон, читать не трогая:**
- `src/Structura.Reporting/ConsoleDiffReporter.cs` — паттерн `WriteColoredLine`, `LineContext.Of`, обработка CRLF через `TrimEnd('\r')`.
- `src/Structura.Reporting/SimpleReporter.cs` — паттерн API (`Print(doc)`/`Print(doc, writer)`).
- `tests/Structura.UnitTests/Reporting/ConsoleDiffReporterTests.cs` + `FakeStructuraDocument` (используется в обоих test-файлах) — тестовая инфраструктура.

## Design

### 1. API surface

```csharp
namespace Structura.Reporting;

public static class UnifiedDiffReporter
{
    public static void Print(IStructuraDocument document);
    public static void Print(IStructuraDocument document, TextWriter writer);
    public static void Print(IStructuraDocument document, TextWriter writer, UnifiedDiffOptions options);
}

public sealed record UnifiedDiffOptions
{
    public int ContextLines { get; init; } = 3;
    public bool InlineHighlight { get; init; } = true;
}
```

Поведение цвета:
- `Print(doc)` → пишет в `Console.Out`, цвета **только** если `!Console.IsOutputRedirected`.
- `Print(doc, writer)` и `Print(doc, writer, options)` — **никогда** цветов (plain text). Парирует существующие репортёры.

`UnifiedDiffOptions.DocumentName` отсутствует — имя приходит через `IStructuraDocument.DocumentName`.

### 2. `IStructuraDocument.DocumentName`

```csharp
public interface IStructuraDocument
{
    string OriginalText { get; }
    string CurrentText { get; }
    IReadOnlyList<DocumentChange> Changes { get; }
    string DocumentName { get; }  // NEW (non-null — см. ниже)
}
```

**`DocumentName` всегда берётся из имени сэмпла. Никакого override.** Генератор уже знает имя `AdditionalFile`'а (это его источник имени класса: `order.sample.json` → `OrderSampleJson`), и эмитит обратно статическую константу:

```csharp
// generated
public sealed partial class OrderSampleJson : IStructuraJsonDocument<OrderSampleJson>
{
    public const string SourceFileName = "order.sample.json";
    // ...
}
```

Соответственно `IStructuraJsonDocument<T>` (и XML-аналог) расширяется static-abstract'ом:

```csharp
public interface IStructuraJsonDocument<T> where T : IStructuraJsonDocument<T>
{
    static abstract string SourceFileName { get; }
    static abstract T ParseFromJson(string json);
}
```

`StructuraDocumentContext`:

```csharp
public StructuraDocumentContext(string originalText, string documentName)
{
    ArgumentNullException.ThrowIfNull(originalText);
    ArgumentNullException.ThrowIfNull(documentName);
    OriginalText = originalText;
    DocumentName = documentName;
}
public string DocumentName { get; }
```

Parse extensions **остаются с одной перегрузкой** — никакого второго аргумента:

```csharp
public static T ParseJson<T>(this string json) where T : IStructuraJsonDocument<T>
    => T.ParseFromJson(json);
```

Generated `ParseFromJson` всегда подставляет `SourceFileName`:

```csharp
public static OrderSampleJson ParseFromJson(string json)
{
    var ctx = new StructuraDocumentContext(json, SourceFileName);
    // ... existing parse + bind
}
```

**Семантика:**
- `text.ParseJson<OrderSampleJson>()` → `DocumentName == "order.sample.json"`.
- API не позволяет переопределить имя. Если когда-нибудь понадобится «patched <custom path>» (например, для логирования в продакшене разных физических файлов из одного сэмпла) — добавим overload отдельным шагом по реальному запросу. YAGNI.
- `DocumentName` всегда non-null. Баннер всегда печатает `Patched <docname> with ...`, без специального случая.
- Для тестов с `FakeStructuraDocument` (не идёт через генератор) — конструктор фейка просто принимает `documentName` явно, как обычный required параметр.

### 3. Banner layout (двухстрочный)

```
● Patched(order.sample.json)
  └ Patched order.sample.json with 3 additions and 3 removals
```

Правила:
- **Строка 1:** `<green ●> <bold>Patched</bold>(<docname>)`.
- **Строка 2:** `  └ <dim>Patched </dim><bold>{docname}</bold><dim> with </dim><bold>{N}</bold><dim> {addition-noun} and </dim><bold>{M}</bold><dim> {removal-noun}</dim>`, где `{addition-noun}` = `"addition"` если `N == 1` иначе `"additions"`; `{removal-noun}` симметрично. Так `1 addition` / `2 additions`.
- `{docname}` всегда non-null (см. §2 — берётся из `T.SourceFileName` если caller не передал явно).
- Между баннером и первым хунком — одна пустая строка.
- `N` = суммарное число `+`-строк во всех хунках. `M` = `-`-строк.
- Если `Changes.Count == 0` → плоское `(no changes)` без баннера и без хунков (паритет с `SimpleReporter` / `ConsoleDiffReporter`).

### 4. Body layout

Формат строки: `{lineNum:>W} {sigil} {content}`, где
- `W` — ширина gutter, число цифр в максимальном номере строки в выводе (по обоим текстам).
- `sigil` — один символ: `-`, `+`, или `' '` (пробел для контекста).
- `content` — оригинальная строка с её собственными ведущими пробелами, **без** trailing `\r`.

**Сигил после номера**, как на скрине Claude Code.

Пример:

```
   6     "is_priority": true,
   7 -   "version": 7,
   7 +   "version": 42,
   8 -   "currency": "RUB"
   8 +   "currency": "USD"
   9   }
   …
  14         "first_name": "John",
  15 -       "first_name": "John",
  15 +       "first_name": "Ivan",
  16         "last_name": "Doe"
```

### 5. Алгоритм span-based hunks (без LCS)

1. **Pre-split.** `oldLines = OriginalText.Split('\n')`, `newLines = CurrentText.Split('\n')`. CRLF: при выводе `TrimEnd('\r')` (рантайм-нормализации не делаем).
2. **Map changes to line ranges.** `Changes` уже отсортированы по `Span.Start`. Поддерживаем cursor `(oldOffset, newOffset)` → `(oldLineDelta, newLineDelta)`. Для каждой `DocumentChange c`:
    - `oldStartLine = LineOf(OriginalText, c.Span.Start)`
    - `oldEndLine = LineOf(OriginalText, c.Span.End - 1)` (если длина 0 → `oldStartLine`)
    - `newStartLine = oldStartLine + cumulativeLineDelta`
    - `newEndLine = newStartLine + (lineCount(c.NewText) - 1)` (минимум 1 строка результата)
    - `cumulativeLineDelta += lineCount(c.NewText) - lineCount(c.OldText)`
3. **Group changes.** Две правки попадают в один хунок, если `next.OldStartLine - prev.OldEndLine ≤ 2 * ContextLines`. Соседние группы дают отдельные хунки.
4. **Render hunk.** Для группы:
    - **Pre-context:** `min(ContextLines, group.OldStartLine)` строк из `oldLines` перед группой → context-строки с gutter = **NEW-file line number** (после применения cumulative delta от предыдущих хунков). Для самого первого хунка старый и новый номера совпадают.
    - **`-`-строки:** `oldLines[group.OldStartLine .. group.OldEndLine]` (incl), gutter = **OLD** (исходный) номер строки.
    - **`+`-строки:** `newLines[group.NewStartLine .. group.NewEndLine]`, gutter = **NEW** номер.
    - **Post-context:** `min(ContextLines, oldLines.Count - group.OldEndLine - 1)` строк после, gutter = NEW-file номер.

    **Правило gutter (важно для соответствия Claude Code 1:1):** на `-` строках показываем OLD-номер, на всём остальном (context + `+`) — NEW-номер. Так пользователь видит «где это будет в файле после применения патча».
5. **Между хунками:** одиночная строка с символом `…` (U+2026), без gutter и без сигила. Просто три пробела отступа + `…`. Если хунк один — разделителей нет. Если хунков ноль — баннер «N=0, M=0», но согласно §3 в этом случае мы вообще выводим `(no changes)` и не доходим до тела.
6. **Edge cases:**
    - Группа в начале файла → `group.OldStartLine == 0` → 0 строк pre-context.
    - Группа в конце файла → 0 строк post-context.
    - Multi-line replacement: одна `DocumentChange` где `c.NewText` содержит `\n` → несколько `+`-строк для одной правки. Аналогично для `c.OldText` с `\n` → несколько `-`. Рендерится одной группой.
    - Две правки на одной строке: `oldStartLine == oldEndLine` для обеих → одна `-`-строка (взятая из `oldLines`), одна `+`-строка (взятая из `newLines` после применения обеих); inline-highlight рисует **два** диапазона на одной строке.

### 6. Цвета (256-color ANSI)

Палитра захардкожена в `AnsiPalette.cs`:

| Назначение | Escape |
|---|---|
| `-` row bg | `\x1b[48;5;52m` (тёмно-красный) |
| `+` row bg | `\x1b[48;5;22m` (тёмно-зелёный) |
| Inline `-` highlight | `\x1b[48;5;88m` (ярче-красный) |
| Inline `+` highlight | `\x1b[48;5;28m` (ярче-зелёный) |
| Banner `●` | `\x1b[32m...\x1b[39m` (foreground green) |
| Bold | `\x1b[1m...\x1b[22m` |
| Dim | `\x1b[2m...\x1b[22m` |
| Reset bg | `\x1b[49m` |

Bg-padding: ровно под печатным текстом + 1 трейлинг space перед `\x1b[49m`. Без расширения до longest-line / ширины терминала. На контекст-строках фона нет вообще.

Inline highlight внутри bg-row: **переключение** bg-кода (`\x1b[48;5;28m` поверх `\x1b[48;5;22m`), затем возврат к row-bg. Пример для `+` строки `"version": 42,` где `42` подсвечен:

```
\x1b[48;5;22m   7 +   "version": \x1b[48;5;28m42\x1b[48;5;22m, \x1b[49m
```

(Прыгаем bg-цветом туда-обратно, не сбрасывая до конца строки.)

### 7. Inline highlight: маппинг span → колонки

Для строки на которой произошла правка `c`:
- На `-`-строке: `colStart = c.Span.Start - lineStartOffset`, `colEnd = colStart + c.OldText.Length`. Подсвечиваем `[colStart, colEnd)`.
- На `+`-строке: тот же `colStart` (потому что префикс строки до правки идентичен), `colEnd = colStart + c.NewText.Length`.
- Если на одной строке несколько правок (несколько `DocumentChange` с одинаковым `lineStart`) → несколько highlight-диапазонов. Сортируем по `colStart`, рендерим последовательно.
- Multi-line replacement: на первой `-`/`+` строке highlight идёт от `colStart` до конца строки; на промежуточных строках — вся строка highlighted; на последней — от начала до `colEnd`. (Edge case; реализация может упрощённо выкрасить целые строки, что визуально приемлемо.)

### 8. Glyph fallback

`●` (U+25CF) и `└` (U+2514) и `…` (U+2026) — Unicode. На большинстве современных терминалов работают. Ситуация одного хардкода: если `Console.OutputEncoding` не `UTF-8`, заменяем на ASCII:

| Unicode | Fallback |
|---|---|
| `●` | `*` |
| `└` | `\` |
| `…` | `...` |

Решается флагом `bool useUnicode = Console.OutputEncoding.WebName == "utf-8"` в момент построения баннера / разделителя. На `StringWriter`-перегрузке (где цветов нет) — оставляем Unicode (тесты будут проще).

### 9. Glyph отображение в layout

Расположение:

```
●␣Patched(<docname>)
␣␣└␣Patched␣<docname>␣with␣N␣addition(s)␣and␣M␣removal(s)
␣
{hunk-1}
␣␣␣…
{hunk-2}
```

Где `␣` — пробел. Тело начинается без отступа от levee (gutter — выровненный номер строки).

### 10. Демо в `Program.cs`

Текущие три блока на каждый сэмпл (`=== Modified ===` / `=== Changes (SimpleReporter) ===` / `=== Diff (ConsoleDiffReporter) ===`) сжимаются до двух:

```
=== Modified JSON ===
{json}

=== Diff (UnifiedDiffReporter) ===
{unified-output}
```

Имена сэмплов прокидываются в parse:

```csharp
var order = orderJson.ParseJson<OrderSampleJson>("order.sample.json");
```

Так баннер будет содержательным (`Patched order.sample.json with ...`).

### 11. Граничные сценарии

- **`Changes.Count == 0`:** `(no changes)` plain text, без баннера. (Совпадает с двумя другими репортёрами.)
- **Single-line файл, single change:** банер 1+1, хунк без pre/post-context.
- **Multi-line replacement (одна правка > одной строки):** один хунк, gutter номера старой строки на `-`, gutter номера новой строки на `+`. Inline highlight по правилу из §7 (упрощение допустимо).
- **Правка задевает только конец строки (`\n`):** `Span.End == lineEnd + 1` где `\n` входит в правку → правка фактически несколько строк. Рендерим как multi-line.
- **Two changes touching the same line:** одна `-` (исходная), одна `+` (`newLines` после обеих правок), два inline highlight-диапазона.
- **Файл без `\n` в конце:** `oldLines.Last()` без trailing newline. Рендерим как обычно.

## Tests

### Unit (`tests/Structura.UnitTests/Reporting/UnifiedDiffReporterTests.cs`)

13 тестов на базе `FakeStructuraDocument` (уже существующий helper). Все используют `StringWriter` → no ANSI (paritет с `ConsoleDiffReporter` тестами).

1. `Print_NoChanges_WritesNoChanges` — `Changes` пустой → точно `"(no changes)\n"`.
2. `Print_SingleChange_EmitsBannerAndHunk` — 1/1 счётчики в баннере, 3 строки pre-context, 3 post-context, корректные номера в gutter, сигилы после номера.
3. `Print_DocumentNameFromFake_AppearsInBanner` — `FakeStructuraDocument` сконструирован с `documentName: "x.json"` → банер `Patched(x.json)` / `Patched x.json with ...`.
5. `Print_TwoNearbyChanges_MergedIntoOneHunk` — две правки в радиусе 2*ContextLines → один хунк, без `…`.
6. `Print_TwoFarChanges_TwoHunksSeparatedByEllipsis` — расстояние > 2*ContextLines → `   …` строкой между.
7. `Print_ChangeAtFileStart_TruncatesPreContext` — нет строк перед.
8. `Print_ChangeAtFileEnd_TruncatesPostContext` — нет строк после.
9. `Print_MultiLineReplacement_OneHunkWithMultipleSigils` — `NewText` содержит `\n` → несколько `+` строк.
10. `Print_TwoChangesSameLine_OneMinusOnePlus` — две правки на одной строке → одна `-`, одна `+` (содержит результат обеих).
11. `Print_ContextLinesZero_NoContextOnlyChanges` — `Options { ContextLines = 0 }` → нет context-строк.
12. `Print_StringWriter_NoAnsiEscapes` — выходной буфер не содержит `\x1b`.
13. `Print_NullDocument_Throws` / `Print_NullWriter_Throws` (одним фактом, два суб-кейса).

### Unit (color rendering, optional)

Если время позволяет — отдельный тест-класс `UnifiedDiffReporterColorTests.cs` с собственным `Print` overload, принимающим `useColor: true` (internals-visible через `InternalsVisibleTo` `Structura.UnitTests`):

14. `Print_ColorEnabled_SingleChange_EmitsExpectedBgEscapes` — проверяем, что `+`-строка содержит `\x1b[48;5;22m` и `\x1b[49m`, а inline-highlight даёт `\x1b[48;5;28m`.
15. `Print_ColorEnabled_InlineHighlightOff_NoInlineEscape` — `Options { InlineHighlight = false }` → нет `\x1b[48;5;28m`.

(Может быть отнесено в Step 11 follow-up — не блокер ключевого функционала.)

### Integration (`tests/Structura.IntegrationTests/Reporting/`)

- `UnifiedDiffReporter_NoMutation_PrintsNoChanges` — реальный `order.sample.json` без мутаций.
- `UnifiedDiffReporter_RealMutations_BannerAndExpectedHunks` — current Program.cs-флоу (8 правок: `Currency`, `Version`, `IsPriority`, `Customer.FirstName`, `Customer.Preferences.MarketingConsent`, `BillingAddress.City`, `Items[0].Quantity`, `Items[1].Manufacturer.CountryCode`), assert: банер `Patched order.sample.json with 8 additions and 8 removals`, хунки в порядке возрастания line-номера, gutter числовой.
- `UnifiedDiffReporter_DocumentName_FromSourceFileName` — `text.ParseJson<OrderSampleJson>()` → банер содержит `Patched(order.sample.json)` (имя берётся из `OrderSampleJson.SourceFileName` автоматически).
- `OrderSampleJson_SourceFileName_EqualsSampleFile` — точечная проверка генератора: `OrderSampleJson.SourceFileName == "order.sample.json"`.

Существующие тесты `OrderSampleJsonReportingTests` (для `SimpleReporter`/`ConsoleDiffReporter`) **не модифицируем** — они продолжают работать.

### Byte-equality сторонний тест

Не требуется для репортёра как такового (гарантия патчера, не вывода). Существующие тесты пайплайна сохраняют byte-equality.

## Step decomposition (commit cadence)

1. **Step 11 foundation — runtime plumbing.**
    - `IStructuraDocument.DocumentName` (non-null), `StructuraDocumentContext` принимает `documentName` вторым required параметром, `IStructuraJsonDocument<T>.SourceFileName` static-abstract + аналог для XML, generator эмитит `public const string SourceFileName = "<sample-file-name>";` и в `ParseFromJson` подставляет его в `StructuraDocumentContext`. Сигнатуры `ParseJson<T>()`/`ParseXml<T>()` не меняются. Новый репортёр НЕ добавляется. Существующие репортёры просто игнорируют новое поле. Сборка зелёная, demo пока ещё на `SimpleReporter`/`ConsoleDiffReporter`.

2. **Step 11 main chunk — `UnifiedDiffReporter` + Options + helpers.**
    - Создать `UnifiedDiffReporter`, `UnifiedDiffOptions`, `DiffHunkBuilder`, `DiffLineRenderer`, `AnsiPalette`. Полный layout, все фичи (банер, gutter, inline highlight, эллипсис, опции, glyph fallback). Без modifications в `Program.cs`.

3. **Step 11 tests.**
    - Unit + integration по списку выше. Все зелёные.

4. **Step 11 demo.**
    - В `Program.cs` пробрасываем имя файла через ParseJson/ParseXml-overload, заменяем `SimpleReporter` + `ConsoleDiffReporter` блоки на один `UnifiedDiffReporter`.

## Risks & open questions

- **Generator-side добавление `SourceFileName` константы.** Сигнатура `ParseFromJson(string json)` / `ParseFromXml(string xml)` НЕ меняется — добавляется только `public const string SourceFileName = "...";`. Это безопасное аддитивное изменение; существующие пользовательские вызовы продолжают работать. Достаточно проверить, что `IdentifierSanitizer` / file-name-derivation не теряют исходное имя файла к моменту эмиссии (генератор сейчас уже работает с `AdditionalFiles`-путями, имя берётся напрямую — без преобразований нужно).
- **256-color на Windows-хостах до Windows 10 1607.** Старые консоли не парсят `\x1b[48;5;NNm`. Текущий `ConsoleDiffReporter` использует `Console.ForegroundColor`, который работает везде. Принимаем риск: проект targets `net10.0`, минимальный SDK — modern; кто запустит на cmd.exe из 2015 — увидит сырые escape. Не блокер.
- **Inline-highlight на multi-line replacement.** На «средних» строках (между первой и последней) фактически вся строка изменилась, highlight = вся строка. Реализация может упрощённо красить **всю** строку как inline-highlight bg, что визуально приемлемо. Если это окажется ugly — Step 11 follow-up.
- **`DocumentName` против существующих интеграционных тестов.** Тесты, которые делают `text.ParseJson<T>()` без имени, должны продолжать работать (default `null`). Проверить в foundation-коммите.
- **Glyph detection через `Console.OutputEncoding`.** При `Print(doc, writer)` (StringWriter) `Console.OutputEncoding` всё равно отвечает по системе, не по writer'у. Это OK — без цвета все равно plain text, и `…`/`└`/`●` спокойно живут в UTF-16-string.

## Verification

End-to-end:

1. **`dotnet build Structura.slnx`** — собирается без новых warning'ов. Существующие STR0009 на library.sample.xml (residual cases) остаются.

2. **`dotnet test`** — все существующие тесты зелёные (благодаря backward-compatible `DocumentName?`). Новые тесты зелёные.

3. **`dotnet run --project src/Structura/Structura.csproj`** — демо печатает на каждый сэмпл (`order`, `blrwbl`, `library.xml`, `library.json`):
    - `=== Modified <FORMAT> ===` + текст
    - `=== Diff (UnifiedDiffReporter) ===` + двухстрочный баннер с именем сэмпла + хунки с цветным gutter (на TTY) и сигилами после номера.

4. **Спот-проверка цвета.** Запустить `dotnet run` в Windows Terminal / iTerm2 / gnome-terminal, убедиться:
    - `-` строки на тёмно-красном фоне.
    - `+` строки на тёмно-зелёном.
    - Изменённый подсегмент на `+` ярче-зелёным.
    - Banner: зелёный `●`, bold `Patched`, dim вторая строка с bold-выделениями.
    - При `dotnet run > out.txt` — никаких ANSI escape-кодов в файле.

5. **Acceptance criterion.** На сэмпле `order.sample.json` после демо-мутаций (`Currency`, `Version`, `IsPriority`, `Customer.FirstName`, `Customer.Preferences.MarketingConsent`, `BillingAddress.City`, `Items[0].Quantity`, `Items[1].Manufacturer.CountryCode`) `UnifiedDiffReporter` выводит **корректные**:
    - Баннер `Patched order.sample.json with 8 additions and 8 removals`.
    - Хунки в порядке возрастания позиции в исходнике, со склеенными группами при близости.
    - Gutter номера соответствуют исходным/новым строкам.
    - Inline highlight на изменённых литералах.
