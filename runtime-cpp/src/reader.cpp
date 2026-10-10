// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/reader.hpp"

#include "constructed_decoder.hpp"
#include "text_codec.hpp"

#include <exception>
#include <stdexcept>
#include <utility>

namespace asn1kit {

// Forward declare boolean decoder implemented in boolean.cpp
bool decode_boolean_contents(std::span<const std::uint8_t> contents, encoding enc);

reader_scope::reader_scope(
    reader* r,
    detail::decode_cursor saved_cursor,
    std::uint64_t expected_scope_token,
    std::uint64_t parent_scope_token) noexcept
    : reader_(r)
    , saved_cursor_(std::move(saved_cursor))
    , expected_scope_token_(expected_scope_token)
    , parent_scope_token_(parent_scope_token)
    , active_(true) {}

reader_scope::reader_scope(reader_scope&& other) noexcept
    : reader_(std::exchange(other.reader_, nullptr))
    , saved_cursor_(std::move(other.saved_cursor_))
    , expected_scope_token_(other.expected_scope_token_)
    , parent_scope_token_(other.parent_scope_token_)
    , active_(std::exchange(other.active_, false)) {}

reader_scope::~reader_scope() noexcept {
    if (!active_) {
        return;
    }
    try {
        end();
    } catch (...) {
        std::terminate();
    }
}

void reader_scope::end() {
    if (!active_) {
        return;
    }
    reader_->pop_contents_window(
        std::move(saved_cursor_), expected_scope_token_, parent_scope_token_);
    active_ = false;
    reader_ = nullptr;
}

reader::reader(bytes data, encoding enc, reader_options options)
    : cursor_(std::move(data), enc, std::move(options)) {}

reader::reader(std::span<const std::uint8_t> data, encoding enc, reader_options options)
    : reader(bytes::borrow(data), enc, std::move(options)) {}

reader::reader(const std::vector<std::uint8_t>& data, encoding enc, reader_options options)
    : reader(bytes::from_vector(data), enc, std::move(options)) {}

encoding reader::encoding_rules() const noexcept { return cursor_.encoding_rules(); }

const reader_options& reader::options() const noexcept { return cursor_.options(); }

bool reader::eof() const noexcept { return cursor_.eof(); }

std::size_t reader::remaining() const noexcept { return cursor_.remaining(); }

void reader::throw_if_not_empty() const {
    if (!eof()) {
        throw exception("ASN.1 reader contains trailing data.", cursor_.absolute_offset());
    }
}

bool reader::try_peek_tag(tag& out) const {
    return cursor_.try_peek_tag(out);
}

bool reader::next_is(const tag& expected) const {
    tag peeked;
    return try_peek_tag(peeked) && peeked.matches_ignore_constructed(expected);
}

bool reader::read_boolean(const tag& expected) {
    return decode_boolean_contents(read_primitive_contents(expected).span(), encoding_rules());
}

integer reader::read_integer_value(const tag& expected) {
    auto contents = read_primitive_contents(expected);
    ensure_minimal_integer_contents(contents);
    return integer::from_contents(std::move(contents));
}

std::int32_t reader::read_int32(const tag& expected) {
    return read_integer_value(expected).get_int32();
}

std::uint32_t reader::read_uint32(const tag& expected) {
    return read_integer_value(expected).get_uint32();
}

std::int64_t reader::read_int64(const tag& expected) {
    return read_integer_value(expected).get_int64();
}

std::uint64_t reader::read_uint64(const tag& expected) {
    return read_integer_value(expected).get_uint64();
}

std::string reader::read_integer_decimal(const tag& expected) {
    return read_integer_value(expected).to_decimal();
}

std::string reader::read_enumerated_decimal(const tag& expected) {
    return read_integer_value(expected).to_decimal();
}

bytes reader::read_octet_string(const tag& expected) {
    return detail::constructed_decoder::read_octet_like(cursor_, expected);
}

bool reader::try_read_octet_string(
    const tag& expected,
    std::span<std::uint8_t> destination,
    std::size_t& bytes_written) {
    auto candidate = cursor_;
    if (!detail::constructed_decoder::try_read_octet_like(
            candidate, expected, destination, bytes_written)) {
        return false;
    }
    cursor_ = candidate;
    return true;
}

void reader::read_null(const tag& expected) {
    auto contents = read_primitive_contents(expected);
    if (!contents.empty()) {
        throw exception("NULL must have empty contents.");
    }
}

oid reader::read_oid(const tag& expected) {
    auto contents = read_primitive_contents(expected);
    if (contents.empty()) {
        throw exception("OBJECT IDENTIFIER is empty.");
    }
    const auto span = contents.span();
    std::size_t index = 0;
    while (index < span.size()) {
        (void)oid::read_arc(span, index, options().reject_overlong_oid_base128);
    }
    return oid::from_contents(std::move(contents));
}

std::string reader::read_object_identifier(const tag& expected) {
    return read_oid(expected).to_string();
}

bit_string reader::read_bit_string(const tag& expected) {
    return detail::constructed_decoder::read_bit_string(
        cursor_, expected, options().reject_bit_string_trailing_bits);
}

std::string reader::read_string(const tag& expected, string_form form) {
    auto data = detail::constructed_decoder::read_octet_like(cursor_, expected);
    return detail::text_codec::decode_string(data.span(), form);
}

utc_date_time reader::read_time(const tag& expected, time_form form) {
    auto data = detail::constructed_decoder::read_octet_like(cursor_, expected);
    return detail::text_codec::parse_time(data.span(), form, encoding_rules());
}

reader_scope reader::enter_sequence(const tag& expected) {
    return push_contents_window(read_constructed_contents(expected));
}

reader_scope reader::enter_set(const tag& expected) {
    return push_contents_window(read_constructed_contents(expected));
}

reader_scope reader::enter_explicit(const tag& expected) {
    return push_contents_window(read_constructed_contents(expected));
}

reader_scope reader::enter_encoded(bytes encoded) {
    return push_contents_window(std::move(encoded));
}

reader_scope reader::push_contents_window(bytes contents) {
    auto nested = cursor_.create_nested(std::move(contents));
    detail::decode_cursor saved = std::move(cursor_);
    cursor_ = std::move(nested);
    const std::uint64_t parent = active_scope_token_;
    const std::uint64_t token = ++next_scope_token_;
    active_scope_token_ = token;
    return reader_scope(this, std::move(saved), token, parent);
}

void reader::pop_contents_window(
    detail::decode_cursor saved_cursor,
    std::uint64_t expected_scope_token,
    std::uint64_t parent_scope_token) {
    if (active_scope_token_ != expected_scope_token) {
        throw std::logic_error("ASN.1 reader scopes must be disposed once in LIFO order.");
    }
    cursor_ = std::move(saved_cursor);
    active_scope_token_ = parent_scope_token;
}

bytes reader::read_constructed_contents(const tag& expected) {
    const std::size_t tlv_start = cursor_.absolute_offset();
    auto tlv = cursor_.read_tlv();
    ensure_expected_tag(tlv_start, tlv.tag_value, expected);
    if (!tlv.tag_value.constructed()) {
        throw exception("Tag " + expected.to_string() + " must be constructed.", tlv_start);
    }
    return std::move(tlv.contents);
}

bytes reader::read_primitive_contents(const tag& expected) {
    const std::size_t tlv_start = cursor_.absolute_offset();
    auto tlv = cursor_.read_tlv();
    ensure_expected_tag(tlv_start, tlv.tag_value, expected);
    if (tlv.tag_value.constructed()) {
        throw exception("Tag " + expected.to_string() + " must be primitive.", tlv_start);
    }
    return tlv.contents;
}

void reader::ensure_minimal_integer_contents(const bytes& contents) const {
    if (options().reject_non_minimal_integer && !integer::is_minimal_contents(contents.span())) {
        throw exception("INTEGER contents are not minimally encoded.");
    }
}

void reader::ensure_expected_tag(std::size_t tlv_start, const tag& actual, const tag& expected) {
    if (!actual.matches_ignore_constructed(expected)) {
        throw exception(
            "Expected tag " + expected.to_string() + ", found " + actual.to_string() + ".",
            tlv_start);
    }
}

tag default_string_tag(string_form form) {
    return detail::text_codec::default_string_tag(form);
}

tag default_time_tag(time_form form) {
    return detail::text_codec::default_time_tag(form);
}

} // namespace asn1kit
