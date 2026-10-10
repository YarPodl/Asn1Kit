// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bit_string.hpp"
#include "asn1kit/detail/encode_buffer.hpp"
#include "asn1kit/encoding.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/oid.hpp"
#include "asn1kit/tag.hpp"
#include "asn1kit/utc_date_time.hpp"

#include <cstddef>
#include <cstdint>
#include <span>
#include <string_view>
#include <vector>

namespace asn1kit {

/// Public BER/DER encode facade for primitive types.
class writer {
public:
    explicit writer(encoding enc = encoding::der);

    writer(const writer&) = delete;
    writer& operator=(const writer&) = delete;
    writer(writer&&) noexcept = default;
    writer& operator=(writer&&) noexcept = default;
    ~writer() = default;

    [[nodiscard]] encoding encoding_rules() const noexcept;
    [[nodiscard]] std::size_t encoded_length() const noexcept;

    void ensure_capacity(std::size_t capacity);
    void reset();

    [[nodiscard]] std::vector<std::uint8_t> encode() const;
    [[nodiscard]] bool try_encode(std::span<std::uint8_t> destination, std::size_t& bytes_written) const;

    void write_boolean(const tag& t, bool value);
    void write_integer(const tag& t, std::int32_t value);
    void write_integer(const tag& t, std::uint32_t value);
    void write_integer(const tag& t, std::int64_t value);
    void write_integer(const tag& t, std::uint64_t value);
    void write_integer(const tag& t, const integer& value);
    /// Encode INTEGER from decimal ASCII (no external bigint library).
    void write_integer_decimal(const tag& t, std::string_view decimal);
    void write_enumerated_decimal(const tag& t, std::string_view decimal);

    void write_octet_string(const tag& t, std::span<const std::uint8_t> value);
    void write_null(const tag& t);
    void write_object_identifier(const tag& t, std::string_view oid_text);
    void write_object_identifier(const tag& t, const oid& value);
    void write_bit_string(const tag& t, const bit_string& value);
    void write_string(const tag& t, std::string_view value, string_form form);
    void write_time(const tag& t, const utc_date_time& value, time_form form, int fraction_digits = 3);

    void write_raw(std::span<const std::uint8_t> tlv);

    [[nodiscard]] std::span<const std::uint8_t> written_span() const;

private:
    encoding encoding_;
    detail::encode_buffer buffer_;
};

} // namespace asn1kit
