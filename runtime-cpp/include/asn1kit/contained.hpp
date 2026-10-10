// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bytes.hpp"
#include "asn1kit/exception.hpp"

#include <stdexcept>
#include <utility>

namespace asn1kit {

/// Octets of a CONTAINING value and, when its structure is known, its decoded value.
template <typename T>
class contained {
public:
    contained() = default;

    /// Original content octets, excluding the BIT STRING unused-bits octet.
    [[nodiscard]] const bytes& contents() const noexcept { return contents_; }

    /// Unused bits for an opaque BIT STRING; typed contents are octet-aligned.
    [[nodiscard]] int unused_bits() const noexcept { return unused_bits_; }

    /// Whether the typed content was decoded or supplied.
    [[nodiscard]] bool has_value() const noexcept { return has_value_; }

    /// Gets the typed content; throws for opaque content.
    [[nodiscard]] const T& value() const {
        if (!has_value_) {
            throw std::invalid_argument("Contained value is opaque.");
        }
        return value_;
    }

    [[nodiscard]] T& value() {
        if (!has_value_) {
            throw std::invalid_argument("Contained value is opaque.");
        }
        return value_;
    }

    /// Creates typed content. Its encoding is produced by the writer.
    [[nodiscard]] static contained from_value(T value) {
        return contained(bytes{}, 0, true, std::move(value));
    }

    /// Wraps opaque octets without copying.
    [[nodiscard]] static contained from_encoded(bytes contents, int unused_bits = 0) {
        return contained(std::move(contents), unused_bits, false, T{});
    }

    /// Known decode: retains original octets and the decoded value.
    [[nodiscard]] static contained decoded(bytes contents, T value) {
        return contained(std::move(contents), 0, true, std::move(value));
    }

    friend bool operator==(const contained& a, const contained& b) {
        if (a.has_value_ != b.has_value_ || a.unused_bits_ != b.unused_bits_
            || a.contents_ != b.contents_) {
            return false;
        }
        if (a.has_value_) {
            return a.value_ == b.value_;
        }
        return true;
    }

    friend bool operator!=(const contained& a, const contained& b) { return !(a == b); }

private:
    contained(bytes contents, int unused_bits, bool has_value, T value)
        : contents_(std::move(contents))
        , unused_bits_(unused_bits)
        , has_value_(has_value)
        , value_(std::move(value)) {
        if (unused_bits_ < 0 || unused_bits_ > 7
            || (contents_.empty() && unused_bits_ != 0)) {
            throw std::invalid_argument("unused_bits");
        }
    }

    bytes contents_{};
    int unused_bits_{0};
    bool has_value_{false};
    T value_{};
};

} // namespace asn1kit
