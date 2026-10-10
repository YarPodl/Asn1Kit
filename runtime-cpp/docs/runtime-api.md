# C++ runtime API (фаза 2 — примитивы + constructed scopes)

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

## Инвентарь

| Область | API |
| --- | --- |
| Tag / encoding | `tag`, `tag_class`, `encoding`, `string_form`, `time_form`, `exception` |
| Soft-read | `reader_options` — `default_profile`, `strict`, `allow_non_minimal_length`, `allow_overlong_oid_base128` |
| Writer primitives | `write_boolean`, `write_integer` (i32/u32/i64/u64/`integer`), `write_integer_decimal`, `write_enumerated_decimal`, `write_octet_string`, `write_null`, `write_object_identifier`, `write_bit_string`, `write_string`, `write_time`, `write_raw`, `encode` / `try_encode` / `reset` |
| Writer scopes | `enter_sequence` / `enter_set` / `enter_sequence_of` / `enter_set_of` / `enter_explicit` → `writer_scope`; `write_sequence_of` / `write_set_of` (template + range + invocable) |
| Reader primitives | соответствующие `read_*`, `try_peek_tag`, `next_is`, `eof`, `throw_if_not_empty` |
| Reader scopes (низкий) | `enter_sequence` / `enter_set` / `enter_explicit` / `enter_encoded(bytes)` → `reader_scope` |
| Reader scopes (рекомендуемый) | `with_sequence` / `with_set` / `with_explicit` / `with_encoded` — enter → callback → `throw_if_not_empty` на success → pop |
| Reader OF (callback) | `for_each_sequence_of` / `for_each_set_of`; `read_sequence_of` / `read_set_of` + invocable |
| Reader type-driven | `read<T>` / `read_from_explicit<T>` / `read_sequence_of<T>` / `read_set_of<T>` / `read_sequence_of_from_explicit<T>` — без лямбд (T = generated или примитив) |
| Writer type-driven | `write` / `write_to_explicit` / `write_sequence_of` / `write_set_of` / `write_sequence_of_to_explicit` — без лямбд |
| Value types | `integer`, `bit_string`, `oid`, `null_value`, `utc_date_time` |
| Time | `utc_date_time` (календарные поля UTC + `time_fraction` до 7 десятичных цифр) |

Вне фазы 2: ANY / Lazy / Value / Contained / Codec, modern IOC, oracle, `external/` фикстуры, codegen C++.

## Constructed scopes

- Scopes **move-only**. Публично — `end()` и destructor.
- Writer: `end()` бросает `std::logic_error` при LIFO / double-end; destructor `noexcept` финализирует frame и при misuse вызывает `std::terminate`. При открытом scope запрещены `encode` / `try_encode` / `reset` (`std::logic_error`).
- Reader: push/pop окна на одном объекте; double-end идемпотентен; LIFO-нарушение в `end()` → `std::logic_error`. Destructor scope **не** проверяет empty.
- `with_*`: на успешном return callback’а — `throw_if_not_empty`; при исключении из callback — только pop (исходная ошибка не заменяется). Нужен для составных тел/`DEFAULT`/`OPTIONAL` peek-логики.
- `next_is(tag)` — `try_peek_tag` + `matches_ignore_constructed` (для OPTIONAL/DEFAULT).
- Type-driven (`concepts.hpp` + `detail/typed_value.hpp`): generated-типы (`default_tag` / `decode` / `encode`) и примитивы (`bool`, fixed integers, `integer`, `bytes`, `oid`, `bit_string`). Codegen: `if (r.next_is(t)) x = r.read_from_explicit<std::int32_t>(t);`, `r.read_sequence_of<Ext>()`, `w.write_to_explicit(t, value)`.
- `enter_set_of` / `write_set_of`: DER сортирует TLV contents лексикографически; BER сохраняет порядок. Обычный `enter_set` порядок полей не меняет.
- `enter_*` помечены `[[nodiscard]]`.

Wire-ошибки (тег, length, trailing data) — `asn1kit::exception`. API misuse scopes — `std::logic_error`.

## Soft-read

Default принимает: non-minimal INTEGER, nonzero BIT STRING trailing bits.  
Default отклоняет: non-minimal length, overlong OID base-128.  
Constructed OCTET/BIT/string soft-accept и под DER (как у эталонного runtime).

Encode всегда канонический DER (definite length).

## `exception`

Ошибки encode/decode — `asn1kit::exception` (`std::runtime_error`).

На decode-path исключение может нести **absolute offset** во входном буфере root-`reader` (вложенные constructed-окна учитывают base вложенности):

- `has_offset()` / `offset()` — позиция TLV/EOF во входном буфере;
- `what()` = reason + суффикс ` (offset=N)` при наличии offset.

Ошибки разбора contents примитива остаются message-only. Encode без курсора — message-only (`has_offset() == false`).

## Внутренности

`encode_buffer`, `decode_cursor`, `constructed_decoder`, `text_codec` — не публичный ABI; заголовки в `include/asn1kit/detail/` и `src/`.
