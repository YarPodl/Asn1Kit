# C# runtime

- Общая документация: [README.md](README.md); публичный контракт: [docs/runtime-api.md](docs/runtime-api.md).
- Обязательные правила: `../.cursor/rules/reliability-and-testing.mdc` и `../.cursor/rules/runtime.mdc`.
- Для фикстур и generated дополнительно действует `../.cursor/rules/fixtures.mdc`.
- Runtime реализует BER/DER и не знает об ASN.1-модулях или IR.
- Изменение публичного API, BER/DER-поведения или горячего пути покрывай профильными тестами и полным `dotnet test Asn1Kit.sln`.
