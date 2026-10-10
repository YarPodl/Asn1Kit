# C++ runtime API (фаза 1 — примитивы)

Публичный namespace `asn1kit`, методы в `snake_case`. Wire-правила BER/DER и soft-profile совпадают с эталонным runtime репозитория; layer-1 векторы — общие JSON в [`runtime-csharp/fixtures/ber-der`](../../runtime-csharp/fixtures/ber-der/).

## `bytes` vs `span`

| Тип | Роль |
| --- | --- |
| `std::span<const uint8_t>` | Эфемерный borrow: входы encode, `try_encode` destination. Lifetime — на вызывающем. |
| `asn1kit::bytes` | Окно (`span`) + type-erased keep-alive источника (`shared_ptr<void>`). |

Фабрики `bytes`:

- `borrow(span)` — без keep-alive (горячий ephemeral путь).
- `from_shared(shared_ptr<T>, span)` / `from_vector` — продлевают жизнь источника.
- `copy_from(span)` — owned копия.
- `slice(offset[, length])` — наследует тот же owner.

`reader` принимает `bytes` (и удобные обёртки). Возвраты `read_octet_string`, INTEGER/BIT STRING contents — `bytes`-срезы входного owner’а (или новый owned буфер после BER concat).

Мутация исходных октетов, пока живы views — UB. Detach: `to_vector()` / `copy_from`.

## Инвентарь фазы 1

| Область | API |
| --- | --- |
| Tag / encoding | `tag`, `tag_class`, `encoding`, `string_form`, `time_form`, `exception` |
| Soft-read | `reader_options` — `default_profile`, `strict`, `allow_non_minimal_length`, `allow_overlong_oid_base128` |
| Writer | `write_boolean`, `write_integer` (i32/u32/i64/u64/`integer`), `write_integer_decimal`, `write_enumerated_decimal`, `write_octet_string`, `write_null`, `write_object_identifier`, `write_bit_string`, `write_string`, `write_time`, `write_raw`, `encode` / `try_encode` / `reset` |
| Reader | соответствующие `read_*`, `try_peek_tag`, `eof`, `throw_if_not_empty` |
| Value types | `integer`, `bit_string`, `oid`, `null_value`, `utc_date_time` |
| Time | `utc_date_time` (календарные поля UTC + `time_fraction` до 7 десятичных цифр) |

Вне фазы 1: публичные `enter_sequence` / SET scopes, ANY / Lazy / Value / Contained / Codec, modern IOC, oracle, `external/` фикстуры.

## Soft-read

Default принимает: non-minimal INTEGER, nonzero BIT STRING trailing bits.  
Default отклоняет: non-minimal length, overlong OID base-128.  
Constructed OCTET/BIT/string soft-accept и под DER (как у эталонного runtime).

Encode всегда канонический DER.

## `exception`

Ошибки encode/decode — `asn1kit::exception` (`std::runtime_error`).

На decode-path исключение может нести **absolute offset** во входном буфере root-`reader` (вложенные constructed-окна учитывают base вложенности):

- `has_offset()` / `offset()` — позиция TLV/EOF во входном буфере (для ориентации во вложенных структурах);
- `what()` = reason + суффикс ` (offset=N)` при наличии offset.

Пример: `Length exceeds buffer. (offset=42)`.

Ошибки разбора contents примитива (BOOLEAN, OID arcs, charset и т.п.) остаются message-only: достаточно reason. Имена файлов и номера строк в сообщение не входят. Encode без курсора — тоже message-only (`has_offset() == false`).

## Внутренности

`encode_buffer`, `decode_cursor`, `constructed_decoder`, `text_codec` — не публичный ABI; лежат в `src/`.
