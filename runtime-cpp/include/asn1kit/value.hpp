// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bytes.hpp"

#include <utility>

namespace asn1kit {

/// Decoded ASN.1 value together with the complete TLV consumed from the input, when available.
template <typename T>
class value {
public:
    value() = default;

    /// Application-provided value with no original encoding.
    [[nodiscard]] static value from_value(T v) {
        return value(std::move(v), bytes{});
    }

    /// Decoded value that retains a view of its original complete TLV.
    [[nodiscard]] static value decoded(T v, bytes original_encoding) {
        return value(std::move(v), std::move(original_encoding));
    }

    [[nodiscard]] const T& get() const noexcept { return value_; }

    /// Non-const access invalidates the retained TLV so a later encode reflects mutations.
    [[nodiscard]] T& get() noexcept
    {
        original_encoding_ = bytes{};
        return value_;
    }

    [[nodiscard]] const bytes& original_encoding() const noexcept { return original_encoding_; }
    [[nodiscard]] bool has_original_encoding() const noexcept { return !original_encoding_.empty(); }

    friend bool operator==(const value& a, const value& b) {
        return a.value_ == b.value_ && a.original_encoding_ == b.original_encoding_;
    }

    friend bool operator!=(const value& a, const value& b) { return !(a == b); }

private:
    value(T v, bytes original_encoding)
        : value_(std::move(v)), original_encoding_(std::move(original_encoding)) {}

    T value_{};
    bytes original_encoding_{};
};

} // namespace asn1kit
