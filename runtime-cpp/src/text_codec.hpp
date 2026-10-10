// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/encoding.hpp"
#include "asn1kit/tag.hpp"
#include "asn1kit/utc_date_time.hpp"

#include <cstdint>
#include <span>
#include <string>
#include <string_view>

namespace asn1kit::detail {

struct text_codec {
    static constexpr int max_encoded_time_bytes = 24;

    static tag default_string_tag(string_form form);
    static tag default_time_tag(time_form form);

    static int get_encoded_byte_count(std::string_view value, string_form form);
    static void encode_string(std::string_view value, string_form form, std::span<std::uint8_t> destination);
    static std::string decode_string(std::span<const std::uint8_t> contents, string_form form);

    static int encode_time(
        const utc_date_time& value,
        time_form form,
        int fraction_digits,
        std::span<std::uint8_t> destination);

    static utc_date_time parse_time(
        std::span<const std::uint8_t> contents,
        time_form form,
        encoding enc);

    static void ensure_trailing_bits_zero(std::span<const std::uint8_t> data, int unused_bits);
};

} // namespace asn1kit::detail
