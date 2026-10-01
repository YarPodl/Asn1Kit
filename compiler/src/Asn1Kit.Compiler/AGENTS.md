# ASN.1 compiler

- Обязательное правило области: `../../../.cursor/rules/compiler.mdc`.
- Пошаговый процесс новой конструкции: `../../docs/playbooks/compiler-construct.md`.
- Здесь живёт знание X.680; целевой язык в lexer/parser/AST/`IrBuilder` не проникает.
- Неизвестную или внепрофильную форму отклоняй явно с позицией либо сохраняй как `constraint.unsupported`, если это форма constraint.
- После изменений `IrBuilder` регенерируй IR golden скриптом `../../../scripts/regenerate-ir-goldens.ps1`.
