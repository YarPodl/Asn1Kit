// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bytes.hpp"
#include "asn1kit/detail/oid_encoding.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/tag.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace asn1kit {

class writer;
class reader;

/// OBJECT IDENTIFIER value: DER contents octets (no tag/length).
class oid {
public:
    oid() = default;

    [[nodiscard]] static oid from_contents(bytes contents);
    [[nodiscard]] static oid copy_from(std::span<const std::uint8_t> contents);
    [[nodiscard]] static oid parse(std::string_view dotted);

    [[nodiscard]] std::span<const std::uint8_t> span() const noexcept { return contents_.span(); }
    [[nodiscard]] const bytes& contents() const noexcept { return contents_; }
    [[nodiscard]] std::vector<std::uint8_t> to_vector() const { return contents_.to_vector(); }
    [[nodiscard]] oid clone() const { return copy_from(span()); }

    [[nodiscard]] std::string to_string() const;

    [[nodiscard]] static constexpr std::size_t get_encode_contents_max_length(std::string_view dotted) {
        return detail::get_encode_contents_max_length(dotted);
    }
    [[nodiscard]] static constexpr std::size_t encode_contents(
        std::string_view dotted, std::span<std::uint8_t> destination) {
        return detail::encode_contents(dotted, destination);
    }
    [[nodiscard]] static std::vector<std::uint8_t> encode_contents(std::string_view dotted);

    [[nodiscard]] static int read_arc(
        std::span<const std::uint8_t> contents,
        std::size_t& offset,
        bool reject_overlong);

    static void encode(writer& w, const oid& value, const tag& t = tag::object_identifier);
    static void encode(writer& w, std::string_view dotted, const tag& t = tag::object_identifier);
    static oid decode(reader& r, const tag& t = tag::object_identifier);
    static std::string decode_string(reader& r, const tag& t = tag::object_identifier);

    friend bool operator==(const oid& a, const oid& b);
    friend bool operator!=(const oid& a, const oid& b) { return !(a == b); }

private:
    explicit oid(bytes contents) : contents_(std::move(contents)) {}

    bytes contents_{};
};

namespace detail {

template <std::size_t N>
struct oid_text {
    char value[N]{};

    constexpr oid_text(const char (&text)[N]) {
        for (std::size_t i = 0; i < N; ++i) value[i] = text[i];
    }

    [[nodiscard]] constexpr std::string_view view() const { return {value, N - 1}; }
};

template <oid_text Text>
consteval auto encode_oid_literal() {
    constexpr auto encoded = [] {
        std::array<std::uint8_t, oid::get_encode_contents_max_length(Text.view())> buffer{};
        const auto size = oid::encode_contents(Text.view(), buffer);
        return std::pair{buffer, size};
    }();
    std::array<std::uint8_t, encoded.second> result{};
    for (std::size_t i = 0; i < result.size(); ++i) result[i] = encoded.first[i];
    return result;
}

} // namespace detail

/// Compile-time DER contents with static storage; calling yields a non-owning oid.
template <detail::oid_text Text>
struct oid_constant {
    static constexpr auto contents = detail::encode_oid_literal<Text>();

    [[nodiscard]] constexpr const char* text() const noexcept { return Text.value; }
    [[nodiscard]] constexpr std::span<const std::uint8_t> span() const noexcept { return contents; }
    [[nodiscard]] oid get() const { return oid::from_contents(bytes::borrow(contents)); }
    [[nodiscard]] oid operator()() const { return get(); }
    [[nodiscard]] operator oid() const { return get(); }
};

template <detail::oid_text Text>
[[nodiscard]] bool operator==(const oid& a, oid_constant<Text> b) noexcept {
    const auto left = a.span();
    const auto right = b.span();
    if (left.size() != right.size()) {
        return false;
    }
    return std::memcmp(left.data(), right.data(), left.size()) == 0;
}

template <detail::oid_text Text>
[[nodiscard]] bool operator==(oid_constant<Text> a, const oid& b) noexcept {
    return b == a;
}

template <detail::oid_text Text>
[[nodiscard]] bool operator!=(const oid& a, oid_constant<Text> b) noexcept {
    return !(a == b);
}

template <detail::oid_text Text>
[[nodiscard]] bool operator!=(oid_constant<Text> a, const oid& b) noexcept {
    return !(a == b);
}

template <detail::oid_text Text>
[[nodiscard]] consteval auto oid_literal() {
    // Instantiate the encoder here so invalid literals fail even if never used.
    static_assert(oid_constant<Text>::contents.size() > 0);
    return oid_constant<Text>{};
}

} // namespace asn1kit
