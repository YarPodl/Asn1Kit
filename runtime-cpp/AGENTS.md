# C++ runtime

- Общая документация: [README.md](README.md); публичный контракт: [docs/runtime-api.md](docs/runtime-api.md).
- Обязательные правила: [`../.cursor/rules/reliability-and-testing.mdc`](../.cursor/rules/reliability-and-testing.mdc).
- Runtime реализует BER/DER и не знает об ASN.1-модулях или IR.
- После правок в этом дереве из корня репозитория запусти `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-runtime-cpp.ps1`. Скрипт выполняет обязательную последовательность configure → build → test для `fetch-default`; один `ctest` не пересобирает изменённый код и итоговой проверкой не считается.
- Для подготовленного Conan build добавь `-Preset conan-default` (или `conan-release`) к этой команде. `dotnet test` не нужен, если не затронуты .NET-код или общие BER/DER-фикстуры.
