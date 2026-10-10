// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/writer.hpp"

#include "text_codec.hpp"

#include <exception>
#include <stdexcept>
#include <utility>
#include <vector>

namespace asn1kit {

writer_scope::writer_scope(
    writer* w,
    detail::encode_frame frame,
    std::uint64_t scope_token,
    std::uint64_t parent_scope_token,
    bool sort_der_set_of) noexcept
    : writer_(w)
    , frame_(frame)
    , scope_token_(scope_token)
    , parent_scope_token_(parent_scope_token)
    , sort_der_set_of_(sort_der_set_of)
    , active_(true) {}

writer_scope::writer_scope(writer_scope&& other) noexcept
    : writer_(std::exchange(other.writer_, nullptr))
    , frame_(other.frame_)
    , scope_token_(other.scope_token_)
    , parent_scope_token_(other.parent_scope_token_)
    , sort_der_set_of_(other.sort_der_set_of_)
    , active_(std::exchange(other.active_, false)) {}

writer_scope::~writer_scope() noexcept {
    if (!active_) {
        return;
    }
    try {
        end();
    } catch (...) {
        std::terminate();
    }
}

void writer_scope::end() {
    if (!active_) {
        throw std::logic_error("ASN.1 writer scopes must be disposed once in LIFO order.");
    }
    writer_->end_scope(frame_, scope_token_, parent_scope_token_, sort_der_set_of_);
    active_ = false;
    writer_ = nullptr;
}

writer::writer(encoding enc) : encoding_(enc) {}

encoding writer::encoding_rules() const noexcept { return encoding_; }

std::size_t writer::encoded_length() const noexcept { return buffer_.length(); }

void writer::ensure_capacity(std::size_t capacity) { buffer_.ensure_capacity(capacity); }

void writer::reset() {
    ensure_no_active_scope("reset");
    buffer_.reset();
}

std::vector<std::uint8_t> writer::encode() const {
    ensure_no_active_scope("encode");
    return buffer_.to_vector();
}

bool writer::try_encode(std::span<std::uint8_t> destination, std::size_t& bytes_written) const {
    ensure_no_active_scope("try_encode");
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

writer_scope writer::enter_sequence(const tag& t) {
    return begin_scope(t, false);
}

writer_scope writer::enter_set(const tag& t) {
    return begin_scope(t, false);
}

writer_scope writer::enter_sequence_of(const tag& t) {
    return begin_scope(t, false);
}

writer_scope writer::enter_set_of(const tag& t) {
    return begin_scope(t, encoding_ == encoding::der);
}

writer_scope writer::enter_explicit(const tag& t) {
    return begin_scope(t, false);
}

writer_scope writer::begin_scope(const tag& t, bool sort_der_set_of) {
    const std::uint64_t parent = active_scope_token_;
    const std::uint64_t token = ++next_scope_token_;
    auto frame = buffer_.begin_constructed(t);
    active_scope_token_ = token;
    return writer_scope(this, frame, token, parent, sort_der_set_of);
}

void writer::end_scope(
    const detail::encode_frame& frame,
    std::uint64_t scope_token,
    std::uint64_t parent_scope_token,
    bool sort_der_set_of) {
    if (active_scope_token_ != scope_token) {
        throw std::logic_error("ASN.1 writer scopes must be disposed once in LIFO order.");
    }
    buffer_.end_constructed(frame, sort_der_set_of);
    active_scope_token_ = parent_scope_token;
}

void writer::ensure_no_active_scope(const char* operation) const {
    if (active_scope_token_ != 0) {
        throw std::logic_error(
            std::string("Cannot ") + operation + " while an ASN.1 writer scope is active.");
    }
}

} // namespace asn1kit
