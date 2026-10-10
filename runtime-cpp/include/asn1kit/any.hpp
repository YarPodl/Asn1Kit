// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bytes.hpp"
#include "asn1kit/encoding.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/tag.hpp"

#include <concepts>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <span>
#include <string>
#include <type_traits>
#include <utility>
#include <vector>

namespace asn1kit {

class reader;
class writer;

/// ANY value: a complete encoded TLV (tag + length + value), without open-type resolution.
class any {
public:
    any() = default;

    /// Wraps a complete TLV without copying when `encoded` already owns/borrows one TLV.
    /// The span must be exactly one TLV; trailing octets are rejected.
    explicit any(bytes encoded);

    /// Copies a complete TLV into an owned buffer.
    [[nodiscard]] static any copy_from(std::span<const std::uint8_t> encoded);

    /// Builds a definite-length TLV from `t` and value octets into an owned buffer.
    [[nodiscard]] static any from_tag_and_contents(const tag& t, std::span<const std::uint8_t> contents);

    /// Encodes a supplied value as one DER TLV using the runtime writer.
    template <typename T, std::invocable<writer&, T&> F>
    [[nodiscard]] static any from_value(T value, F&& encode);

    /// Trusted wrap used by `reader::read_any` (views may alias the reader buffer).
    [[nodiscard]] static any wrap(tag t, bytes encoded, bytes contents) {
        return any(std::move(t), std::move(encoded), std::move(contents));
    }

    [[nodiscard]] const tag& tag_value() const noexcept { return tag_; }
    [[nodiscard]] const bytes& encoded() const noexcept { return encoded_; }
    [[nodiscard]] const bytes& contents() const noexcept { return contents_; }
    [[nodiscard]] std::span<const std::uint8_t> span() const noexcept { return encoded_.span(); }

    [[nodiscard]] std::vector<std::uint8_t> to_vector() const { return encoded_.to_vector(); }
    [[nodiscard]] any clone() const { return copy_from(span()); }

    [[nodiscard]] std::string to_string() const;

    /// Decodes the complete TLV as one value and rejects unconsumed bytes.
    template <std::invocable<reader&> F>
    [[nodiscard]] auto decode_value(F&& decode, encoding enc = encoding::ber) const
        -> std::invoke_result_t<F&, reader&>;

    static void encode(writer& w, const any& value);
    static void encode(writer& w, const any& value, const tag& t);
    static any decode(reader& r);
    static any decode(reader& r, const tag& t);

    friend bool operator==(const any& a, const any& b) noexcept { return a.encoded_ == b.encoded_; }
    friend bool operator!=(const any& a, const any& b) noexcept { return !(a == b); }

private:
    any(tag t, bytes encoded, bytes contents)
        : tag_(std::move(t)), encoded_(std::move(encoded)), contents_(std::move(contents)) {}

    tag tag_{};
    bytes encoded_{};
    bytes contents_{};
};

} // namespace asn1kit

#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

namespace asn1kit {

template <typename T, std::invocable<writer&, T&> F>
any any::from_value(T value, F&& encode) {
    writer w(encoding::der);
    std::invoke(std::forward<F>(encode), w, value);
    return any(bytes::from_vector(w.encode()));
}

template <std::invocable<reader&> F>
auto any::decode_value(F&& decode, encoding enc) const -> std::invoke_result_t<F&, reader&> {
    reader r(encoded_, enc);
    auto value = std::invoke(std::forward<F>(decode), r);
    r.throw_if_not_empty();
    return value;
}

inline void any::encode(writer& w, const any& value) {
    w.write_any(value);
}

inline void any::encode(writer& w, const any& value, const tag& t) {
    w.write_any(t, value);
}

inline any any::decode(reader& r) {
    return r.read_any();
}

inline any any::decode(reader& r, const tag& t) {
    return r.read_any(t);
}

} // namespace asn1kit
