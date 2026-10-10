// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bit_string.hpp"
#include "asn1kit/bytes.hpp"
#include "asn1kit/concepts.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/oid.hpp"
#include "asn1kit/tag.hpp"

#include <cstdint>
#include <type_traits>

namespace asn1kit::detail {

template <typename T>
inline constexpr bool is_typed_primitive_v =
    std::is_same_v<T, bool> || std::is_same_v<T, std::int32_t> || std::is_same_v<T, std::uint32_t>
    || std::is_same_v<T, std::int64_t> || std::is_same_v<T, std::uint64_t>
    || std::is_same_v<T, integer> || std::is_same_v<T, bytes> || std::is_same_v<T, oid>
    || std::is_same_v<T, bit_string>;

template <typename T>
concept typed_readable = asn1_decodable<T> || is_typed_primitive_v<T>;

template <typename T>
concept typed_writable = asn1_encodable<T> || is_typed_primitive_v<T>;

template <typename T>
[[nodiscard]] tag default_tag_for() {
    if constexpr (requires { T::default_tag(); }) {
        return T::default_tag();
    } else if constexpr (std::is_same_v<T, bool>) {
        return tag::boolean;
    } else if constexpr (
        std::is_same_v<T, std::int32_t> || std::is_same_v<T, std::uint32_t>
        || std::is_same_v<T, std::int64_t> || std::is_same_v<T, std::uint64_t>
        || std::is_same_v<T, integer>) {
        return tag::integer;
    } else if constexpr (std::is_same_v<T, bytes>) {
        return tag::octet_string;
    } else if constexpr (std::is_same_v<T, oid>) {
        return tag::object_identifier;
    } else if constexpr (std::is_same_v<T, bit_string>) {
        return tag::bit_string;
    } else {
        static_assert(sizeof(T) == 0, "Unsupported typed ASN.1 value.");
    }
}

} // namespace asn1kit::detail
