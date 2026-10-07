# Плейбук: поддержка `kind` в C# backend

Всё происходит в [CSharpBackend.cs](../../src/Asn1Kit.Codegen.CSharp/CSharpBackend.cs). Текущее состояние — в [docs/status.md](../../../docs/status.md).

## Предусловие

Нужный примитив обязан существовать в runtime. Если для kind нет пары `Write*` / `Read*` в [Asn1Kit.Runtime](../../../runtime-csharp/src/Asn1Kit.Runtime), сначала [runtime.md](../../../runtime-csharp/docs/playbooks/runtime.md): кодек пишется там, а не в шаблоне генератора.

## Порядок

1. **Снять запрет.** Убрать kind из `EnsureBackendSupport` — но только вместе с реализацией, не «на будущее».
2. **Тип C#.** `CsType`: маппинг kind → тип C# и его nullable-форма для `OPTIONAL`. Текущие: `boolean` → `bool`, `integer` → по `options.integer.representation` или выводу из `constraint.value` / `namedValues` (`int` / `uint` / `long` / `ulong` / `BigInteger` / `Asn1Integer`; при метках без опции — `int`), `enumerated` → именованный `enum`, `octetString` → `ReadOnlyMemory<byte>`, `oid` → `Asn1Oid`, `null` → `Asn1Null`, `bitString` → `Asn1BitString`, `string` → `string`, `time` → `DateTimeOffset`. `any` с `bindings` → именованный CHOICE-like `Owner_Field` (не `object`). Open-type bindings объединяются в одну кодирующую альтернативу только при одинаковом семантическом `name` и структурно одинаковом ASN.1 type expression: например, `UTF8String` и `IA5String` должны сохранить разные wire-теги. В публичном API альтернативы затем группируются по non-nullable `CsType`: одно свойство на CLR-тип, а при повторе CLR-типа соседний `Owner_FieldKind` различает семантические/wire-альтернативы. Если повторов нет, сохраняется компактный API без enum.
3. **Примитив или именованный тип.** `ResolvePrimitive` возвращает kind для «плоских» типов; `NeedsNamedType` перечисляет те, для которых эмитится отдельный класс (`sequence`, `set`, многовариантный `choice`). `sequenceOf` / `setOf` и **CHOICE с одним вариантом** **не** эмитятся как классы: поле → underlying (`List<T>` / тип альтернативы). Многовариантный `choice`: `EmitChoice` — enum `…Kind`, `From…(T)` на альтернативу; если все альты дают один `CsType` — одно свойство `Value` (иначе по свойству на альт с `private set`). `enumerated` и `namedBits` тоже эмитятся как отдельные типы (не через `ResolvePrimitive`). Новый составной kind добавляется в обе функции и в `CollectNested`.
4. **Тег по умолчанию.** `UniversalTag` и `UniversalFallback` — universal-тег kind, когда в IR тега нет. Для строк и времени тег выбирается по `stringType` / `timeType`. Для `SET` / `SET OF` это тег 17, а не 16. Для `enumerated` — `Asn1Tag.Enumerated` (10).
5. **Encode / Decode.** `WriteCall` / `ReadCall` — полные вызовы runtime (у строк и времени с аргументом `Asn1StringForm` / `Asn1TimeForm`). Для `enumerated`: `WriteEnumerated` / `ReadEnumerated` с cast `(long)` / `(EnumName)`. SEQUENCE/SET и EXPLICIT используют симметричные `using (writer.EnterSequence|EnterSet|EnterExplicit(tag))` / `using (reader.EnterSequence|EnterSet|EnterExplicit(tag))`; OF encode — generic `WriteSequenceOf` / `WriteSetOf` со `static (inner, item)` lambda. `EmitEncodeValue` и `EmitDecodeAssign` / `EmitDecodeExpr` уже обрабатывают `EXPLICIT`-обёртку, `OPTIONAL` и OF-списки; повторять это в новой ветке не нужно. `CloneUntagged` обязан создавать новый объект без `Tag` — иначе `EXPLICIT` уйдёт в бесконечную рекурсию.
6. **Инициализация.** `Initializer` — значение по умолчанию для non-optional свойства, чтобы `#nullable enable` не давал предупреждений в сгенерированном коде. Для `Asn1BitString` / `DateTimeOffset` / `enum` инициализатор не нужен (`default` валиден). Передавай в `Initializer` тип **после** `UnwrapAliases`.
7. **Алиасы.** Typedef без `sequence`/`set`/многовариантного `choice` RHS, без `namedBits` и без `enumerated` **не эмитится** (включая `SEQUENCE OF` / `SET OF` и **CHOICE с одним вариантом**): `UnwrapAliases` подставляет исходный тип в `CsType` / encode / decode; на поле — `/// <summary>ASN.1 alias Name ::= …</summary>`. Для OF при пропуске typedef всё равно вызывай `CollectNested`, чтобы эмитить вложенные `_Item` (`SEQUENCE OF SEQUENCE {…}`). Single-alt CHOICE при обходе вложенности считается прозрачным: вложенный тип именуется как `Owner`, а не `Owner_AltName`.
8. **`namedBits`.** `EmitNamedBitString`: `[Flags]` enum `{Type}Flags` (`1 << index`), класс с `Asn1BitString Value`, свойство `Flags`, статические `ToFlags` / `FromFlags`, Encode/Decode через `WriteBitString` / `ReadBitString`.
9. **`enumerated`.** `EmitEnumerated`: `public enum {Type}` с членами `Name = value` из IR; `: long`, если значение вне `int`; encode/decode через `WriteEnumerated` / `ReadEnumerated` (tag 10). Inline ENUMERATED → `Owner_Field`.
10. **`integer` с `namedValues`.** `EmitNamedIntegerConstants`: `public static class {Type}` с `public const int|long Name = value`; свойство поля остаётся числом (`int` по умолчанию). Inline → `Owner_Field`.
11. **Именованные OID.** Локальные `module.values` с `kind: oid` эмитятся в публичный `<ModuleName>Oids`: dotted-форма как `const string`, DER contents как лениво и однократно разобранный `Asn1Oid`. `DEFAULT` и open-type keys переиспользуют каталог: сначала локальный, затем каталоги импортированных модулей, затем остальных модулей документа. Внешние ссылки квалифицируются с учётом `options.csharp.namespace`; значения с `generate: false` не используются. Если именованного OID нет, сохраняется разбор dotted-строки через runtime. Полный TLV не кешируется, потому что тег может быть переопределён.

## Требования к сгенерированному коду

- Для raw open types из `ref.openTypes` планируй typed bindings до окончательного сбора nested-типов. Обходи aliases, OF и CONTAINING. Конечный descriptor — `sealed record Binding<T>(key, Func<…> Decoder, Func<…> Encoder)` (decoder-only — `DecoderBinding<T>`); per-stem named delegates не эмить. Отдельный класс значения binding не эмитится. Примитивные leaf-типы обязаны переиспользовать singleton-кодеки `Asn1Codecs` из runtime. Для остальных wire-специализаций эмить один модульный `Asn1Codec<T>`, дедуплицированный по типу и wire-форме; отдельный codec-класс на binding запрещён. Простые ANY, CONTAINING и массивы адаптируются общими generic-методами runtime. Только более сложный вложенный raw-маршрут может получить локальный адаптер. Codec использует существующие `EmitEncodeValue` / `EmitDecodeAssign`, проверяет полное потребление BER и сохраняет форму массивов. Неизвестные значения остаются в исходной модели. Instance open-type Uses идут через те же `Asn1Codecs` / модульные codecs.
- Binding, который после раскрытия алиасов имеет тип `NULL`, сохраняй как `Binding<Asn1Null>`; внутри OF тип остаётся массивом. Наличие `NULL` и отсутствие поля — разные wire-формы. Именованный setter для одиночного `NULL` имеет форму `Set…()` без аргумента `Asn1Null`; именованные convenience `TryDecode…` / `TryGet…` с `out Asn1Null` не генерируй — достаточно generic descriptor-overload и `Set…()`.
- Все типизированные операции над raw open types используют descriptor конкретной таблицы без `typeof(T)`. Stem каталога: `ref.openTypes[].table` (+ open-field, если путь без `[]`) либо `Container+OpenField`. Сайты с одинаковой таблицей (контейнер + selector + payload + bindings) переиспользуют один каталог; SEQUENCE-владелец в identity не входит. Contextual `TryDecode{OpenField}` / `Set{OpenField}` эмитятся на контейнере один раз на stem; instance Uses на SEQUENCE — forwarders к тому же `Binding<T>` (decoder контейнера). `TryGet{Field}` на владельце и `TryGet` на `Container[]` для OF; overload с `out RawContainer` возвращает совпадение. `Create<T>(key, decoder, encoder)` создаёт полный codec (decoder принимает raw-контейнер). Для сплющенных однотипных ветвей `CHOICE` генерируй decoder-only `DecoderBinding<T>` и `Create<T>(key, decoder)`. Flattened string decode — typed CHOICE `.Value` либо `Asn1Codecs.DecodeStringChoice` со **static** `Asn1StringForm[]`; именованные convenience — только для encodeable bindings и без коллизий на контейнере. Table Wrappers оставляют `DecodeX` (source → raw); `EncodeX` только для `EncodeEach` / `EncodeContained`. Все модульные `Asn1Codec<T>` эмитятся в один `__<Module>OpenTypeCodecs`.
- Setter локального selector копирует вложенных владельцев ключа, чтобы не менять разделяемые объекты, сохраняет поля raw-контейнера и канонизирует новое значение в DER. Selector предка принимает явный контекст, проверяет соответствие key и не изменяется. Для raw-контейнера-`struct` extension setter использует `this ref`; lazy/retainEncoded оборачиваются обратно. Для `struct` с инициализаторами свойств генерируй конструктор C# 10, который присваивает остальные поля, сохраняя DEFAULT.

- Нетегированный `CHOICE` внутри open-type binding раскрывается рекурсивно в альтернативы `Owner_Field` до группировки по CLR-типам. Известный ключ выбирает набор поднятых веток, затем ASN.1-тег выбирает ветку; неизвестный OID использует существующие примитивные альтернативы по universal-тегу. Тегированный или рекурсивный `CHOICE` сохраняется отдельной альтернативой. Для inline-составных веток `CollectNested` должен сгенерировать тип с тем же именем, которое использует `CsType`; ссылки из импортированного `CHOICE` разрешаются в исходном модуле. IR не изменяется.
- Только вызовы runtime: байтов тегов, длин и правил DER в шаблоне быть не должно.
- `options.csharp.namespace` / `typeName` / `propertyName` / `valueType`, `options.generate: false`, `options.integer.representation`, `options.lazy` и `options.retainEncoded` уважаются (читаются через `IrOptions`). Для INTEGER без опции: `namedValues` → `int32` (или `int64`, если метка вне `int`); иначе полный `constraint.value` → наименьший подходящий `int32`→`uint32`→`int64`→`uint64`; иначе `der` (`Asn1Integer`). `options.lazy: true` на поле/типе/модуле оборачивает SEQUENCE/SET в `Asn1Lazy<T>`, SEQUENCE OF/SET OF — в `Asn1Lazy<List<T>>` (`ReadLazy` / `HasEncoded` → `WriteRaw`). `options.retainEncoded: true` — то же с `Asn1Value<T>` (eager + исходный TLV в `OriginalEncoding`; encode всегда из `Value`; lazy побеждает при конфликте). `options.csharp.valueType: true` на typedef SEQUENCE/SET — `struct` вместо `sealed class`. OID → `Asn1Oid`.
- Никаких лишних аллокаций: без промежуточных `MemoryStream` на поле, без `Func` на элемент `SEQUENCE OF`, коллекции с известной ёмкостью, где размер известен.
- OID-каталог ленив по каждому значению: обращение к строковой константе не должно разбирать остальные OID модуля. Runtime остаётся единственным местом dotted-string ↔ DER contents codec.
- Ломаный вход падает `Asn1Exception` (чужой тег, неизвестная альтернатива `CHOICE`, лишние байты) — это обеспечивает runtime, задача шаблона его не обходить.

## Тесты

Образец — `RoundTripTests.GeneratedCSharp_CompilesAndRoundTripsPerson` в [Asn1KitTests.cs](../../tests/Asn1Kit.Compiler.Tests/Asn1KitTests.cs): IR → генерация → компиляция Roslyn → encode/decode через рефлексию.

Golden C# для PKIX: [runtime-csharp/generated/Asn1Kit.Pkix](../../../runtime-csharp/generated/Asn1Kit.Pkix/), сверка `PkixGeneratedCodeTests`. После правок шаблона пересобери:

```powershell
dotnet run --project compiler/src/Asn1Kit.Cli -- generate -i compiler/fixtures/ir/pkix1-implicit88.json --lang csharp -O csharp.namespace=Asn1Kit.Pkix -o runtime-csharp/generated/Asn1Kit.Pkix
```

Минимум на новый kind:

1. Сгенерированный код компилируется (`CompileGenerated` кидает с текстом диагностик, если нет).
2. Round-trip encode → decode → encode даёт те же DER-байты.
3. Байты DER сверены с ожидаемым вектором, а не только «раскодировалось обратно».
4. `OPTIONAL` присутствует и отсутствует — оба случая.
5. Порченый вход даёт `Asn1Exception`.

Не забудь строку в матрице [docs/status.md](../../../docs/status.md).

```powershell
dotnet test Asn1Kit.sln
```
