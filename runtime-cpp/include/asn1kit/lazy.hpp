// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bytes.hpp"
#include "asn1kit/encoding.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/reader_options.hpp"

#include <functional>
#include <optional>
#include <stdexcept>
#include <utility>
#include <vector>

namespace asn1kit {

class reader;
class writer;

/// Deferred decode of a complete TLV: the reader advances past the value immediately,
/// and `get()` runs the decoder only on first access.
template <typename T>
class lazy {
public:
    lazy() = default;

    /// Builds a lazy value from a complete TLV (views may alias the reader buffer).
    [[nodiscard]] static lazy from_encoded(
        bytes encoded,
        encoding enc,
        reader_options options,
        std::function<T(reader&)> decode) {
        if (!decode) {
            throw std::invalid_argument("Lazy decode callback is required.");
        }
        if (encoded.empty()) {
            throw exception("Lazy encoded TLV must not be empty.");
        }
        lazy result;
        result.encoded_ = std::move(encoded);
        result.encoding_ = enc;
        result.options_ = std::move(options);
        result.decode_ = std::move(decode);
        result.has_encoded_ = true;
        return result;
    }

    /// Already-materialized object (encode-only; no stored TLV).
    [[nodiscard]] static lazy from_value(T v) {
        lazy result;
        result.cached_ = std::move(v);
        result.has_encoded_ = false;
        return result;
    }

    [[nodiscard]] bool has_encoded() const noexcept { return has_encoded_; }
    [[nodiscard]] bool is_materialized() const noexcept { return cached_.has_value(); }

    [[nodiscard]] const bytes& encoded() const {
        if (!has_encoded_) {
            throw std::invalid_argument(
                "Lazy value was constructed from an object and has no encoded TLV.");
        }
        return encoded_;
    }

    /// Decodes the stored TLV on first access and caches the result.
    [[nodiscard]] const T& get() const {
        materialize();
        return *cached_;
    }

    [[nodiscard]] T& get() {
        materialize();
        return *cached_;
    }

    [[nodiscard]] std::vector<std::uint8_t> to_vector() const { return encoded().to_vector(); }

    [[nodiscard]] lazy clone() const {
        if (has_encoded_) {
            lazy result = from_encoded(
                bytes::copy_from(encoded_.span()), encoding_, options_, decode_);
            if (cached_.has_value()) {
                result.cached_ = *cached_;
            }
            return result;
        }
        return from_value(*cached_);
    }

    /// Writes the stored TLV when present; otherwise throws — callers that used
    /// `from_value` must encode via `get()` themselves.
    void write_to(writer& w) const;

private:
    void materialize() const;

    bytes encoded_{};
    encoding encoding_{encoding::ber};
    reader_options options_{reader_options::default_profile()};
    std::function<T(reader&)> decode_{};
    mutable std::optional<T> cached_{};
    bool has_encoded_{false};
};

} // namespace asn1kit
