# Что поддержано сейчас

Срез состояния по слоям. Обновляется тем же изменением, которым расширяется поддержка, — иначе документ бесполезен.

## Типы IR


| `kind`                          | Компилятор                                                           | C# backend                                                                                                                                | Покрытие тестами                                                                                                                                    |
| ------------------------------- | -------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| `boolean`                       | да                                                                   | да → `bool`                                                                                                                               | `ParserTests`, `PrimitiveCodecTests`, `PrimitiveOracleTests`                                                                                        |
| `integer`                       | да, с `namedValues`                                                  | да → `int`/`uint`/`long`/`ulong`/`BigInteger`/`Asn1Integer` по опции или выводу (`namedValues` → `int` + `static class` констант; иначе `constraint.value`; иначе `Asn1Integer`) | `CompilerTests`, `PrimitiveCodecTests`, `PrimitiveOracleTests`, `RoundTripTests`                                                                    |
| `enumerated`                    | да, с `values`                                                       | да → C# `enum` (tag 10); inline → `Owner_Field`                                                                                           | `PkixImplicit88Tests`, `RoundTripTests.GeneratedCSharp_Enumerated_*`, `PrimitiveCodecTests`                                                         |
| `bitString`                     | да, с `namedBits`                                                    | без имён → `Asn1BitString`; с `namedBits` → класс + `[Flags]` enum (`ToFlags`/`FromFlags`)                                                | `PrimitiveCodecTests`, `PrimitiveOracleTests`, `RuntimeTests`, `RoundTripTests.PrimitivesAsn_*`, `RoundTripTests.GeneratedCSharp_CollapsesAliases*` |
| `octetString`                   | да                                                                   | да → `ReadOnlyMemory<byte>`                                                                                                               | `PrimitiveCodecTests`, `PrimitiveOracleTests`, `RoundTripTests`, `RuntimeTests`                                                                     |
| `oid`                           | да, dotted-строка; первый subidentifier — base-128 (в т.ч. `2.999…`) | да → `string`                                                                                                                             | `PrimitiveCodecTests`, `PrimitiveOracleTests`, `RuntimeTests.ObjectIdentifier_*`                                                                    |
| `string` (12 форм `stringType`) | да                                                                   | да → `string` + `Asn1StringForm`                                                                                                          | `PrimitiveCodecTests` (все 12), `PrimitiveOracleTests` (BCL charset + UniversalString UTF-32BE reference; Teletex/… — только свои векторы), `RuntimeTests`, `RoundTripTests.PrimitivesAsn_*` |
| `time` (`utc` / `generalized`)  | да; `fractionDigits` 0…7 (default 3) для `generalized`               | да → `DateTimeOffset` + `Asn1TimeForm`; запись с округлением                                                                              | `PrimitiveCodecTests` (UTCTime pivot), `PrimitiveOracleTests` (pivot + GeneralizedTime fraction), `RuntimeTests`, `RoundTripTests.PrimitivesAsn_*` |
| `any` (+ `definedBy`)           | да, с проверкой sibling-компонента                                   | да → `Asn1Any` (Tag + ContentsMemory; `definedBy` не резолвится); typedef `Name ::= ANY` сворачивается                                    | `ParserTests`, `RuntimeTests.Any_*`, `RoundTripTests.GeneratedCSharp_Any_*`, `ValueResolutionTests.RejectsAnyDefinedByUnknownField`                 |
| `sequence`                      | да, `extensible`                                                     | да → класс с `Encode` / `Decode`                                                                                                          | `RoundTripTests`, `PkixExplicit88Tests`, `PkixImplicit88Tests`, `PkixGeneratedCodeTests`                                                            |
| `set`                           | да                                                                   | да → класс с `Encode` / `Decode` (DER: порядок по тегу; decode по тегу)                                                                   | `RoundTripTests`, `ParserTests`, `RuntimeTests`                                                                                                     |
| `choice`                        | да                                                                   | да → класс + enum `…Kind` + `From…`; однотипные альты → `Kind`+`Value`; **один** вариант → алиас                                      | `ParserTests`, `PkixExplicit88Tests`, `PkixGeneratedCodeTests`, `RoundTripTests.GeneratedCSharp_Collapses*Choice`                                  |
| `sequenceOf`                    | да                                                                   | да → `List<T>` (typedef сворачивается; `WriteSequenceOf` / `ReadSequenceOf`; `/// <summary>` с именем алиаса)                            | `ParserTests`, `RoundTripTests.GeneratedCSharp_CollapsesSequenceOf*`, `PkixGeneratedCodeTests`, `RuntimeTests.WriteSequenceOf_*`                  |
| `setOf`                         | да                                                                   | да → `List<T>` (`WriteSetOf<T>` / `ReadSetOf`; DER-сортировка в runtime)                                                                  | `RoundTripTests`, `ParserTests`, `RuntimeTests`                                                                                                     |
| `ref`                           | да, с резолвом через модули и `IMPORTS`                              | да; алиасы на примитивы/`SEQUENCE OF`/`SET OF`/другие имена **сворачиваются** (класс не эмитится, `/// <summary>` на поле); cross-module → квалифицированное имя | `PkixExplicit88Tests`, `PkixImplicit88Tests`, `ImportResolutionTests`, `PkixGeneratedCodeTests`, `RoundTripTests.GeneratedCSharp_CollapsesAliases`* |


Неподдержанные бэкендом kind отсекает `CSharpBackend.EnsureBackendSupport` с текстом `C# backend does not support kind '<kind>' yet.` — рекурсивно, включая вложенные компоненты и элементы `SEQUENCE OF` / `SET OF`.

## Значения IR

`integer`, `boolean`, `null`, `oid` (dotted-строка), `string`, `bitString` (`bits` / `hex`), `ref`.

Компилятор резолвит их двухфазно: OID-цепочки разворачиваются, `ub-*` подставляются в `SIZE` и диапазоны, `DEFAULT` с именованным числом разрешается через тип компонента. В скомпилированном IR `ref` обычно уже раскрыт.

## Теги и constraints

- `EXPLICIT` / `IMPLICIT` / `AUTOMATIC TAGS`; `AUTOMATIC` раскрывается в явные `tag` на компонентах, для `CHOICE` тег остаётся `explicit`.
- `IMPORTS … FROM Module` между переданными файлами: символы проверяются в модуле-источнике (`ImportResolutionTests`, golden PKIX1Implicit88).
- `SIZE` и диапазоны значений → `constraint.size` / `constraint.value`; `MAX` кодируется отсутствующим `max`.
- `SIZE` сразу после `SEQUENCE` / `SET` без `OF` и `MIN` как конкретная граница — явный отказ.
- Остальные формы (например `FROM`, union `|`) сохраняются строкой в `constraint.unsupported`.



## Runtime BER/DER


| Операция                     | `Asn1Writer`                                                     | `Asn1Reader`                                                                                                  |
| ---------------------------- | ---------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| BOOLEAN                      | `WriteBoolean`                                                   | `ReadBoolean`                                                                                                 |
| INTEGER                      | `WriteInteger` (`BigInteger` / фиксированные числа / `Asn1Integer` as-is) | `ReadInteger` / `ReadIntegerValue` / `ReadInt32`…`ReadUInt64`                                      |
| ENUMERATED                   | `WriteEnumerated` (`BigInteger`, contents как INTEGER)           | `ReadEnumerated`                                                                                              |
| BIT STRING                   | `WriteBitString` (`Asn1BitString`)                               | `ReadBitString` (BER: constructed и indefinite length склеиваются)                                            |
| OCTET STRING                 | `WriteOctetString` (`ReadOnlySpan`)                              | `ReadOctetString` → `ReadOnlyMemory` (view; BER constructed — owned); `TryReadOctetString(Span)` copy-out |
| NULL                         | `WriteNull`                                                      | `ReadNull`                                                                                                    |
| OBJECT IDENTIFIER            | `WriteObjectIdentifier` (dotted)                                 | `ReadObjectIdentifier`                                                                                        |
| STRING (12 форм)             | `WriteString` + `Asn1StringForm`                                 | `ReadString` (BER: constructed склеивается)                                                                   |
| TIME (`utc` / `generalized`) | `WriteTime` (`DateTimeOffset`, `fractionDigits` для generalized) | `ReadTime` (дробь 1…7, хвостовые нули ок)                                                                     |
| SEQUENCE / SET / constructed | `WriteSequence`, `WriteSet`, `WriteSequenceOf<T>`, `WriteSetOf` / `WriteSetOf<T>` | `ReadSequence`, `ReadSet`, `ReadSequenceOf<T>`, `ReadSetOf<T>`, `TryPeekTag`, `Eof` |
| SET OF (DER sort)            | `WriteSetOf(Action)`                                             | через `ReadSet`                                                                                               |
| EXPLICIT-обёртка             | `WriteExplicit`                                                  | через `ReadSequence`                                                                                          |
| Готовый TLV                  | `WriteRaw`; `Encode` / `EncodedLength` / `TryEncode`             | `ReadValue` / `TryReadValue` / `ReadTlv`                                                                      |
| ANY                          | `WriteAny` (`Asn1Any`)                                           | `ReadAny` (с ожидаемым тегом или без)                                                                         |


**Запись (всегда канон):** definite length; BOOLEAN `0x00` / `0xFF`; BIT STRING с нулевыми хвостовыми битами; время с секундами и суффиксом `Z` (GeneralizedTime: `fractionDigits` 0…7, default 3; без хвостовых нулей дроби); INTEGER — минимальная signed big-endian форма (`BigInteger.TryWriteBytes` / прямой encode для `int`…`ulong`). На типичном размере contents примитивов пишутся без промежуточного `byte[]` (stackalloc / прямой write в буфер); oversized INTEGER/строки — heap.

**Чтение — soft-profile (см. [decisions.md](decisions.md) «мягкое чтение»):** часть запретов DER/X.690 по умолчанию **не** роняет decode; строгий reject — через `Asn1ReaderOptions`. Зафиксированные soft-accept по умолчанию:


| Форма                                             | Default | Строгая опция |
| ------------------------------------------------- | ------- | ------------- |
| Non-minimal INTEGER contents (`02 02 00 01`, …)   | accept  | `RejectNonMinimalInteger` |
| BIT STRING nonzero trailing bits (в т.ч. под DER) | accept  | `RejectBitStringTrailingBits` |


Не в soft-profile (по умолчанию **reject**; ослабление только явным профилем):

| Форма | Default | Опция ослабления |
| --- | --- | --- |
| Non-minimal definite length (`02 81 01 01`, …) | reject | `RejectNonMinimalLength = false` (`AllowNonMinimalLength`) |
| OID overlong base-128 (`… 80 01 …`) | reject | `RejectOverlongOidBase128 = false` (`AllowOverlongOidBase128`) |

Всегда reject (не soft): BOOLEAN length≠1 / constructed; empty INTEGER; truncated EOC; indefinite в DER.

BER: indefinite length, constructed строки/BIT STRING, время без секунд и `±hhmm`; дробь 1…7 с хвостовыми нулями допускается и в DER.

Примитивы runtime: матрица hex в [runtime-csharp/fixtures/ber-der/](../runtime-csharp/fixtures/ber-der/) (`PrimitiveCodecTests`), перекрёстный oracle с `System.Formats.Asn1` (`PrimitiveOracleTests`; Teletex/T61/Videotex/Graphic/General — только свои векторы, Latin-1; UniversalString и UTCTime year pivot — в oracle), внешние фрагменты RFC/X.690 — `ExternalVectorTests`.

Публичный API Writer/Reader — [runtime-csharp/docs/runtime-api.md](../runtime-csharp/docs/runtime-api.md).

## Вне профиля компилятора

Явный `CompileException` с позицией: `CLASS` и information object classes, `COMPONENTS OF`, `REAL`, `EXTERNAL`, параметризованные типы.

## Backlog



### Текущие задачи

1. ~~Для CHOICE типов дать способ формировать его кодом (сейчас все свойства private set)~~ — статические `From…` на альтернативу (`Time.FromUtcTime(…)`); свойства остаются `private set`
2. Добавить **XML-документацию для public API Runtime**
3. ~~Классы, созданные для SEQUENCE OF слишком похожи, может сделать шаблоном?~~ — `List<T>` + `WriteSequenceOf` / `ReadSequenceOf` (SET OF аналогично)
4. ~~Для INTEGER в рантайме добавить тип обёртку~~ — `Asn1Integer` (DER contents + `ToBigInteger` / `GetInt32`…)
5. Для составных типов и отдельных значений в сгенерированном коде в комментарих писать копию их описания в ASN.1. Возможно еще туда же захватывать комментарий из ASN.1 модуля
6. Резолв `ANY DEFINED BY` в конкретный тип по значению sibling-компонента (сейчас `Asn1Any` остаётся сырым TLV).
7. Encode/decode тесты на golden-либе `[runtime-csharp/generated/Asn1Kit.Pkix](../runtime-csharp/generated/Asn1Kit.Pkix/)` (сама либа и сверка `PkixGeneratedCodeTests` уже есть; мелкий round-trip через Roslyn — в `compiler/tests`).
8. ~~Убрать лишний алиас для CHOICE из одного варианта~~ — `Name ::= CHOICE { rdnSequence RDNSequence }` сворачивается в underlying (как typedef-алиас)
9. Для Asn1Integer создать дефотный вариант (например пустой конструктор), чтобы оптимизировать места по типу public Asn1Integer UserCertificate { get; set; } = Asn1Integer.FromInt32(0);
10. ~~Все таки подумать над логикой API, слишком много byte[], можно лучше. В Decode не принимать владение.~~ — decode zero-copy `ReadOnlyMemory` / structs с Memory на буфер reader; detach — `ToArray`/`Clone`. Follow-up: `List<T>` → массивы в `ReadSequenceOf` / `Array.Empty`.
11. ~~Оптимизация API для Time и строк (например оптимизация Choice для случая, если все элементы мапятся в один тип)~~ — однотипные альты → `Kind` + `Value`
12. Добавить опцию Lazy, чтобы откладывать разбор структуры
13. Добавить опцию сохранения исходного (и неизменного) закодированного представления в поле класса
14. Пул массивов, где нужны временные (constructed BER concat, DER SET OF sort).
15. ~~В Encode оптимизировать, не выделять каждый раз на contents примитивов~~ — `Write*` без temp-`byte[]` на типичном размере; остаётся рост `MemoryStream` / `Encode()→ToArray` (RecyclableMemoryStream — отдельно).
16. Второй oracle — BouncyCastle (только при расхождении с BCL; не gate `dotnet test`).


### Крупные задачи

1. C++backend (++`compiler/src/Asn1Kit.Codegen.Cpp`++) и C++ runtime (`runtime-cpp/`) — см. [compiler/docs/playbooks/new-backend.md](../compiler/docs/playbooks/new-backend.md).
2. Инструменты PKI поверх сгенерированного.





