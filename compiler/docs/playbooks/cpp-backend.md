# Плейбук: C++ backend

Генератор — [Asn1Kit.Codegen.Cpp](../../src/Asn1Kit.Codegen.Cpp/). Контракт runtime — [runtime-api.md](../../../runtime-cpp/docs/runtime-api.md). Общий каркас нового языка — [new-backend.md](new-backend.md).

## Предусловие

Нужный примитив или scope обязан существовать в `runtime-cpp/`. Кодек не дублируется в шаблоне.

## Порядок поддержки kind

1. Снять запрет в `EnsureBackendSupport` вместе с реализацией.
2. Маппинг `CppType` / encode / decode (`WriteCall` / `ReadCall` или `encode`/`decode` именованного типа).
3. Nested-типы — через `CollectNested` / `OfferNested`.
4. Тест: golden и/или emit-assert; итоговый .NET-прогон — весь `CppCodegenTests`. При изменении C++ runtime/generated также `ctest` в `runtime-cpp/`.
5. Строка в [docs/status.md](../../../docs/status.md).

## Сгенерированный контракт

- Один `.hpp` на IR-модуль, header-only.
- Именованные SEQUENCE/SET/CHOICE: `struct` с `default_tag()`, `decode(reader&, tag)`, `encode(writer&, tag) const` — concepts `asn1_decodable` / `asn1_encodable`.
- OPTIONAL → `std::optional<T>`; OF → `std::vector<T>`; CHOICE → `enum class …_kind` + `std::variant` уникальных C++-типов; при единственном типе — обычное поле `value`.
- `string` / `time` / `null` — явные `read_string` / `write_string` / `read_time` / `write_time` / `write_null` / `null_value::decode` (не type-driven примитивы).
- Опции: `options.cpp.namespace`, `typeName`, `fieldName`; общие `aliasOf`, `generate`, `integer.representation`.
- `any` → `asn1kit::any` (`read_any` / `write_any`; untagged OPTIONAL — через `eof()`).
- Open-type Binding (direct / CONTAINING / SEQUENCE·SET OF / ancestor, local OID selector): `OpenTypeBindings.NormalizeLegacy` → plan/emit в `CppBackend.OpenTypes.cs`.
  - Descriptor `{stem}_binding<T>` — только `asn1kit::oid key`; encode/decode выводятся из `T`:
    - direct `any` → `encode_open` / `decode_open<T>`;
    - `CONTAINING` → `encode_contained` / `decode_contained<T>`;
    - OF → `encode_each` / `decode_each<T>` (`std::vector<T>` + overload на один `T`).
  - Ancestor (`selector.levels > 0`): `try_decode`/`set` принимают `const ParentN& parentN`; ключ читается с предка; `set` не меняет ключ (mismatch → exception); `create` не эмитится.
  - String/time в binding — `asn_string<form>` / `asn_time<form>` (форма в типе, не runtime-codec).
  - Операции на descriptor: `try_decode` / `set` / `create` (create — только local selector).
  - Каталог — `namespace {table}_{field}_bindings` с `inline const` members (`{ key }`).
  - Policy: unknown/wrong key → `try_decode` = false (raw живёт); match + bad wire → reject.
  - Selector: OID и INTEGER; nested path (`path.Count > 1`) через поля SEQUENCE/SET (нужно для Certificate/SIGNED parameters).
  - Вне среза: `AsString`, SIGNED-шаблон как у C#, Legacy Explicit88 C++ golden.
  - CMS demo на Modern generated: `runtime-cpp/examples/cms_demo` (`asn1kit_cms_demo`).
- Cross-module: peer `#include "Peer.hpp"` / `Peer.types.hpp` по фактическим layout-deps (open-type `AnyType.Bindings` в граф deps не входят). Peer-includes в `Module.types.hpp` / `Module.hpp` **откладываются** до первого owning-типа (`OrderNestedForEmit`), чтобы include-only треугольники (CMS → AttrCert → ESS → CMS) видели локальные типы до реentrant `#include`. Eager наверху остаются `cycle_*.hpp` и собственный `Module.types.hpp`. Нетривиальные SCC → `cycle_….hpp` только с членами SCC. `Module.types.hpp` — только если модулю нужен facade (owned cycle / post-типы / open-type catalogs) и есть нециклические типы; иначе один `Module.hpp`.
- Named BIT STRING (`EmitNamedBitString`): converting ctor от `asn1kit::bit_string` и `operator==`/`!=` с `bit_string`, чтобы SEQUENCE DEFAULT / omit-if-default с `bit_string::copy_from(...)` компилировались.
- CHOICE: одинаковые C++-типы используют один слот `std::variant` (порядок первого появления); если тип единственный, `value` имеет этот тип без `variant`. `kind` сохраняет ASN.1-ветку, её тег и правила кодирования; обёртки `…_box` не генерируются.
- `OCTET STRING` / `BIT STRING` `CONTAINING` → `asn1kit::contained<T>` (`read_contained` / `write_contained`; content ANY → `known=false`; nested content — `Owner_Content`).
- Extensible SEQUENCE/SET: skip-unknown через `read_any`, extension additions + groups (`CppBackend.Extensibility.cs`); extensible SET encode — `enter_set_of`. Extensible CHOICE → `…_kind::unknown` + `asn1kit::any`.
- `retainEncoded` → `asn1kit::value<T>` + `read_with_original_encoding` (encode: original TLV если есть, иначе `.get()`); `lazy` → `asn1kit::lazy<T>` + `read_lazy` (encode: raw TLV если есть, иначе `.get()`). Только SEQUENCE/SET/OF; lazy побеждает при обоих флагах (`CppBackend.Deferred.cs`).
- Structured / collection / choice / typed DEFAULT — `ValueExpression` в `CppBackend.EncodeDecode.cs`.
- OID-каталоги: одна `static constexpr auto Name = asn1kit::oid_literal<"…">();`. Текст доступен через `Name.text()`, DER-содержимое — через `Name.contents` / `Name.span()`, runtime `oid` — через `Name.get()` без аллокаций. DER-содержимое вычисляет runtime через `consteval`. DEFAULT и ключи open type также используют литералы вместо динамического разбора строк; ссылки на каталог используют `.get()`.

## CLI и golden

Для изолированных изменений C++-генератора (включая его тесты) достаточно:

```powershell
dotnet test compiler/tests/Asn1Kit.Compiler.Tests/Asn1Kit.Compiler.Tests.csproj --filter "FullyQualifiedName~Asn1Kit.Tests.CppCodegenTests"
```

Полный solution не запускай, если общие слои compiler/IR/codegen/CLI не изменены. Если затронуты C++ runtime/generated, дополнительно собери C++ и выполни `ctest` актуального preset. Регенерация должна завершиться до сборки и тестов, читающих golden.

```powershell
dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i compiler/fixtures/ir/example.json --lang cpp `
  -o runtime-cpp/generated/example

# или
./scripts/regenerate-cpp-golden.ps1
./scripts/regenerate-modern-cpp.ps1
```

Golden и native headers — один SoT: `runtime-cpp/generated/`. Сверяет `CppCodegenTests` (example / extensible / deferred / **modern**). Нативный round-trip — `generated_person_tests.cpp`, open-type — `open_binding_tests.cpp`, extensible — `generated_extensible_tests.cpp`, deferred — `generated_deferred_tests.cpp` + `value_lazy_tests.cpp`, Modern Certificate — `generated_modern_certificate_tests.cpp` (`ctest`).
