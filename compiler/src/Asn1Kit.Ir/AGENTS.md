# IR

- Обязательное правило области: `../../../.cursor/rules/ir-schema.mdc`.
- Контракт: `../../../docs/ir-schema.md`; новый kind: `../../docs/playbooks/new-ir-kind.md`.
- `irVersion: 1` изменяется только аддитивно; неизвестные `options` сохраняются при round-trip.
- Изменение kind синхронизируй со схемой, моделями, конвертерами, валидатором, документацией и тестами.
- После изменения IR регенерируй golden скриптом `../../../scripts/regenerate-ir-goldens.ps1`.
