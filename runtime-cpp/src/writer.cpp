// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/writer.hpp"

#include "text_codec.hpp"

#include <array>
#include <vector>

namespace asn1kit {

writer::writer(encoding enc) : encoding_(enc) {}

encoding writer::encoding_rules() const noexcept { return encoding_; }

std::size_t writer::encoded_length() const noexcept { return buffer_.length(); }

void writer::ensure_capacity(std::size_t capacity) { buffer_.ensure_capacity(capacity); }

void writer::reset() { buffer_.reset(); }

std::vector<std::uint8_t> writer::encode() const { return buffer_.to_vector(); }

bool writer::try_encode(std::span<std::uint8_t> destination, std::size_t& bytes_written) const {
    return buffer_.try_copy_to(destination, bytes_written);
}

std::span<const std::uint8_t> writer::written_span() const {
    return buffer_.written_span();
}

void writer::write_boolean(const tag& t, bool value) {
    std::uint8_t contents[1] = {value ? static_cast<std::uint8_t>(0xFF) : static_cast<std::uint8_t>(0x00)};
    buffer_.write_primitive(t, contents);
}

void writer::write_integer(const tag& t, std::int32_t value) {
    std::uint8_t contents[4];
    const std::size_t written = integer::encode_contents(value, contents);
    buffer_.write_primitive(t, std::span<const std::uint8_t>(contents, written));
}

void writer::write_integer(const tag& t, std::uint32_t value) {
    std::uint8_t contents[5];
    const std::size_t written = integer::encode_contents(value, contents);
    buffer_.write_primitive(t, std::span<const std::uint8_t>(contents, written));
}

void writer::write_integer(const tag& t, std::int64_t value) {
    std::uint8_t contents[8];
    const std::size_t written = integer::encode_contents(value, contents);
    buffer_.write_primitive(t, std::span<const std::uint8_t>(contents, written));
}

void writer::write_integer(const tag& t, std::uint64_t value) {
    std::uint8_t contents[9];
    const std::size_t written = integer::encode_contents(value, contents);
    buffer_.write_primitive(t, std::span<const std::uint8_t>(contents, written));
}

void writer::write_integer(const tag& t, const integer& value) {
    buffer_.write_primitive(t, value.span());
}

void writer::write_integer_decimal(const tag& t, std::string_view decimal) {
    write_integer(t, integer::from_decimal(decimal));
}

void writer::write_enumerated_decimal(const tag& t, std::string_view decimal) {
    write_integer_decimal(t, decimal);
}

void writer::write_octet_string(const tag& t, std::span<const std::uint8_t> value) {
    buffer_.write_primitive(t, value);
}

void writer::write_null(const tag& t) {
    buffer_.write_primitive(t, {});
}

void writer::write_object_identifier(const tag& t, std::string_view oid_text) {
    const std::size_t max_bytes = oid::get_encode_contents_max_length(oid_text);
    if (max_bytes <= 128) {
        std::uint8_t stack[128];
        const std::size_t written = oid::encode_contents(oid_text, std::span<std::uint8_t>(stack, max_bytes));
        buffer_.write_primitive(t, std::span<const std::uint8_t>(stack, written));
        return;
    }
    std::vector<std::uint8_t> heap(max_bytes);
    const std::size_t written = oid::encode_contents(oid_text, heap);
    buffer_.write_primitive(t, std::span<const std::uint8_t>(heap.data(), written));
}

void writer::write_object_identifier(const tag& t, const oid& value) {
    buffer_.write_primitive(t, value.span());
}

void writer::write_bit_string(const tag& t, const bit_string& value) {
    if (encoding_ == encoding::der) {
        detail::text_codec::ensure_trailing_bits_zero(value.span(), value.unused_bits());
    }
    buffer_.write_primitive(
        t,
        static_cast<std::uint8_t>(value.unused_bits()),
        value.span());
}

void writer::write_string(const tag& t, std::string_view value, string_form form) {
    const std::size_t byte_count = detail::text_codec::get_encoded_byte_count(value, form);
    constexpr std::size_t stack_threshold = 64;
    if (byte_count <= stack_threshold) {
        std::uint8_t stack[64];
        detail::text_codec::encode_string(value, form, std::span<std::uint8_t>(stack, byte_count));
        buffer_.write_primitive(t, std::span<const std::uint8_t>(stack, byte_count));
        return;
    }
    std::vector<std::uint8_t> heap(byte_count);
    detail::text_codec::encode_string(value, form, heap);
    buffer_.write_primitive(t, heap);
}

void writer::write_time(const tag& t, const utc_date_time& value, time_form form, int fraction_digits) {
    std::uint8_t contents[detail::text_codec::max_encoded_time_bytes];
    const std::size_t written = detail::text_codec::encode_time(value, form, fraction_digits, contents);
    buffer_.write_primitive(t, std::span<const std::uint8_t>(contents, written));
}

void writer::write_raw(std::span<const std::uint8_t> tlv) {
    buffer_.write_raw(tlv);
}

} // namespace asn1kit
