// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "ber_der_fixtures.hpp"

#include "asn1kit/exception.hpp"

#include <cctype>
#include <fstream>
#include <sstream>
#include <stdexcept>

#ifndef ASN1KIT_BER_DER_FIXTURES_DIR
#error ASN1KIT_BER_DER_FIXTURES_DIR must be defined
#endif

namespace asn1kit::test {
namespace {

std::string fixtures_dir() {
    return ASN1KIT_BER_DER_FIXTURES_DIR;
}

int hex_nibble(char c) {
    if (c >= '0' && c <= '9') {
        return c - '0';
    }
    if (c >= 'a' && c <= 'f') {
        return 10 + (c - 'a');
    }
    if (c >= 'A' && c <= 'F') {
        return 10 + (c - 'A');
    }
    throw std::runtime_error("Invalid hex character.");
}

} // namespace

std::vector<std::uint8_t> parse_hex(std::string_view hex) {
    std::string cleaned;
    cleaned.reserve(hex.size());
    for (char c : hex) {
        if (c == ' ' || c == '-') {
            continue;
        }
        cleaned.push_back(c);
    }
    if ((cleaned.size() & 1) != 0) {
        throw std::runtime_error("Hex string has odd length.");
    }
    std::vector<std::uint8_t> result(cleaned.size() / 2);
    for (std::size_t i = 0; i < result.size(); ++i) {
        result[i] = static_cast<std::uint8_t>(
            (hex_nibble(cleaned[i * 2]) << 4) | hex_nibble(cleaned[i * 2 + 1]));
    }
    return result;
}

std::string format_hex(std::span<const std::uint8_t> data) {
    static const char* digits = "0123456789ABCDEF";
    std::string result;
    result.resize(data.size() * 2);
    for (std::size_t i = 0; i < data.size(); ++i) {
        result[i * 2] = digits[(data[i] >> 4) & 0xF];
        result[i * 2 + 1] = digits[data[i] & 0xF];
    }
    return result;
}

encoding ber_der_case::encoding_rules() const {
    if (rules.size() == 3
        && (rules[0] == 'b' || rules[0] == 'B')
        && (rules[1] == 'e' || rules[1] == 'E')
        && (rules[2] == 'r' || rules[2] == 'R')) {
        return encoding::ber;
    }
    return encoding::der;
}

reader_options ber_der_case::get_reader_options() const {
    if (!reader_profile || reader_profile->empty() || *reader_profile == "default") {
        return reader_options::default_profile();
    }
    if (*reader_profile == "strict") {
        return reader_options::strict();
    }
    if (*reader_profile == "allowNonMinimalLength") {
        return reader_options::allow_non_minimal_length();
    }
    if (*reader_profile == "allowOverlongOid") {
        return reader_options::allow_overlong_oid_base128();
    }
    throw std::runtime_error("Unknown readerProfile '" + *reader_profile + "'.");
}

std::vector<std::uint8_t> ber_der_case::get_bytes() const {
    return parse_hex(bytes_hex);
}

bool ber_der_case::should_encode() const {
    return encode && !reject && encoding_rules() == encoding::der;
}

bool ber_der_case::has_value() const {
    return !value.is_null() && !value.is_discarded();
}

std::vector<ber_der_case> load_cases(const std::string& file_name) {
    const auto path = fixtures_dir() + "/" + file_name;
    std::ifstream in(path);
    if (!in) {
        throw std::runtime_error("Cannot open fixture file: " + path);
    }
    nlohmann::json root;
    in >> root;
    if (!root.is_array() || root.empty()) {
        throw std::runtime_error("No cases in '" + file_name + "'.");
    }

    std::vector<ber_der_case> cases;
    cases.reserve(root.size());
    for (const auto& item : root) {
        ber_der_case c;
        c.name = item.at("name").get<std::string>();
        c.rules = item.value("rules", std::string("der"));
        c.op = item.value("op", std::string());
        c.bytes_hex = item.at("bytes").get<std::string>();
        if (item.contains("value")) {
            c.value = item.at("value");
        }
        c.encode = item.value("encode", true);
        c.reject = item.value("reject", false);
        if (item.contains("form") && !item.at("form").is_null()) {
            c.form = item.at("form").get<std::string>();
        }
        if (item.contains("unusedBits") && !item.at("unusedBits").is_null()) {
            c.unused_bits = item.at("unusedBits").get<int>();
        }
        if (item.contains("fractionDigits") && !item.at("fractionDigits").is_null()) {
            c.fraction_digits = item.at("fractionDigits").get<int>();
        }
        if (item.contains("readerProfile") && !item.at("readerProfile").is_null()) {
            c.reader_profile = item.at("readerProfile").get<std::string>();
        }
        if (c.name.empty()) {
            throw std::runtime_error("Case in '" + file_name + "' is missing name.");
        }
        cases.push_back(std::move(c));
    }
    return cases;
}

std::string get_string(const ber_der_case& c) {
    if (!c.has_value()) {
        return {};
    }
    if (c.value.is_string()) {
        return c.value.get<std::string>();
    }
    return c.value.dump();
}

bool get_boolean(const ber_der_case& c) {
    if (c.value.is_boolean()) {
        return c.value.get<bool>();
    }
    if (c.value.is_string()) {
        const auto s = c.value.get<std::string>();
        if (s == "true" || s == "True") {
            return true;
        }
        if (s == "false" || s == "False") {
            return false;
        }
    }
    throw std::runtime_error("Case '" + c.name + "' has invalid boolean value.");
}

std::string get_integer_decimal(const ber_der_case& c) {
    auto text = get_string(c);
    if (text.empty()) {
        throw std::runtime_error("Case '" + c.name + "' missing integer value.");
    }
    return text;
}

std::vector<std::uint8_t> get_octet_value(const ber_der_case& c) {
    const auto text = get_string(c);
    if (text.empty()) {
        return {};
    }
    return parse_hex(text);
}

utc_date_time get_time(const ber_der_case& c) {
    const auto text = get_string(c);
    if (text.empty()) {
        throw std::runtime_error("Case '" + c.name + "' missing time value.");
    }
    return utc_date_time::parse_iso(text);
}

} // namespace asn1kit::test
