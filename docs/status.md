# Что поддержано сейчас

Срез по слоям. Обновляется тем же изменением, которым расширяется поддержка.

Детали API runtime — [runtime-api.md](../runtime-csharp/docs/runtime-api.md); почему так — [decisions.md](decisions.md); IR — [ir-schema.md](ir-schema.md).

## Типы IR

| `kind` | Компилятор | C# backend | Тесты |
| --- | --- | --- | --- |
| `boolean` | да | `bool` | `ParserTests`, `PrimitiveCodecTests`, `PrimitiveOracleTests` |
| `integer` | да, `namedValues` | `int`…`Asn1Integer` по опции / выводу | `CompilerTests`, `Primitive*`, `RoundTripTests` |
| `enumerated` | да, автоматическая нумерация и `...` | C# `enum`; inline → `Owner_Field` | `ModernAsn1Tests`, `PkixImplicit88Tests`, `PrimitiveCodecTests` |
| `bitString` | да, `namedBits` | `Asn1BitString` или класс + `[Flags]` | `Primitive*`, `RuntimeTests`, `RoundTripTests` |
| `octetString` | да, `CONTAINING` | `ReadOnlyMemory<byte>` / `Asn1Contained<T>` | `ModernAsn1Tests`, `ContainedValueTests`, `Primitive*` |
| `oid` | да (dotted; base-128 в т.ч. `2.999…`) | `Asn1Oid` — единственный codec (string↔arcs↔contents); dotted `string` — warm (`Encode`/`DecodeString`) | `Primitive*`, `RuntimeTests` |
| `string` (12 форм) | да | `string` + `Asn1StringForm` | `Primitive*` (все 12), `RuntimeTests`, `RoundTripTests` |
| `time` (`utc` / `generalized`) | да; `fractionDigits` 0…7 | `DateTimeOffset` + `Asn1TimeForm` | `Primitive*`, `RuntimeTests`, `RoundTripTests` |
| `any` | legacy `definedBy` и overlay; IOC → `selector` + `bindings` | `Asn1Any` / `Owner_Field`; современный выбор строго по ключу | `ModernAsn1Tests`, `ModernRfcTests`, `OpenTypeBindingsTests` |
| `sequence` | да, `extensible` | класс или `struct` (`options.csharp.valueType`); `lazy` → `Asn1Lazy<T>`; `retainEncoded` → `Asn1Value<T>` с исходным TLV | `RoundTripTests`, `Pkix*`, `Asn1Kit.Pkix.Tests` |
| `set` | да | класс/`struct`; DER-порядок по тегу; `lazy` / `retainEncoded` | `RoundTripTests`, `ParserTests`, `RuntimeTests` |
| `choice` | да | `…Kind` + `From…`; однотипные → `Kind`+`Value`; один вариант → алиас | `ParserTests`, `Pkix*`, `RoundTripTests`, `Asn1Kit.Pkix.Tests` |
| `sequenceOf` | да | `T[]` (typedef сворачивается); `lazy` / `retainEncoded` на OF | `ParserTests`, `RoundTripTests`, `PkixGeneratedCodeTests`, `RuntimeTests` |
| `setOf` | да | `T[]` + DER-сортировка в runtime; `lazy` / `retainEncoded` | `RoundTripTests`, `ParserTests`, `RuntimeTests` |
| `ref` | да (`IMPORTS`) | алиасы сворачиваются; cross-module → квалиф. имя | `Pkix*`, `ImportResolutionTests`, `RoundTripTests` |

Неподдержанный kind → отказ до генерации (`EnsureBackendSupport`).

## Значения, теги, constraints

- Значения IR: `integer`, `boolean`, `null`, `oid`, `string`, `bitString`, `octetString`, `structured`, `collection`, `choice`, `typed`, `ref` (в скомпилированном IR обычно раскрыт).
- C# компоненты с `DEFAULT` генерируются как ненуллабельные свойства с ASN.1-значением по умолчанию; decoder подставляет его при отсутствии компонента, DER encoder не записывает равное default значение.
- `EXPLICIT` / `IMPLICIT` / `AUTOMATIC TAGS`; `IMPORTS` между переданными файлами.
- Тег без mode на локальном или импортированном `CHOICE` раскрывается как `EXPLICIT`.
- Open-type `bindings`: CLI `--bindings` / overlay; ключи `Module.Type.field`.
- C# `ANY DEFINED BY`: при неизвестном OID примитив с однозначно подходящей существующей альтернативой декодируется в её свойство по universal-тегу, сохраняя wire-форму и `Kind`. Неоднозначные, составные и нераспознанные значения остаются в `Asn1Any`; IMPLICIT-тег не используется для угадывания типа. Правило действует в soft и strict; mismatch известного binding сохраняет прежнее поведение.
- Нетегированные `CHOICE` в open-type bindings рекурсивно раскрываются в альтернативы `Owner_Field`, включая именованные, импортированные и inline-типы. Вложенный объект `CHOICE` не создаётся; свойства группируются по CLR-типу, `Kind` сохраняет выбранную wire-альтернативу. OID ограничивает набор допустимых веток; тегированный или рекурсивный `CHOICE` остаётся отдельной альтернативой. Самостоятельные типы `CHOICE` сохраняют свой API.
- Options patch: CLI `--patch` / `IrOptionsPatch`; `modules`, `fields` (`Module.Type.field`), `types` (`Module.Type`); bench — [cms-2004-bench.patch.json](../compiler/fixtures/ir/cms-2004-bench.patch.json).
- `SIZE` / диапазоны → `constraint.size` / `constraint.value`; прочее → `constraint.unsupported`.
- Вне профиля (явный `CompileException`): `COMPONENTS OF`, `REAL`, `EXTERNAL`, параметризованные значения/объекты и формы IOC, перечисленные ниже.

## Современные PKIX/CMS

Проверяемый корпус — **35 модулей** RFC 5911/5912, RFC 6268 и RFC 8410 со всеми зависимостями внутри корпуса. Исходники и документированные исправления: [modern/README.md](../compiler/fixtures/asn1/modern/README.md). Полный корпус компилируется в [modern-pkix-cms.json](../compiler/fixtures/ir/modern-pkix-cms.json), C# выпускается в отдельной сборке [Asn1Kit.Modern](../runtime-csharp/generated/Asn1Kit.Modern/README.md), с namespace на каждый модуль. Исходный граф PKIX/CMS/DVCS и его namespace сохраняются: современный граф имеет другие module identifiers.

Поддержано:

- `CLASS`, фиксированные поля типов/значений, поля объектов/наборов, обязательные/OPTIONAL/DEFAULT поля, default object syntax и `WITH SYNTAX` с вложенными optional groups по шаблону класса.
- Объекты, aliases, наборы с объединением (`|`/`,`), ссылками, inline objects и `...`; обращения `object.&field`, включая вложенные поля. Проверяются governors, обязательные поля и конфликты UNIQUE для ключей OID/INTEGER.
- Параметризованные типы с параметрами типов, классов, значений и object sets; специализации с одинаковой структурой и разными IOC-таблицами сворачиваются в один IR-тип, таблицы находятся на `ref.openTypes`. Структурно разные типы получают имена без хеша, преимущественно от ASN.1 typedef. Анонимные actual types сохраняют TAGS и ссылки исходного модуля. Обычная рекурсия остаётся ссылкой; растущая параметризация завершается диагностикой по лимиту.
- `IMPORTS` разрешаются по объявлениям/governors, квалификация модулем снимает неоднозначность одноимённых наборов. При отсутствующем текстовом имени допускается точное совпадение явно указанного module OID; эвристики похожих имён нет.
- Component relation selectors: локальный, внешний контекст и вложенный путь; `SEQUENCE OF`/`SET OF` передают состояние элементам через static callbacks. SET откладывает зависимое значение до чтения дискриминатора.
- `OCTET STRING` / `BIT STRING (CONTAINING ...)`: исходные contents и типизированное содержимое, включая OID-таблицы. CMS `id-data`, которому не объявлена ASN.1-структура, сохраняет сырые октеты.
- Составные assignments/DEFAULT (SEQUENCE/SET, OF, CHOICE, typed open values); каждый экземпляр получает собственные изменяемые значения. DER сравнивает DEFAULT структурно, SET OF — как мультимножество.
- Граница расширений, trailing root и `[[n: ...]]`; неизвестные additions сохраняются, обязательные поля группы проверяются при её присутствии. Расширяемый CHOICE сохраняет неизвестную альтернативу. `INSTANCE OF TYPE-IDENTIFIER` поддерживается в стандартной форме.

Современные открытые типы при неизвестном OID сохраняют raw TLV без угадывания по тегу; у несвёрнутых типов несовместимое содержимое известного binding вызывает `Asn1Exception` при `Decode`. Пустой набор `{...}` остаётся пустым. Legacy ANY сохраняет прежнюю политику fallback/mismatch.

Для свёрнутой табличной специализации C# сохраняет raw-контейнер и публикует конечный CLR-тип непосредственно в `Binding<T>`: например, `CertExtensionsBinding<AuthorityKeyIdentifier>` / `NumbersPayloadBinding<int>` и `SignerInfoSignedAttrsBinding<ReadOnlyMemory<byte>[]>`. Имя каталога берётся из `ref.openTypes[].table` (ASN.1 object set) плюс open-field при необходимости (`SignatureAlgorithmsParametersBindings`), а не из SEQUENCE-владельца. Descriptor содержит OID/INTEGER key, decoder и encoder; `Set…(binding, value)` записывает DER в raw-поле, устанавливает локальный selector и сохраняет остальные поля контейнера. OF сохраняет форму массивов, CONTAINING поддерживает исходные октеты и `HasValue`/`Value`; типизированный BIT STRING требует выравнивания по октетам. Отдельные классы для каждого известного binding и пары `To…` / `TryFrom…` не генерируются. Примитивные leaf-типы используют singleton-кодеки runtime `Asn1Codecs`; непримитивные wire-специализации дедуплицируются в один модульный `__…OpenTypeCodecs` (`Asn1Codec<T>`), общий для instance Uses и table Wrappers; instance Uses передают `codec.Decode`/`codec.Encode` method group в `Binding<T>` без тонких обёрток; table Wrappers оставляют `DecodeX` (source → raw) и передают `codec.Encode` / `BindingCodec.Encode` без тонкой `EncodeX` (адаптер только для `EncodeEach` / `EncodeContained`). Простые raw-формы используют общие generic-адаптеры вместо классов с повторяющимися `Decode0`/`Encode0`.

Для каждой таблицы open type C# генерирует отдельный тип descriptor и каталог. Сайты с одинаковым контейнером, selector, формой payload и набором bindings переиспользуют один каталог (SEQUENCE-владелец в identity не входит). Contextual API живёт на контейнере (`algorithm.TryDecodeParameters(SignatureAlgorithmsParametersBindings.…)` / `extension.SetPayload(…)`); instance-методы на SEQUENCE-родителе — тонкие forwarders к тому же каталогу. Поиск по OF: `TryGetExtensions` на владельце или `extensions.TryGet(CertExtensionsBindings.…)` на массиве; overload с `out Extension raw` возвращает совпавший контейнер. Тип результата выводится из descriptor; именованные convenience-методы — для encodeable `Binding<T>` (не для decoder-only `*StringValue`), на контейнере без коллизий имён между таблицами. Несовместимые таблицы нельзя смешать через owner `TryGet`; `Create<T>(key, decoder, encoder)` добавляет полный codec (decoder принимает raw-контейнер). Decoder-only `Create<T>(key, decoder)` и `DecoderBinding<T>` остаются для сплющенных ветвей `CHOICE`. Отсутствие даёт `false`, дубликаты поиска и повреждённые известные значения вызывают `Asn1Exception`. Selectors предков передаются явным контекстом; setter проверяет ключ предка и не изменяет его. Семейство `SIGNED` генерируется как `Signed<T>` с именованными производными; поля без объявленной таблицы остаются raw.

Это профиль структуры и BER/DER-кодеков, **не полная поддержка X.680–X.683**. Пока не исполняются закрытость наборов, `WITH COMPONENTS`, фиксированные value sets и правила присутствия параметров (`PRESENT`/`ABSENT`/`OPTIONAL`). Нераспознанные ограничения сохраняются в `constraint.unsupported`; поля IOC остаются в частной семантической модели. Переменные governors `&Type`, параметризованные CLASS/объекты/наборы, произвольные операции пересечения/разности наборов, неоднозначные optional templates и обобщённый `INSTANCE OF` вне профиля. Контекст selectors требуется в месте определения; произвольное перенесение такого типа через отдельный typedef не поддерживается.

Проверки: `ModernAsn1Tests` (минимальные конструкции, ошибки, специализации, selectors, defaults, extensions, Roslyn и DER), `OpenTypeBindingTests` (typed bindings, raw metadata, таблицы источников, aliases, imports, OF/CONTAINING, lazy/retainEncoded, value types), `ModernOpenTypeBindingTests` (PKIX/CMS, строковые CHOICE и внешние сертификаты), `ModernRfcTests` (весь IR/C# golden, внешние сертификаты, CMS, RSA-PSS), `ContainedValueTests` (DER/BER, opaque contents, повреждения). Старый граф и codec-тесты DVCS остаются в обязательном полном `dotnet test Asn1Kit.sln`.

Bindings с типом `NULL` (включая алиасы) присутствуют в каталогах как `Binding<Asn1Null>`; внутри OF сохраняется соответствующая форма массива. Основной descriptor-overload различает закодированный `NULL` и отсутствие поля. Именованный setter имеет форму `Set…()` без бессмысленного аргумента `Asn1Null`; именованные convenience `TryDecode…` / `TryGet…` для одиночного `NULL` не генерируются (`out Asn1Null` бесполезен). Generic API остаётся полностью типизированным. Регрессии: `NullAlgorithmBindingsDoNotGenerateDataWrappersOrEmptySourceMethods`, `NullBindingsAreAvailableForContainingAndOfContainers`.

Именованные OID в DEFAULT и open-type keys переиспользуются из каталогов всего документа: локальные значения имеют приоритет, затем идут импортированные модули. Внешние ссылки учитывают C# namespace, а `generate: false` исключает значение из поиска; отдельный разбор dotted-строки остаётся только для ключей без доступного именованного значения.

Сборка современного generated-кода пока выдаёт nullable-предупреждения для обязательных ссылочных свойств и выбранных ветвей CHOICE. Объекты для encode должны быть заполнены вызывающим кодом; nullable-контекст включён, предупреждения не подавляются.

## Runtime BER/DER

Runtime-значения и обёртки поддерживают полезный `ToString()`: числа и время независимы от культуры, бинарные данные показывают только длину, ANY — тег и длину TLV. Форматирование `Asn1Lazy<T>` не запускает декодирование. Сгенерированные CHOICE и ANY DEFINED BY с bindings показывают выбранное значение без имени альтернативы, используя существующие `Kind` и свойства без дополнительного состояния. Контракт описан в [runtime-api.md](../runtime-csharp/docs/runtime-api.md).

Полный инвентарь `Write*` / `Read*` и ownership — [runtime-api.md](../runtime-csharp/docs/runtime-api.md). Hex-матрица — [ber-der/](../runtime-csharp/fixtures/ber-der/); oracle BCL — `PrimitiveOracleTests`.

**Запись** всегда канонический DER. **Чтение** — soft-profile ([decisions.md](decisions.md), инвентарь опций — [runtime-api.md](../runtime-csharp/docs/runtime-api.md)):

| Soft-accept (default) | Strict-флаг |
| --- | --- |
| Non-minimal INTEGER contents | `RejectNonMinimalInteger` |
| BIT STRING nonzero trailing bits | `RejectBitStringTrailingBits` |

Не soft (default reject): non-minimal length, OID overlong base-128. Всегда reject: BOOLEAN length≠1 / constructed; empty INTEGER; truncated EOC; indefinite в DER. BER: indefinite, constructed строки/BIT STRING, время без секунд / `±hhmm`.

## Демонстрация проверки CMS

Демонстрационный проект [Asn1Kit.Cms.Demo](../runtime-csharp/examples/Asn1Kit.Cms.Demo/) проверяет один сценарий attached `SignedData` в двух режимах: `Asn1Kit.Pkix.Bench` и `Asn1Kit.Modern` (`--modern`). Он проверяет структуру, атрибуты и связь подписанта с вложенным сертификатом. С переданными `--trusted-root` и `--certificate` инспекторы строят цепочку из generated-типов, сопоставляют `AuthorityKeyIdentifier` с `SubjectKeyIdentifier` либо парой issuer/serial и используют сохранённые исходные TLV сертификата, TBS, имён и SPKI; общий verifier проверяет подписи RSA/SHA-256. Без доверенных корней работает учебная заглушка. Ограничения алгоритма и различия API перечислены в README проекта.

## Бенчмарки PKIX/CMS

[runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks](../runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks/) — BenchmarkDotNet Decode/Encode для `Certificate`, `CertificateList`, CMS `ContentInfo` (attached SignedData). Типы из [Asn1Kit.Pkix.Bench](../runtime-csharp/generated/Asn1Kit.Pkix.Bench/) (`Asn1Kit.Pkix.Bench` / `Asn1Kit.Cms.Bench`), собранного из golden `cms-2004.json` + [cms-2004-bench.patch.json](../compiler/fixtures/ir/cms-2004-bench.patch.json) (lazy на `CertificateChoices.certificate`; `retainEncoded` на `Certificate.tbsCertificate` и TBS Name/SPKI/Extensions; `AttributeTypeAndValue` как `struct`). Encode Cert/CRL — hand-built object graph (не decode→encode). CMS-группы: Lazy / Lazy+Materialize / Eager (golden) / BCL ± materialize / BouncyCastle. Не в gate `dotnet test`.

```powershell
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks
```

## Сгенерированные протокольные модули

Проект [Asn1Kit.Pkix](../runtime-csharp/generated/Asn1Kit.Pkix/) содержит PKIX/CMS и полный граф DVCS: `PKIXDVCS` (RFC 3029), `PKIXCMP` (RFC 2510), `PKIXCRMF` (RFC 2511), `OCSP` (RFC 2560), `ExtendedSecurityServices` (RFC 2634), `SecureMimeMessageV3` (RFC 2633) и предоставленный для CMP модуль PKCS#10 (RFC 2314). Namespace разделены по протоколам; отдельного golden IR для этого графа нет, golden C# воспроизводится из `.asn` + [dvcs.patch.json](../compiler/fixtures/ir/dvcs.patch.json).

DVCS codec-тесты покрывают request `message` / `messageImprint`, обе response-альтернативы, DER round-trip и повреждённый TLV. Криптографическая проверка и транспорт не входят в этот слой.

## Backlog

### Открыто

1. Расширение современного профиля: исполняемые table/presence constraints, `WITH COMPONENTS`, переменные governors и остальные формы X.681–X.683. Базовый корпус RFC и семантическое разрешение реализованы; отдельные curated `.asn` без CLASS не нужны как предварительный этап. Legacy overlay `ExtensionAttribute` остаётся самостоятельной задачей.
2. Пул массивов (constructed BER concat, DER SET OF sort)
3. Второй oracle — BouncyCastle (не gate `dotnet test`)
4. **DER: reject constructed OCTET / BIT STRING / string** (или soft-флаг в `Asn1ReaderOptions`, default как сейчас accept; выровнять код с формулировкой § Runtime «BER: constructed…»). Фикстуры + runtime-api soft-inventory
5. **Лимиты hostile BER**: ограничить глубину рекурсивного indefinite scan и общий размер materialized constructed values

### Крупные

1. C++ backend и C++ runtime — [new-backend.md](../compiler/docs/playbooks/new-backend.md)
2. Инструменты PKI поверх сгенерированного
