# Плейбук: C++ backend

Генератор — [Asn1Kit.Codegen.Cpp](../../src/Asn1Kit.Codegen.Cpp/). Контракт runtime — [runtime-api.md](../../../runtime-cpp/docs/runtime-api.md). Общий каркас нового языка — [new-backend.md](new-backend.md).

## Предусловие

Нужный примитив или scope обязан существовать в `runtime-cpp/`. Кодек не дублируется в шаблоне.

## Порядок поддержки kind

1. Снять запрет в `EnsureBackendSupport` вместе с реализацией.
2. Маппинг `CppType` / encode / decode (`WriteCall` / `ReadCall` или `encode`/`decode` именованного типа).
3. Nested-типы — через `CollectNested` / `OfferNested`.
4. Тест: golden и/или emit-assert; итоговый .NET-прогон — весь `CppCodegenTests`. При изменении C++ runtime/generated также `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-runtime-cpp.ps1` из корня репозитория.
5. Строка в [docs/status.md](../../../docs/status.md).

## Сгенерированный контракт

- Один `.hpp` на IR-модуль, header-only.
- Именованные SEQUENCE/SET/named BIT STRING: `struct` с `match_tag(tag)` и одним `decode(reader&, tag = natural)` / `encode(writer&, tag = natural) const` — concepts `asn1_decodable` / `asn1_encodable` (вызов без тега идёт через default). Untagged CHOICE: альтернативы в `decode(r)` / `encode(w)`, overload с тегом игнорирует `t`. Tagged CHOICE (`[APPLICATION n] CHOICE`): `decode_content` / `encode_content` + обёртка в натуральном `decode`/`encode`, чтобы `with_explicit` не снимал тег дважды.
- OPTIONAL → `std::optional<T>` (`next_matches<T>()` для именованных типов без собственного тега поля, иначе `next_is`); OF → `std::vector<T>` (type-driven `read_*_of` / `write_*_of` без лямбды, когда элемент — typed generated/примитив без per-element tag); CHOICE → `enum class …_kind` + `std::variant` уникальных C++-типов; при единственном типе — обычное поле `value`.
- `string` / `time` / `null` — явные `read_string` / `write_string` / `read_time` / `write_time` / `write_null` / `null_value::decode` (не type-driven примитивы).
- Опции: `options.cpp.namespace`, `typeName`, `fieldName`; общие `aliasOf`, `generate`, `integer.representation`.
- `any` → `asn1kit::any` (`read_any` / `write_any`; untagged OPTIONAL — через `eof()`).
- Open-type Binding (direct / CONTAINING / SEQUENCE·SET OF / ancestor, OID/INTEGER selector): `OpenTypeBindings.NormalizeLegacy` → plan/emit в `CppBackend.OpenTypes.cs`.
  - Runtime-descriptor `asn1kit::open_binding<Context, Key, Value>` хранит только key; phantom `Context` идентифицирует таблицу, `Value` — публичный decoded-тип:
    - direct `any` → `encode_open` / `decode_open<T>`;
    - `CONTAINING` → `encode_contained` / `decode_contained<T>`;
    - OF → `encode_each` / `decode_each<T>` (`std::vector<T>` + overload на один `T`).
  - Carrier с local selector получает `open_binding<T>(key)` для пользовательского binding с `default_open_context`, а также `try_decode(binding) -> std::optional<Value>`, `set(binding, value)` и `static create(binding, value)`; carrier принимает binding любого `Context`. Для OF параметр `T` фабрики — тип элемента, возвращаемый descriptor содержит `std::vector<T>`.
  - Contextual read API эмитится на общем владельце selector/value: `try_decode_<route>(binding) -> std::optional<Value>`. Он проходит OPTIONAL/deferred/OF-маршрут, требует phantom-context своей таблицы и отклоняет несколько совпадений. Contextual `set` не эмитится.
  - Ancestor (`selector.levels > 0`) декодируется contextual-методом без явных parent-параметров; низкоуровневые `set/create` для такого carrier не эмитятся.
  - String/time в binding — `asn_string<form>` / `asn_time<form>` (форма в типе, не runtime-codec).
  - Каталог — `namespace {table}_{field}_bindings` с `inline const` members (`{ key }`).
  - Policy: unknown/wrong key → `std::nullopt` (raw живёт); match + bad wire → reject.
  - Selector: OID и INTEGER; nested path (`path.Count > 1`) через поля SEQUENCE/SET (нужно для Certificate/SIGNED parameters).
  - Inline SEQUENCE/SET, совпадающие с typedef, уже схлопнуты в IR (`InlineCompositeDeduper`) → общие типы вроде `AlgorithmIdentifier`, без `Owner_AlgorithmIdentifier`.
  - Вне среза: `AsString`, SIGNED-шаблон `Signed<T>` как у C#, Legacy Explicit88 C++ golden.
  - CMS demo на Modern generated: `runtime-cpp/examples/cms_demo` (`asn1kit_cms_demo`).
- Cross-module: peer `#include "Peer.hpp"` / `Peer.types.hpp` по фактическим layout-deps (open-type `AnyType.Bindings` в граф deps не входят). Peer-includes в `Module.types.hpp` / `Module.hpp` **откладываются** до первого owning-типа (`OrderNestedForEmit`), чтобы include-only треугольники (CMS → AttrCert → ESS → CMS) видели локальные типы до реentrant `#include`. Eager наверху остаются `cycle_*.hpp` и собственный `Module.types.hpp`. Нетривиальные SCC → `cycle_….hpp` только с членами SCC. `Module.types.hpp` — только если модулю нужен facade (owned cycle / post-типы / open-type catalogs) и есть нециклические типы; иначе один `Module.hpp`.
- Namespace-квалификация кросс-модульных имён: `QualifyCrossModule(emitting, owning, name)` → `::ns::name`, когда namespace владельца ≠ namespace эмиттера. Применяется к синтезированному владельцу OF-элемента (`ResolveOfItemNaming`) и к именам ENUMERATED-кодеков (`encode_`/`decode_<Type>`, `EnumeratedCodecName`). `NamedTypeName` квалифицирует определение, если его namespace не совпадает ни с резолвящим, ни с эмитирующим модулем (после alias-unwrap контексты расходятся).
- Named BIT STRING (`EmitNamedBitString`): converting ctor от `asn1kit::bit_string` и `operator==`/`!=` с `bit_string`, чтобы SEQUENCE DEFAULT / omit-if-default с `bit_string::copy_from(...)` компилировались.
- CHOICE: одинаковые C++-типы используют один слот `std::variant` (порядок первого появления); если тип единственный, `value` имеет этот тип без `variant`. `kind` сохраняет ASN.1-ветку, её тег и правила кодирования; обёртки `…_box` не генерируются. `match_tag` — `constexpr`; фабрики `from_*` также `constexpr` у нерасширяемого CHOICE, если все payload-типы заведомо literal (`BOOLEAN`, `NULL`, time, ENUMERATED, fixed-width INTEGER). Владеющие `bytes` / `any` / `oid` / `integer` и содержащие их типы остаются runtime.
- `OCTET STRING` / `BIT STRING` `CONTAINING T` → непосредственно `T` (`read_*_containing` / `write_*_containing`; nested content — `Owner_Content`). `CONTAINING ANY` сохраняет непрозрачное содержимое как исходный `asn1kit::bytes` / `asn1kit::bit_string` и декодируется только явным open-type binding.
- Extensible SEQUENCE/SET: skip-unknown через `read_any`, extension additions + groups (`CppBackend.Extensibility.cs`); extensible SET encode — `enter_set_of`. Extensible CHOICE → `…_kind::unknown` + `asn1kit::any`.
- `retainEncoded` → `asn1kit::value<T>` + `read_with_original_encoding` (encode: original TLV если есть, иначе `.get()`); `lazy` → `asn1kit::lazy<T>` + `read_lazy` (encode: raw TLV если есть, иначе `.get()`). Только SEQUENCE/SET/OF; lazy побеждает при обоих флагах (`CppBackend.Deferred.cs`). Именованные SEQUENCE/SET — type-driven `read_*<T>()` / `read_*<T>(tag)` без лямбды; OF и прочие — callback, однострочные тела как `return expr;`. Typed `CONTAINING` — type-driven `read_*_containing<T>(tag)` без лямбды, когда content = natural `read<T>()`.
- Structured / collection / choice / typed DEFAULT — named `default_<field>` / `is_default_<field>` helpers на SEQUENCE/SET (`EmitStructuredType`); вложенные значения — многострочный IIFE в `ValueExpression` (`CppBackend.EncodeDecode.cs`).
- OID-каталоги: одна `static constexpr auto Name = asn1kit::oid_literal<"…">();`. Текст доступен через `Name.text()`, DER-содержимое — через `Name.contents` / `Name.span()`, runtime `oid` — через `Name.get()` без аллокаций. DER-содержимое вычисляет runtime через `consteval`. DEFAULT и ключи open type также используют литералы вместо динамического разбора строк; ссылки на каталог используют `.get()`.
- Debug-формат (`CppBackend.Format.cs`): на каждый модуль с форматированными типами — отдельный `{Module}.format.hpp`. Форматируются SEQUENCE/SET/CHOICE/enumerated/named BIT STRING; named INTEGER-структуры констант — нет, `generate: false` уважается. Порядок файла: `#include` своего `Module.hpp` + `asn1kit/format.hpp`, forward-объявления `inline void to_string(const T&, std::ostream&, int)` своих типов, `#include` `{Peer}.format.hpp` owning-модулей всех форматированных layout-ссылок, `using asn1kit::to_string;`, затем `inline`-определения. Вызовы `to_string` внутри тел — **неквалифицированные** (ADL); объявления до пиров разрывают взаимную рекурсию циклов через include-guard. Отдельного `cycle_*.format.hpp` нет — facade `Module.hpp` покрывает амальгамы. Runtime-перегрузки — header-only в `runtime-cpp/include/asn1kit/format.hpp`; generic primary-шаблон `(…, std::ostream&, int)` не вводится.

## CLI и golden

Для изолированных изменений C++-генератора (включая его тесты) достаточно:

```powershell
dotnet test compiler/tests/Asn1Kit.Compiler.Tests/Asn1Kit.Compiler.Tests.csproj --filter "FullyQualifiedName~Asn1Kit.Tests.CppCodegenTests"
```

Полный solution не запускай, если общие слои compiler/IR/codegen/CLI не изменены. Если затронуты C++ runtime/generated, дополнительно выполни канонический нативный скрипт: он конфигурирует, собирает и только затем запускает тесты. Регенерация должна завершиться до сборки и тестов, читающих golden.

```powershell
dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i compiler/fixtures/ir/example.json --lang cpp `
  -o runtime-cpp/generated/example

# или
./scripts/regenerate-cpp-golden.ps1
./scripts/regenerate-modern-cpp.ps1
```

Golden и native headers — один SoT: `runtime-cpp/generated/`. Сверяет `CppCodegenTests` (example / extensible / deferred / **modern**, включая `*.format.hpp`). Нативный round-trip — `generated_person_tests.cpp`, open-type — `open_binding_tests.cpp`, extensible — `generated_extensible_tests.cpp`, deferred — `generated_deferred_tests.cpp` + `value_lazy_tests.cpp`, debug-формат — `format_tests.cpp`, Modern Certificate — `generated_modern_certificate_tests.cpp` (канонический нативный скрипт).
