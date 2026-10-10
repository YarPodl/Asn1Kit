# C++ codegen

- Обязательное правило области: `../../../.cursor/rules/generated-code.mdc`.
- Пошаговый процесс: `../../docs/playbooks/cpp-backend.md`.
- Генератор использует API `asn1kit` (`runtime-cpp/`) и не реализует BER/DER самостоятельно.
- Неподдержанный kind отклоняй через `NotSupportedException` с его именем.
- Сгенерированные `.hpp` не правь вручную; регенерируй через `../../../scripts/regenerate-cpp-golden.ps1`.
