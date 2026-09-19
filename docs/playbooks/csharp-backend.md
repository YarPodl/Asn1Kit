# Плейбук: поддержка `kind` в C# backend

Всё происходит в [src/Asn1Kit.Codegen.CSharp/CSharpBackend.cs](../../src/Asn1Kit.Codegen.CSharp/CSharpBackend.cs). Текущее состояние — в [docs/status.md](../status.md).

## Предусловие

Нужный примитив обязан существовать в runtime. Если для kind нет пары `Write*` / `Read*` в [src/Asn1Kit.Runtime](../../src/Asn1Kit.Runtime), сначала [runtime.md](runtime.md): кодек пишется там, а не в шаблоне генератора.

## Порядок

1. **Снять запрет.** Убрать kind из `EnsureBackendSupport` — но только вместе с реализацией, не «на будущее».
2. **Тип C#.** `CsType`: маппинг kind → тип C# и его nullable-форма для `OPTIONAL`. Текущие: `boolean` → `bool`, `integer` / `enumerated` → `BigInteger`, `octetString` → `byte[]`, `oid` → `string`, `null` → `bool`.
3. **Примитив или именованный тип.** `ResolvePrimitive` возвращает kind для «плоских» типов; `NeedsNamedType` перечисляет те, для которых эмитится отдельный класс (`sequence`, `choice`, `sequenceOf`). Новый составной kind добавляется в обе функции и в `CollectNested`.
4. **Тег по умолчанию.** `UniversalTag` и `UniversalFallback` — universal-тег kind, когда в IR тега нет. Для `SET` / `SET OF` это тег 17, а не 16.
5. **Encode / Decode.** `WriteMethod` / `ReadMethod` — имена методов runtime. `EmitEncodeValue` и `EmitDecodeExpr` уже обрабатывают `EXPLICIT`-обёртку и `OPTIONAL`; повторять это в новой ветке не нужно.
6. **Инициализация.** `Initializer` — значение по умолчанию для non-optional свойства, чтобы `#nullable enable` не давал предупреждений в сгенерированном коде.

## Требования к сгенерированному коду

- Только вызовы runtime: байтов тегов, длин и правил DER в шаблоне быть не должно.
- `options.csharp.namespace` / `typeName` / `propertyName` и `options.generate: false` уважаются (читаются через `IrOptions`).
- Никаких лишних аллокаций: без промежуточных `MemoryStream` на поле, без `Func` на элемент `SEQUENCE OF`, коллекции с известной ёмкостью, где размер известен.
- Ломаный вход падает `Asn1Exception` (чужой тег, неизвестная альтернатива `CHOICE`, лишние байты) — это обеспечивает runtime, задача шаблона его не обходить.

## Тесты

Образец — `RoundTripTests.GeneratedCSharp_CompilesAndRoundTripsPerson` в [tests/Asn1Kit.Tests/Asn1KitTests.cs](../../tests/Asn1Kit.Tests/Asn1KitTests.cs): IR → генерация → компиляция Roslyn → encode/decode через рефлексию.

Минимум на новый kind:

1. Сгенерированный код компилируется (`CompileGenerated` кидает с текстом диагностик, если нет).
2. Round-trip encode → decode → encode даёт те же DER-байты.
3. Байты DER сверены с ожидаемым вектором, а не только «раскодировалось обратно».
4. `OPTIONAL` присутствует и отсутствует — оба случая.
5. Порченый вход даёт `Asn1Exception`.

Не забудь строку в матрице [docs/status.md](../status.md).

```powershell
dotnet test Asn1Kit.sln
```
