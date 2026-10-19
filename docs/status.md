# Что поддержано сейчас

Срез по слоям. Обновляется тем же изменением, которым расширяется поддержка.

Детали API runtime — [runtime-api.md](../runtime-csharp/docs/runtime-api.md); почему так — [decisions.md](decisions.md); IR — [ir-schema.md](ir-schema.md).

## Типы IR

| `kind` | Компилятор | C# backend | C++ backend | Тесты |
| --- | --- | --- | --- | --- |
| `boolean` | да | `bool` | `bool` | `ParserTests`, `PrimitiveCodecTests`, `PrimitiveOracleTests`, `CppCodegenTests` |
| `integer` | да, `namedValues` | `int`…`Asn1Integer` по опции / выводу | `int32_t`…`asn1kit::integer`; namedValues → константы | `CompilerTests`, `Primitive*`, `RoundTripTests`, `CppCodegenTests` |
| `enumerated` | да, автоматическая нумерация и `...` | C# `enum`; inline → `Owner_Field` | `enum class` + ADL `encode_enumerated`/`decode_enumerated`; generated enum поддержан в open types | `ModernAsn1Tests`, `PkixImplicit88Tests`, `PrimitiveCodecTests`, `CppCodegenTests`, `generated_modern_certificate_tests` |
| `bitString` | да, `namedBits` | `Asn1BitString` / Flags; `CONTAINING T` → `T` | `asn1kit::bit_string` / Flags; `CONTAINING T` → `T` | `Primitive*`, `RuntimeTests`, `RoundTripTests`, `CppCodegenTests`, `contained_tests` |
| `octetString` | да, `CONTAINING` | `ReadOnlyMemory<byte>`; `CONTAINING T` → `T` | `asn1kit::bytes`; `CONTAINING T` → `T` | `ModernAsn1Tests`, `ContainedValueTests`, `Primitive*`, `CppCodegenTests`, `contained_tests` |
| `oid` | да (dotted; base-128 в т.ч. `2.999…`) | `Asn1Oid` — единственный codec (string↔arcs↔contents); dotted `string` — warm (`Encode`/`DecodeString`) | `asn1kit::oid` + каталог `…Oids`: единая `constexpr` константа с `.text()` / `.span()` / `.get()`, DER-содержимое через `consteval`; DEFAULT/open-type keys без динамического разбора | `Primitive*`, `RuntimeTests`, `CppCodegenTests`, `oid_literal_tests` |
| `string` (12 форм) | да | `string` + `Asn1StringForm` | `std::string` + `string_form` | `Primitive*` (все 12), `RuntimeTests`, `RoundTripTests` |
| `time` (`utc` / `generalized`) | да; `fractionDigits` 0…7 | `DateTimeOffset` + `Asn1TimeForm` | `asn1kit::utc_date_time` + `time_form` | `Primitive*`, `RuntimeTests`, `RoundTripTests` |
| `any` | `definedBy` / overlay / IOC → `selector` + `ref.openTypes` (или definition-scoped bindings) | `Asn1Any` + `Binding<T>` / каталоги; unknown→raw, known mismatch→reject | `asn1kit::any` + functional view `value.<table>().<entry>()` / `<entry>(value)` / `make_<table>` / безопасный rvalue decode; user `open_binding(key)` + `try_decode`/`set`/`create`; прямые descriptor-каталоги в `detail`, без emitted registry; direct / CONTAINING / OF / ancestor | `ModernAsn1Tests`, `ModernRfcTests`, `OpenTypeBindingsTests`, `OpenTypeWrapperTests`, `CppCodegenTests`, `any_tests`, `codec_tests`, `open_binding_tests` |
| `sequence` | да, `extensible` | класс или `struct` (`options.csharp.valueType`); `lazy` → `Asn1Lazy<T>`; `retainEncoded` → `Asn1Value<T>` с исходным TLV | `struct` + scopes; extensible → skip-unknown + extension groups; `lazy` → `asn1kit::lazy<T>`; `retainEncoded` → `asn1kit::value<T>` | `RoundTripTests`, `Pkix*`, `Asn1Kit.Legacy.Tests`, `CppCodegenTests`, `generated_person_tests`, `generated_extensible_tests`, `generated_deferred_tests`, `value_lazy_tests` |
| `set` | да | класс/`struct`; DER-порядок по тегу; `lazy` / `retainEncoded` | `struct`; encode по тегу / extensible → `enter_set_of` + skip-unknown; `lazy`/`retainEncoded` как у SEQUENCE | `RoundTripTests`, `ParserTests`, `RuntimeTests`, `CppCodegenTests`, `generated_extensible_tests`, `generated_deferred_tests` |
| `choice` | да | `…Kind` + `From…`; однотипные → `Kind`+`Value`; один вариант → алиас; extensible → `Unknown` | `…_kind` + `std::variant` уникальных типов; однотипные → `kind` + `value`; один вариант → алиас; extensible → `unknown` + `asn1kit::any` | `ParserTests`, `Pkix*`, `RoundTripTests`, `CppCodegenTests`, `generated_extensible_tests` |
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

Корпус Modern — **26 модулей** RFC 5911/5912/6268/8410 и [RFC 9215](https://www.rfc-editor.org/rfc/rfc9215) (`GostR3410-2012-PKISyntax`, `RuStrongCertsSyntax`) ([modern/README.md](../compiler/fixtures/asn1/modern/README.md) → IR [modern-pkix-cms.json](../compiler/fixtures/ir/modern-pkix-cms.json) → сборка [Asn1Kit.Modern](../runtime-csharp/generated/Asn1Kit.Modern/README.md)). Legacy PKIX/CMS/DVCS — [Asn1Kit.Legacy](../runtime-csharp/generated/Asn1Kit.Legacy/) (регрессия; другие module identifiers).

**Поддержано:** `CLASS` / objects/sets / `WITH SYNTAX`; параметризованные типы со свёрткой одинаковых IOC-таблиц в `ref.openTypes`; governors и UNIQUE для OID/INTEGER; component relation selectors (в т.ч. через OF/SET); `CONTAINING`; составные DEFAULT; границы расширений / `[[n: …]]`; стандартный `INSTANCE OF TYPE-IDENTIFIER`; `IMPORTS` по имени или точному module OID (без эвристик).

**Open-type (C#):** один Binding-путь для Modern и Legacy (`Asn1Kit.Legacy.*`): raw-контейнер + `Binding<T>` / table-каталог / `TryDecode`·`Set`·`TryGet` / `Create` (local selector) / `AsString`; неизвестный ключ — raw; mismatch известного — reject. Overlay — источник таблиц (`OpenTypeBindings` → normalize). Потребительский срез — [Asn1Kit.Modern](../runtime-csharp/generated/Asn1Kit.Modern/README.md); правила эмиссии — [csharp-backend.md](../compiler/docs/playbooks/csharp-backend.md).

**Пока не** (профиль структуры и BER/DER, не полные X.680–X.683): закрытость наборов, `WITH COMPONENTS`, presence (`PRESENT`/`ABSENT`/`OPTIONAL`), переменные governors, параметризованные CLASS/объекты/наборы, пересечения/разности наборов, неоднозначные optional templates, обобщённый `INSTANCE OF`, перенос selector-типа через отдельный typedef.

Проверки: `ModernAsn1Tests`, `OpenTypeBindingTests`, `ModernOpenTypeBindingTests`, `ModernRfcTests`, `ContainedValueTests` + полный `dotnet test Asn1Kit.sln`. Nullable-предупреждения в modern generated пока не подавляются.

## Runtime BER/DER

Инвентарь `Write*` / `Read*`, ownership и `ToString` — [runtime-api.md](../runtime-csharp/docs/runtime-api.md). Warm top-level decode: `Asn1Utils.Decode` / `DecodeRetained`. Hex-матрица — [ber-der/](../runtime-csharp/fixtures/ber-der/); oracle BCL — `PrimitiveOracleTests`.

**C++ runtime** ([runtime-cpp/](../runtime-cpp/)): примитивы BER/DER **да** (boolean/null/integer/enumerated/octet/oid/bitstring/string/time/`any`/`encode_open`/`asn_string` + soft-profile + `asn1kit::bytes`); известный `CONTAINING T` поддержан через typed `read_*_containing` / `write_*_containing`, неизвестный payload остаётся `bytes` / `bit_string`; публичные SEQUENCE/SET/OF/EXPLICIT `enter_*` / `with_*` **да**; typed codec concepts `asn1_decodable` / `asn1_encodable` через `match_tag` + натуральные `decode`/`encode` (без `default_tag`; untagged CHOICE поддержан в `read`/`write`/OF/`asn1kit::encode|decode`); `next_matches<T>`; те же layer-1 фикстуры.

**C++ codegen** ([Asn1Kit.Codegen.Cpp](../compiler/src/Asn1Kit.Codegen.Cpp/)): MVP **да** (`--lang cpp`, golden SoT `runtime-cpp/generated/`, Person + open-binding + extensible + deferred round-trip). Bare `any` → `asn1kit::any`; `CONTAINING T` → непосредственно `T`, `CONTAINING ANY` → исходный `asn1kit::bytes` / `asn1kit::bit_string`; open-type Binding (direct / CONTAINING / OF / ancestor; OID и INTEGER selector; nested selector path) использует общий `open_binding<Context, Key, Value>`: local carrier публикует `try_decode/set/create`, а общий владелец selector/value — read-only contextual `try_decode_<route>` с phantom-защитой таблицы; поверх low-level API генерируются bound views и factory views; peer `#include` по layout-deps (без рёбер от open-type bindings); нетривиальные SCC → `cycle_….hpp` только с членами SCC, с предварительными объявлениями и вынесенными после полных типов CHOICE-codec definitions; `Module.types.hpp` (типы + OID) только если модулю нужен facade, иначе один `Module.hpp`; CHOICE → `kind` + `variant` уникальных C++-типов + `constexpr match_tag`; `from_*` — `constexpr` для нерасширяемых CHOICE только с literal payload; единственный тип payload → обычное поле `value`; type-driven OF без лямбд для named generated/примитивов; `lazy` / `retainEncoded` (encode сохраняет original TLV). Общая `options.aliasOf` объединяет X.520 CHOICE в `DirectoryString` с `kind` и `std::string value`. **Debug-формат** — глубокий рекурсивный `to_string(const T&, std::ostream&)` (`asn1kit/format.hpp`) плюс сгенерированные `{Module}.format.hpp` (forward-объявления своих типов, `#include` `{Peer}.format.hpp` owning-модулей layout-ссылок, затем `inline`-определения; named INTEGER-структуры формат не получают, `generate: false` уважается); регрессия — `format_tests`, golden `*.format.hpp` сверяет `CppCodegenTests`. **Modern C++ golden** — модули + `*.types.hpp` + `cycle_*.hpp` в `runtime-cpp/generated/modern/` (`scripts/regenerate-modern-cpp.ps1`); native gate компилирует общую consumer translation unit со всеми 24 `*.format.hpp` и выполняет Certificate DER round-trip в `generated_modern_certificate_tests` (`TrustAnchorRootCertificate.crt`, `ValidCertificatePathTest1EE.crt`). Legacy Explicit88 C++ golden — вне среза (регрессия только на C#). **CMS ContentInfo demo** — [runtime-cpp/examples/cms_demo](../runtime-cpp/examples/cms_demo/) (`asn1kit_cms_demo`, educational stub), включая `SignerInfo::signed_attributes_set()`. Inline SEQUENCE, совпадающие с typedef (в т.ч. `SIGNED.algorithmIdentifier` → `AlgorithmIdentifier`), схлопывает IR `InlineCompositeDeduper` — отдельных `Owner_AlgorithmIdentifier` нет. Вне среза C++: `AsString`, SIGNED-шаблон как `Signed<T>` у C#.

Open types в C++ доступны через functional view (`info.content_set().ct_signed_data()`), mutable entry, `Type::make_<table>()`, и rvalue decode (`Type::decode(r).<table>().<entry>()`). Owned carrier переносится через вложенные field-view; одинаковая таблица на разных routes получает семантический суффикс. Descriptor-каталоги — напрямую в module `detail`, без emitted registry; user bindings — `open_binding(key)` + `try_decode`/`set`/`create`. Generated `ENUMERATED` участвует в этом API через строгий ADL codec; произвольный C++ `enum` без generated codec не считается ASN.1-типом. Прежние synthetic namespaces и публичный `open_types` удалены без compatibility aliases.

**Запись** — канонический DER. **Чтение** — soft-profile ([decisions.md](decisions.md); полный инвентарь — [runtime-api.md](../runtime-csharp/docs/runtime-api.md)):

| Soft-accept (default) | Strict-флаг |
| --- | --- |
| Non-minimal INTEGER contents | `RejectNonMinimalInteger` |
| BIT STRING nonzero trailing bits | `RejectBitStringTrailingBits` |

Не soft (default reject): non-minimal length, OID overlong base-128. Всегда reject: BOOLEAN length≠1 / constructed; empty INTEGER; truncated EOC; indefinite в DER; невалидные Unicode scalar/BMP surrogate; невозможная Gregorian date; OID-дуга или первый subidentifier вне диапазона `int`. BER: indefinite, constructed строки/BIT STRING, время без секунд / `±hhmm`; offset корректно переносится через календарные границы. Hostile BER ограничен в обоих runtime: глубина рекурсивного indefinite scan и nested constructed materialization — 64, суммарный payload одного materialized constructed значения — 100 MiB; оба лимита настраиваются через reader options.

## Продуктовые артефакты

- **Modern:** [Asn1Kit.Modern](../runtime-csharp/generated/Asn1Kit.Modern/) — 26 модулей (CMS-2009 + PKIX/OCSP/PKCS10/CMP/… + GOST RFC 9215); regenerate `scripts/regenerate-modern.ps1`.
- **Demo CMS (C#):** [Asn1Kit.Cms.Demo](../runtime-csharp/examples/Asn1Kit.Cms.Demo/) — default Modern; `--legacy` для regression-стека.
- **Demo CMS (C++):** [runtime-cpp/examples/cms_demo](../runtime-cpp/examples/cms_demo/) — Modern generated API (`verify` / `print-cert` / `print-cms` / `sign`, educational stub).
- **Legacy (регрессия):** [Asn1Kit.Legacy](../runtime-csharp/generated/Asn1Kit.Legacy/) — ASN.1:1988 PKIX/CMS/DVCS (`Asn1Kit.Legacy.*`); options — [dvcs.patch.json](../compiler/fixtures/ir/dvcs.patch.json).
- Бенчмарки C# (не gate): [Asn1Kit.Legacy.Benchmarks](../runtime-csharp/benchmarks/Asn1Kit.Legacy.Benchmarks/).
- Бенчмарки C++ (не gate): [runtime-cpp/benchmarks](../runtime-cpp/benchmarks/) — Modern Cert/CRL/CMS Lazy Decode+Encode (Google Benchmark, `ASN1KIT_BUILD_BENCHMARKS`).

## Backlog

### Открыто

1. Расширение современного профиля: исполняемые table/presence constraints, `WITH COMPONENTS`, переменные governors и остальные формы X.681–X.683. Базовый корпус RFC и семантическое разрешение реализованы; отдельные curated `.asn` без CLASS не нужны как предварительный этап. Legacy overlay `ExtensionAttribute` остаётся самостоятельной задачей.
2. Curated **TSP** (RFC 3161) с `IMPORTS` на modern CMS-2009.
3. Curated **DVCS** с `IMPORTS` на modern (вместо legacy-графа).
4. При необходимости — дальнейший trim Modern / дополнительные protocol-пакеты.
5. **GOST этап 2:** минимальный CPALGS-срез [RFC 4357](https://www.rfc-editor.org/rfc/rfc4357) (`Cryptographic-Gost-Useful-Definitions`, `GostR3410-2001-PKISyntax`, `GostR3411-94-DigestSyntax`, `Gost28147-89-EncryptionSyntax`) и [RFC 4490](https://www.rfc-editor.org/rfc/rfc4490) `GostR3410-EncryptionSyntax` с `SubjectPublicKeyInfo` из `PKIX1Explicit-2009`; без полных `*-ParamSetSyntax`. CMS EnvelopedData для R 34.12 (Kuznyechik/Magma) — вне IETF ASN.1.
6. Пул массивов (constructed BER concat, DER SET OF sort)
7. Второй oracle — BouncyCastle (не gate `dotnet test`)
8. **DER: reject constructed OCTET / BIT STRING / string** (или soft-флаг в `Asn1ReaderOptions`, default как сейчас accept; выровнять код с формулировкой § Runtime «BER: constructed…»). Фикстуры + runtime-api soft-inventory
9. **C++ modern headers: standalone self-containment.** Общая consumer translation unit компилирует все 24 `*.format.hpp`, но отдельное включение некоторых facade всё ещё упирается в ациклические type-deps при цикле модулей (CMS → AttributeCertificate → ESS → CMS). Это не type-SCC: нужен более мелкий layout header split или module-SCC amalgam без раздувания/поломки соседних facade.
10. **`InlineCompositeDeduper` и field `options`.** Fingerprint снимает `options`, поэтому inline SEQUENCE/SET/CHOICE с разным `lazy` / `retainEncoded` / `integer.representation` при одинаковом wire-shape схлопываются в один typedef. Учитывать codegen-значимые options в fingerprint или явно документировать ограничение.
11. **C++ `is_default_*` для CHOICE DEFAULT.** Сравнение идёт через `!=`, а у generated CHOICE нет `operator==`/`!=` (есть у named BIT STRING). В modern IR CHOICE DEFAULT нет — latent; при первом таком DEFAULT C++ не соберётся. Нужен `operator==` для CHOICE или отдельный compare в helper.
12. **`asn1kit::value<T>::get()` non-const.** Любой non-const доступ сбрасывает retained TLV — footgun для bit-exact re-encode (CMS `signedAttrs` / `toBeSigned`). Усилить API/документацию и тест «inspect via non-const → `!has_original_encoding()`».
13. **Ergonomic `asn1kit::encode` / `decode` и `reader_options`.** Обёртки всегда используют `default_profile()`; ужесточить/ослабить depth/materialize лимиты можно только через raw `reader`. Прокинуть options в API или явно оставить gap.
14. **Hostile BER: structural depth и segment storm.** Сейчас лимиты только на indefinite EOC-scan и nested constructed string/BIT STRING materialize (depth 64, payload 100 MiB). Глубина definite SEQUENCE/SET/OF и число tiny-сегментов до concat не ограничены — остаточный DoS; при необходимости — structural depth / segment cap + тесты.
15. **CONTAINING ADR.** Удаление `Asn1Contained` / `contained<T>` (typed → `T`, opaque → `ROM`/`bytes`/`bit_string`) зафиксировано в status/runtime-api, но без отдельной записи в [decisions.md](decisions.md).
16. **BIT STRING CONTAINING success path (C#).** Есть write + reject unaligned; нет успешного read round-trip через `ReadBitStringContaining` (в отличие от OCTET).

### Крупные

1. C++ codegen: паритет ergonomics с C# (`AsString`, `Signed<T>` поверх общего `AlgorithmIdentifier`) — [cpp-backend.md](../compiler/docs/playbooks/cpp-backend.md)
2. Инструменты PKI поверх сгенерированного
