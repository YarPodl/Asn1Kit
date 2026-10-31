// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bytes.hpp"
#include "asn1kit/concepts.hpp"
#include "asn1kit/encoding.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/reader_options.hpp"
#include "asn1kit/writer.hpp"

#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace asn1kit {

/// Ergonomic one-call wrappers for generated types.
///
/// These helpers remove the `writer`/`reader` boilerplate for the common
/// "value -> complete TLV" and "complete TLV -> value" scenarios:
///
///     auto encoded = asn1kit::encode(content_info, encoding::der);
///     auto info    = asn1kit::decode<Cms::ContentInfo>(encoded, encoding::der);
///
/// `decode` requires the input to be consumed completely and throws
/// `asn1kit::exception` on trailing octets. Use `decode_prefix` when the input
/// may carry subsequent TLVs.

namespace detail {

template <asn1_decodable T>
[[nodiscard]] T decode_impl(
    std::span<const std::uint8_t> data,
    encoding enc,
    reader_options options,
    bool require_complete) {
    reader r(data, enc, options);
    T value = T::decode(r);
    if (require_complete) {
        r.throw_if_not_empty();
    }
    return value;
}

} // namespace detail

/// Encodes `value` as a complete TLV using `enc`. Equivalent to
/// `writer w(enc); value.encode(w); w.encode();`.
template <asn1_encodable T>
[[nodiscard]] std::vector<std::uint8_t> encode(const T& value, encoding enc = encoding::der) {
    writer w(enc);
    value.encode(w);
    return w.encode();
}

/// Encodes `value` into caller-provided `destination`. On success reports the
/// number of bytes written via `bytes_written`. Returns `false` when
/// `destination` is too small; `bytes_written` is unspecified in that case.
template <asn1_encodable T>
[[nodiscard]] bool try_encode(
    std::span<std::uint8_t> destination,
    const T& value,
    std::size_t& bytes_written,
    encoding enc = encoding::der) {
    writer w(enc);
    value.encode(w);
    return w.try_encode(destination, bytes_written);
}

/// Convenience overload of `try_encode` that discards the written byte count.
template <asn1_encodable T>
[[nodiscard]] bool try_encode(
    std::span<std::uint8_t> destination,
    const T& value,
    encoding enc = encoding::der) {
    std::size_t written = 0;
    return try_encode(destination, value, written, enc);
}

/// Decodes a complete TLV; throws `asn1kit::exception` on trailing octets.
template <asn1_decodable T>
[[nodiscard]] T decode(
    std::span<const std::uint8_t> data,
    encoding enc = encoding::der,
    reader_options options = reader_options::default_profile()) {
    return detail::decode_impl<T>(data, enc, options, true);
}

template <asn1_decodable T>
[[nodiscard]] T decode(
    const std::vector<std::uint8_t>& data,
    encoding enc = encoding::der,
    reader_options options = reader_options::default_profile()) {
    return detail::decode_impl<T>(
        std::span<const std::uint8_t>(data.data(), data.size()), enc, options, true);
}

template <asn1_decodable T>
[[nodiscard]] T decode(
    const bytes& data,
    encoding enc = encoding::der,
    reader_options options = reader_options::default_profile()) {
    return detail::decode_impl<T>(data.span(), enc, options, true);
}

/// Decodes the leading TLV and ignores any trailing octets.
template <asn1_decodable T>
[[nodiscard]] T decode_prefix(
    std::span<const std::uint8_t> data,
    encoding enc = encoding::der,
    reader_options options = reader_options::default_profile()) {
    return detail::decode_impl<T>(data, enc, options, false);
}

template <asn1_decodable T>
[[nodiscard]] T decode_prefix(
    const std::vector<std::uint8_t>& data,
    encoding enc = encoding::der,
    reader_options options = reader_options::default_profile()) {
    return detail::decode_impl<T>(
        std::span<const std::uint8_t>(data.data(), data.size()), enc, options, false);
}

template <asn1_decodable T>
[[nodiscard]] T decode_prefix(
    const bytes& data,
    encoding enc = encoding::der,
    reader_options options = reader_options::default_profile()) {
    return detail::decode_impl<T>(data.span(), enc, options, false);
}

} // namespace asn1kit
