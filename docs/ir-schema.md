# Схема IR v1

Контракт между компилятором ASN.1 и генераторами кода. Формат — только JSON, схема: [schemas/asn1kit-ir-v1.json](../schemas/asn1kit-ir-v1.json) (Draft 2020-12).

Имена полей — camelCase. `kind` типов — camelCase (`octetString`, `sequenceOf`, `bitString`).

Расширения аддитивны: `irVersion` остаётся `1`, старые документы валидны.

## Корень

| Поле | Тип | Описание |
| --- | --- | --- |
| `irVersion` | number | Сейчас `1`. Несовместимые изменения → `2`. |
| `sourceFiles` | string[]? | Имена исходных файлов (диагностика). |
| `options` | object? | Свободный словарь. |
| `modules` | array | Модули (несколько — для `IMPORTS`). |

## Модуль

| Поле | Описание |
| --- | --- |
| `name` | Имя модуля |
| `oid` | Dotted-строка (`"1.3.6.1"`), если OID был в исходнике |
| `tagDefault` | `explicit`, `implicit` или `automatic` |
| `imports` | `{ module, types[], values? }` |
| `options` | Свободный словарь |
| `types` | Именованные определения `{ name, type, options? }` |
| `values` | Value assignments `{ name, type, value, options? }` |

## Выражение типа (`type`)

Дискриминатор `kind`:

- `boolean`, `null`, `octetString`, `oid`
- `integer` — опционально `namedValues: [{ name, value }]`
- `enumerated` — `values: [{ name, value }]`
- `bitString` — опционально `namedBits: [{ name, value }]`
- `string` — обязательно `stringType`: `utf8` \| `printable` \| `teletex` \| `t61` \| `ia5` \| `numeric` \| `visible` \| `bmp` \| `universal` \| `general` \| `graphic` \| `videotex`
- `time` — обязательно `timeType`: `utc` \| `generalized`
- `any` — опционально `definedBy` (имя sibling-компонента)
- `sequence` / `set` / `choice` — `components[]`, опционально `extensible`
- `sequenceOf` / `setOf` — `element`
- `ref` — `name`, опционально `module`

У любого выражения опциональны `tag`, `constraint`, `options`.

```json
{
  "class": "context",
  "number": 0,
  "mode": "implicit"
}
```

`class`: `universal` | `application` | `context` | `private`.  
`mode`: `implicit` | `explicit`.

Компилятор раскрывает `AUTOMATIC TAGS` в явные `tag` на компонентах. Генератор правила X.680 не повторяет.

### Constraint

```json
{
  "size": { "min": 1, "max": 64 },
  "value": { "min": 0, "max": 256 },
  "unsupported": "(FROM (\"a\"..\"z\"))"
}
```

- `size` / `value`: `{ min, max? }` — отсутствие `max` означает `MAX`.
- Ссылки на value assignments (`ub-name`) разрешаются компилятором в числа.
- Немоделируемые формы (например `FROM`) сохраняются в `unsupported`.

## Компонент SEQUENCE / SET / CHOICE

`name`, `type`, `optional`, `default?` (выражение значения), `options`.

Для CHOICE `optional` / `default` запрещены. `DEFAULT` в ASN.1 делает компонент optional и записывает разрешённое значение в `default`.

## Значения (`module.values` и `default`)

Дискриминатор `kind`:

| kind | Поля |
| --- | --- |
| `integer` | `value` |
| `boolean` | `value` |
| `null` | — |
| `oid` | `value: string` dotted-форма (`"1.3.6.1"`; цепочки вроде `{ id-pkix 1 }` уже развёрнуты) |
| `string` | `value` |
| `bitString` | `bits?` / `hex?` |
| `ref` | `name`, опционально `module` (в скомпилированном IR обычно уже разрешён) |

## Options

Объект с `additionalProperties: true`. Неизвестные ключи сохраняются при round-trip.

Рекомендуемая вложенность по бэкенду:

| Путь | Где | Смысл |
| --- | --- | --- |
| `options.csharp.namespace` | модуль | Namespace |
| `options.csharp.typeName` | тип | Имя класса |
| `options.csharp.propertyName` | поле | Имя свойства |
| `options.generate` | тип | `false` — не генерировать |

## Примеры

- [fixtures/ir/example.json](../fixtures/ir/example.json) — минимальный SEQUENCE
- [fixtures/ir/pkix1-explicit88.json](../fixtures/ir/pkix1-explicit88.json) — RFC 5280 Appendix A.1
