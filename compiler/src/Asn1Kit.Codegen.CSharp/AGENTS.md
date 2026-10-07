# C# codegen

- Обязательное правило области: `../../../.cursor/rules/generated-code.mdc`.
- Пошаговый процесс: `../../docs/playbooks/csharp-backend.md`.
- Генератор использует API `Asn1Kit.Runtime` и не реализует BER/DER самостоятельно.
- Неподдержанный kind отклоняй через `NotSupportedException` с его именем.
- `*.g.cs` не правь вручную; регенерируй через `../../../scripts/regenerate-csharp-golden.ps1` или `../../../scripts/regenerate-modern.ps1`.
