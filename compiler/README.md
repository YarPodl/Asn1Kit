# Compiler & codegen

Каталог инструментов: ASN.1 → JSON IR → исходный код целевого языка. Runtime сюда не входит — только `ProjectReference` из тестов round-trip.

## Проекты

| Проект | Роль |
| --- | --- |
| `src/Asn1Kit.Ir` | Модель IR, JSON, валидация по схеме |
| `src/Asn1Kit.Compiler` | Лексер, парсер, резолв имён, эмит IR |
| `src/Asn1Kit.Codegen` | Контракт `ILanguageBackend` |
| `src/Asn1Kit.Codegen.CSharp` | Генерация `.g.cs` |
| `src/Asn1Kit.Cli` | Команды `compile` и `generate` |
| `tests/Asn1Kit.Compiler.Tests` | IR, лексер/парсер, PKIX golden, codegen round-trip |

Схема IR — общий контракт в корне: [schemas/asn1kit-ir-v1.json](../schemas/asn1kit-ir-v1.json).

## CLI

Из корня репозитория:

```powershell
dotnet run --project compiler/src/Asn1Kit.Cli -- compile -i compiler/fixtures/asn1/example.asn -o out.json
dotnet run --project compiler/src/Asn1Kit.Cli -- generate -i compiler/fixtures/ir/example.json --lang csharp -o ./generated

# -O / --option path=value — override module.options (repeatable); same flag on compile
# --bindings — open-type overlay (Module.Type.field → OID/INTEGER → type); repeatable
# пересборка golden-фикстур
dotnet run --project compiler/src/Asn1Kit.Cli -- compile -i compiler/fixtures/asn1/pkix1-explicit88.asn -o compiler/fixtures/ir/pkix1-explicit88.json --bindings compiler/fixtures/opentype/pkix-bindings.json
dotnet run --project compiler/src/Asn1Kit.Cli -- compile -i compiler/fixtures/asn1/pkix1-explicit88.asn -i compiler/fixtures/asn1/pkix1-implicit88.asn -o compiler/fixtures/ir/pkix1-implicit88.json --bindings compiler/fixtures/opentype/pkix-bindings.json
dotnet run --project compiler/src/Asn1Kit.Cli -- compile `
  -i compiler/fixtures/asn1/pkix1-explicit88.asn `
  -i compiler/fixtures/asn1/pkix1-implicit88.asn `
  -i compiler/fixtures/asn1/cms-2004.asn `
  -o compiler/fixtures/ir/cms-2004.json `
  --bindings compiler/fixtures/opentype/pkix-bindings.json `
  --bindings compiler/fixtures/opentype/cms-bindings.json
# затем csharp.namespace: PKIX* → Asn1Kit.Pkix, CMS → Asn1Kit.Cms

# пересборка golden C# (PKIX + CMS)
dotnet run --project compiler/src/Asn1Kit.Cli -- generate -i compiler/fixtures/ir/cms-2004.json --lang csharp -o runtime-csharp/generated/Asn1Kit.Pkix
```

## Фикстуры

| Путь | Назначение |
| --- | --- |
| `fixtures/asn1/` | Входные модули ASN.1 (в т.ч. `cms-2004.asn`) |
| `fixtures/opentype/pkix-bindings.json` | Sidecar open-type bindings для golden PKIX |
| `fixtures/opentype/cms-bindings.json` | Bindings `ContentInfo.content` для CMS |
| `fixtures/ir/pkix1-explicit88.json` | **Golden**: только через CLI (+ `--bindings`), руками не править |
| `fixtures/ir/pkix1-implicit88.json` | **Golden**: Explicit + Implicit (оба `-i`) + `--bindings`, руками не править |
| `fixtures/ir/cms-2004.json` | **Golden**: Explicit + Implicit + CMS + оба bindings; namespaces в `options` |
| `fixtures/ir/example.json` | **Ручная**: `options.csharp.*`; компилятором не пересобирать |
| `../runtime-csharp/generated/Asn1Kit.Pkix/*.g.cs` | **Golden C#**: из `cms-2004.json`, руками не править |

Golden IR сверяют `PkixExplicit88Tests` / `PkixImplicit88Tests` / `Cms2004Tests`; golden C# — `PkixGeneratedCodeTests`. Diff фикстуры — часть ревью.

## Плейбуки

- [Новая конструкция ASN.1](docs/playbooks/compiler-construct.md)
- [Новый kind IR](docs/playbooks/new-ir-kind.md)
- [C# backend](docs/playbooks/csharp-backend.md)
- [Новый язык (C++)](docs/playbooks/new-backend.md)

Сквозная документация: [docs/architecture.md](../docs/architecture.md), [docs/status.md](../docs/status.md), [docs/ir-schema.md](../docs/ir-schema.md).
