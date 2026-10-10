// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bit_string.hpp"
#include "asn1kit/bytes.hpp"
#include "asn1kit/detail/decode_cursor.hpp"
#include "asn1kit/encoding.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/oid.hpp"
#include "asn1kit/reader_options.hpp"
#include "asn1kit/tag.hpp"
#include "asn1kit/utc_date_time.hpp"

#include <cstddef>
#include <cstdint>
#include <span>
#include <string>
#include <vector>

namespace asn1kit {

/// BER/DER decode facade for primitive types.
class reader {
public:
    explicit reader(
        bytes data,
        encoding enc = encoding::ber,
        reader_options options = reader_options::default_profile());

    explicit reader(
        std::span<const std::uint8_t> data,
        encoding enc = encoding::ber,
        reader_options options = reader_options::default_profile());

    explicit reader(
        const std::vector<std::uint8_t>& data,
        encoding enc = encoding::ber,
        reader_options options = reader_options::default_profile());

    reader(const reader&) = delete;
    reader& operator=(const reader&) = delete;
    reader(reader&&) noexcept = default;
    reader& operator=(reader&&) noexcept = default;
    ~reader() = default;

    [[nodiscard]] encoding encoding_rules() const noexcept;
    [[nodiscard]] const reader_options& options() const noexcept;
    [[nodiscard]] bool eof() const noexcept;
    [[nodiscard]] std::size_t remaining() const noexcept;

    void throw_if_not_empty() const;

    [[nodiscard]] bool try_peek_tag(tag& out) const;

    [[nodiscard]] bool read_boolean(const tag& expected = tag::boolean);
    [[nodiscard]] integer read_integer_value(const tag& expected = tag::integer);
    [[nodiscard]] std::int32_t read_int32(const tag& expected = tag::integer);
    [[nodiscard]] std::uint32_t read_uint32(const tag& expected = tag::integer);
    [[nodiscard]] std::int64_t read_int64(const tag& expected = tag::integer);
    [[nodiscard]] std::uint64_t read_uint64(const tag& expected = tag::integer);
    /// Decimal ASCII of the INTEGER value.
    [[nodiscard]] std::string read_integer_decimal(const tag& expected = tag::integer);
    [[nodiscard]] std::string read_enumerated_decimal(const tag& expected = tag::enumerated);

    [[nodiscard]] bytes read_octet_string(const tag& expected = tag::octet_string);
    [[nodiscard]] bool try_read_octet_string(
        const tag& expected,
        std::span<std::uint8_t> destination,
        std::size_t& bytes_written);

    void read_null(const tag& expected = tag::null);

    [[nodiscard]] oid read_oid(const tag& expected = tag::object_identifier);
    [[nodiscard]] std::string read_object_identifier(const tag& expected = tag::object_identifier);

    [[nodiscard]] bit_string read_bit_string(const tag& expected = tag::bit_string);

    [[nodiscard]] std::string read_string(const tag& expected, string_form form);
    [[nodiscard]] utc_date_time read_time(const tag& expected, time_form form);

private:
    bytes read_primitive_contents(const tag& expected);
    void ensure_minimal_integer_contents(const bytes& contents) const;
    static void ensure_expected_tag(std::size_t tlv_start, const tag& actual, const tag& expected);

    detail::decode_cursor cursor_;
};

[[nodiscard]] tag default_string_tag(string_form form);
[[nodiscard]] tag default_time_tag(time_form form);

} // namespace asn1kit
