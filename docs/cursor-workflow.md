# Как вести доработки через Cursor

Памятка для человека, а не для агента. Про то, что и когда давать в контекст, чтобы агент не тратил ходы на разведку и не ломал слои.

## Что агент читает сам

- [AGENTS.md](../AGENTS.md) подхватывается в каждом чате: карта репозитория, команды, инварианты.
- Правила из `.cursor/rules` применяются по путям файлов: `reliability-and-testing.mdc` — всегда, `compiler.mdc` / `ir-schema.mdc` / `generated-code.mdc` / `runtime.mdc` / `fixtures.mdc` — когда задеты соответствующие файлы.
- `.cursorindexingignore` убирает golden-фикстуру и артефакты сборки из семантического поиска. Файлы при этом остаются читаемыми: агент откроет их по прямой ссылке.

Отдельно напоминать об этом в запросе не нужно.

## Что стоит приложить к запросу

| Задача | Контекст |
| --- | --- |
| «Что уже сделано / что осталось» | [docs/status.md](status.md) |
| Расширение компилятора | [compiler/docs/playbooks/compiler-construct.md](../compiler/docs/playbooks/compiler-construct.md) |
| Новое поле или `kind` в IR | [compiler/docs/playbooks/new-ir-kind.md](../compiler/docs/playbooks/new-ir-kind.md) |
| Генерация C# | [compiler/docs/playbooks/csharp-backend.md](../compiler/docs/playbooks/csharp-backend.md) |
| BER/DER | [runtime-csharp/docs/playbooks/runtime.md](../runtime-csharp/docs/playbooks/runtime.md) |
| Публичный API runtime | [runtime-csharp/docs/runtime-api.md](../runtime-csharp/docs/runtime-api.md) |
| C++ | [compiler/docs/playbooks/new-backend.md](../compiler/docs/playbooks/new-backend.md) |
| «Почему так сделано» | [docs/decisions.md](decisions.md) |

Конкретный `.asn`-фрагмент, на котором воспроизводится проблема, стоит больше любого описания словами.

## Режимы

- **Agent** — точечные правки в одном слое: баг в парсере, новый тест, ветка в бэкенде.
- **Plan** — всё, что трогает схему IR или несколько слоёв сразу. Такие изменения требуют синхронной правки шести файлов (см. `ir-schema.mdc`), и дешевле согласовать список до редактирования, чем разбирать половинчатый результат.
- **Explore-субагенты** — когда вопрос звучит как «где в проекте...» и ответ не очевиден. Для известного файла быстрее дать ссылку.

## Границы чата

Начинай новый чат, когда меняется направление работы (закончил с компилятором — перешёл к runtime). Длинный чат с посторонней историей ухудшает качество ответов сильнее, чем помогает «помнить контекст».

Полезная привычка: просить обновить [docs/status.md](status.md) в том же чате, где расширялась поддержка. Через неделю никто не вспомнит, что `setOf` уже разбирается компилятором, но ещё не генерируется.

## Проверка результата

```powershell
dotnet test Asn1Kit.sln
```

Прогон занимает секунды, так что «проверю потом» не окупается. Отдельно стоит посмотреть `git diff` по [compiler/fixtures/ir/pkix1-explicit88.json](../compiler/fixtures/ir/pkix1-explicit88.json): неожиданные строки в нём означают регрессию компилятора, даже если тесты зелёные после пересборки фикстуры.
