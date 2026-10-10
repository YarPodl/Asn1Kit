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
- Пока отклоняются: `any`, `containing`, `extensible`, `lazy`, `retainEncoded`.

## CLI и golden

```powershell
dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i compiler/fixtures/ir/example.json --lang cpp `
  -o compiler/fixtures/codegen-cpp/example

# или
./scripts/regenerate-cpp-golden.ps1
```

Golden сверяет `CppCodegenTests.GenerateMatchesCommittedExampleGolden`. Нативный round-trip — `runtime-cpp/tests/generated_person_tests.cpp` (`ctest`).
