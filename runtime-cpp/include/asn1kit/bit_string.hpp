// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bytes.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/tag.hpp"

#include <cstdint>
#include <span>
#include <vector>

namespace asn1kit {

class writer;
class reader;

/// BIT STRING value: content octets plus unused trailing bits count.
class bit_string {
public:
    bit_string() = default;

    bit_string(bytes data, int unused_bits);

    [[nodiscard]] static bit_string copy_from(std::span<const std::uint8_t> data, int unused_bits);

    [[nodiscard]] std::span<const std::uint8_t> span() const noexcept { return bytes_.span(); }
    [[nodiscard]] const bytes& contents() const noexcept { return bytes_; }
    [[nodiscard]] int unused_bits() const noexcept { return unused_bits_; }
    [[nodiscard]] int bit_length() const noexcept {
        return bytes_.empty() ? 0 : static_cast<int>(bytes_.size() * 8 - unused_bits_);
    }

    [[nodiscard]] std::vector<std::uint8_t> to_vector() const { return bytes_.to_vector(); }
    [[nodiscard]] bit_string clone() const { return bit_string(bytes::copy_from(span()), unused_bits_); }

    [[nodiscard]] static bit_string parse_primitive(bytes contents, bool reject_trailing_bits);

    static void encode(writer& w, const bit_string& value, const tag& t = tag::bit_string);
    static bit_string decode(reader& r, const tag& t = tag::bit_string);

    friend bool operator==(const bit_string& a, const bit_string& b);
    friend bool operator!=(const bit_string& a, const bit_string& b) { return !(a == b); }

private:
    bytes bytes_{};
    int unused_bits_{0};
};

} // namespace asn1kit
