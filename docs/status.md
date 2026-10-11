# Что поддержано сейчас

Срез по слоям. Обновляется тем же изменением, которым расширяется поддержка.

Детали API runtime — [runtime-api.md](../runtime-csharp/docs/runtime-api.md); почему так — [decisions.md](decisions.md); IR — [ir-schema.md](ir-schema.md).

## Типы IR

| `kind` | Компилятор | C# backend | C++ backend | Тесты |
| --- | --- | --- | --- | --- |
| `boolean` | да | `bool` | `bool` | `ParserTests`, `PrimitiveCodecTests`, `PrimitiveOracleTests`, `CppCodegenTests` |
| `integer` | да, `namedValues` | `int`…`Asn1Integer` по опции / выводу | `int32_t`…`asn1kit::integer`; namedValues → константы | `CompilerTests`, `Primitive*`, `RoundTripTests`, `CppCodegenTests` |
| `enumerated` | да, автоматическая нумерация и `...` | C# `enum`; inline → `Owner_Field` | `enum class` + `encode_`/`decode_` | `ModernAsn1Tests`, `PkixImplicit88Tests`, `PrimitiveCodecTests`, `CppCodegenTests` |
| `bitString` | да, `namedBits` | `Asn1BitString` или класс + `[Flags]` | `asn1kit::bit_string` / Flags / `asn1kit::contained<T>` | `Primitive*`, `RuntimeTests`, `RoundTripTests`, `CppCodegenTests`, `contained_tests` |
| `octetString` | да, `CONTAINING` | `ReadOnlyMemory<byte>` / `Asn1Contained<T>` | `asn1kit::bytes` / `asn1kit::contained<T>` | `ModernAsn1Tests`, `ContainedValueTests`, `Primitive*`, `CppCodegenTests`, `contained_tests` |
| `oid` | да (dotted; base-128 в т.ч. `2.999…`) | `Asn1Oid` — единственный codec (string↔arcs↔contents); dotted `string` — warm (`Encode`/`DecodeString`) | `asn1kit::oid` + каталог `…Oids` | `Primitive*`, `RuntimeTests`, `CppCodegenTests` |
| `string` (12 форм) | да | `string` + `Asn1StringForm` | `std::string` + `string_form` | `Primitive*` (все 12), `RuntimeTests`, `RoundTripTests` |
| `time` (`utc` / `generalized`) | да; `fractionDigits` 0…7 | `DateTimeOffset` + `Asn1TimeForm` | `asn1kit::utc_date_time` + `time_form` | `Primitive*`, `RuntimeTests`, `RoundTripTests` |
| `any` | `definedBy` / overlay / IOC → `selector` + `ref.openTypes` (или definition-scoped bindings) | `Asn1Any` + `Binding<T>` / каталоги; unknown→raw, known mismatch→reject | `asn1kit::any` + Binding (direct / CONTAINING / OF / ancestor `levels`, local OID selector: key-only `*_binding<T>`, wire из `T` / `asn_string`) | `ModernAsn1Tests`, `ModernRfcTests`, `OpenTypeBindingsTests`, `OpenTypeWrapperTests`, `CppCodegenTests`, `any_tests`, `codec_tests`, `open_binding_tests` |
| `sequence` | да, `extensible` | класс или `struct` (`options.csharp.valueType`); `lazy` → `Asn1Lazy<T>`; `retainEncoded` → `Asn1Value<T>` с исходным TLV | `struct` + scopes; extensible → skip-unknown + extension groups; `lazy` → `asn1kit::lazy<T>`; `retainEncoded` → `asn1kit::value<T>` | `RoundTripTests`, `Pkix*`, `Asn1Kit.Legacy.Tests`, `CppCodegenTests`, `generated_person_tests`, `generated_extensible_tests`, `generated_deferred_tests`, `value_lazy_tests` |
| `set` | да | класс/`struct`; DER-порядок по тегу; `lazy` / `retainEncoded` | `struct`; encode по тегу / extensible → `enter_set_of` + skip-unknown; `lazy`/`retainEncoded` как у SEQUENCE | `RoundTripTests`, `ParserTests`, `RuntimeTests`, `CppCodegenTests`, `generated_extensible_tests`, `generated_deferred_tests` |
| `choice` | да | `…Kind` + `From…`; однотипные → `Kind`+`Value`; один вариант → алиас; extensible → `Unknown` | `…_kind` + `std::variant`; один вариант → алиас; extensible → `unknown` + `asn1kit::any` | `ParserTests`, `Pkix*`, `RoundTripTests`, `CppCodegenTests`, `generated_extensible_tests` |
| `sequenceOf` | да | `T[]` (typedef сворачивается); `lazy` / `retainEncoded` на OF | `std::vector<T>`; `lazy`/`retainEncoded` → обёртка над вектором | `ParserTests`, `RoundTripTests`, `CppCodegenTests` |
| `setOf` | да | `T[]` + DER-сортировка в runtime; `lazy` / `retainEncoded` | `std::vector<T>` + DER sort; `lazy`/`retainEncoded` → обёртка над вектором | `RoundTripTests`, `ParserTests`, `CppCodegenTests` |
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

Корпус Modern — **24 модуля** RFC 5911/5912/6268/8410 ([modern/README.md](../compiler/fixtures/asn1/modern/README.md) → IR [modern-pkix-cms.json](../compiler/fixtures/ir/modern-pkix-cms.json) → сборка [Asn1Kit.Modern](../runtime-csharp/generated/Asn1Kit.Modern/README.md)). Legacy PKIX/CMS/DVCS — [Asn1Kit.Legacy](../runtime-csharp/generated/Asn1Kit.Legacy/) (регрессия; другие module identifiers).

**Поддержано:** `CLASS` / objects/sets / `WITH SYNTAX`; параметризованные типы со свёрткой одинаковых IOC-таблиц в `ref.openTypes`; governors и UNIQUE для OID/INTEGER; component relation selectors (в т.ч. через OF/SET); `CONTAINING`; составные DEFAULT; границы расширений / `[[n: …]]`; стандартный `INSTANCE OF TYPE-IDENTIFIER`; `IMPORTS` по имени или точному module OID (без эвристик).

**Open-type (C#):** один Binding-путь для Modern и Legacy (`Asn1Kit.Legacy.*`): raw-контейнер + `Binding<T>` / table-каталог / `TryDecode`·`Set`·`TryGet` / `Create` (local selector) / `AsString`; неизвестный ключ — raw; mismatch известного — reject. Overlay — источник таблиц (`OpenTypeBindings` → normalize). Потребительский срез — [Asn1Kit.Modern](../runtime-csharp/generated/Asn1Kit.Modern/README.md); правила эмиссии — [csharp-backend.md](../compiler/docs/playbooks/csharp-backend.md).

**Пока не** (профиль структуры и BER/DER, не полные X.680–X.683): закрытость наборов, `WITH COMPONENTS`, presence (`PRESENT`/`ABSENT`/`OPTIONAL`), переменные governors, параметризованные CLASS/объекты/наборы, пересечения/разности наборов, неоднозначные optional templates, обобщённый `INSTANCE OF`, перенос selector-типа через отдельный typedef.

Проверки: `ModernAsn1Tests`, `OpenTypeBindingTests`, `ModernOpenTypeBindingTests`, `ModernRfcTests`, `ContainedValueTests` + полный `dotnet test Asn1Kit.sln`. Nullable-предупреждения в modern generated пока не подавляются.

## Runtime BER/DER

Инвентарь `Write*` / `Read*`, ownership и `ToString` — [runtime-api.md](../runtime-csharp/docs/runtime-api.md). Warm top-level decode: `Asn1Utils.Decode` / `DecodeRetained`. Hex-матрица — [ber-der/](../runtime-csharp/fixtures/ber-der/); oracle BCL — `PrimitiveOracleTests`.

**C++ runtime** ([runtime-cpp/](../runtime-cpp/)): примитивы BER/DER **да** (boolean/null/integer/enumerated/octet/oid/bitstring/string/time/`any`/`contained`/`encode_open`/`asn_string` + soft-profile + `asn1kit::bytes`); публичные SEQUENCE/SET/OF/EXPLICIT `enter_*` / `with_*` **да**; те же layer-1 фикстуры.

**C++ codegen** ([Asn1Kit.Codegen.Cpp](../compiler/src/Asn1Kit.Codegen.Cpp/)): MVP **да** (`--lang cpp`, golden SoT `runtime-cpp/generated/`, Person + open-binding + extensible + deferred round-trip). Bare `any` → `asn1kit::any`; `CONTAINING` → `asn1kit::contained<T>`; open-type Binding (direct / CONTAINING / OF / ancestor; OID и INTEGER selector; nested selector path); peer `#include` по deps; нетривиальные SCC именованных типов → `cycle_….hpp` (амальгама + trivial closure); CHOICE с одинаковым C++ payload → `…_box` в `std::variant`; `lazy` / `retainEncoded` (encode сохраняет original TLV). **Modern C++ golden** — 24 модуля + `cycle_*.hpp` в `runtime-cpp/generated/modern/` (`scripts/regenerate-modern-cpp.ps1`); native Certificate DER round-trip — `generated_modern_certificate_tests` (`TrustAnchorRootCertificate.crt`, `ValidCertificatePathTest1EE.crt`). Legacy Explicit88 C++ golden — вне среза (регрессия только на C#). Вне среза C++: `AsString` / DirectoryString ergonomics, SIGNED-шаблон, полный CMS ContentInfo demo.

**Запись** — канонический DER. **Чтение** — soft-profile ([decisions.md](decisions.md); полный инвентарь — [runtime-api.md](../runtime-csharp/docs/runtime-api.md)):

| Soft-accept (default) | Strict-флаг |
| --- | --- |
| Non-minimal INTEGER contents | `RejectNonMinimalInteger` |
| BIT STRING nonzero trailing bits | `RejectBitStringTrailingBits` |

Не soft (default reject): non-minimal length, OID overlong base-128. Всегда reject: BOOLEAN length≠1 / constructed; empty INTEGER; truncated EOC; indefinite в DER. BER: indefinite, constructed строки/BIT STRING, время без секунд / `±hhmm`.

## Продуктовые артефакты

- **Modern:** [Asn1Kit.Modern](../runtime-csharp/generated/Asn1Kit.Modern/) — 24 модуля (CMS-2009 + PKIX/OCSP/PKCS10/CMP/…); regenerate `scripts/regenerate-modern.ps1`.
- **Demo CMS:** [Asn1Kit.Cms.Demo](../runtime-csharp/examples/Asn1Kit.Cms.Demo/) — default Modern; `--legacy` для regression-стека.
- **Legacy (регрессия):** [Asn1Kit.Legacy](../runtime-csharp/generated/Asn1Kit.Legacy/) — ASN.1:1988 PKIX/CMS/DVCS (`Asn1Kit.Legacy.*`); options — [dvcs.patch.json](../compiler/fixtures/ir/dvcs.patch.json).
- Бенчмарки (не gate): [Asn1Kit.Legacy.Benchmarks](../runtime-csharp/benchmarks/Asn1Kit.Legacy.Benchmarks/).

## Backlog

### Открыто

1. Расширение современного профиля: исполняемые table/presence constraints, `WITH COMPONENTS`, переменные governors и остальные формы X.681–X.683. Базовый корпус RFC и семантическое разрешение реализованы; отдельные curated `.asn` без CLASS не нужны как предварительный этап. Legacy overlay `ExtensionAttribute` остаётся самостоятельной задачей.
2. Curated **TSP** (RFC 3161) с `IMPORTS` на modern CMS-2009.
3. Curated **DVCS** с `IMPORTS` на modern (вместо legacy-графа).
4. При необходимости — дальнейший trim Modern / дополнительные protocol-пакеты.
5. Пул массивов (constructed BER concat, DER SET OF sort)
6. Второй oracle — BouncyCastle (не gate `dotnet test`)
7. **DER: reject constructed OCTET / BIT STRING / string** (или soft-флаг в `Asn1ReaderOptions`, default как сейчас accept; выровнять код с формулировкой § Runtime «BER: constructed…»). Фикстуры + runtime-api soft-inventory
8. **Лимиты hostile BER**: ограничить глубину рекурсивного indefinite scan и общий размер materialized constructed values

### Крупные

1. C++ codegen: паритет ergonomics с C# (`AsString` / DirectoryString, SIGNED-шаблон) и полный CMS ContentInfo demo на C++ — [cpp-backend.md](../compiler/docs/playbooks/cpp-backend.md)
2. Инструменты PKI поверх сгенерированного
