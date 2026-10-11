# Compiler и codegen

- Общая документация и команды: [README.md](README.md).
- Набор тестов выбирай по [`../.cursor/rules/reliability-and-testing.mdc`](../.cursor/rules/reliability-and-testing.mdc). Изолированные изменения `Asn1Kit.Codegen.Cpp` и его тестов требуют только `CppCodegenTests` через `--filter`, а не полного solution; при изменениях C++ runtime/generated добавь `ctest`. Изменения общих слоёв compiler/IR/codegen требуют более широкого прогона по таблице правил.
- Для parser/AST/`IrBuilder` дополнительно действует `../.cursor/rules/compiler.mdc`.
- Для IR и схемы действует `../.cursor/rules/ir-schema.mdc`; для codegen — `../.cursor/rules/generated-code.mdc`.
- При изменении golden или входных фикстур прочитай `../.cursor/rules/fixtures.mdc` и используй скрипты из `../scripts/`.
- Расширение поддержанного профиля сопровождай обновлением `../docs/status.md`.
