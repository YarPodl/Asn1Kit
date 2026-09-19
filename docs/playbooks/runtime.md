# Плейбук: правки runtime BER/DER

[src/Asn1Kit.Runtime](../../src/Asn1Kit.Runtime) — горячий путь каждого encode/decode и единственное место, где живут правила кодирования. Требования жёстче, чем к остальному проекту, см. правило `runtime.mdc`.

## Устройство

| Файл | Что внутри |
| --- | --- |
| [Asn1Tag.cs](../../src/Asn1Kit.Runtime/Asn1Tag.cs) | Класс тега, universal-константы, `Asn1StringForm` / `Asn1TimeForm`, `MatchesIgnoreConstructed`, `AsPrimitive` / `AsConstructed` |
| [Asn1Writer.cs](../../src/Asn1Kit.Runtime/Asn1Writer.cs) | `WriteTag` / `WriteLength` / `WriteTlv`, `WriteSequence` через вложенный writer, `WriteExplicit`, `WriteRaw`, примитивы включая `WriteBitString` / `WriteString` / `WriteTime` |
| [Asn1Reader.cs](../../src/Asn1Kit.Runtime/Asn1Reader.cs) | Чтение TLV, definite и indefinite length, `TryPeekTag`, `Eof`, `ReadValue`, `ReadBitString` / `ReadString` / `ReadTime` |
| [Asn1BitString.cs](../../src/Asn1Kit.Runtime/Asn1BitString.cs) | `Asn1BitString` (`Bytes` + `UnusedBits`), индексатор MSB-first, `FromBits`, статические `Encode` / `Decode` |
| [Asn1TextCodec.cs](../../src/Asn1Kit.Runtime/Asn1TextCodec.cs) | Внутренние encode/decode строк и времени (наборы символов, DER/BER-формы) |
| [Asn1Primitives.cs](../../src/Asn1Kit.Runtime/Asn1Primitives.cs) | `Asn1Boolean` / `Asn1Integer` / `Asn1OctetString` / `Asn1Null` / `Asn1ObjectIdentifier` / `Asn1String` / `Asn1Time` — тонкие обёртки для сгенерированного кода |

Кодировка выбирается через `Asn1Encoding.Ber` / `Asn1Encoding.Der` в конструкторе writer'а и reader'а.

## Новый примитив

1. `Write*` в `Asn1Writer` и `Read*` в `Asn1Reader` — строго парой.
2. Universal-тег в `Asn1Tag`, если его ещё нет (например `Set` = 17).
3. Обёртка `Asn1Xxx.Encode` / `Decode` в `Asn1Primitives` с параметром `Asn1Tag? tag = null` — её вызывает сгенерированный код.
4. Только после этого — ветка в C# backend, см. [csharp-backend.md](csharp-backend.md).

## Правила, которые нельзя нарушать

- **DER:** только definite length, минимальная кодировка INTEGER, BOOLEAN строго `0x00` / `0xFF`. Нарушение на чтении — `Asn1Exception`, а не «терпимо принять».
- **BER на чтении:** definite и indefinite length, constructed `OCTET STRING` склеивается. На записи indefinite length не порождается.
- Ошибка ввода — всегда `Asn1Exception` с внятным текстом: чужой тег, обрезанный TLV, лишние байты, невалидная строка OID.
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

Пишутся в классе `RuntimeTests` в [tests/Asn1Kit.Tests/Asn1KitTests.cs](../../tests/Asn1Kit.Tests/Asn1KitTests.cs) — на байтовых векторах, а не через сгенерированные типы.

Обязательный набор на каждый тип:

1. DER: encode даёт ожидаемые байты; decode их читает.
2. BER: decode известного вектора, включая indefinite length и constructed форму, где она возможна.
3. Round-trip encode → decode → encode совпадает побайтово.
4. Границы: пустое значение, `0`, `-1`, длинный `BigInteger`, длина > 127 (длинная форма), вложенность.
5. Отказы: indefinite length в DER, неминимальный INTEGER, BOOLEAN не `0x00` / `0xFF`, чужой тег, обрезанный TLV, EOF.

```csharp
var ber = new byte[] { 0x24, 0x80, 0x04, 0x03, 0x41, 0x6E, 0x6E, 0x00, 0x00 };
var reader = new Asn1Reader(ber, Asn1Encoding.Ber);
Assert.Equal("Ann", Encoding.UTF8.GetString(reader.ReadOctetString(Asn1Tag.OctetString)));
```

```powershell
dotnet test Asn1Kit.sln
```
