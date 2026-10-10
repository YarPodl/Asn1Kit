# Что поддержано сейчас

Срез по слоям. Обновляется тем же изменением, которым расширяется поддержка.

Детали API runtime — [runtime-api.md](../runtime-csharp/docs/runtime-api.md); почему так — [decisions.md](decisions.md); IR — [ir-schema.md](ir-schema.md).

## Типы IR

| `kind` | Компилятор | C# backend | C++ backend | Тесты |
| --- | --- | --- | --- | --- |
| `boolean` | да | `bool` | `bool` | `ParserTests`, `PrimitiveCodecTests`, `PrimitiveOracleTests`, `CppCodegenTests` |
| `integer` | да, `namedValues` | `int`…`Asn1Integer` по опции / выводу | `int32_t`…`asn1kit::integer`; namedValues → константы | `CompilerTests`, `Primitive*`, `RoundTripTests`, `CppCodegenTests` |
| `enumerated` | да, автоматическая нумерация и `...` | C# `enum`; inline → `Owner_Field` | `enum class` + `encode_`/`decode_` | `ModernAsn1Tests`, `PkixImplicit88Tests`, `PrimitiveCodecTests`, `CppCodegenTests` |
| `bitString` | да, `namedBits` | `Asn1BitString` или класс + `[Flags]` | `asn1kit::bit_string` или класс + Flags | `Primitive*`, `RuntimeTests`, `RoundTripTests`, `CppCodegenTests` |
| `octetString` | да, `CONTAINING` | `ReadOnlyMemory<byte>` / `Asn1Contained<T>` | `asn1kit::bytes` (CONTAINING — нет) | `ModernAsn1Tests`, `ContainedValueTests`, `Primitive*`, `CppCodegenTests` |
| `oid` | да (dotted; base-128 в т.ч. `2.999…`) | `Asn1Oid` — единственный codec (string↔arcs↔contents); dotted `string` — warm (`Encode`/`DecodeString`) | `asn1kit::oid` + каталог `…Oids` | `Primitive*`, `RuntimeTests`, `CppCodegenTests` |
| `string` (12 форм) | да | `string` + `Asn1StringForm` | `std::string` + `string_form` | `Primitive*` (все 12), `RuntimeTests`, `RoundTripTests` |
| `time` (`utc` / `generalized`) | да; `fractionDigits` 0…7 | `DateTimeOffset` + `Asn1TimeForm` | `asn1kit::utc_date_time` + `time_form` | `Primitive*`, `RuntimeTests`, `RoundTripTests` |
| `any` | `definedBy` / overlay / IOC → `selector` + `ref.openTypes` (или definition-scoped bindings) | `Asn1Any` + `Binding<T>` / каталоги; unknown→raw, known mismatch→reject | `asn1kit::any` (raw TLV; Binding/open-type — нет) | `ModernAsn1Tests`, `ModernRfcTests`, `OpenTypeBindingsTests`, `OpenTypeWrapperTests`, `CppCodegenTests`, `any_tests` |
| `sequence` | да, `extensible` | класс или `struct` (`options.csharp.valueType`); `lazy` → `Asn1Lazy<T>`; `retainEncoded` → `Asn1Value<T>` с исходным TLV | `struct` + scopes; extensible/lazy/retainEncoded — нет | `RoundTripTests`, `Pkix*`, `Asn1Kit.Pkix.Tests`, `CppCodegenTests`, `generated_person_tests` |
| `set` | да | класс/`struct`; DER-порядок по тегу; `lazy` / `retainEncoded` | `struct`; encode по тегу; extensible — нет | `RoundTripTests`, `ParserTests`, `RuntimeTests`, `CppCodegenTests` |
| `choice` | да | `…Kind` + `From…`; однотипные → `Kind`+`Value`; один вариант → алиас | `…_kind` + `std::variant`; один вариант → алиас | `ParserTests`, `Pkix*`, `RoundTripTests`, `CppCodegenTests` |
| `sequenceOf` | да | `T[]` (typedef сворачивается); `lazy` / `retainEncoded` на OF | `std::vector<T>` | `ParserTests`, `RoundTripTests`, `CppCodegenTests` |
| `setOf` | да | `T[]` + DER-сортировка в runtime; `lazy` / `retainEncoded` | `std::vector<T>` + DER sort в runtime | `RoundTripTests`, `ParserTests`, `CppCodegenTests` |
| `ref` | да (`IMPORTS`) | алиасы сворачиваются; cross-module → квалиф. имя | алиасы сворачиваются; cross-module → `::ns::Type` | `Pkix*`, `ImportResolutionTests`, `RoundTripTests`, `CppCodegenTests` |

Неподдержанный kind → отказ до генерации (`EnsureBackendSupport`).

## Значения, теги, constraints

- Значения IR: `integer`, `boolean`, `null`, `oid`, `string`, `bitString`, `octetString`, `structured`, `collection`, `choice`, `typed`, `ref` (в скомпилированном IR обычно раскрыт).
- C# `DEFAULT`: ненуллабельное свойство; decoder подставляет значение при отсутствии; DER encoder не пишет равное default.
- `EXPLICIT` / `IMPLICIT` / `AUTOMATIC TAGS`; `IMPORTS` между переданными файлами; тег без mode на `CHOICE` → `EXPLICIT`.
- Open-type overlay: CLI `--bindings`, ключи `Module.Type.field`. Options patch: CLI `--patch` / `IrOptionsPatch`; продуктовый PKIX/CMS — [dvcs.patch.json](../compiler/fixtures/ir/dvcs.patch.json).
- Open-type (overlay и IOC): неизвестный ключ — raw `Asn1Any`; известный mismatch — reject. Soft/universal-tag fallback для open types снят. Детали эмиссии — [csharp-backend.md](../compiler/docs/playbooks/csharp-backend.md).
- `SIZE` / диапазоны → `constraint.size` / `constraint.value`; прочее → `constraint.unsupported`.
- Вне профиля (`CompileException`): `COMPONENTS OF`, `REAL`, `EXTERNAL` и формы IOC/параметризации из § «Пока не».

## Современные PKIX/CMS

Корпус — **35 модулей** RFC 5911/5912/6268/8410 ([modern/README.md](../compiler/fixtures/asn1/modern/README.md) → IR [modern-pkix-cms.json](../compiler/fixtures/ir/modern-pkix-cms.json) → сборка [Asn1Kit.Modern](../runtime-csharp/generated/Asn1Kit.Modern/README.md)). Legacy PKIX/CMS/DVCS сохранён отдельно (другие module identifiers).

**Поддержано:** `CLASS` / objects/sets / `WITH SYNTAX`; параметризованные типы со свёрткой одинаковых IOC-таблиц в `ref.openTypes`; governors и UNIQUE для OID/INTEGER; component relation selectors (в т.ч. через OF/SET); `CONTAINING`; составные DEFAULT; границы расширений / `[[n: …]]`; стандартный `INSTANCE OF TYPE-IDENTIFIER`; `IMPORTS` по имени или точному module OID (без эвристик).

**Open-type (C#):** один Binding-путь для Modern и legacy product (`Asn1Kit.Pkix`/`Cms`): raw-контейнер + `Binding<T>` / table-каталог / `TryDecode`·`Set`·`TryGet` / `Create` (local selector) / `AsString`; неизвестный ключ — raw; mismatch известного — reject. Overlay — источник таблиц (`OpenTypeBindings` → normalize). Потребительский срез — [Asn1Kit.Modern README](../runtime-csharp/generated/Asn1Kit.Modern/README.md); правила эмиссии — [csharp-backend.md](../compiler/docs/playbooks/csharp-backend.md).

**Пока не** (профиль структуры и BER/DER, не полные X.680–X.683): закрытость наборов, `WITH COMPONENTS`, presence (`PRESENT`/`ABSENT`/`OPTIONAL`), переменные governors, параметризованные CLASS/объекты/наборы, пересечения/разности наборов, неоднозначные optional templates, обобщённый `INSTANCE OF`, перенос selector-типа через отдельный typedef.

Проверки: `ModernAsn1Tests`, `OpenTypeBindingTests`, `ModernOpenTypeBindingTests`, `ModernRfcTests`, `ContainedValueTests` + полный `dotnet test Asn1Kit.sln`. Nullable-предупреждения в modern generated пока не подавляются.

## Runtime BER/DER

Инвентарь `Write*` / `Read*`, ownership и `ToString` — [runtime-api.md](../runtime-csharp/docs/runtime-api.md). Warm top-level decode: `Asn1Utils.Decode` / `DecodeRetained`. Hex-матрица — [ber-der/](../runtime-csharp/fixtures/ber-der/); oracle BCL — `PrimitiveOracleTests`.

**C++ runtime** ([runtime-cpp/](../runtime-cpp/)): примитивы BER/DER **да** (boolean/null/integer/enumerated/octet/oid/bitstring/string/time/`any` + soft-profile + `asn1kit::bytes`); публичные SEQUENCE/SET/OF/EXPLICIT `enter_*` / `with_*` **да**; те же layer-1 фикстуры.

**C++ codegen** ([Asn1Kit.Codegen.Cpp](../compiler/src/Asn1Kit.Codegen.Cpp/)): MVP **да** (`--lang cpp`, golden `compiler/fixtures/codegen-cpp/`, native Person round-trip). Bare `any` → `asn1kit::any`. Пока нет: open-type Binding, `CONTAINING`, `extensible`, `lazy`, `retainEncoded`, PKIX/Modern C++ golden.

**Запись** — канонический DER. **Чтение** — soft-profile ([decisions.md](decisions.md); полный инвентарь — [runtime-api.md](../runtime-csharp/docs/runtime-api.md)):

| Soft-accept (default) | Strict-флаг |
| --- | --- |
| Non-minimal INTEGER contents | `RejectNonMinimalInteger` |
| BIT STRING nonzero trailing bits | `RejectBitStringTrailingBits` |

Не soft (default reject): non-minimal length, OID overlong base-128. Всегда reject: BOOLEAN length≠1 / constructed; empty INTEGER; truncated EOC; indefinite в DER. BER: indefinite, constructed строки/BIT STRING, время без секунд / `±hhmm`.

## Продуктовые артефакты

- Demo CMS: [Asn1Kit.Cms.Demo](../runtime-csharp/examples/Asn1Kit.Cms.Demo/) — `verify` / `print-cert` / `print-cms` / educational `sign` (`Asn1Kit.Pkix` / `--modern`).
- Бенчмарки (не gate): [Asn1Kit.Pkix.Benchmarks](../runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks/).
- Legacy generated: [Asn1Kit.Pkix](../runtime-csharp/generated/Asn1Kit.Pkix/) (PKIX/CMS/DVCS + зависимости; options — [dvcs.patch.json](../compiler/fixtures/ir/dvcs.patch.json)).

## Backlog

### Открыто

1. Расширение современного профиля: исполняемые table/presence constraints, `WITH COMPONENTS`, переменные governors и остальные формы X.681–X.683. Базовый корпус RFC и семантическое разрешение реализованы; отдельные curated `.asn` без CLASS не нужны как предварительный этап. Legacy overlay `ExtensionAttribute` остаётся самостоятельной задачей.
2. Пул массивов (constructed BER concat, DER SET OF sort)
3. Второй oracle — BouncyCastle (не gate `dotnet test`)
4. **DER: reject constructed OCTET / BIT STRING / string** (или soft-флаг в `Asn1ReaderOptions`, default как сейчас accept; выровнять код с формулировкой § Runtime «BER: constructed…»). Фикстуры + runtime-api soft-inventory
5. **Лимиты hostile BER**: ограничить глубину рекурсивного indefinite scan и общий размер materialized constructed values

### Крупные

1. C++ codegen: паритет с C# — open-type Binding, `CONTAINING`, extensible SEQUENCE/SET, `lazy` / `retainEncoded`, PKIX/Modern golden (нужен runtime Lazy/Contained + Binding) — [cpp-backend.md](../compiler/docs/playbooks/cpp-backend.md)
2. Инструменты PKI поверх сгенерированного
