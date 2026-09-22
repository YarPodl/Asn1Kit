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
- `time` — обязательно `timeType`: `utc` \| `generalized`; опционально `fractionDigits` `0…7` (только `generalized`; отсутствие = 3 при записи)
- `any` — опционально `definedBy` (имя sibling-компонента); опционально `bindings: [{ key, type }]` — таблица open-type (ключ — dotted OID или десятичный INTEGER); заполняется overlay после compile, не парсером ASN.1 1988. C# при `bindings` эмитит `Owner_Field` с `From…`/`Unknown` (без enum `Kind`); `options.openType.mismatch`: `soft` \| `strict` (default `soft`).
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
| `options.integer.representation` | тип / модуль | Представление INTEGER: `int32` \| `uint32` \| `int64` \| `uint64` \| `bigint` \| `der`. На типе перекрывает модуль. Если не задано, C# backend выводит: при `namedValues` → `int32` (или `int64` при метке вне `int`); иначе из полного `constraint.value`; иначе `der`. |
| `options.openType.mismatch` | модуль / документ / тип `any` | При известном ключе bindings, если **тег** TLV не совпал с типом: `soft` (default) → `Unknown`/`Asn1Any`; `strict` → `Asn1Exception`. |
| `options.lazy` | поле / тип / модуль | `true` — отложенный разбор SEQUENCE/SET и SEQUENCE OF/SET OF (C#: `Asn1Lazy<T>` / `Asn1Lazy<List<T>>`). Разрешение: component → TypeExpr → typedef → module; default `false`. |

Sidecar options-patch (CLI `--patch`, API `IrOptionsPatch`): JSON `{ "modules": { "<Module>": { … } }, "fields": { "<Module>.<Type>.<field>": { … } } }` — deep-merge в `options`. Неизвестный module/type/field → ошибка. Пример: [cms-2004-bench.patch.json](../compiler/fixtures/ir/cms-2004-bench.patch.json).

## Примеры

- [compiler/fixtures/ir/example.json](../compiler/fixtures/ir/example.json) — минимальный SEQUENCE
- [compiler/fixtures/ir/pkix1-explicit88.json](../compiler/fixtures/ir/pkix1-explicit88.json) — RFC 5280 Appendix A.1
- [compiler/fixtures/ir/cms-2004-bench.json](../compiler/fixtures/ir/cms-2004-bench.json) — производный bench IR (golden + patch)
