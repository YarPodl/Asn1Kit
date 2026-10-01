# Compiler и codegen

- Общая документация и команды: [README.md](README.md).
- Изменения кода требуют `../.cursor/rules/reliability-and-testing.mdc` и полного `dotnet test Asn1Kit.sln`.
- Для parser/AST/`IrBuilder` дополнительно действует `../.cursor/rules/compiler.mdc`.
- Для IR и схемы действует `../.cursor/rules/ir-schema.mdc`; для codegen — `../.cursor/rules/generated-code.mdc`.
- При изменении golden или входных фикстур прочитай `../.cursor/rules/fixtures.mdc` и используй скрипты из `../scripts/`.
- Расширение поддержанного профиля сопровождай обновлением `../docs/status.md`.
