# Asn1Kit — инструкция для агента

ASN.1 → JSON IR → генератор кода → runtime BER/DER. Слои независимы: компилятор не знает целевой язык, генератор не дублирует кодек, runtime не знает про ASN.1-модули.

## Навигация

- Архитектура и карта репозитория: [docs/architecture.md](docs/architecture.md).
- Контракт IR: [docs/ir-schema.md](docs/ir-schema.md).
- Поддержанный профиль и backlog: [docs/status.md](docs/status.md).
- Архитектурные решения: [docs/decisions.md](docs/decisions.md).
- Локальные сведения: [compiler/README.md](compiler/README.md), [runtime-csharp/README.md](runtime-csharp/README.md), [runtime-cpp/README.md](runtime-cpp/README.md).

Читай только документы, относящиеся к текущей задаче. Для пошаговой реализации используй соответствующий playbook из `compiler/docs/playbooks/` или `runtime-csharp/docs/playbooks/`.

По умолчанию не ищи в generated C# и больших IR golden, исключённых через `.ignore`. Читай их только для задач codegen, fixtures, регенерации или проверки diff; для целевого поиска используй явный путь или `rg --no-ignore`.

## Предметные правила

Перед реализацией или ревью полностью прочитай правила всех затрагиваемых областей:

- любые изменения кода — [`.cursor/rules/reliability-and-testing.mdc`](.cursor/rules/reliability-and-testing.mdc);
- lexer, parser, AST или `IrBuilder` — [`.cursor/rules/compiler.mdc`](.cursor/rules/compiler.mdc);
- IR, JSON Schema или IR-фикстуры — [`.cursor/rules/ir-schema.mdc`](.cursor/rules/ir-schema.mdc);
- codegen или `*.g.cs` — [`.cursor/rules/generated-code.mdc`](.cursor/rules/generated-code.mdc);
- runtime BER/DER — [`.cursor/rules/runtime.mdc`](.cursor/rules/runtime.mdc);
- golden, ASN.1, IR или BER/DER-фикстуры — [`.cursor/rules/fixtures.mdc`](.cursor/rules/fixtures.mdc);
- только при явно запрошенном commit — [`.cursor/rules/git-commit.mdc`](.cursor/rules/git-commit.mdc).

Frontmatter `.mdc` предназначен для Cursor и не заменяет эту маршрутизацию. При противоречии приоритет имеет `AGENTS.md`.

## Среда и проверка

- Только .NET SDK 6.0.402, `net6.0`, C# 10, nullable-контекст включён.
- Оболочка — PowerShell на Windows; в командах используй пути с `/`.
- Репозиторий локальный, без remote и CI.
- После изменения кода запускай полный `dotnet test Asn1Kit.sln`; точечный тест не заменяет полный прогон.
- Golden и `*.g.cs` не правь вручную: регенерируй каноническими командами из профильного README или playbook.

## Глобальные инварианты

- Любой IR проходит `IrSerializer.ValidateSchema`; неизвестные ключи `options` переживают round-trip.
- Вход вне профиля вызывает `CompileException` с позицией; молчаливый или приблизительный разбор запрещён.
- Неподдержанный бэкендом kind вызывает `NotSupportedException` с именем kind.
- Нераспознанные формы constraint сохраняются в `constraint.unsupported`.
- Генератор вызывает только API runtime; байты тегов и длин в шаблонах запрещены.
- Новая функциональность и исправленный баг требуют теста; существующие assert не ослабляются.
- Расширение поддержки требует обновления [docs/status.md](docs/status.md) в том же изменении.

## Язык

Документация, `.mdc` и общение с пользователем — на русском. Код, идентификаторы и тексты исключений — на английском.
