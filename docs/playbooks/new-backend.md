# Плейбук: новый языковой бэкенд (C++)

Второй целевой язык не меняет фронтенд: компилятор и IR остаются прежними. Любая правка в `Asn1Kit.Compiler` ради нового языка — признак утечки слоёв.

## Что добавляется

1. **Проект генератора.** `src/Asn1Kit.Codegen.Cpp` по образцу [src/Asn1Kit.Codegen.CSharp](../../src/Asn1Kit.Codegen.CSharp): ссылки на `Asn1Kit.Codegen` и `Asn1Kit.Ir`, реализация `ILanguageBackend` с `LanguageId => "cpp"`. Добавить в решение: `dotnet sln Asn1Kit.sln add src/Asn1Kit.Codegen.Cpp/Asn1Kit.Codegen.Cpp.csproj`.
2. **Регистрация.** [src/Asn1Kit.Cli/Program.cs](../../src/Asn1Kit.Cli/Program.cs), метод `Generate`: добавить бэкенд в массив `CodeGenerator`. Неизвестный `--lang` уже падает `InvalidOperationException($"Unsupported language '{language}'.")`.
3. **Runtime.** Отдельная C++-библиотека с теми же правилами BER/DER, что и [src/Asn1Kit.Runtime](../../src/Asn1Kit.Runtime): DER только definite length, минимальный INTEGER, BOOLEAN `0x00` / `0xFF`; BER на чтении принимает indefinite length и constructed `OCTET STRING`. Правила перечислены в [runtime.md](runtime.md) — это спецификация, а не описание C#-реализации.
4. **Опции.** Свои ключи в `options.cpp.*` (namespace, имена типов и полей) по аналогии с `IrOptions.CSharpNamespace` и компанией. Чужие ключи (`options.csharp.*`) бэкенд игнорирует, но не удаляет.
5. **Документация.** [README.md](../../README.md) — команда `generate --lang cpp`, [docs/architecture.md](../architecture.md) — таблица проектов, [docs/status.md](../status.md) — колонка поддержки kind.

## Что нельзя тащить из C#

- Логику тегов X.680 (`AUTOMATIC TAGS`, `IMPLICIT` поверх `CHOICE`): она уже раскрыта компилятором в явные `tag` в IR.
- Разрешение имён и value assignments: в IR они уже резолвнуты, `ref` указывает на существующий тип.
- Кодек: генератор эмитит вызовы C++ runtime, а не байты тегов и длин.
- `GeneratedFile` возвращает пару путь-содержимое; запись на диск — дело CLI, бэкенд в файловую систему не ходит.

## Стратегия тестирования

C# тесты компилируют сгенерированный код через Roslyn (`RoundTripTests.CompileGenerated`) — для C++ такого нет. Порядок:

1. **Golden-файлы.** Фикстура IR → сгенерированные `.h` / `.cpp` сравниваются с эталоном в `fixtures/`. Ловит непреднамеренные изменения шаблона так же, как golden-IR ловит изменения компилятора.
2. **Отказы.** Неподдержанный kind — `NotSupportedException` с именем kind, тест по образцу `ParserTests.BackendRejectsUnsupportedKinds`.
3. **Кодек C++.** Тесты runtime на байтовых векторах — в нативном тестовом проекте, на тех же векторах, что и C#-runtime: одинаковый вход обязан давать одинаковые байты.
4. **Кросс-проверка.** Один и тот же IR, закодированный C#-кодеком, декодируется C++-кодеком и наоборот. Это главный тест совместимости, ради которого два бэкенда вообще существуют.

Пока нативная сборка не заведена в решении, пункты 3–4 фиксируются как явно отложенные в [docs/status.md](../status.md), а не как «проверим позже».
