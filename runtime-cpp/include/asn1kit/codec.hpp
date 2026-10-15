// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/any.hpp"
#include "asn1kit/bit_string.hpp"
#include "asn1kit/bytes.hpp"
#include "asn1kit/concepts.hpp"
#include "asn1kit/detail/typed_value.hpp"
#include "asn1kit/encoding.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/null.hpp"
#include "asn1kit/oid.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/tag.hpp"
#include "asn1kit/utc_date_time.hpp"
#include "asn1kit/writer.hpp"

#include <cstdint>
#include <optional>
#include <string>
#include <type_traits>
#include <utility>
#include <vector>

namespace asn1kit {

/// Identifies one generated open-type table at compile time.
template <detail::oid_text Name>
struct open_context final {};

/// Associates an open-type selector key with its decoded public value type.
/// Encoding and decoding remain operations of the generated carrier/owner types.
template <typename Context, typename Key, typename Value>
struct open_binding {
    using context_type = Context;
    using key_type = Key;
    using value_type = Value;

    Key key{};
};

template <typename T>
void merge_unique(std::optional<T>& result, std::optional<T> candidate) {
    if (!candidate.has_value()) return;
    if (result.has_value())
        throw exception("Multiple values match open-type contextual decode.");
    result = std::move(candidate);
}

template <typename Source, typename Context, typename Key, typename Value>
[[nodiscard]] std::optional<Value> try_decode_unique(
    const Source& source, const open_binding<Context, Key, Value>& binding) {
    if constexpr (requires { source.try_decode(binding); }) {
        return source.try_decode(binding);
    } else {
        std::optional<Value> result;
        for (const auto& item : source)
            merge_unique(result, try_decode_unique(item, binding));
        return result;
    }
}

/// UTF8String / PrintableString / … with the ASN.1 form in the type (not a runtime codec).
template <string_form Form>
struct asn_string {
    std::string value{};

    asn_string() = default;
    asn_string(std::string v) : value(std::move(v)) {}
    asn_string(const char* v) : value(v) {}

    [[nodiscard]] operator const std::string&() const noexcept { return value; }
    [[nodiscard]] operator std::string&() noexcept { return value; }

    friend bool operator==(const asn_string& a, const asn_string& b) noexcept {
        return a.value == b.value;
    }
    friend bool operator!=(const asn_string& a, const asn_string& b) noexcept { return !(a == b); }
};

/// UTCTime / GeneralizedTime with the ASN.1 form (and fraction digits) in the type.
template <time_form Form, int FractionDigits = 3>
struct asn_time {
    utc_date_time value{};

    constexpr asn_time() = default;
    constexpr asn_time(utc_date_time v) : value(std::move(v)) {}

    [[nodiscard]] constexpr operator const utc_date_time&() const noexcept { return value; }
    [[nodiscard]] constexpr operator utc_date_time&() noexcept { return value; }

    friend constexpr bool operator==(const asn_time& a, const asn_time& b) noexcept {
        return a.value == b.value;
    }
    friend constexpr bool operator!=(const asn_time& a, const asn_time& b) noexcept {
        return !(a == b);
    }
};

namespace detail {

template <typename T>
inline constexpr bool is_asn_string_v = false;
template <string_form Form>
inline constexpr bool is_asn_string_v<asn_string<Form>> = true;

template <typename T>
inline constexpr bool is_asn_time_v = false;
template <time_form Form, int FractionDigits>
inline constexpr bool is_asn_time_v<asn_time<Form, FractionDigits>> = true;

template <typename T>
concept open_primitive =
    is_typed_primitive_v<T> || std::is_same_v<T, null_value> || std::is_same_v<T, any>;

template <typename T>
struct open_wire_ops;

template <string_form Form>
struct open_wire_ops<asn_string<Form>> {
    static void encode(writer& w, const asn_string<Form>& value) {
        w.write_string(default_string_tag(Form), value.value, Form);
    }
    static asn_string<Form> decode(reader& r) {
        return asn_string<Form>{r.read_string(default_string_tag(Form), Form)};
    }
};

template <time_form Form, int FractionDigits>
struct open_wire_ops<asn_time<Form, FractionDigits>> {
    static void encode(writer& w, const asn_time<Form, FractionDigits>& value) {
        w.write_time(default_time_tag(Form), value.value, Form, FractionDigits);
    }
    static asn_time<Form, FractionDigits> decode(reader& r) {
        return asn_time<Form, FractionDigits>{r.read_time(default_time_tag(Form), Form)};
    }
};

template <typename T>
void encode_open_value(writer& w, T& value) {
    if constexpr (asn1_encodable<T>) {
        value.encode(w);
    } else if constexpr (std::is_same_v<T, null_value>) {
        null_value::encode(w, tag::null);
    } else if constexpr (std::is_same_v<T, any>) {
        w.write_any(value);
    } else if constexpr (is_asn_string_v<T> || is_asn_time_v<T>) {
        open_wire_ops<T>::encode(w, value);
    } else {
        w.write(value);
    }
}

template <typename T>
[[nodiscard]] T decode_open_value(reader& r) {
    if constexpr (asn1_decodable<T>) {
        return T::decode(r);
    } else if constexpr (std::is_same_v<T, null_value>) {
        return null_value::decode(r, tag::null);
    } else if constexpr (std::is_same_v<T, any>) {
        return r.read_any();
    } else if constexpr (is_asn_string_v<T> || is_asn_time_v<T>) {
        return open_wire_ops<T>::decode(r);
    } else {
        return r.read<T>();
    }
}

} // namespace detail

/// Types whose open-type wire form is fully determined at compile time.
template <typename T>
concept open_typed = (asn1_decodable<T> && asn1_encodable<T>) || detail::open_primitive<T>
    || detail::is_asn_string_v<T> || detail::is_asn_time_v<T>;

/// Encodes one value as a complete DER TLV; wire rules come from `T`.
template <open_typed T>
[[nodiscard]] any encode_open(const T& value) {
    return any::from_value(value, [](writer& w, T& x) { detail::encode_open_value(w, x); });
}

/// Decodes one complete BER TLV; wire rules come from `T`.
template <open_typed T>
[[nodiscard]] T decode_open(const any& raw, encoding enc = encoding::ber) {
    return raw.decode_value([](reader& r) { return detail::decode_open_value<T>(r); }, enc);
}

template <open_typed T>
[[nodiscard]] T decode_contained(const bytes& raw) {
    return decode_open<T>(any(raw));
}

template <open_typed T>
[[nodiscard]] T decode_contained(const bit_string& raw) {
    if (raw.unused_bits() != 0) {
        throw exception("Typed contained value must be octet-aligned.");
    }
    return decode_open<T>(any(raw.contents()));
}

template <open_typed T>
[[nodiscard]] bytes encode_contained_octets(const T& value) {
    return encode_open(value).encoded();
}

template <open_typed T>
[[nodiscard]] bit_string encode_contained_bits(const T& value) {
    return bit_string{encode_open(value).encoded(), 0};
}

template <open_typed T>
[[nodiscard]] std::vector<T> decode_each(const std::vector<any>& raw) {
    std::vector<T> result;
    result.reserve(raw.size());
    for (const auto& item : raw) {
        result.push_back(decode_open<T>(item));
    }
    return result;
}

template <open_typed T>
[[nodiscard]] std::vector<any> encode_each(const std::vector<T>& value) {
    std::vector<any> result;
    result.reserve(value.size());
    for (const auto& item : value) {
        result.push_back(encode_open(item));
    }
    return result;
}

} // namespace asn1kit
