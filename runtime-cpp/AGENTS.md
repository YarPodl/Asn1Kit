# C++ runtime

- Общая документация: [README.md](README.md); публичный контракт: [docs/runtime-api.md](docs/runtime-api.md).
- Обязательные правила: [`../.cursor/rules/reliability-and-testing.mdc`](../.cursor/rules/reliability-and-testing.mdc).
- Runtime реализует BER/DER и не знает об ASN.1-модулях или IR.
- После правок в этом дереве — `ctest` актуального preset; `dotnet test` не нужен, если не трогали .NET-код или общие BER/DER-фикстуры.
