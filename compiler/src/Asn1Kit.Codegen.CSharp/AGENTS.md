# C# codegen

- Обязательное правило области: `../../../.cursor/rules/generated-code.mdc`.
- Пошаговый процесс и карта файлов: `../../docs/playbooks/csharp-backend.md`.
- Ядро — `CSharpBackend.cs`; open-type Binding/каталоги — `CSharpBackend.OpenTypeWrappers.cs` / `CSharpBackend.OpenTypeUses.cs`; узкие partials — `StructuredDefaults`, `DecodeContexts`, `Extensibility`, `ModernTypes`.
- Генератор использует API `Asn1Kit.Runtime` и не реализует BER/DER самостоятельно.
- Неподдержанный kind отклоняй через `NotSupportedException` с его именем.
- `*.g.cs` не правь вручную; регенерируй через `../../../scripts/regenerate-csharp-golden.ps1` или `../../../scripts/regenerate-modern.ps1`.
