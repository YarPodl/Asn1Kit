# Плейбук: C++ backend

Генератор — [Asn1Kit.Codegen.Cpp](../../src/Asn1Kit.Codegen.Cpp/). Контракт runtime — [runtime-api.md](../../../runtime-cpp/docs/runtime-api.md). Общий каркас нового языка — [new-backend.md](new-backend.md).

## Предусловие

Нужный примитив или scope обязан существовать в `runtime-cpp/`. Кодек не дублируется в шаблоне.

## Порядок поддержки kind

1. Снять запрет в `EnsureBackendSupport` вместе с реализацией.
2. Маппинг `CppType` / encode / decode (`WriteCall` / `ReadCall` или `encode`/`decode` именованного типа).
3. Nested-типы — через `CollectNested` / `OfferNested`.
4. Тест: golden и/или emit-assert + при затронутом кодеке — `ctest` в `runtime-cpp/`.
5. Строка в [docs/status.md](../../../docs/status.md).

## Сгенерированный контракт

- Один `.hpp` на IR-модуль, header-only.
- Именованные SEQUENCE/SET/CHOICE: `struct` с `default_tag()`, `decode(reader&, tag)`, `encode(writer&, tag) const` — concepts `asn1_decodable` / `asn1_encodable`.
- OPTIONAL → `std::optional<T>`; OF → `std::vector<T>`; CHOICE → `enum class …_kind` + `std::variant`.
- `string` / `time` / `null` — явные `read_string` / `write_string` / `read_time` / `write_time` / `write_null` / `null_value::decode` (не type-driven примитивы).
- Опции: `options.cpp.namespace`, `typeName`, `fieldName`, `aliasOf`; общие `generate`, `integer.representation`.
- `any` → `asn1kit::any` (`read_any` / `write_any`; untagged OPTIONAL — через `eof()`).
- Open-type Binding MVP (direct-`any`, local OID selector): `OpenTypeBindings.NormalizeLegacy` → plan/emit в `CppBackend.OpenTypes.cs`.
  - Descriptor `{stem}_binding<T>` — только `asn1kit::oid key`; encode/decode выводятся из `T` (`asn1kit::encode_open` / `decode_open<T>`).
  - String/time в binding — `asn_string<form>` / `asn_time<form>` (форма в типе, не runtime-codec).
  - Операции на descriptor: `try_decode` / `set` / `create`.
  - Каталог — `namespace {table}_{field}_bindings` с `inline const` members (`{ key }`).
  - Policy: unknown/wrong key → `try_decode` = false (raw живёт); match + bad wire → reject.
  - Вне среза MVP: contained/SET OF payload, ancestor selector (`levels > 0`), `AsString`, INTEGER selector.
- `OCTET STRING` / `BIT STRING` `CONTAINING` → `asn1kit::contained<T>` (`read_contained` / `write_contained`; content ANY → `known=false`; nested content — `Owner_Content`).
- Extensible SEQUENCE/SET: skip-unknown через `read_any`, extension additions + groups (`CppBackend.Extensibility.cs`); extensible SET encode — `enter_set_of`. Extensible CHOICE → `…_kind::unknown` + `asn1kit::any`.
- Пока отклоняются: `lazy`, `retainEncoded`.

## CLI и golden

```powershell
dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i compiler/fixtures/ir/example.json --lang cpp `
  -o compiler/fixtures/codegen-cpp/example

# или
./scripts/regenerate-cpp-golden.ps1
```

Golden сверяет `CppCodegenTests.GenerateMatchesCommittedExampleGolden` и `GenerateMatchesCommittedExtensibleGolden`. Нативный round-trip — `generated_person_tests.cpp`, open-type — `open_binding_tests.cpp`, extensible — `generated_extensible_tests.cpp` (`ctest`; headers в `runtime-cpp/generated/`).
