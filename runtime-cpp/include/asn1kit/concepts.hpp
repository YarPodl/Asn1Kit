// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/tag.hpp"

#include <concepts>
#include <utility>

namespace asn1kit {

class reader;
class writer;

/// Generated / structured ASN.1 type with tag matching and natural/override decode.
template <typename T>
concept asn1_decodable = requires(reader& r, const tag& t) {
    { T::match_tag(t) } -> std::convertible_to<bool>;
    { T::decode(r) } -> std::convertible_to<T>;
    { T::decode(r, t) } -> std::convertible_to<T>;
};

/// Generated / structured ASN.1 type with natural and override encode.
template <typename T>
concept asn1_encodable = requires(const T& value, writer& w, const tag& t) {
    { value.encode(w) } -> std::same_as<void>;
    { value.encode(w, t) } -> std::same_as<void>;
};

} // namespace asn1kit
