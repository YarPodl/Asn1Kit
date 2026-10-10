// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/tag.hpp"

#include <concepts>
#include <utility>

namespace asn1kit {

class reader;
class writer;

/// Generated / structured ASN.1 type with static decode + default_tag.
template <typename T>
concept asn1_decodable = requires(reader& r, const tag& t) {
    { T::default_tag() } -> std::convertible_to<tag>;
    { T::decode(r, t) } -> std::convertible_to<T>;
};

/// Generated / structured ASN.1 type with instance encode + default_tag.
template <typename T>
concept asn1_encodable = requires(const T& value, writer& w, const tag& t) {
    { T::default_tag() } -> std::convertible_to<tag>;
    { value.encode(w, t) } -> std::same_as<void>;
};

} // namespace asn1kit
