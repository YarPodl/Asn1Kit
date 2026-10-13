# Плейбук: новый языковой бэкенд (C++)

Второй целевой язык не меняет фронтенд: компилятор и IR остаются прежними. Любая правка в `Asn1Kit.Compiler` ради нового языка — признак утечки слоёв.

Генератор живёт в каталоге [compiler/](../../); C++ runtime — в [runtime-cpp/](../../../runtime-cpp/).

## Что добавляется

1. **Проект генератора.** `compiler/src/Asn1Kit.Codegen.Cpp` по образцу [Asn1Kit.Codegen.CSharp](../../src/Asn1Kit.Codegen.CSharp): ссылки на `Asn1Kit.Codegen` и `Asn1Kit.Ir`, реализация `ILanguageBackend` с `LanguageId => "cpp"`. Добавить в решение: `dotnet sln Asn1Kit.sln add compiler/src/Asn1Kit.Codegen.Cpp/Asn1Kit.Codegen.Cpp.csproj`.
2. **Регистрация.** [Program.cs](../../src/Asn1Kit.Cli/Program.cs), метод `Generate`: добавить бэкенд в массив `CodeGenerator`. Неизвестный `--lang` уже падает `InvalidOperationException($"Unsupported language '{language}'.")`.
3. **Runtime.** Библиотека в [runtime-cpp/](../../../runtime-cpp/) с теми же правилами BER/DER, что и [Asn1Kit.Runtime](../../../runtime-csharp/src/Asn1Kit.Runtime). Полная проверка configure → build → test — `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-runtime-cpp.ps1` из корня; Conan preset передаётся через `-Preset` после `conan install`.
4. **Опции.** Свои ключи в `options.cpp.*` (namespace, имена типов и полей) по аналогии с `IrOptions.CSharpNamespace` и компанией. Чужие ключи (`options.csharp.*`) бэкенд игнорирует, но не удаляет.
5. **Документация.** [README.md](../../../README.md) — команда `generate --lang cpp`, [docs/architecture.md](../../../docs/architecture.md) — таблица проектов, [docs/status.md](../../../docs/status.md) — колонка поддержки kind, [runtime-cpp/README.md](../../../runtime-cpp/README.md). Детали эмиссии — [cpp-backend.md](cpp-backend.md).

## Что нельзя тащить из C#

- Логику тегов X.680 (`AUTOMATIC TAGS`, `IMPLICIT` поверх `CHOICE`): она уже раскрыта компилятором в явные `tag` в IR.
- Разрешение имён и value assignments: в IR они уже резолвнуты, `ref` указывает на существующий тип.
- Кодек: генератор эмитит вызовы C++ runtime, а не байты тегов и длин.
- `GeneratedFile` возвращает пару путь-содержимое; запись на диск — дело CLI, бэкенд в файловую систему не ходит.

## Стратегия тестирования

C# тесты компилируют сгенерированный код через Roslyn (`RoundTripTests.CompileGenerated`) — для C++ такого нет. Порядок:

1. **Golden-файлы.** Фикстура IR → сгенерированные `.hpp` сравниваются с эталоном в `runtime-cpp/generated/`. Ловит непреднамеренные изменения шаблона.
2. **Отказы.** Неподдержанный kind — `NotSupportedException` с именем kind (`CppCodegenTests`).
3. **Кодек C++.** Тесты runtime на байтовых векторах — в `runtime-cpp/tests/` (запускаются каноническим нативным скриптом); generated types — `generated_person_tests.cpp`.
4. **Кросс-проверка.** Тот же DER-вектор, что даёт C# encode Person, декодируется C++ Person (и обратно по возможности).
