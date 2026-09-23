# Asn1Kit — инструкция для агента

ASN.1 → JSON IR → генератор кода → runtime BER/DER. Слои независимы: компилятор не знает целевой язык,
генератор не дублирует кодек, runtime не знает про ASN.1-модули.

Репозиторий: [compiler/](compiler/) (инструменты), [runtime-csharp/](runtime-csharp/) (C# кодек), [runtime-cpp/](runtime-cpp/) (заглушка).

Детали: [docs/architecture.md](docs/architecture.md), [docs/ir-schema.md](docs/ir-schema.md).
Что уже поддержано — [docs/status.md](docs/status.md). Почему сделано так — [docs/decisions.md](docs/decisions.md).

Пошаговые чеклисты:
[новый kind IR](compiler/docs/playbooks/new-ir-kind.md) ·
[конструкция ASN.1](compiler/docs/playbooks/compiler-construct.md) ·
[C# backend](compiler/docs/playbooks/csharp-backend.md) ·
[runtime BER/DER](runtime-csharp/docs/playbooks/runtime.md) ·
[публичный API runtime](runtime-csharp/docs/runtime-api.md) ·
[новый язык](compiler/docs/playbooks/new-backend.md).

Локальные README каталогов: [compiler/README.md](compiler/README.md), [runtime-csharp/README.md](runtime-csharp/README.md), [runtime-cpp/README.md](runtime-cpp/README.md).

## Среда

- Только .NET 6 SDK (локально 6.0.402), `net6.0`, `LangVersion 10`, `Nullable enable`.
- Оболочка — PowerShell на Windows; пути в командах пиши через `/`, они работают.
- Репозиторий локальный: remote и CI нет, проверка только локальными `dotnet build` / `dotnet test`.
- Git commit: правило [`.cursor/rules/git-commit.mdc`](.cursor/rules/git-commit.mdc) — PowerShell-синтаксис, один проход, approval для `main` с первой попытки.

## Команды

```powershell
dotnet build Asn1Kit.sln
dotnet test Asn1Kit.sln

# точечный прогон
dotnet test Asn1Kit.sln --filter FullyQualifiedName~PkixExplicit88Tests

# CLI
dotnet run --project compiler/src/Asn1Kit.Cli -- compile -i compiler/fixtures/asn1/example.asn -o out.json
dotnet run --project compiler/src/Asn1Kit.Cli -- generate -i compiler/fixtures/ir/example.json --lang csharp -o ./generated

# пересборка golden-фикстур (после любых правок компилятора или IR)
dotnet run --project compiler/src/Asn1Kit.Cli -- compile -i compiler/fixtures/asn1/pkix1-explicit88.asn -o compiler/fixtures/ir/pkix1-explicit88.json --bindings compiler/fixtures/opentype/pkix-bindings.json
dotnet run --project compiler/src/Asn1Kit.Cli -- compile -i compiler/fixtures/asn1/pkix1-explicit88.asn -i compiler/fixtures/asn1/pkix1-implicit88.asn -o compiler/fixtures/ir/pkix1-implicit88.json --bindings compiler/fixtures/opentype/pkix-bindings.json
dotnet run --project compiler/src/Asn1Kit.Cli -- compile `
  -i compiler/fixtures/asn1/pkix1-explicit88.asn `
  -i compiler/fixtures/asn1/pkix1-implicit88.asn `
  -i compiler/fixtures/asn1/cms-2004.asn `
  -o compiler/fixtures/ir/cms-2004.json `
  --bindings compiler/fixtures/opentype/pkix-bindings.json `
  --bindings compiler/fixtures/opentype/cms-bindings.json
# затем в cms-2004.json выставить options.csharp.namespace: PKIX* → Asn1Kit.Pkix, CMS → Asn1Kit.Cms

# пересборка golden C# (после правок генератора или IR; namespaces уже в cms-2004.json)
dotnet run --project compiler/src/Asn1Kit.Cli -- generate -i compiler/fixtures/ir/cms-2004.json --lang csharp -o runtime-csharp/generated/Asn1Kit.Pkix

# bench IR + C# (options из cms-2004-bench.patch.json; namespaces *.Bench, lazy CertificateChoices.certificate)
dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i compiler/fixtures/ir/cms-2004.json `
  --patch compiler/fixtures/ir/cms-2004-bench.patch.json `
  --ir-output compiler/fixtures/ir/cms-2004-bench.json `
  --lang csharp `
  -o runtime-csharp/generated/Asn1Kit.Pkix.Bench

# бенчмарки Certificate / CRL / CMS vs BCL и BouncyCastle (не входят в dotnet test; ссылаются на Asn1Kit.Pkix.Bench)
dotnet run -c Release --project runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks
```

Полный прогон тестов — секунды, отдельная «быстрая» цель не нужна: гоняй `dotnet test Asn1Kit.sln` целиком.

## Карта репозитория

| Путь | Что там |
| --- | --- |
| [compiler/src/Asn1Kit.Ir/IrModels.cs](compiler/src/Asn1Kit.Ir/IrModels.cs) | Классы IR, константы `TypeKinds` / `ValueKinds` / … |
| [compiler/src/Asn1Kit.Ir/TypeExprConverter.cs](compiler/src/Asn1Kit.Ir/TypeExprConverter.cs) | `switch` по `kind` при чтении/записи JSON |
| [compiler/src/Asn1Kit.Ir/IrValidator.cs](compiler/src/Asn1Kit.Ir/IrValidator.cs) | Семантические проверки поверх схемы |
| [compiler/src/Asn1Kit.Ir/IrSerializer.cs](compiler/src/Asn1Kit.Ir/IrSerializer.cs) | `ToJson` / `FromJson` / `Load` / `Save` / `ValidateSchema` |
| [compiler/src/Asn1Kit.Ir/IrOptions.cs](compiler/src/Asn1Kit.Ir/IrOptions.cs) | Чтение `options.csharp.*` / `options.generate`; CLI `-O path=value` |
| [compiler/src/Asn1Kit.Ir/IrOptionsPatch.cs](compiler/src/Asn1Kit.Ir/IrOptionsPatch.cs) | Sidecar options-patch (`modules` / `fields`); CLI `--patch` |
| [schemas/asn1kit-ir-v1.json](schemas/asn1kit-ir-v1.json) | JSON Schema Draft 2020-12, единственный контракт IR |
| [compiler/src/Asn1Kit.Compiler/Asn1Lexer.cs](compiler/src/Asn1Kit.Compiler/Asn1Lexer.cs) | Токены, ключевые слова |
| [compiler/src/Asn1Kit.Compiler/Asn1Parser.cs](compiler/src/Asn1Kit.Compiler/Asn1Parser.cs) | Парсер X.680, отказы «вне профиля» |
| [compiler/src/Asn1Kit.Compiler/Ast.cs](compiler/src/Asn1Kit.Compiler/Ast.cs) | AST компилятора |
| [compiler/src/Asn1Kit.Compiler/IrBuilder.cs](compiler/src/Asn1Kit.Compiler/IrBuilder.cs) | AST → IR, резолв значений, `AUTOMATIC TAGS`, constraints |
| [compiler/src/Asn1Kit.Codegen/ILanguageBackend.cs](compiler/src/Asn1Kit.Codegen/ILanguageBackend.cs) | Контракт бэкенда и `CodeGenerator` |
| [compiler/src/Asn1Kit.Codegen.CSharp/CSharpBackend.cs](compiler/src/Asn1Kit.Codegen.CSharp/CSharpBackend.cs) | Генерация `.g.cs`, `EnsureBackendSupport` |
| [runtime-csharp/src/Asn1Kit.Runtime/](runtime-csharp/src/Asn1Kit.Runtime/) | `Asn1Tag`, `Asn1Writer`, `Asn1Reader`, `Asn1Oid`, `Asn1Primitives` |
| [runtime-csharp/generated/Asn1Kit.Pkix/](runtime-csharp/generated/Asn1Kit.Pkix/) | Golden C# PKIX + CMS; руками не править `*.g.cs` |
| [runtime-csharp/generated/Asn1Kit.Pkix.Bench/](runtime-csharp/generated/Asn1Kit.Pkix.Bench/) | Bench C# (`Asn1Kit.Pkix.Bench` / `Asn1Kit.Cms.Bench`); из patch; руками не править `*.g.cs` |
| [compiler/src/Asn1Kit.Cli/Program.cs](compiler/src/Asn1Kit.Cli/Program.cs) | Команды `compile` и `generate` |
| [compiler/tests/Asn1Kit.Compiler.Tests/](compiler/tests/Asn1Kit.Compiler.Tests/) | IR, компилятор, codegen round-trip |
| [runtime-csharp/tests/Asn1Kit.Runtime.Tests/](runtime-csharp/tests/Asn1Kit.Runtime.Tests/) | BER/DER матрица, oracle, `RuntimeTests` |
| [runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks/](runtime-csharp/benchmarks/Asn1Kit.Pkix.Benchmarks/) | BenchmarkDotNet Cert/CRL/CMS vs BCL и BouncyCastle |

## Фикстуры

- [compiler/fixtures/ir/pkix1-explicit88.json](compiler/fixtures/ir/pkix1-explicit88.json) — **golden-артефакт**: генерируется CLI из `.asn`, руками не правится.
- [compiler/fixtures/ir/pkix1-implicit88.json](compiler/fixtures/ir/pkix1-implicit88.json) — **golden**: Explicit88 + Implicit88 (multi-module `IMPORTS`), руками не правится.
- [compiler/fixtures/ir/cms-2004.json](compiler/fixtures/ir/cms-2004.json) — **golden**: Explicit + Implicit + CMS; namespaces `Asn1Kit.Pkix` / `Asn1Kit.Cms` в `options`.
- [compiler/fixtures/ir/cms-2004-bench.patch.json](compiler/fixtures/ir/cms-2004-bench.patch.json) — **source of truth** bench options (`*.Bench` namespaces, `lazy` на `CertificateChoices.certificate`).
- [compiler/fixtures/ir/cms-2004-bench.json](compiler/fixtures/ir/cms-2004-bench.json) — **производный** IR: golden + patch; не в `MatchesGoldenIr`.
- [runtime-csharp/generated/Asn1Kit.Pkix/](runtime-csharp/generated/Asn1Kit.Pkix/) — **golden C#**: из `cms-2004.json` (`Asn1Kit.Pkix` + `Asn1Kit.Cms`), руками не править `*.g.cs`.
- [runtime-csharp/generated/Asn1Kit.Pkix.Bench/](runtime-csharp/generated/Asn1Kit.Pkix.Bench/) — **bench C#**: из `cms-2004-bench.json`, руками не править `*.g.cs`.
- [compiler/fixtures/ir/example.json](compiler/fixtures/ir/example.json) — **ручная** фикстура с правками `options`; компилятором не воспроизводится.
- [runtime-csharp/fixtures/ber-der/](runtime-csharp/fixtures/ber-der/) — hex-векторы runtime.
- [runtime-csharp/fixtures/cms/](runtime-csharp/fixtures/cms/) — attached SignedData для бенчмарков.

Подробнее — [compiler/README.md](compiler/README.md), [runtime-csharp/README.md](runtime-csharp/README.md).

## Инварианты

- Любой IR проходит `IrSerializer.ValidateSchema`; неизвестные ключи `options` обязаны переживать round-trip.
- Вход вне профиля — явное исключение с позицией (`CompileException`), не молчаливый пропуск и не «примерно распарсили».
- Неподдержанный бэкендом kind — `NotSupportedException` с именем kind, а не кривой код.
- Нераспознанные формы constraint сохраняются в `constraint.unsupported`, а не отбрасываются.
- Генератор вызывает только API runtime; байты тегов и длин в шаблонах запрещены.
- Новая функциональность и любой исправленный баг — с тестом; существующие assert не ослабляются.
- Расширил поддержку (новый kind, новая конструкция, новый бэкенд) — обнови [docs/status.md](docs/status.md) в том же изменении.

## Язык

Документация, комментарии в `.mdc`-правилах и общение с пользователем — русский.
Код, идентификаторы и тексты исключений — английский: `throw new IrException($"Unknown type kind '{kind}'.")`.
