// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bit_string.hpp"
#include "asn1kit/bytes.hpp"
#include "asn1kit/detail/decode_cursor.hpp"
#include "asn1kit/tag.hpp"

#include <cstddef>
#include <cstdint>
#include <span>

namespace asn1kit::detail {

/// BER constructed string and BIT STRING decoding (soft-accepted under DER).
struct constructed_decoder {
    static bytes read_octet_like(decode_cursor& cursor, const tag& expected);

    static bool try_read_octet_like(
        decode_cursor& cursor,
        const tag& expected,
        std::span<std::uint8_t> destination,
        std::size_t& bytes_written);

    static bit_string read_bit_string(
        decode_cursor& cursor,
        const tag& expected,
        bool reject_trailing_bits);
};

} // namespace asn1kit::detail
