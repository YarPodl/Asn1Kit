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
- `SIZE` / диапазоны → `constraint.size` / `constraint.value`; прочее → `constraint.unsupported`. Constraints описательны: decode и encode их не исполняют. Generated C# (`///`) и C++ (`//`) выводят рядом с полем нормализованные size/value, исходный `unsupported` и closed/extensible object-set metadata с пометкой, что runtime их не проверяет.
- Вне профиля (`CompileException`): конструкции из списка «Не реализовано, поскольку не потребовалось» ниже.

## Современные PKIX/CMS

Корпус Modern — **28 модулей** RFC 5911/5912/6268/8410, [RFC 9215](https://www.rfc-editor.org/rfc/rfc9215) (`GostR3410-2012-PKISyntax`, `RuStrongCertsSyntax`), curated [RFC 3161](https://www.rfc-editor.org/rfc/rfc3161) TSP (`PKIXTSP-2009`) и curated [RFC 3029](https://www.rfc-editor.org/rfc/rfc3029) DVCS (`PKIXDVCS-2009`) ([modern/README.md](../compiler/fixtures/asn1/modern/README.md) → IR [modern-pkix-cms.json](../compiler/fixtures/ir/modern-pkix-cms.json) → сборка [Asn1Kit.Modern](../runtime-csharp/generated/Asn1Kit.Modern/README.md)). Legacy PKIX/CMS/DVCS — [Asn1Kit.Legacy](../runtime-csharp/generated/Asn1Kit.Legacy/) (регрессия; другие module identifiers).

**Поддержано:** `CLASS` / objects/sets / `WITH SYNTAX`; параметризованные типы со свёрткой одинаковых IOC-таблиц в `ref.openTypes`; governors и UNIQUE для OID/INTEGER; component relation selectors (в т.ч. через OF/SET); `CONTAINING`; составные DEFAULT; границы расширений / `[[n: …]]`; стандартный `INSTANCE OF TYPE-IDENTIFIER`; `IMPORTS` по имени или точному module OID (без эвристик).

**Open-type (C#):** один Binding-путь для Modern и Legacy (`Asn1Kit.Legacy.*`): raw-контейнер + `Binding<T>` / table-каталог / `TryDecode`·`Set`·`TryGet` / `Create` (local selector) / `AsString`; неизвестный ключ — raw; mismatch известного — reject. Overlay — источник таблиц (`OpenTypeBindings` → normalize). Потребительский срез — [Asn1Kit.Modern](../runtime-csharp/generated/Asn1Kit.Modern/README.md); правила эмиссии — [csharp-backend.md](../compiler/docs/playbooks/csharp-backend.md).

**Намеренно не исполняется:** schema constraints (`SIZE`, диапазоны значений, `WITH COMPONENTS`, `PRESENT` / `ABSENT` / constraint-`OPTIONAL`, `FROM`, `PATTERN`) и закрытость object sets. Они сохраняются в IR как описательные метаданные, но generated decode и encode их не проверяют: структурно корректные объекты сторонних реализаций должны оставаться читаемыми. Неизвестный open-type ключ сохраняется raw даже для закрытой таблицы; известный ключ с несовместимым payload отклоняется. Метаданные эмитятся в комментариях generated C# и C++ (см. § «Значения, теги, constraints»).

**Не реализовано, поскольку не потребовалось активному корпусу из 28 модулей:** переменные governors, параметризованные CLASS/объекты/наборы, пересечения/разности object sets, неоднозначные optional templates `WITH SYNTAX`, обобщённый `INSTANCE OF`, сложный перенос selector relation через отдельный typedef, `COMPONENTS OF`, `REAL`, `EXTERNAL`. При появлении реального входного модуля конструкция добавляется с его fixture и явной диагностикой соседних неподдержанных форм.

Проверки: `ModernAsn1Tests`, `OpenTypeBindingTests`, `ModernOpenTypeBindingTests`, `ModernRfcTests`, `ContainedValueTests` + полный `dotnet test Asn1Kit.sln`. Nullable-предупреждения в modern generated пока не подавляются.

## Runtime BER/DER

Инвентарь `Write*` / `Read*`, ownership и `ToString` — [runtime-api.md](../runtime-csharp/docs/runtime-api.md). Warm top-level API (паритет с C++ `asn1kit::encode`/`decode`): `Asn1Utils.Decode` / `DecodeRetained` / `Encode` / `TryEncode`, маркер `IAsn1Encodable`, type-driven `WriteSequenceOf`/`WriteSetOf` без лямбды для приложений (C# codegen OF остаётся на static-лямбдах). Hex-матрица — [ber-der/](../runtime-csharp/fixtures/ber-der/); oracle BCL — `PrimitiveOracleTests`.

**C++ runtime** ([runtime-cpp/](../runtime-cpp/)): примитивы BER/DER **да** (boolean/null/integer/enumerated/octet/oid/bitstring/string/time/`any`/`encode_open`/`asn_string` + soft-profile + `asn1kit::bytes`); известный `CONTAINING T` поддержан через typed `read_*_containing` / `write_*_containing`, неизвестный payload остаётся `bytes` / `bit_string`; публичные SEQUENCE/SET/OF/EXPLICIT `enter_*` / `with_*` **да**; typed codec concepts `asn1_decodable` / `asn1_encodable` через `match_tag` + натуральные `decode`/`encode` (без `default_tag`; untagged CHOICE поддержан в `read`/`write`/OF/`asn1kit::encode|decode`); `next_matches<T>`; те же layer-1 фикстуры.

**C++ codegen** ([Asn1Kit.Codegen.Cpp](../compiler/src/Asn1Kit.Codegen.Cpp/)): MVP **да** (`--lang cpp`, golden SoT `runtime-cpp/generated/`, Person + open-binding + extensible + deferred round-trip). Bare `any` → `asn1kit::any`; `CONTAINING T` → непосредственно `T`, `CONTAINING ANY` → исходный `asn1kit::bytes` / `asn1kit::bit_string`; open-type Binding (direct / CONTAINING / OF / ancestor; OID и INTEGER selector; nested selector path) использует общий `open_binding<Context, Key, Value>`: local carrier публикует `try_decode/set/create`, а общий владелец selector/value — read-only contextual `try_decode_<route>` с phantom-защитой таблицы; поверх low-level API генерируются bound views и factory views; peer `#include` по layout-deps (без рёбер от open-type bindings); нетривиальные SCC → `cycle_….hpp` только с членами SCC, с предварительными объявлениями и вынесенными после полных типов CHOICE-codec definitions; `Module.types.hpp` (типы + OID) только если модулю нужен facade, иначе один `Module.hpp`; CHOICE → `kind` + `variant` уникальных C++-типов + `constexpr match_tag`; `from_*` — `constexpr` для нерасширяемых CHOICE только с literal payload; единственный тип payload → обычное поле `value`; type-driven OF без лямбд для named generated/примитивов; `lazy` / `retainEncoded` (encode сохраняет original TLV). Общая `options.aliasOf` объединяет X.520 CHOICE в `DirectoryString` с `kind` и `std::string value`. **Debug-формат** — глубокий рекурсивный `to_string(const T&, std::ostream&)` (`asn1kit/format.hpp`) плюс сгенерированные `{Module}.format.hpp` (forward-объявления своих типов, `#include` `{Peer}.format.hpp` owning-модулей layout-ссылок, затем `inline`-определения; named INTEGER-структуры формат не получают, `generate: false` уважается); регрессия — `format_tests`, golden `*.format.hpp` сверяет `CppCodegenTests`. **Modern C++ golden** — модули + `*.types.hpp` + `cycle_*.hpp` в `runtime-cpp/generated/modern/` (`scripts/regenerate-modern-cpp.ps1`); native gate компилирует общую consumer translation unit со всеми 26 `*.format.hpp` и выполняет Certificate DER round-trip в `generated_modern_certificate_tests` (`TrustAnchorRootCertificate.crt`, `ValidCertificatePathTest1EE.crt`). Legacy Explicit88 C++ golden — вне среза (регрессия только на C#). **CMS ContentInfo demo** — [runtime-cpp/examples/cms_demo](../runtime-cpp/examples/cms_demo/) (`asn1kit_cms_demo`, educational stub), включая `SignerInfo::signed_attributes_set()`. Inline SEQUENCE, совпадающие с typedef (в т.ч. `SIGNED.algorithmIdentifier` → `AlgorithmIdentifier`), схлопывает IR `InlineCompositeDeduper` — отдельных `Owner_AlgorithmIdentifier` нет. Вне среза C++: `AsString`, SIGNED-шаблон как `Signed<T>` у C#.

Open types в C++ доступны через functional view (`info.content_set().ct_signed_data()`), mutable entry, `Type::make_<table>()`, и rvalue decode (`Type::decode(r).<table>().<entry>()`). Owned carrier переносится через вложенные field-view; одинаковая таблица на разных routes получает семантический суффикс. Descriptor-каталоги — напрямую в module `detail`, без emitted registry; user bindings — `open_binding(key)` + `try_decode`/`set`/`create`. Generated `ENUMERATED` участвует в этом API через строгий ADL codec; произвольный C++ `enum` без generated codec не считается ASN.1-типом. Contained SEQUENCE OF (`std::vector<E>` как Value binding, напр. CertificatePolicies / AIA) поддержан в `open_typed` / `decode_contained` / `encode_contained_*` как SEQUENCE TLV. Прежние synthetic namespaces и публичный `open_types` удалены без compatibility aliases.

**Запись** — канонический DER. **Чтение** — soft-profile ([decisions.md](decisions.md); полный инвентарь — [runtime-api.md](../runtime-csharp/docs/runtime-api.md)):

| Soft-accept (default) | Strict-флаг |
| --- | --- |
| Non-minimal INTEGER contents | `RejectNonMinimalInteger` |
| BIT STRING nonzero trailing bits | `RejectBitStringTrailingBits` |

Не soft (default reject): non-minimal length, OID overlong base-128. Всегда reject: BOOLEAN length≠1 / constructed; empty INTEGER; truncated EOC; indefinite в DER; невалидные Unicode scalar/BMP surrogate; невозможная Gregorian date; OID-дуга или первый subidentifier вне диапазона `int`. BER: indefinite, constructed строки/BIT STRING, время без секунд / `±hhmm`; offset корректно переносится через календарные границы. Hostile BER ограничен в обоих runtime: глубина рекурсивного indefinite scan, definite SEQUENCE/SET/EXPLICIT/OF nesting и nested constructed materialization — 64; суммарный payload одного materialized constructed значения — 100 MiB; число leaf-сегментов до concat — 4096. Все лимиты настраиваются через reader options. Bounded fuzz — optional C++ libFuzzer (`ASN1KIT_BUILD_FUZZ`); регрессии — [hostile-ber.json](../runtime-csharp/fixtures/ber-der/hostile-ber.json).

## Продуктовые артефакты

- **Modern:** [Asn1Kit.Modern](../runtime-csharp/generated/Asn1Kit.Modern/) — 28 модулей (CMS-2009 + PKIX/OCSP/PKCS10/CMP/TSP/DVCS/… + GOST RFC 9215); regenerate `scripts/regenerate-modern.ps1`.
- **Demo CMS (C#):** [Asn1Kit.Cms.Demo](../runtime-csharp/examples/Asn1Kit.Cms.Demo/) — default Modern; `--legacy` для regression-стека.
- **Demo CMS (C++):** [runtime-cpp/examples/cms_demo](../runtime-cpp/examples/cms_demo/) — Modern generated API (`verify` / `print-cert` / `print-cms` / `sign`, educational stub).
- **CMS interop (RFC 4134):** [runtime-csharp/fixtures/cms/rfc4134/](../runtime-csharp/fixtures/cms/rfc4134/) — независимые `.bin` + `expected.json` (`SignedCms` / `System.Formats.Asn1`); gate — `CmsRfc4134InteropTests` (Modern + Legacy) и C++ `generated_modern_cms_tests`.
- **Legacy (регрессия):** [Asn1Kit.Legacy](../runtime-csharp/generated/Asn1Kit.Legacy/) — ASN.1:1988 PKIX/CMS/DVCS (`Asn1Kit.Legacy.*`); options — [dvcs.patch.json](../compiler/fixtures/ir/dvcs.patch.json).
- Бенчмарки C# (не gate): [Asn1Kit.Legacy.Benchmarks](../runtime-csharp/benchmarks/Asn1Kit.Legacy.Benchmarks/).
- Бенчмарки C++ (не gate): [runtime-cpp/benchmarks](../runtime-cpp/benchmarks/) — сравнение Asn1Kit/OpenSSL 3.5+ для Modern Cert/CRL/CMS Decode+Encode (Google Benchmark, `ASN1KIT_BUILD_BENCHMARKS`).

## Backlog

### Открыто

1. **C++ modern headers: standalone self-containment.** Общая consumer translation unit компилирует все 26 `*.format.hpp`, но отдельное включение некоторых facade всё ещё упирается в ациклические type-deps при цикле модулей (CMS → AttributeCertificate → ESS → CMS). Это не type-SCC: нужен более мелкий layout header split или module-SCC amalgam без раздувания/поломки соседних facade.
2. При необходимости — дальнейший trim Modern / дополнительные protocol-пакеты; typed `eContent`→`TSTInfo` (сейчас opaque + `TSTInfo.Decode`).
3. Пул массивов (constructed BER concat, DER SET OF sort).
4. Второй oracle — BouncyCastle (не gate `dotnet test`).
5. **C++ `is_default_*` для CHOICE DEFAULT.** Сравнение идёт через `!=`, а у generated CHOICE нет `operator==`/`!=` (есть у named BIT STRING). В modern IR CHOICE DEFAULT нет — latent; при первом таком DEFAULT C++ не соберётся. Нужен `operator==` для CHOICE или отдельный compare в helper.
6. **`asn1kit::value<T>::get()` non-const.** Любой non-const доступ сбрасывает retained TLV — footgun для bit-exact re-encode (CMS `signedAttrs` / `toBeSigned`). Усилить API/документацию и тест «inspect via non-const → `!has_original_encoding()`».
7. **Ergonomic `asn1kit::encode` / `decode` и `reader_options`.** Обёртки всегда используют `default_profile()`; ужесточить/ослабить depth/materialize/segment лимиты можно только через raw `reader`. Прокинуть options в API или явно оставить gap.
8. **CONTAINING ADR.** Удаление `Asn1Contained` / `contained<T>` (typed → `T`, opaque → `ROM`/`bytes`/`bit_string`) зафиксировано в status/runtime-api, но без отдельной записи в [decisions.md](decisions.md).
9. **BIT STRING CONTAINING success path (C#).** Есть write + reject unaligned; нет успешного read round-trip через `ReadBitStringContaining` (в отличие от OCTET).
10. **CMS signedAttrs SET OF sort oracle (G021 remainder).** RFC 4134 `4.10` закрывает field asserts и retained SET TLV; синтетическая перестановка unsorted attributes → канонический DER ещё не покрыта.
11. **C++ open-view: decode extension без `critical` / raw carrier.** `cert_extensions().key_usage()` и аналоги возвращают только typed value; для critical-unknown / инспектора нужен API `(value, critical)` или доступ к `Extension` вместе с decode (сейчас — повторный скан `tbs.extensions`).
12. **Name equality / compare.** CMS SID и AKI path validation требуют bit-exact DN; сейчас только `original_encoding()` при `retainEncoded`. Нет семантического/канонического `Name` compare, если TLV не сохранён.
13. **RDN / Attribute formatting helper.** Прикладной код перебирает десятки `supported_attributes().x520_*` и ловит exceptions; нет единого visitor / known-attribute format.
14. **CHOICE ergonomics.** `kind` + `std::get<N>` (в т.ч. shared variant slot у `GeneralName` string alts) — хрупко; именованные accessors по альтернативам снизят ошибки.
15. **CMS signedAttrs set builder.** Сборка — несколько `Attribute::make_signed_attributes_set().…` в `vector`; нет fluent builder набора и enumeration «неизвестных» attrs без raw-цикла.
16. **AlgorithmIdentifier parameters discipline.** Часто выставляют только `.algorithm` без `NULL`/open-view parameters — wire может отличаться от OpenSSL; усилить docs/examples (`make_….kt_rsa(null)` и т.п.).
17. **Мелкий ergonomics open/PKIX types.** `KeyUsageFlags` без `&` / `has_flag`; нет `operator==` у `CertID`; нет generated `to_string` для ENUMERATED (`OCSPResponseStatus` и др.).
18. **Contained SET OF как bare `std::vector`.** Contained SEQUENCE OF поддержан; Contained SET OF отдельным wire-тегом через тот же `std::vector` не выражается — при появлении таких bindings нужен отличимый тип или codegen path.

### Крупные

1. C++ codegen: паритет ergonomics с C# (`AsString`, `Signed<T>` поверх общего `AlgorithmIdentifier`) — [cpp-backend.md](../compiler/docs/playbooks/cpp-backend.md)
2. Инструменты PKI поверх сгенерированного
