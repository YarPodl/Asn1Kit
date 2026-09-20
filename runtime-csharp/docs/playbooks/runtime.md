# Плейбук: правки runtime BER/DER

[Asn1Kit.Runtime](../../src/Asn1Kit.Runtime) — горячий путь каждого encode/decode и единственное место, где живут правила кодирования. Требования жёстче, чем к остальному проекту, см. правило `runtime.mdc`.

## Устройство

| Файл | Что внутри |
| --- | --- |
| [Asn1Tag.cs](../../src/Asn1Kit.Runtime/Asn1Tag.cs) | Класс тега, universal-константы, `Asn1StringForm` / `Asn1TimeForm`, `MatchesIgnoreConstructed`, `AsPrimitive` / `AsConstructed` |
| [Asn1Writer.cs](../../src/Asn1Kit.Runtime/Asn1Writer.cs) | Публичные `Write*` / `Encode` / `WriteRaw`; внутри (`private`) `WriteTag` / `WriteLength` / `WriteTlv` / `WritePrimitive` / `WriteConstructed` (один буфер + резерв длины) |
| [Asn1Reader.cs](../../src/Asn1Kit.Runtime/Asn1Reader.cs) | Чтение TLV, definite и indefinite length, `TryPeekTag`, `Eof`, публичные `ReadValue` / `ReadTlv`, ctors: `byte[]`, `(byte[], offset, length)`, `ReadOnlyMemory<byte>` |
| [Asn1BitString.cs](../../src/Asn1Kit.Runtime/Asn1BitString.cs) | `Asn1BitString` (Memory wrap / `CopyFrom`), `UnusedBits`, indexer MSB-first, `FromBits` |
| [Asn1Any.cs](../../src/Asn1Kit.Runtime/Asn1Any.cs) | `Asn1Any` (`Tag` + `ContentsMemory`); Memory wrap / `CopyFrom` |
| [Asn1TextCodec.cs](../../src/Asn1Kit.Runtime/Asn1TextCodec.cs) | `internal`: encode/decode строк и времени (наборы символов, DER/BER-формы) |
| [Asn1Integer.cs](../../src/Asn1Kit.Runtime/Asn1Integer.cs) | Value type: DER contents as Memory (view from reader); `FromContents` / `CopyFrom` / `FromBigInteger` / `GetInt32`…; hot для codegen `der` |
| [Asn1Primitives.cs](../../src/Asn1Kit.Runtime/Asn1Primitives.cs) | `Asn1Boolean` / `Asn1Enumerated` / `Asn1OctetString` / … — warm обёртки; **C# backend эмитит `writer.Write*` / `reader.Read*` напрямую** |

Кодировка выбирается через `Asn1Encoding.Ber` / `Asn1Encoding.Der` в конструкторе writer'а и reader'а.

Публичный API (ownership, инвентарь, чеклист) — [runtime-api.md](../runtime-api.md). Backlog оптимизаций — [status.md](../../../docs/status.md) § «Backlog: оптимизация runtime».

## Новый примитив

1. `Write*` в `Asn1Writer` и `Read*` в `Asn1Reader` — строго парой (это то, что эмитит C# backend).
2. Universal-тег в `Asn1Tag`, если его ещё нет (например `Set` = 17).
3. Обёртка `Asn1Xxx.Encode` / `Decode` в `Asn1Primitives` с параметром `Asn1Tag? tag = null` — для симметрии тестов и ручного использования; codegen её не вызывает.
4. Только после этого — ветка в C# backend, см. [csharp-backend.md](../../../compiler/docs/playbooks/csharp-backend.md).
5. Обновить инвентарь в [runtime-api.md](../runtime-api.md).

## Правила, которые нельзя нарушать

- **Запись DER:** только definite length, BOOLEAN строго `0x00` / `0xFF`, BIT STRING с нулевыми хвостовыми битами, минимальный INTEGER из чисел. `WriteInteger(Asn1Integer)` пишет сохранённые contents as-is (владение проводом). Encode из числовых типов не ослабляется soft-profile.
- **Чтение:** soft-accept для зафиксированных неканоничных форм (см. [decisions.md](../../../docs/decisions.md), [status.md](../../../docs/status.md), [runtime-api.md](../runtime-api.md)). Строгий reject — через опции reader’а, не через молчаливое ужесточение default. Новый soft-accept без записи в status/runtime-api — запрещён.
- **BER на чтении:** definite и indefinite length, constructed `OCTET STRING` склеивается. На записи indefinite length не порождается (`definiteOnly` в private `WriteTlv` сейчас не используется — поведение то же).
- Ошибка ввода (вне soft-списка) — всегда `Asn1Exception` с внятным текстом: чужой тег, обрезанный TLV, лишние байты, невалидная строка OID.
- Runtime ничего не знает про ASN.1-модули, имена типов и IR.

## Производительность

- Срез `ReadOnlySpan<byte>` вместо копии буфера; копия — только когда владение уходит наружу.
- Не выделять `byte[]` на каждый тег и длину; переиспользовать буфер writer'а.
- Без LINQ, `ToArray()` и `new Asn1Reader(copy)` на горячем пути.

```csharp
// ❌ BAD: копия содержимого на каждый ReadValue
var contents = new byte[length];
Buffer.BlockCopy(_data, _offset, contents, 0, length);

// ✅ GOOD: срез без копии
ReadOnlySpan<byte> contents = _data.AsSpan(_offset, length);
```

## Тесты

Три слоя (см. [fixtures/ber-der/README.md](../../fixtures/ber-der/README.md)):

1. **Своя матрица** — hex JSON в `runtime-csharp/fixtures/ber-der/`, прогон в `PrimitiveCodecTests` (и точечные кейсы в `RuntimeTests`).
2. **Oracle** — `PrimitiveOracleTests` + `DotnetAsnOracle` против `System.Formats.Asn1` (DER байт-в-байт; BER — по значению).
3. **Внешние векторы** — `runtime-csharp/fixtures/ber-der/external/` + `ExternalVectorTests` (поле `source` обязательно).

Обязательный набор на каждый тип:

1. DER: encode даёт ожидаемые байты; decode их читает.
2. BER: decode известного вектора, включая indefinite length и constructed форму, где она возможна.
3. Round-trip encode → decode → encode совпадает побайтово.
4. Границы: пустое значение, `0`, `-1`, длинный `BigInteger`, длина > 127 (длинная форма), вложенность.
5. Отказы всегда: indefinite length в DER, BOOLEAN не `0x00` / `0xFF`, чужой тег, обрезанный TLV, EOF.
6. Soft-формы: default decode OK; при включённой strict-опции — `Asn1Exception` (пара фикстур).

```csharp
var ber = new byte[] { 0x24, 0x80, 0x04, 0x03, 0x41, 0x6E, 0x6E, 0x00, 0x00 };
var reader = new Asn1Reader(ber, Asn1Encoding.Ber);
Assert.Equal("Ann", Encoding.UTF8.GetString(reader.ReadOctetString(Asn1Tag.OctetString).Span));
```

```powershell
dotnet test Asn1Kit.sln
```
