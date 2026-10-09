# Публичный API Asn1Kit.Runtime

На данный момент проект в разработке и его API может меняться в любой момент!

Контракт Writer/Reader: ownership буферов, горячий путь codegen и инвентарь символов.

Реализация — [`src/Asn1Kit.Runtime`](../src/Asn1Kit.Runtime). Как править кодек — [playbooks/runtime.md](playbooks/runtime.md).
Матрица kind / слоёв — [status.md](../../docs/status.md).

## Политика Memory / Span

- **Вход** и **буфер вызывающего** — `ReadOnlySpan` / `Span` (у `Memory` вызывайте `.Span`; парные перегрузки Span+Memory не делаем — `byte[]` неоднозначен).
- **Вход reader** — дополнительно `ReadOnlyMemory<byte>` ctor без копии, включая custom `MemoryManager`. Reader содержит value-type `Asn1DecodeCursor`; `Remaining` — байты до конца текущего окна.
- **Значения, уходящие из reader** — `ReadOnlyMemory<byte>` / structs с `Memory` / `EncodedMemory` / `ContentsMemory`: **view на буфер reader** (primitive / definite / ANY TLV). Мутация исходного буфера после decode — UB для views. Долговременное хранение без буфера — явный detach (`ToArray` / `Clone`).
- **Исключения (owned):** constructed BER (OCTET / BIT / string — конкатенация сегментов); materialize в `string` / `BigInteger` / `DateTimeOffset`.
- **Value-types:** ctor / `FromContents(ReadOnlyMemory)` — wrap без копии; `CopyFrom(ReadOnlySpan)` — owned копия (отдельное имя, чтобы `byte[]` не был неоднозначен между Span и Memory).
- **Encode (writer):** contents примитивов — scratch на стеке (порог) или прямой write в буфер; heap только для oversized INTEGER/строк и owned API. Числовые фабрики `Asn1Integer` для однобайтового DER-диапазона используют общий read-only lookup, остальные `Asn1Integer.From*` владеют отдельным массивом. Финальный `Encode()→byte[]` и рост внутреннего буфера — отдельно.

## Кто кого вызывает

Горячий путь — прямые `Write*` / `Read*` из C# backend, не обёртки `Asn1Primitives`:

```text
CSharpBackend → Asn1Writer.Write* / Asn1Reader.Read*
              → Asn1Tag, Asn1BitString, Asn1Any, Asn1Lazy<T>, Asn1Value<T>, Asn1Exception
```

Статические `Asn1Boolean` / `Asn1Enumerated` / … — warm convenience; codegen их не эмитит.
`Asn1Integer` — **hot** value type (DER contents как `ReadOnlyMemory`; из reader — view) для codegen при `representation=der`; `default` / `Zero` = 0; статические `Encode(BigInteger)` / `DecodeBigInteger` — warm.

Непублично: `Asn1EncodeBuffer` (`internal struct`, буфер и TLV framing), `Asn1DecodeCursor` / `Asn1Tlv` / `Asn1ConstructedDecoder` / `Asn1TextCodec` (`internal`); `Asn1Boolean.DecodeContents` / numeric contents codec и `IsMinimalContents` в `Asn1Integer` / `Asn1BitString.ParsePrimitive` (`internal`).

## Строковое представление значений

`Asn1Formatting.Format(object? value)` используется для отображения, независимо от текущей культуры: числа — `InvariantCulture`, время — формат `O`, строки — без кавычек, `null` — строка `null`. Для остальных объектов вызывается их `ToString()`; рекурсивного обхода составных значений нет.

`Asn1Integer.ToString()` возвращает десятичное число, `Asn1Null` — `NULL`, `Asn1BitString` — `N bits` с учётом `UnusedBits`. Байт-массивы и `Memory<byte>` / `ReadOnlyMemory<byte>` форматируются как `N bytes`, без HEX. `Asn1Any` показывает тег и длину полного TLV, например `Universal-2P (3 bytes)`; значение по умолчанию — `<empty>`. Форматы OID и тегов сохранены.

`Asn1Value<T>` форматирует `Value`; default с отсутствующим ссылочным значением возвращает `<unset>`. `Asn1Lazy<T>` до материализации возвращает `<not decoded: N bytes>` без вызова декодера, после — представление сохранённого значения.

Сгенерированные CHOICE и ANY DEFINED BY с bindings выводят только выбранное значение; неизвестная open-type альтернатива использует представление `Asn1Any`. Дополнительного состояния для форматирования нет. CHOICE выбирает значение по существующему `Kind`, в том числе первую ветку у нового экземпляра. Open type с `Kind.None` возвращает `<unset>`; компактная форма без `Kind` возвращает `<unset>`, если все свойства равны `null`, и не различает новый экземпляр и фабрику с переданным `null`. Выбранная через `Kind` альтернатива с `null` форматируется как `null`.

## Инвентарь

Роли: **hot** — каждый generated encode/decode; **warm** — тесты / helpers; **cold** — escape hatch.

| Символ | Роль | Ownership |
| --- | --- | --- |
| `Asn1Writer.EncodedLength` / `EnsureCapacity` / `TryEncode(Span)` / `Encode() → byte[]` / `Encode(Asn1EncodeFunc|Asn1EncodeAction)` / `Reset()` | hot | snapshot / copy-out / zero-copy callback; `Reset` reuse without shrinking capacity |
| `WriteOctetString(ReadOnlySpan)` / `WriteRaw(ReadOnlySpan)` | hot/cold | borrow |
| `WriteSequenceOf<T>` / `WriteSetOf<T>` | hot | collection + item callback; `SET OF` inherits DER sorting from its internal scope |
| `EnterSequence` / `EnterSet` / `EnterSequenceOf` / `EnterSetOf` / `EnterExplicit` → `Asn1WriterScope` | hot | allocation-free begin/end frame; запись содержимого через тот же writer; dispose только один раз и в LIFO-порядке |
| `ReadSequenceOf<T>` / `ReadSetOf<T>` | hot | owned `T[]` (`Array.Empty<T>` when empty); OF fill без capturing-лямбды вокруг `decodeItem` |
| `ReadSequenceOf<T,TState>` / `ReadSetOf<T,TState>` | hot | явное состояние + `Func<Asn1Reader,TState,T>`; generated static callback не захватывает контекст |
| `EnterSequence` / `EnterSet` / `EnterExplicit` → `Asn1ReaderScope` | hot | allocation-free push/pop окна; фактический тег обязан быть constructed; dispose только один раз и в LIFO-порядке |
| `Asn1Reader(byte[]\|offset/length\|ReadOnlyMemory, encoding, options?)` / `Remaining` / `Options` / `ThrowIfNotEmpty()` | hot | входной `ReadOnlyMemory` не копируется; `Remaining` — байты до конца текущего окна; `ThrowIfNotEmpty` отвергает непрочитанный хвост без продвижения; options сохраняются во вложенных окнах |
| `Asn1ReaderScope` | hot | публично только `Dispose`; произвольного `Push` и доступа к reader через scope нет |
| `Asn1WriterScope` | hot | публично только `Dispose`; `Encode` / `TryEncode` / `Reset` запрещены, пока открыт хотя бы один scope |
| `Asn1ReaderOptions` (`Default` / `Strict` / `AllowNonMinimalLength` / `AllowOverlongOidBase128`) | warm | immutable flags |
| `ReadOctetString → ReadOnlyMemory` / `TryReadOctetString(Span)` | hot | primitive view / copy-out; short destination → `false`, `bytesWritten=0`, reader не продвигается; constructed BER — owned |
| `ReadAny` | hot/cold | единственный raw TLV escape hatch: tag + encoded/contents views |
| `Asn1Any.EncodedMemory` / `ContentsMemory` / `ToArray` / `DecodeValue<T>` | hot/warm | view полного TLV / срез V / detach; `DecodeValue<T>` создаёт reader поверх TLV без копии и требует полного потребления |
| `Asn1Lazy<T>` / `ReadLazy` / `HasEncoded` / `Value` / `WriteTo` | hot | отложенный decode полного TLV (`options.lazy`); view до `.Value` |
| `Asn1Value<T>` / `ReadWithOriginalEncoding` / `Value` / `OriginalEncoding` | hot | allocation-free eager decode + view исходного полного TLV (`options.retainEncoded`); encode использует `Value`, `T` неявно оборачивается без исходного TLV |
| `Asn1Oid` / `Parse` / `ParseArcs` / `EncodeContents` / `ReadOid` / `WriteObjectIdentifier` | hot | единственный OID-codec (string↔arcs↔contents); dotted string — warm `Encode(string)` / `DecodeString` / `ReadObjectIdentifier` |
| `Asn1BitString.Span` / `Memory` / `ToArray` | hot | view (из reader) / detach |
| `Asn1Integer.Span` / `Memory` / `ToArray` | hot | view DER contents / detach |
| `Asn1Primitives` wrappers (+ `Asn1OctetString.TryDecode`) | warm | делегируют |
| `Asn1Contained<T>` / `ReadContained` / `WriteContained` | hot | исходные contents + optional typed value; typed decode в том же reader, encode в том же writer |
| `EnterEncoded(ReadOnlyMemory<byte>)` | hot | окно ранее прочитанного TLV для отложенного SET decode; те же encoding/options, scope восстанавливает исходное окно |
| `Asn1Any.FromValue<T>(T, Action<Asn1Writer,T>)` | warm | owned DER TLV одного значения, используется для констант typed DEFAULT |
| `Asn1Codec<T>` / `Asn1Codecs` | warm/hot | переиспользуемая пара reader/writer; singleton-кодеки примитивов и общие адаптеры raw open type для ANY, CONTAINING, массивов и decoder-only string CHOICE (`DecodeStringChoice`) |
| `Asn1Collection.Count<T>(IReadOnlyList<T>, Func<T,bool>)` | hot | подсчёт без временных коллекций для структурного сравнения SET OF DEFAULT |

## CONTAINING и неизвестные расширения

`Asn1Contained<T>.FromValue(value)` создаёт типизированное содержимое; `FromEncoded(contents, unusedBits = 0)` сохраняет opaque октеты без копирования. `Contents` — первоначальные contents внешнего OCTET/BIT STRING (без unused-bits октета у BIT STRING); `Value` доступно только при `HasValue`. Decode известного содержимого проверяет полное потребление и сохраняет исходные октеты. Encode известного значения использует текущее `Value`, opaque — исходные `Contents`. Typed BIT STRING требует выравнивания по октетам; opaque сохраняет `UnusedBits`.

`ReadContained<T,TState>(tag, bitString, known, state, decode)` использует обычный string decoder, включая constructed BER, и при `known` открывает окно contents с теми же options. `WriteContained<T>(tag, bitString, value, encode)` помещает DER-представление в primitive string, используя тот же буфер. Открытые современные таблицы передают `known` по OID; неизвестное содержимое не декодируется по догадке о теге.

Расширяемые SEQUENCE/SET при decode пропускают неизвестные extension additions (`ReadAny` без сохранения); encode пишет только известные компоненты. Расширяемый CHOICE сохраняет неизвестную альтернативу целиком как raw TLV.

## Заметки

- `Asn1Writer` — публичный фасад над `Asn1EncodeBuffer`; на записи всегда эмитится минимальная definite length.
- `EnterSequence` / `EnterSet` / `EnterSequenceOf` / `EnterSetOf` / `EnterExplicit` пишут nested contents в тот же encode-буфер. Для коллекций есть компактные `WriteSequenceOf<T>` / `WriteSetOf<T>` с item callback; они открывают соответствующий scope и вызывают callback для каждого элемента. Под length резервируется один октет, а `Asn1WriterScope.Dispose()` завершает frame и при необходимости расширяет длинную форму с минимальным сдвигом. `EnterSetOf` и `WriteSetOf<T>` сортируют TLV только в DER; обычный `EnterSet` сохраняет порядок полей.
- Writer scopes закрываются ровно один раз и строго в LIFO-порядке. Scope не транзакционный: при исключении внутри `using` уже записанное содержимое финализируется; после обработки ошибки writer можно очистить через `Reset()`. Внутренний буфер — `byte[]` (не `MemoryStream`), `Reset()` не уменьшает capacity.
- `ReadSequenceOf` / `ReadSetOf` возвращают `T[]`: пустой OF → `Array.Empty<T>()`; один элемент → `new T[1]` без pool; иначе grow через `ArrayPool<T>` и точный `T[count]`. Заполнение идёт через `EnterSequence` (без capturing-лямбды вокруг `decodeItem`).
- `EnterSequence` / `EnterSet` / `EnterExplicit` открывают обычные constructed значения; `EnterEncoded` открывает сохранённый TLV, `ReadContained` — contents string-типа. Для encode scope-методы обязательны для structured значений и ручного OF; collection OF дополнительно использует `WriteSequenceOf<T>` / `WriteSetOf<T>` с `Action<Asn1Writer, T>`.
- `Asn1ReaderScope.Dispose()` только восстанавливает внешнее окно и проверяет LIFO. Полное потребление проверяется явным `ThrowIfNotEmpty()` в успешной ветке decode, чтобы исключение при unwind не заменяло исходную ошибку.
- `ReadInt32` / `TryGetInt32` (и UInt32/Int64/UInt64) разбирают short contents без `BigInteger`.
- `ReadTime` парсит UTCTime/GeneralizedTime из contents octets без промежуточной `string` (`Asn1TextCodec.ParseTime(span)`).
- Open-type DEFINED BY OID: codegen передаёт `Asn1Oid` и сравнивает со статическими константами (без `Oid.ToString()` на hot path).
- `TryPeekTag` работает на копии cursor: malformed tag бросает `Asn1Exception`, не меняя позицию. `TryReadOctetString` коммитит cursor только при успехе.
- Обычный `Read*` после исключения не гарантирует сохранение позиции: продолжать decode после ошибки входа нельзя.

## Soft-read и строгие опции

Политика: [docs/decisions.md](../../docs/decisions.md) «мягкое чтение». Ниже — инвентарь soft-accept; краткий срез также в [docs/status.md](../../docs/status.md) § Runtime.

- **Encode** всегда канонический (минимальный INTEGER, нулевые trailing bits BIT STRING, …).
- **Decode** по умолчанию принимает soft-формы; строгий reject — `Asn1ReaderOptions` на конструкторе reader’а (`Default` / `Strict` / точечные профили).
- Soft по умолчанию (`Reject* = false`): non-minimal INTEGER contents; BIT STRING nonzero trailing bits (включая режим DER).
- Не soft (default reject): non-minimal length (`RejectNonMinimalLength`); OID overlong base-128 (`RejectOverlongOidBase128`).
- Новый soft-accept без строки в этой секции (+ краткий срез в status) — не допускается.
- В фикстурах: default-кейс = `encode: false` + успешный decode; strict-кейс = `reject: true` + `readerProfile: "strict"` (или `allowNonMinimalLength` / `allowOverlongOid`).

`Asn1Reader` / nested readers наследуют `Options` родителя.

Минимум покрытия API по тестам — [playbooks/runtime.md](playbooks/runtime.md) § Чеклист.