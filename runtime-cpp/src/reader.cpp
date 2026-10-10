// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/reader.hpp"

#include "constructed_decoder.hpp"
#include "decode_cursor.hpp"
#include "text_codec.hpp"

#include <utility>

namespace asn1kit {

// Forward declare boolean decoder implemented in boolean.cpp
bool decode_boolean_contents(std::span<const std::uint8_t> contents, encoding enc);

class reader::impl {
public:
    detail::decode_cursor cursor;

    impl(bytes data, encoding enc, reader_options options)
        : cursor(std::move(data), enc, options) {}
};

reader::reader(bytes data, encoding enc, reader_options options)
    : impl_(std::make_unique<impl>(std::move(data), enc, std::move(options))) {}

reader::reader(std::span<const std::uint8_t> data, encoding enc, reader_options options)
    : reader(bytes::borrow(data), enc, std::move(options)) {}

reader::reader(const std::vector<std::uint8_t>& data, encoding enc, reader_options options)
    : reader(bytes::from_vector(data), enc, std::move(options)) {}

reader::~reader() = default;
reader::reader(reader&&) noexcept = default;
reader& reader::operator=(reader&&) noexcept = default;

encoding reader::encoding_rules() const noexcept { return impl_->cursor.encoding_rules(); }

const reader_options& reader::options() const noexcept { return impl_->cursor.options(); }

bool reader::eof() const noexcept { return impl_->cursor.eof(); }

int reader::remaining() const noexcept { return impl_->cursor.remaining(); }

void reader::throw_if_not_empty() const {
    if (!eof()) {
        throw exception("ASN.1 reader contains trailing data.");
    }
}

bool reader::try_peek_tag(tag& out) const {
    return impl_->cursor.try_peek_tag(out);
}

bool reader::read_boolean(const tag& expected) {
    return decode_boolean_contents(read_primitive_contents(expected).span(), encoding_rules());
}

integer reader::read_integer_value(const tag& expected) {
    auto contents = read_primitive_contents(expected);
    ensure_minimal_integer_contents(contents.span());
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
    return detail::constructed_decoder::read_octet_like(impl_->cursor, expected);
}

bool reader::try_read_octet_string(
    const tag& expected,
    std::span<std::uint8_t> destination,
    int& bytes_written) {
    auto candidate = impl_->cursor;
    if (!detail::constructed_decoder::try_read_octet_like(
            candidate, expected, destination, bytes_written)) {
        return false;
    }
    impl_->cursor = candidate;
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
    int index = 0;
    while (index < static_cast<int>(span.size())) {
        (void)oid::read_arc(span, index, options().reject_overlong_oid_base128);
    }
    return oid::from_contents(std::move(contents));
}

std::string reader::read_object_identifier(const tag& expected) {
    return read_oid(expected).to_string();
}

bit_string reader::read_bit_string(const tag& expected) {
    return detail::constructed_decoder::read_bit_string(
        impl_->cursor, expected, options().reject_bit_string_trailing_bits);
}

std::string reader::read_string(const tag& expected, string_form form) {
    auto data = detail::constructed_decoder::read_octet_like(impl_->cursor, expected);
    return detail::text_codec::decode_string(data.span(), form);
}

utc_date_time reader::read_time(const tag& expected, time_form form) {
    auto data = detail::constructed_decoder::read_octet_like(impl_->cursor, expected);
    return detail::text_codec::parse_time(data.span(), form, encoding_rules());
}

bytes reader::read_primitive_contents(const tag& expected) {
    auto tlv = impl_->cursor.read_tlv();
    ensure_expected_tag(tlv.tag_value, expected);
    if (tlv.tag_value.constructed()) {
        throw exception("Tag " + expected.to_string() + " must be primitive.");
    }
    return tlv.contents;
}

void reader::ensure_minimal_integer_contents(std::span<const std::uint8_t> contents) const {
    if (options().reject_non_minimal_integer && !integer::is_minimal_contents(contents)) {
        throw exception("INTEGER contents are not minimally encoded.");
    }
}

void reader::ensure_expected_tag(const tag& actual, const tag& expected) {
    if (!actual.matches_ignore_constructed(expected)) {
        throw exception("Expected tag " + expected.to_string() + ", found " + actual.to_string() + ".");
    }
}

tag default_string_tag(string_form form) {
    return detail::text_codec::default_string_tag(form);
}

tag default_time_tag(time_form form) {
    return detail::text_codec::default_time_tag(form);
}

} // namespace asn1kit
