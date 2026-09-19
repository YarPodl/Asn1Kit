# Архитектура Asn1Kit

Три независимых слоя. Компилятор знает ASN.1, генератор знает целевой язык, runtime знает BER/DER.

```text
ASN.1 module  -->  Compiler  -->  IR (YAML/JSON)
                                      |
                                      v  (пользователь может править options)
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
| `Asn1Kit.Ir` | Модель IR, YAML/JSON, валидация |
| `Asn1Kit.Compiler` | Лексер, парсер, резолв имён, построение IR |
| `Asn1Kit.Codegen` | `ILanguageBackend` |
| `Asn1Kit.Codegen.CSharp` | Генерация `.cs` |
| `Asn1Kit.Runtime` | TLV, примитивы, SEQUENCE/CHOICE helpers |
| `Asn1Kit.Cli` | Команды `compile` и `generate` |

C++ планируется как ещё один backend и отдельный runtime, без изменений фронтенда.

## IR

Контракт версионирован полем `irVersion`. Словари `options` на модуле, типе и поле расширяемы: неизвестные ключи сохраняются при чтении и записи. Генератор читает ключи `csharp.namespace`, `csharp.typeName`, `csharp.propertyName`, `generate`.

Повторный `compile` перезаписывает IR из ASN.1; правки `options` нужно вносить в YAML перед `generate`, либо не пересобирать IR.

## Runtime

Общий TLV-слой. `Asn1Writer` / `Asn1Reader` принимают `Asn1Encoding.Ber` или `Asn1Encoding.Der`.

- DER: только definite length, минимальная кодировка INTEGER, BOOLEAN `0xFF`/`0x00`.
- BER: decode принимает definite и indefinite length; constructed `OCTET STRING` склеивается.

Сгенерированные типы вызывают writer/reader и не дублируют кодек.
