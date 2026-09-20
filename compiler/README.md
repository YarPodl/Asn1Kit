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

# пересборка golden-фикстуры
dotnet run --project compiler/src/Asn1Kit.Cli -- compile -i compiler/fixtures/asn1/pkix1-explicit88.asn -o compiler/fixtures/ir/pkix1-explicit88.json
```

## Фикстуры

| Путь | Назначение |
| --- | --- |
| `fixtures/asn1/` | Входные модули ASN.1 |
| `fixtures/ir/pkix1-explicit88.json` | **Golden**: только через CLI, руками не править |
| `fixtures/ir/example.json` | **Ручная**: `options.csharp.*`; компилятором не пересобирать |

Golden сверяет `PkixExplicit88Tests.CompilesAndMatchesGoldenIr`. Diff фикстуры — часть ревью.

## Плейбуки

- [Новая конструкция ASN.1](docs/playbooks/compiler-construct.md)
- [Новый kind IR](docs/playbooks/new-ir-kind.md)
- [C# backend](docs/playbooks/csharp-backend.md)
- [Новый язык (C++)](docs/playbooks/new-backend.md)

Сквозная документация: [docs/architecture.md](../docs/architecture.md), [docs/status.md](../docs/status.md), [docs/ir-schema.md](../docs/ir-schema.md).
