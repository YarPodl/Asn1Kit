# Архитектура Asn1Kit

Три независимых слоя. Компилятор знает ASN.1, генератор знает целевой язык, runtime знает BER/DER.

```text
ASN.1 module  -->  Compiler (C#)  -->  IR JSON (schema v1)
                                           |
                                           v  (можно править options)
                                      Code generator
                                           |
                                           v
                                   Generated C# types
                                           |
                                           v
                                   Asn1Kit.Runtime (BER/DER)
```

## Проекты

| Проект | Роль |
| --- | --- |
| `Asn1Kit.Ir` | Модель IR, JSON, валидация по JSON Schema |
| `Asn1Kit.Compiler` | Лексер, парсер, резолв имён, эмит IR |
| `Asn1Kit.Codegen` | `ILanguageBackend` |
| `Asn1Kit.Codegen.CSharp` | Генерация `.cs` |
| `Asn1Kit.Runtime` | TLV, примитивы BER/DER |
| `Asn1Kit.Cli` | Команды `compile` и `generate` |

C++ планируется как ещё один backend и отдельный runtime, без изменений фронтенда.

## IR

Единственный контракт — JSON со схемой `schemas/asn1kit-ir-v1.json`. Поле `irVersion`. Словари `options` расширяемы. Генератор читает `options.csharp.*` и `options.generate`.

`asn1kit compile` перезаписывает IR из ASN.1. Правки `options` вносите в JSON перед `generate`, либо не пересобирайте IR.

## Runtime

`Asn1Writer` / `Asn1Reader` принимают `Asn1Encoding.Ber` или `Asn1Encoding.Der`.

- DER: только definite length, минимальная кодировка INTEGER, BOOLEAN `0xFF` / `0x00`.
- BER: decode принимает definite и indefinite length; constructed `OCTET STRING` склеивается.

Сгенерированные типы вызывают writer/reader и не дублируют кодек.
