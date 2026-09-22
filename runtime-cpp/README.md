# C++ runtime

Заглушка под будущий нативный runtime BER/DER.

Правила кодирования те же, что у C# runtime: DER только definite length, BOOLEAN `0x00` / `0xFF`; BER на чтении — indefinite length и constructed строки/BIT STRING. Спецификация — в [runtime-csharp/docs/playbooks/runtime.md](../runtime-csharp/docs/playbooks/runtime.md) и [runtime-csharp/docs/runtime-api.md](../runtime-csharp/docs/runtime-api.md); срез поддержки — [docs/status.md](../docs/status.md).

Генератор C++ (`Asn1Kit.Codegen.Cpp`) появится в [compiler/](../compiler/); чеклист — [compiler/docs/playbooks/new-backend.md](../compiler/docs/playbooks/new-backend.md).
