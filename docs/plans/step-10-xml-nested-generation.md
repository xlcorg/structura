# Step 10 — XML nested generation (parity with JSON Step 9)

## Context

Step 9 закрыл JSON-сторону: `order.Customer.Preferences.MarketingConsent`, `library.Books[i].Publisher.Address.City` теперь работают через рекурсивные `JsonGenObject` / `JsonGenNestedObject`, а `JsonModelEmitter.EmitObjectMembers` эмитит тело любого объекта (root или nested) по одной и той же схеме.

XML-генератор остался на Step 8: умеет три стиля коллекций (Wrapper / Flat / SiblingGroup) и item-типы внутри них (`ItemTypeInfo` — рекурсивно «scalars + collections»), но **одиночный structural-элемент без attribute'ов** (`<Document>`, `<Shipper>`, `<Total>`, `<author>`, `<reviewer>`, `<statistics>`, `<meta:info>`, и т.д.) до сих пор пропускается с STR0009 «nested generation is not supported». Из 12 текущих STR0009 warning'ов на сэмплах ~10 про этот случай.

Сигнал из `src/Structura/Program.cs:50`: пользователь добавил `waybill.Document.DocumentID = 5;`, ожидая, что это компилируется. Сейчас не компилируется — `Document` пропущен.

Цель Step 10 — закрыть зазор по тому же алгоритму, что Step 9 закрыл для JSON: **парсер рекурсивно классифицирует pure-structural single-occurrence элементы как `XmlGenNestedObject`, эмиттер генерирует для них вложенный класс с переиспользуемой логикой, runtime не трогаем**.

**Acceptance criterion (от пользователя)**: после Step 10 любой элемент `blrwbl.sample.xml` можно прочитать и мутировать через сгенерированную модель.

## Scope

**In scope**
- Pure-structural single-occurrence элементы (нет non-xmlns атрибутов, есть child-элементы) — классифицировать как `XmlGenNestedObject`, эмитить вложенный класс с тем же набором scalar/collection/nested-object свойств, что у item-типа коллекции.
- Произвольная глубина вложенности (`<Total>` потенциально может содержать вложенные структурные элементы — поддерживаем рекурсию).
- Namespace-prefixed элементы (`<meta:info>`, `<meta:publisher>`) — генератор уже видит их по литеральному имени; нужно только убедиться, что `IdentifierSanitizer` нормально превращает `meta:info` → `MetaInfo` (двоеточие в C# имени не допустимо).
- JSON-pointer-style путь для record'ов: `/Document/DocumentID`, `/Shipper/GLN` (C# property name по сегментам, как уже делают коллекции — `/Books/0/Title`).
- Сужение STR0009: теперь срабатывает только на residual случай (text + attribute, mixed content). Сообщение остаётся форматнейтральным с прошлого шага.
- Тесты + расширение `Program.cs`.

**Out of scope (Step 11+)**
- Элементы с атрибутами + scalar-текстом (`<title lang="ru">War</title>`, `<price currency="RUB">15.99</price>`) — диагностика STR0009 остаётся.
- CDATA + attribute combinations (`<description ...><![CDATA[...]]></description>` с атрибутами на самом элементе).
- Insertion-aware patcher (изначально планировался Step 10, теперь Step 11). `IReadOnlyList<T>` остаётся read-only, throwing setter для absent-ключей в JSON остаётся.
- Heterogeneous JSON-массивы / numeric nullable promotion / arrays-of-arrays — Step 11+.
- Объединение `JsonModelEmitter` и `XmlModelEmitter` за общей абстракцией — после Step 11 переоценить.

## Coding Principles

### SRP / KISS (как в Step 9)
- Каждая новая функция эмиттера делает одну вещь: `EmitNestedObjectType`, `EmitNestedObjectCtor`, `EmitNestedObjectProperty`. Не вводим «универсальный EmitAnything».
- Парсер: рекурсия — обычная функция (как уже есть для item-типов через `BuildItemTypeFromCollection`), без visitor-паттерна.
- Не делаем общую абстракцию JSON ↔ XML сейчас. Дублирование структуры между `JsonModelEmitter.EmitObjectMembers` и тем, что появится в XML — допустимо до момента, когда станет очевидно, какая абстракция реально нужна.

### Применение к Step 10
- **Не делать рефакторинг существующего `EmitItemType` в XML**. Item-типы коллекций уже работают; новое поведение эмитим в новой паре функций `EmitNestedObjectType` / соответствующие helpers, **которые могут переиспользовать уже существующие** `EmitScalarCtor` / `EmitCollectionCtor` / `EmitScalarProperty` / `EmitCollectionProperty`.
- **Не вводить параметр `isRoot` повсюду**. Если эмитятся одни и те же scalar/collection helpers, разница между root и nested — это только наличие `_ctx` / `_pathPrefix` полей и форма конструктора, как уже сделано для item-типов.
- **Без feature flags**. В out-of-scope попадает только то, что явно перечислено выше.

## Critical files

**Изменяются:**
- `src/Structura.Generator/GeneratorXmlParser.cs` — добавить `XmlGenNestedObject`, расширить `ItemTypeInfo` и `XmlRootInfo` полем `NestedObjects`, в `ClassifyElementContents` после неудачного wrapper-classify пытаться классифицировать как nested object (рекурсивно). Узкий контракт STR0009.
- `src/Structura.Generator/XmlModelEmitter.cs` — добавить эмиссию nested object types на root и в item-типах. На root: backing field, ctor assignment, property. Nested type: класс с `_ctx`/`_pathPrefix`, ctor `(StructuraDocumentContext ctx, string pathPrefix, XmlSourceElement element)`, scalar/collection/nested-object members, рекурсивная эмиссия вложенных types.
- `src/Structura.Generator/StructuraXmlGenerator.cs` — убедиться, что diagnostic-цикл (`obs.SkippedStructural`) переживает изменения парсера (или просто потребляет уже отфильтрованный список).
- `src/Structura/Program.cs` — расширить демо: уже есть `waybill.Document.DocumentID = 5;` (line 50), добавить ещё пару показательных мутаций (`waybill.Shipper.GLN`, `waybill.Total.TotalAmount`) для reporter'а.

**Эталон, читать но не трогать:**
- `src/Structura.Generator/JsonModelEmitter.cs` — `EmitObjectMembers` (lines 82-225), `EmitNestedTypesRecursive`, `NestedObjectTypeName`. Это рабочий шаблон.
- `src/Structura.Generator/GeneratorJsonParser.cs` — `MergeObjectObservations` (рекурсивный merge nested-объектов).
- `src/Structura.Generator/XmlModelEmitter.cs` `EmitItemType` (lines 248-332) — текущая логика item-типа. **Идеи переиспользуем; функцию не модифицируем**, чтобы не сломать коллекции.
- `src/Structura.Generator/GeneratorXmlParser.cs` `BuildItemTypeFromCollection` (lines 256-290 и далее) — паттерн рекурсивной классификации. Возможно частично переиспользуется через extract-helper.
- `src/Structura.Runtime/Xml/*` — runtime уже всё умеет: `XmlSourceElement.RequireElement(name)`, `FindElement(name)`, `XmlSourceAttribute`, span'ы. Никаких изменений не нужно.
- `src/Structura.Runtime/StructuraDocumentContext.cs` `Record(path, span, replacement)` — годится без изменений.

## Design

### 1. Schema model (`GeneratorXmlParser.cs`)

Симметрично с JSON-стороной добавить **рекурсивное** представление объекта.

```csharp
internal sealed class XmlGenNestedObject
{
    public XmlGenNestedObject(string xmlElementName, ItemTypeInfo body) { ... }
    public string XmlElementName { get; }
    public ItemTypeInfo Body { get; }   // переиспользуем существующий тип
}
```

`ItemTypeInfo` уже имеет почти ту же форму, что нужна для тела nested-объекта (`TypeName`, `XmlElementName`, `Scalars`, `Collections`). Расширяем его на третье поле:

```csharp
internal sealed class ItemTypeInfo
{
    // existing: typeName, xmlElementName, scalars, collections
    public List<XmlGenNestedObject> NestedObjects { get; }
}
```

Аналогично `XmlRootInfo` получает `List<XmlGenNestedObject> NestedObjects`.

**Альтернатива** — полностью переименовать `ItemTypeInfo` → `XmlGenObject` для зеркалирования с `JsonGenObject`. Это симпатичнее, но:
- ItemTypeInfo используется в публичном API `XmlGenCollection.Item`, переименование дороже.
- Step 9 plan §Coding Principles говорит «не вводить общую абстракцию между JSON и XML сейчас».

**Решение**: оставить имя `ItemTypeInfo`, добавить `NestedObjects`. Если в Step 11+ окажется, что нужна общая абстракция, переименуем тогда.

### 2. Parser changes (`GeneratorXmlParser.cs:256-290` `ClassifyElementContents`)

Текущий поток для одиночных structural-вхождений:
```csharp
if (occurrences.Count == 1) {
    XmlGenCollection? wrapper = TryClassifyAsWrapper(...);
    if (wrapper != null) { collections.Add(wrapper); }
    else { obs.SkippedStructural.Add(...); }   // ← STR0009
}
```

Новый поток:
```csharp
if (occurrences.Count == 1) {
    XmlGenCollection? wrapper = TryClassifyAsWrapper(...);
    if (wrapper != null) { collections.Add(wrapper); continue; }

    XmlGenNestedObject? nested = TryClassifyAsNestedObject(xml, occurrences[0], obs);
    if (nested != null) { nestedObjects.Add(nested); continue; }

    obs.SkippedStructural.Add(...);   // ← STR0009 теперь только для residual
}
```

`TryClassifyAsNestedObject` (новый метод):
- Парсит `ElementInfo` для одиночного вхождения (как делает `TryClassifyAsWrapper`).
- Возвращает `null`, если на элементе есть non-xmlns attribute (это residual случай — оставляем для STR0009).
- Возвращает `null`, если элемент пустой (нет children) — пустой структурный элемент непонятно как моделировать; STR0009 (или, в перспективе, отдельная диагностика).
- Иначе строит `ItemTypeInfo` через рекурсивный `ClassifyElementContents` для child'ов; **результат заворачивает в `XmlGenNestedObject`**.

Тип-имя для nested-класса формируется тем же `ClassNameDeriver.Derive(xmlElementName)` + суффикс `Type` (см. §3 Naming).

### 3. Naming convention для XML nested types

Существующие соглашения в XML:
- Wrapper-вокруг-коллекции: суффикс `Group` (`DespatchAdviceLogisticUnitLineItemGroup`).
- Item-типы коллекций: без суффикса, используется singular (`LineItem`, `Book`).

Для **новых nested objects**: суффикс `Type`, чтобы:
- Совпасть с JSON-конвенцией (`CustomerType`, `PreferencesType`).
- Не конфликтовать с property-name (CS0102). `waybill.Document` — property, `waybill.DocumentType` — никогда не используется напрямую через `var`.
- Отделить от wrapper-Group, у которого другая семантика.

Helper в эмиттере: `XmlModelEmitter.NestedObjectTypeName(xmlElementName) => Derive(name) + "Type"`.

### 4. Emitter changes (`XmlModelEmitter.cs`)

**Root-уровень.** В `Emit` (line 16+):
- После «backing fields collections» → backing fields для nested objects (`private readonly DocumentType _document;`).
- В ctor: после collection-init → nested-object init (`_document = new DocumentType(_ctx, "/Document", root.RequireElement("Document"));`).
- После collection-properties → nested-object properties (`public DocumentType Document => _document;`).
- В цикле «Nested types»: помимо collection-types эмитим и nested-object types через рекурсивный helper.

**Helper `EmitNestedObjectType(StringBuilder sb, XmlGenNestedObject nested)`** — **новый метод**, симметричный `EmitItemType` (line 248-332):
```text
public sealed partial class DocumentType
{
    private readonly StructuraDocumentContext _ctx;
    private readonly string _pathPrefix;

    // span + value fields (scalars)
    // backing fields (collections)
    // backing fields (further nested objects — recursion)

    internal DocumentType(StructuraDocumentContext ctx, string pathPrefix, XmlSourceElement element)
    {
        _ctx = ctx;
        _pathPrefix = pathPrefix;
        // scalar reads via element.RequireElement(...)
        // collection ctors via existing EmitCollectionCtor (parametrized with sourceElementVar="element", parentPathPrefixExpr="_pathPrefix", contextExpr="_ctx")
        // nested ctors recursively
    }

    // public properties for scalars (via existing EmitScalarProperty pattern)
    // public properties for collections (via existing EmitCollectionProperty)
    // public properties for nested objects: simple `public T Foo => _foo;`
}
// Recursively emit further nested types (depth-first)
```

**Переиспользование существующих helper'ов.** Уже есть параметризованные `EmitCollectionCtor` (line 87 в Emit), `EmitCollectionProperty` (line 141), `EmitNestedTypesForCollection` (line 179). Большую часть из этого вызываем как есть, передавая правильные `sourceElementVar`/`parentPathPrefixExpr`/`contextExpr`. Если что-то завязано на `"root"` / `_ctx` напрямую — выносим в параметры (минимальный рефакторинг).

**Scalar emission.** Существующий root-scalar emitter (`EmitRootScalarCtor`, `EmitRootScalarProperty`) тоже параметризован через `RootScalar`. Нужен **близнец `NestedScalarCtor` / `NestedScalarProperty`**, которые отличаются только: (а) `sourceElementVar = "element"` вместо `"root"`, (б) `_ctx` вызывается через локальное поле, (в) path для `Record` — `_pathPrefix + "/PropName"`. По шаблону того, что уже есть для item-типов (`EmitItemScalarCtor`, `EmitItemScalarProperty` — строки 290+ примерно).

Если при Read-проверке окажется, что item-scalar-helpers уже принимают `(elementVar, pathPrefix, ctx)` параметрами — переиспользуем их 1:1. Если нет — extract-rename, чтобы один и тот же helper обслуживал и item-types, и nested-objects. Это минимальный рефакторинг.

**Важно:** XML nested-objects **не имеют heterogeneous field unions** в Step 10. В отличие от JSON-моделей, где `note` мог быть в одном элементе массива и отсутствовать в другом, XML nested-objects одиночны — все scalar-children либо есть, либо элемент классифицируется как невалидный. Поэтому `IsPresent`-гарды и throwing setter'ы (как в JSON) **не нужны**. Все property nested-объекта required. Это сильно упрощает эмиссию.

### 5. Diagnostics

STR0009 уже отрефакторена в Step 9 foundation в формат-нейтральный текст. Trigger-условия сужаются: теперь срабатывает **только** когда:
- одиночное structural-вхождение с non-xmlns атрибутами (residual `<title lang="ru">...</title>`),
- одиночное structural-вхождение без children (пустой нестандартный элемент),
- multi-occurrence sibling group, не классифицируемая ни как pure-text, ни как item-type.

Никаких новых диагностик для Step 10 не вводим. Возможно, когда в Step 11+ возьмёмся за text+attribute, появится STR0012 «mixed content not supported».

Существующие тесты `StructuraDiagnosticsTests` (`Generator_NestedStructural_EmitsSTR0009Warning`, `Generator_NestedStructural_DeduplicatesPerParentType`) **сломаются**, потому что их сценарий (`<root><a>1</a><nested><x>1</x><y>2</y></nested></root>`) теперь — валидный nested object. Нужно поправить тесты под новый seam:
- Для STR0009-сценария оставить ситуацию text+attribute или single-occurrence-empty.
- Для тестов на dedup использовать тот же residual случай.

### 6. Sample changes

**`src/Structura/Samples/blrwbl.sample.xml`** — не изменять. Использовать как есть; критерий приёмки от пользователя — этот сэмпл целиком проходит.

**`src/Structura/Samples/library.sample.xml`** — не изменять. После Step 10 он будет генерить меньше STR0009 warning'ов (residual: `title`, `price`), это OK.

**Новый sample не нужен.** Текущие XML-сэмплы покрывают: pure structural (`<Document>`, `<Shipper>`, `<author>`, `<reviewer>`, `<statistics>`, `<meta:info>`), namespace prefix (`<meta:info>`), глубокую вложенность (`reviews → review → reviewer`).

### 7. Demo (`Program.cs`)

Уже добавлено: `waybill.Document.DocumentID = 5;` (line 50).

Для большей выразительности reporter'а добавить ещё пару мутаций:
```csharp
waybill.Shipper.GLN = "9999988880001";
waybill.Total.TotalAmount = 700.00m;
```

Reporter покажет паттерн глубоких XML-путей: `/Document/DocumentID: 234/20012 → 5`, `/Shipper/GLN: 4810987000544 → 9999988880001`, `/Total/TotalAmount: 600.00 → 700.00`.

### 8. Tests

**Unit (`tests/Structura.UnitTests/Generator/`):**

- **`XmlNestedObjectsTests.cs`** (новый, ~6-8 тестов):
  - Single nested object — generator эмитит `public DocumentType Document` + `public sealed partial class DocumentType`.
  - Recursive nesting — `Total → TotalRefs → Ref` (или из реального сэмпла) даёт правильную цепочку типов.
  - Path composition — generated code содержит `_pathPrefix + "/DocumentID"` как литерал.
  - Naming collision — если есть два sibling nested objects с одинаковым типом-кандидатом, sanitize даёт уникальные имена.
  - Namespace prefix — `<meta:info>` → `MetaInfo` property + `MetaInfoType` class.
  - Empty nested element (no children) — пропускается, STR0009.

- **`StructuraDiagnosticsTests.cs`** (правки):
  - Существующие STR0009-тесты переписать под residual-сценарий (text+attribute или empty single-occurrence).
  - Можно добавить позитивный тест: «pure structural single-occurrence теперь не даёт STR0009».

**Integration (`tests/Structura.IntegrationTests/Xml/`):**

- **`BlrwblNestedObjectsTests.cs`** (новый, ~12-15 тестов) — **главное доказательство acceptance criterion**:
  - Read every nested object: `Document`, `Shipper`, `Receiver`, `FreightPayer`, `ShipFrom`, `ShipTo`, `Transporter`, `Carrier`, `Total` — для каждого хотя бы один scalar property возвращает корректное значение.
  - Mutate scalar inside nested: `waybill.Document.DocumentID = 5` → change.Path == `/Document/DocumentID`, NewText соответствует.
  - Mutate несколько nested одновременно — ordered by document position в `Changes`.
  - Byte-equality untouched regions — ключевая инвариантная гарантия.
  - Round-trip без мутаций — `ToXml() == originalXml`.

- **`LibraryXmlNestedObjectsTests.cs`** (новый, ~5-7 тестов):
  - `library.Statistics.Total = 6` → corresponding path.
  - `library.MetaInfo.Publisher` — read namespace-prefixed nested + sanitize check.
  - `library.Books[0].Author.FirstName = "Лев Толстой"` (nested object inside collection item).
  - `library.Books[4].Reviews[0].Reviewer.Name` (3-уровневое погружение: collection → item → nested).
  - STR0009 на `<title>` всё ещё фейерверкит — assert что это текущее поведение, фиксируем для Step 11.

- Существующие интеграционные тесты `BlrwblSampleParseTests.cs` — ничего не должно сломаться. Если что-то ломается — починить с минимальным изменением и зафиксировать в плане.

## Step decomposition (commit cadence — match Step 8/9)

1. **Step 10 foundation**: Schema (XmlGenNestedObject + NestedObjects на ItemTypeInfo и XmlRootInfo), parser change (TryClassifyAsNestedObject + рекурсивный обход), narrowed STR0009. Эмиттер ещё ничего нового не эмитит — добавляет defensive `Debug.Assert(info.NestedObjects.Count == 0, ...)` или просто игнорирует поле. Существующие тесты остаются зелёными (после фикса диагностических тестов).

2. **Step 10 main chunk**: `XmlModelEmitter.EmitNestedObjectType` + рекурсивная эмиссия + root/item-уровневый wiring (backing fields, ctor, properties). Здесь возможен extract-rename: общие helper'ы между item-types и nested-objects объединяются. `Program.cs` уже работает с `waybill.Document.DocumentID = 5`.

3. **Step 10 tests**: `XmlNestedObjectsTests`, `BlrwblNestedObjectsTests`, `LibraryXmlNestedObjectsTests`, обновление `StructuraDiagnosticsTests`. Acceptance criterion: каждый nested-объект из `blrwbl.sample.xml` покрыт.

4. **Step 10 demo**: точечное расширение `Program.cs` (несколько nested мутаций + reporter), без других правок.

## Implementation notes (после имплементации заполнить)

Раздел заполняется по ходу — фиксируем решения, не вошедшие в исходный Design (по образцу composed-petting-noodle.md §«Implementation notes»).

## Risks & open questions

- **Парсер уже использует `BuildItemTypeFromCollection` для построения `ItemTypeInfo` внутри коллекций.** Эта функция тоже рекурсивно классифицирует children. Если она в текущем виде НЕ умеет вкладывать nested objects (только scalars + collections), её нужно обновить симметрично. Проверить при имплементации; план по умолчанию — обновить, чтобы item-типы коллекций тоже получили вложенные объекты (например, `LineItem.SomeNested` если такое появится).
- **Naming collision на одном уровне.** Если sanitize двух разных XML-имён даёт одно C# имя (e.g. `meta:info` и `meta-info` оба → `MetaInfo`), нужно `MakeUnique`. Уже есть в существующих helper'ах для коллекций — переиспользовать.
- **`<author>` встречается внутри каждого `<book>` в `library.sample.xml`.** Это nested object внутри **item-типа коллекции**. Step 10 должен это поддержать (см. acceptance criterion с библиотекой). Это требует, чтобы `BuildItemTypeFromCollection` умел вкладывать nested-objects — см. предыдущий пункт.
- **Текстовые элементы с CDATA БЕЗ атрибутов** (`<description><![CDATA[...]]></description>`) — должны классифицироваться как pure-text scalars (как обычный `<description>текст</description>`). Если runtime XML parser даёт правильный inner span — всё работает. Проверить.
- **Что если nested-объект пустой во ВСЕХ observation'ах?** В коллекциях такой кейс не возникает — есть хотя бы один child. Для одиночного structural — пустой `<Foo></Foo>` без атрибутов и без children. План: пропустить с STR0009 (это residual). Проверить что текущий `TryClassifyAsWrapper` его уже отклоняет.
- **Существующие диагностические тесты сломаются** (см. §5). Это ожидаемо и фиксируется в Step 10 foundation вместе с парсером.
- **Generator-side имена `_field` / `_pathPrefix` / `_ctx`.** Эмиттер в нескольких местах хардкодит `"root"` как имя XmlSourceElement-параметра. Если новый helper для nested использует `"element"` (или `"src"`), нужна аккуратная параметризация. Минимальный безопасный путь — оставить «root» имя и в nested-конструкторе тоже принимать параметр с именем `root` (просто другой контекст). Решить в момент написания кода.

## Verification

End-to-end:

1. **`dotnet build Structura.slnx`** — должна собраться. Ожидаемые warning'и:
   - STR0009 на ~2 случая в `library.sample.xml` (`<title lang="...">`, `<price currency="...">`) — residual до Step 11.
   - Прежние warning'и не из STR0009 (STR0006/7/8 для library) остаются.
   - STR0011 на `archive: []` в `library.sample.json` — без изменений.
   - **Целевая разница**: 12 → ~2-4 STR0009. Точная цифра зависит от того, чем обернётся `description` (pure-text → scalar → ноль warning'ов).

2. **`dotnet test`** — все существующие тесты зелёные после правок диагностических тестов; новые unit и integration тесты зелёные.

3. **`dotnet run --project src/Structura/Structura.csproj`** — демо печатает четыре блока (order JSON / blrwbl XML / library XML / library JSON). В BLRWBL Changes виден список вложенных мутаций с XML-style путями (`/Document/DocumentID`, `/Shipper/GLN`, `/Total/TotalAmount`).

4. **Acceptance test от пользователя**: интеграционный `BlrwblNestedObjectsTests` подтверждает, что **любой scalar внутри любого nested-объекта `blrwbl.sample.xml` читается и мутируется**, а byte-equality untouched-регионов сохраняется. Это ключевая гарантия Step 10.
