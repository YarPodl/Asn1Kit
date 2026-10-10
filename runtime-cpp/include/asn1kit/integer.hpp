// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bytes.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/tag.hpp"

#include <cstddef>
#include <cstdint>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace asn1kit {

class writer;
class reader;

/// INTEGER value holding DER contents (big-endian, without TLV).
class integer {
public:
    integer() = default;

    [[nodiscard]] std::span<const std::uint8_t> span() const noexcept;
    [[nodiscard]] const bytes& contents() const noexcept { return bytes_; }
    [[nodiscard]] std::vector<std::uint8_t> to_vector() const { return span_vector(); }
    [[nodiscard]] integer clone() const { return copy_from(span()); }

    [[nodiscard]] static integer from_contents(bytes contents);
    [[nodiscard]] static integer copy_from(std::span<const std::uint8_t> contents);

    [[nodiscard]] static integer from_int32(std::int32_t value);
    [[nodiscard]] static integer from_uint32(std::uint32_t value);
    [[nodiscard]] static integer from_int64(std::int64_t value);
    [[nodiscard]] static integer from_uint64(std::uint64_t value);

    /// Decimal ASCII ↔ DER contents (no external bigint library).
    [[nodiscard]] static integer from_decimal(std::string_view text);
    [[nodiscard]] std::string to_decimal() const;

    [[nodiscard]] static integer zero() { return {}; }

    [[nodiscard]] bool try_get_int32(std::int32_t& value) const;
    [[nodiscard]] bool try_get_uint32(std::uint32_t& value) const;
    [[nodiscard]] bool try_get_int64(std::int64_t& value) const;
    [[nodiscard]] bool try_get_uint64(std::uint64_t& value) const;

    [[nodiscard]] std::int32_t get_int32() const;
    [[nodiscard]] std::uint32_t get_uint32() const;
    [[nodiscard]] std::int64_t get_int64() const;
    [[nodiscard]] std::uint64_t get_uint64() const;

    [[nodiscard]] static bool is_minimal_contents(std::span<const std::uint8_t> contents);

    static std::size_t encode_contents(std::int32_t value, std::span<std::uint8_t> destination);
    static std::size_t encode_contents(std::uint32_t value, std::span<std::uint8_t> destination);
    static std::size_t encode_contents(std::int64_t value, std::span<std::uint8_t> destination);
    static std::size_t encode_contents(std::uint64_t value, std::span<std::uint8_t> destination);

    static void encode(writer& w, const integer& value, const tag& t = tag::integer);
    static void encode(writer& w, std::int32_t value, const tag& t = tag::integer);
    static integer decode(reader& r, const tag& t = tag::integer);

    friend bool operator==(const integer& a, const integer& b);
    friend bool operator!=(const integer& a, const integer& b) { return !(a == b); }

private:
    explicit integer(bytes contents) : bytes_(std::move(contents)) {}

    [[nodiscard]] std::vector<std::uint8_t> span_vector() const {
        auto s = span();
        return std::vector<std::uint8_t>(s.begin(), s.end());
    }

    bytes bytes_{};
};

} // namespace asn1kit
