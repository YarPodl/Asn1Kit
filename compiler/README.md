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
# --patch — options overlay (modules / fields Module.Type.field); repeatable; generate also has --ir-output
# составные workflow регенерации
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/regenerate-ir-goldens.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/regenerate-csharp-golden.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/regenerate-bench.ps1
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
| `fixtures/ir/cms-2004-bench.patch.json` | Bench options overlay (`*.Bench`, `lazy` на `CertificateChoices.certificate`) |
| `fixtures/ir/cms-2004-bench.json` | **Производный**: golden + patch; не в `MatchesGoldenIr` |
| `fixtures/ir/dvcs.patch.json` | Namespace overlay для полного DVCS-графа; отдельный DVCS IR не коммитится |
| `fixtures/ir/example.json` | **Ручная**: `options.csharp.*`; компилятором не пересобирать |
| `../runtime-csharp/generated/Asn1Kit.Pkix/*.g.cs` | **Golden C#**: PKIX/CMS/DVCS и зависимости из полного `.asn`-графа + `dvcs.patch.json`, руками не править |
| `../runtime-csharp/generated/Asn1Kit.Pkix.Bench/*.g.cs` | **Bench C#**: из patch / `cms-2004-bench.json`, руками не править |

Golden IR сверяют `PkixExplicit88Tests` / `PkixImplicit88Tests` / `Cms2004Tests`; golden C# — `PkixGeneratedCodeTests`; bench IR — `Cms2004BenchTests` / `IrOptionsPatchTests`. Diff фикстуры — часть ревью.

## Плейбуки

- [Новая конструкция ASN.1](docs/playbooks/compiler-construct.md)
- [Новый kind IR](docs/playbooks/new-ir-kind.md)
- [C# backend](docs/playbooks/csharp-backend.md)
- [Новый язык (C++)](docs/playbooks/new-backend.md)

Сквозная документация: [docs/architecture.md](../docs/architecture.md), [docs/status.md](../docs/status.md), [docs/ir-schema.md](../docs/ir-schema.md).
