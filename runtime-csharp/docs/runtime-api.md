# Публичный API Asn1Kit.Runtime

Контракт Writer/Reader: ownership буферов, горячий путь codegen и инвентарь символов.

Реализация — [`src/Asn1Kit.Runtime`](../src/Asn1Kit.Runtime). Как править кодек — [playbooks/runtime.md](playbooks/runtime.md).
Матрица kind / слоёв — [status.md](../../docs/status.md).

## Политика Memory / Span

- **Вход** и **буфер вызывающего** — `ReadOnlySpan` / `Span` (у `Memory` вызывайте `.Span`; парные перегрузки Span+Memory не делаем — `byte[]` неоднозначен).
- **Вход reader** — дополнительно `ReadOnlyMemory<byte>` ctor (без копии, если array-backed). Reader держит `_data`; `Source` — окно буфера (якорь lifetime).
- **Значения, уходящие из reader** — `ReadOnlyMemory<byte>` / structs с `Memory` / `EncodedMemory` / `ContentsMemory`: **view на буфер reader** (primitive / definite / ANY TLV). Мутация исходного буфера после decode — UB для views. Долговременное хранение без буфера — явный detach (`ToArray` / `Clone`).
- **Исключения (owned):** constructed BER (OCTET / BIT / string — конкатенация сегментов); materialize в `string` / `BigInteger` / `DateTimeOffset`.
- **Value-types:** ctor / `FromContents(ReadOnlyMemory)` — wrap без копии; `CopyFrom(ReadOnlySpan)` — owned копия (отдельное имя, чтобы `byte[]` не был неоднозначен между Span и Memory).
- **Encode (writer):** contents примитивов — scratch на стеке (порог) или прямой write в буфер; heap только для oversized INTEGER/строк и для owned API (`EncodeInteger`, `EncodeContents(string)→byte[]`). Финальный `Encode()→byte[]` и рост внутреннего буфера — отдельно.

## Кто кого вызывает

Горячий путь — прямые `Write*` / `Read*` из C# backend, не обёртки `Asn1Primitives`:

```text
CSharpBackend → Asn1Writer.Write* / Asn1Reader.Read*
              → Asn1Tag, Asn1BitString, Asn1Any, Asn1Lazy<T>, Asn1Exception
```

Статические `Asn1Boolean` / `Asn1Enumerated` / … — warm convenience; codegen их не эмитит.
`Asn1Integer` — **hot** value type (DER contents как `ReadOnlyMemory`; из reader — view) для codegen при `representation=der`; `default` / `Zero` = 0; статические `Encode(BigInteger)` / `DecodeBigInteger` — warm.

Непублично: `Asn1TextCodec` (`internal`), `Asn1Writer.EncodeInteger` (`internal`), `WriteTag` / `WriteLength` / `WriteTlv` / `WritePrimitive` (`private`).

## Инвентарь

Роли: **hot** — каждый generated encode/decode; **warm** — тесты / helpers; **cold** — escape hatch.

| Символ | Роль | Ownership |
| --- | --- | --- |
| `Asn1Writer.EncodedLength` / `EnsureCapacity` / `TryEncode(Span)` / `Encode() → byte[]` / `Encode(Asn1EncodeFunc|Asn1EncodeAction)` / `Reset()` | hot | snapshot / copy-out / zero-copy callback; `Reset` reuse without shrinking capacity |
| `WriteOctetString(ReadOnlySpan)` / `WriteRaw(ReadOnlySpan)` | hot/cold | borrow |
| `WriteSequence` / `WriteSet` / `WriteSetOf` / `WriteSequenceOf<T>` / `WriteSetOf<T>` / `WriteExplicit(Action)` | hot | callback |
| `ReadSequenceOf<T>` / `ReadSetOf<T>` | hot | owned `List<T>` |
| `Asn1Reader(byte[]\|offset/length\|ReadOnlyMemory, encoding, options?)` / `Source` / `Options` | hot | срез без копии на входе; `Source` якорит lifetime; `Options` наследуются nested; nested SEQUENCE — push/pop без `new Asn1Reader` когда contents alias буфер |
| `Asn1ReaderCursor` | warm | явный push/pop окна contents |
| `Asn1ReaderOptions` (`Default` / `Strict` / `AllowNonMinimalLength` / `AllowOverlongOidBase128`) | warm | immutable flags |
| `ReadOctetString → ReadOnlyMemory` / `TryReadOctetString(Span)` | hot | view / copy-out (Try всегда продвигает reader); constructed BER — owned |
| `ReadValue → ReadOnlyMemory` / `TryReadValue(Span)` / `ReadTlv` | cold/warm | view / copy-out |
| `Asn1Any.EncodedMemory` / `ContentsMemory` / `ToArray` | hot | view полного TLV / срез V / detach |
| `Asn1Lazy<T>` / `ReadLazy` / `HasEncoded` / `Value` / `WriteTo` | hot | отложенный decode полного TLV (`options.lazy`); view до `.Value` |
| `Asn1Retained<T>` / `ReadRetained` / `HasEncoded` / `Value` / `WriteTo` | hot | eager decode + retain TLV (`options.retainEncoded`); мутация `.Value` сбрасывает TLV |
| `Asn1Oid` / `Parse` / `ParseArcs` / `EncodeContents` / `ReadOid` / `WriteObjectIdentifier` | hot | единственный OID-codec (string↔arcs↔contents); dotted string — warm `Encode(string)` / `DecodeString` / `ReadObjectIdentifier` |
| `Asn1BitString.Span` / `Memory` / `ToArray` | hot | view (из reader) / detach |
| `Asn1Integer.Span` / `Memory` / `ToArray` | hot | view DER contents / detach |
| `Asn1Primitives` wrappers (+ `Asn1OctetString.TryDecode`) | warm | делегируют |

## Заметки

- `definiteOnly` в private `WriteTlv` игнорируется; indefinite на записи не эмитится.
- `WriteSequence` / `WriteSet` / `WriteSetOf` / `WriteExplicit` пишут nested contents в тот же буфер writer’а (callback получает outer `Asn1Writer`); length — резерв + patch/compact. Внутренний буфер — `byte[]` (не `MemoryStream`); `Reset()` обнуляет длину без освобождения capacity.
- `ReadSequenceOf` / `ReadSetOf` создают `List<T>` с эвристической capacity: пустой OF → 0; мелкий contents (≤32) → 1; иначе `min(32, remaining/16)`.
- `ReadInt32` / `TryGetInt32` (и UInt32/Int64/UInt64) разбирают short contents без `BigInteger`.
- `ReadTime` парсит UTCTime/GeneralizedTime из contents octets без промежуточной `string` (`Asn1TextCodec.ParseTime(span)`).
- Open-type DEFINED BY OID: codegen передаёт `Asn1Oid` и сравнивает со статическими константами (без `Oid.ToString()` на hot path).
- `TryReadOctetString` / `TryReadValue` при нехватке destination возвращают `false`, но TLV уже потреблён.

## Soft-read и строгие опции

Политика: [docs/decisions.md](../../docs/decisions.md) «мягкое чтение». Ниже — инвентарь soft-accept; краткий срез также в [docs/status.md](../../docs/status.md) § Runtime.

- **Encode** всегда канонический (минимальный INTEGER, нулевые trailing bits BIT STRING, …).
- **Decode** по умолчанию принимает soft-формы; строгий reject — `Asn1ReaderOptions` на конструкторе reader’а (`Default` / `Strict` / точечные профили).
- Soft по умолчанию (`Reject* = false`): non-minimal INTEGER contents; BIT STRING nonzero trailing bits (включая режим DER).
- Не soft (default reject): non-minimal length (`RejectNonMinimalLength`); OID overlong base-128 (`RejectOverlongOidBase128`).
- Новый soft-accept без строки в этой секции (+ краткий срез в status) — не допускается.
- В фикстурах: default-кейс = `encode: false` + успешный decode; strict-кейс = `reject: true` + `readerProfile: "strict"` (или `allowNonMinimalLength` / `allowOverlongOid`).

`Asn1Reader` / nested readers наследуют `Options` родителя.

## Чеклист RuntimeTests

Писать в `PrimitiveCodecTests` / `PrimitiveOracleTests` / `ExternalVectorTests` (и точечно в `RuntimeTests`) на байтовых векторах из [fixtures/ber-der/](../fixtures/ber-der/). Round-trip через Roslyn — дополнение, не замена.

На каждый примитив из status § Runtime: DER encode/decode, BER где применимо, round-trip, границы, отказы (чужой тег, truncated, DER indefinite, BOOLEAN не `00`/`FF`). Для soft-форм — пара default-accept / strict-reject.

| API | Минимум |
| --- | --- |
| `TryEncode` / `EncodedLength` | exact fit; short Span → false; равенство с `Encode()` |
| `TryReadOctetString` / `TryReadValue` | fit; short → false; BER constructed OCTET |
| `WriteBoolean` / `ReadBoolean` | DER `00`/`FF`; BER nonzero-as-true |
| `WriteInteger` / `ReadInteger` / `ReadIntegerValue` / `ReadInt32`… | `0`, `-1`, 127/128, длинный; empty reject; soft non-minimal accept + as-is write через `Asn1Integer`; fixed-width range reject |
| `WriteEnumerated` / `ReadEnumerated` | tag `0A`; contents как INTEGER; empty / wrong tag reject |
| `WriteOctetString` / `ReadOctetString` | empty; long-form; BER constructed + indefinite; ROM overload |
| `WriteNull` / `ReadNull` | empty OK; nonempty reject |
| `WriteObjectIdentifier` / `ReadObjectIdentifier` | OID; arcs; rejects |
| `WriteBitString` / `ReadBitString` | unusedBits; encode trailing-zero; soft nonzero trailing accept + strict reject; BER constructed |
| `WriteString` / `ReadString` | 12 forms smoke; BER constructed UTF8 |
| `WriteTime` / `ReadTime` | UTC + Generalized; fractionDigits; BER |
| `WriteSequence` / `ReadSequence` | вложенность; OPTIONAL |
| `WriteSet` / `ReadSet` | tag SET |
| `WriteSetOf` / `WriteSetOf<T>` | DER sort; BER order |
| `WriteSequenceOf<T>` / `ReadSequenceOf<T>` / `ReadSetOf<T>` | list round-trip; empty list |
| `WriteExplicit` | constructed wrapper |
| `WriteAny` / `ReadAny` | IMPLICIT peel; EncodedMemory bit-exact |
| `ReadLazy` / `Asn1Lazy<T>` | defer decode; Value materialize; WriteTo raw TLV |
| `WriteRaw` | append TLV |
| `ReadValue` / `ReadTlv` | view; wrong tag |
| Wrappers | smoke |
| `Asn1BitString` / `Asn1Any` / `Asn1Integer` / `Asn1Null` | EncodedMemory/ContentsMemory alias source; `FromTagAndContents`; `ToArray` detach; equality; `Asn1Integer` numeric accessors + `Zero`/`default`=0; `Asn1Null` singleton value |
