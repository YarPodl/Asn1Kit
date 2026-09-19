# Asn1Kit — инструкция для агента

ASN.1 → JSON IR → генератор кода → runtime BER/DER. Слои независимы: компилятор не знает целевой язык,
генератор не дублирует кодек, runtime не знает про ASN.1-модули.

Детали: [docs/architecture.md](docs/architecture.md), [docs/ir-schema.md](docs/ir-schema.md).
Что уже поддержано — [docs/status.md](docs/status.md). Почему сделано так — [docs/decisions.md](docs/decisions.md).

Пошаговые чеклисты под типовые доработки:
[новый kind IR](docs/playbooks/new-ir-kind.md) ·
[конструкция ASN.1](docs/playbooks/compiler-construct.md) ·
[C# backend](docs/playbooks/csharp-backend.md) ·
[runtime BER/DER](docs/playbooks/runtime.md) ·
[новый язык](docs/playbooks/new-backend.md).

## Среда

- Только .NET 6 SDK (локально 6.0.402), `net6.0`, `LangVersion 10`, `Nullable enable`.
- Оболочка — PowerShell на Windows; пути в командах пиши через `/`, они работают.
- Репозиторий локальный: remote и CI нет, проверка только локальными `dotnet build` / `dotnet test`.

## Команды

```powershell
dotnet build Asn1Kit.sln
dotnet test Asn1Kit.sln

# точечный прогон
dotnet test Asn1Kit.sln --filter FullyQualifiedName~PkixExplicit88Tests

# CLI
dotnet run --project src/Asn1Kit.Cli -- compile -i fixtures/asn1/example.asn -o out.json
dotnet run --project src/Asn1Kit.Cli -- generate -i fixtures/ir/example.json --lang csharp -o ./generated

# пересборка golden-фикстуры (после любых правок компилятора или IR)
dotnet run --project src/Asn1Kit.Cli -- compile -i fixtures/asn1/pkix1-explicit88.asn -o fixtures/ir/pkix1-explicit88.json
```

Полный прогон тестов — секунды, отдельная «быстрая» цель не нужна: гоняй `dotnet test Asn1Kit.sln` целиком.

## Карта репозитория

| Путь | Что там |
| --- | --- |
| [src/Asn1Kit.Ir/IrModels.cs](src/Asn1Kit.Ir/IrModels.cs) | Классы IR, константы `TypeKinds` / `ValueKinds` / `TagClasses` / `TagModes` / `StringTypes` / `TimeTypes` |
| [src/Asn1Kit.Ir/TypeExprConverter.cs](src/Asn1Kit.Ir/TypeExprConverter.cs) | `switch` по `kind` при чтении/записи JSON (типы и значения) |
| [src/Asn1Kit.Ir/IrValidator.cs](src/Asn1Kit.Ir/IrValidator.cs) | Семантические проверки поверх схемы |
| [src/Asn1Kit.Ir/IrSerializer.cs](src/Asn1Kit.Ir/IrSerializer.cs) | `ToJson` / `FromJson` / `Load` / `Save` / `ValidateSchema` |
| [src/Asn1Kit.Ir/IrOptions.cs](src/Asn1Kit.Ir/IrOptions.cs) | Чтение `options.csharp.*` и `options.generate` |
| [schemas/asn1kit-ir-v1.json](schemas/asn1kit-ir-v1.json) | JSON Schema Draft 2020-12, единственный контракт IR |
| [src/Asn1Kit.Compiler/Asn1Lexer.cs](src/Asn1Kit.Compiler/Asn1Lexer.cs) | Токены, ключевые слова |
| [src/Asn1Kit.Compiler/Asn1Parser.cs](src/Asn1Kit.Compiler/Asn1Parser.cs) | Парсер X.680, отказы «вне профиля» |
| [src/Asn1Kit.Compiler/Ast.cs](src/Asn1Kit.Compiler/Ast.cs) | AST компилятора |
| [src/Asn1Kit.Compiler/IrBuilder.cs](src/Asn1Kit.Compiler/IrBuilder.cs) | AST → IR, резолв значений, `AUTOMATIC TAGS`, constraints |
| [src/Asn1Kit.Codegen/ILanguageBackend.cs](src/Asn1Kit.Codegen/ILanguageBackend.cs) | Контракт бэкенда и `CodeGenerator` |
| [src/Asn1Kit.Codegen.CSharp/CSharpBackend.cs](src/Asn1Kit.Codegen.CSharp/CSharpBackend.cs) | Генерация `.g.cs`, `EnsureBackendSupport` со списком неподдержанных kind |
| [src/Asn1Kit.Runtime/](src/Asn1Kit.Runtime/) | `Asn1Tag`, `Asn1Writer`, `Asn1Reader`, `Asn1Primitives` |
| [src/Asn1Kit.Cli/Program.cs](src/Asn1Kit.Cli/Program.cs) | Команды `compile` и `generate` |
| [tests/Asn1Kit.Tests/](tests/Asn1Kit.Tests/) | xUnit: `Asn1KitTests.cs` (IR, компилятор, runtime, round-trip), `LexerTests`, `ParserTests`, `ValueResolutionTests`, `PkixExplicit88Tests` |

## Фикстуры

- [fixtures/ir/pkix1-explicit88.json](fixtures/ir/pkix1-explicit88.json) — **golden-артефакт**: генерируется CLI из `.asn`, руками не правится. Его сверяет `PkixExplicit88Tests.CompilesAndMatchesGoldenIr`.
- [fixtures/ir/example.json](fixtures/ir/example.json) — **ручная** фикстура: демонстрирует правки `options` (namespace `Example.Asn1`, `propertyName: Nickname`), компилятором не воспроизводится. Не «чини» её перегенерацией.

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
