// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/encoding.hpp"
#include "asn1kit/reader_options.hpp"
#include "asn1kit/utc_date_time.hpp"

#include <nlohmann/json.hpp>

#include <cstdint>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace asn1kit::test {

struct ber_der_case {
    std::string name;
    std::string rules{"der"};
    std::string op;
    std::string bytes_hex;
    nlohmann::json value;
    bool encode{true};
    bool reject{false};
    std::optional<std::string> form;
    std::optional<int> unused_bits;
    std::optional<int> fraction_digits;
    std::optional<std::string> reader_profile;

    [[nodiscard]] encoding encoding_rules() const;
    [[nodiscard]] reader_options get_reader_options() const;
    [[nodiscard]] std::vector<std::uint8_t> get_bytes() const;
    [[nodiscard]] bool should_encode() const;
    [[nodiscard]] bool has_value() const;
};

[[nodiscard]] std::vector<std::uint8_t> parse_hex(std::string_view hex);
[[nodiscard]] std::string format_hex(std::span<const std::uint8_t> data);

[[nodiscard]] std::vector<ber_der_case> load_cases(const std::string& file_name);

[[nodiscard]] std::string get_string(const ber_der_case& c);
[[nodiscard]] bool get_boolean(const ber_der_case& c);
[[nodiscard]] std::string get_integer_decimal(const ber_der_case& c);
[[nodiscard]] std::vector<std::uint8_t> get_octet_value(const ber_der_case& c);
[[nodiscard]] utc_date_time get_time(const ber_der_case& c);

} // namespace asn1kit::test
