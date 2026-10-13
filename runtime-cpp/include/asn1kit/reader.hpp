// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bit_string.hpp"
#include "asn1kit/bytes.hpp"
#include "asn1kit/concepts.hpp"
#include "asn1kit/detail/decode_cursor.hpp"
#include "asn1kit/detail/typed_value.hpp"
#include "asn1kit/encoding.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/lazy.hpp"
#include "asn1kit/oid.hpp"
#include "asn1kit/reader_options.hpp"
#include "asn1kit/reader_scope.hpp"
#include "asn1kit/tag.hpp"
#include "asn1kit/utc_date_time.hpp"
#include "asn1kit/value.hpp"

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

class any;

/// BER/DER decode facade.
class reader {
public:
    explicit reader(
        bytes data,
        encoding enc = encoding::ber,
        reader_options options = reader_options::default_profile());

    explicit reader(
        std::span<const std::uint8_t> data,
        encoding enc = encoding::ber,
        reader_options options = reader_options::default_profile());

    explicit reader(
        const std::vector<std::uint8_t>& data,
        encoding enc = encoding::ber,
        reader_options options = reader_options::default_profile());

    reader(const reader&) = delete;
    reader& operator=(const reader&) = delete;
    reader(reader&&) noexcept = default;
    reader& operator=(reader&&) noexcept = default;
    ~reader() = default;

    [[nodiscard]] encoding encoding_rules() const noexcept;
    [[nodiscard]] const reader_options& options() const noexcept;
    [[nodiscard]] bool eof() const noexcept;
    [[nodiscard]] std::size_t remaining() const noexcept;

    void throw_if_not_empty() const;

    [[nodiscard]] bool try_peek_tag(tag& out) const;

    /// True when the next tag matches `expected` (constructed flag ignored).
    [[nodiscard]] bool next_is(const tag& expected) const;

    /// True when the next tag matches a generated type via `T::match_tag`.
    template <asn1_decodable T>
    [[nodiscard]] bool next_matches() const {
        tag peeked;
        return try_peek_tag(peeked) && T::match_tag(peeked);
    }

    [[nodiscard]] bool read_boolean(const tag& expected = tag::boolean);
    [[nodiscard]] integer read_integer_value(const tag& expected = tag::integer);
    [[nodiscard]] std::int32_t read_int32(const tag& expected = tag::integer);
    [[nodiscard]] std::uint32_t read_uint32(const tag& expected = tag::integer);
    [[nodiscard]] std::int64_t read_int64(const tag& expected = tag::integer);
    [[nodiscard]] std::uint64_t read_uint64(const tag& expected = tag::integer);
    /// Decimal ASCII of the INTEGER value.
    [[nodiscard]] std::string read_integer_decimal(const tag& expected = tag::integer);
    [[nodiscard]] std::string read_enumerated_decimal(const tag& expected = tag::enumerated);

    [[nodiscard]] bytes read_octet_string(const tag& expected = tag::octet_string);
    [[nodiscard]] bool try_read_octet_string(
        const tag& expected,
        std::span<std::uint8_t> destination,
        std::size_t& bytes_written);

    void read_null(const tag& expected = tag::null);

    [[nodiscard]] oid read_oid(const tag& expected = tag::object_identifier);
    [[nodiscard]] std::string read_object_identifier(const tag& expected = tag::object_identifier);

    [[nodiscard]] bit_string read_bit_string(const tag& expected = tag::bit_string);

    [[nodiscard]] std::string read_string(const tag& expected, string_form form);
    [[nodiscard]] utc_date_time read_time(const tag& expected, time_form form);

    [[nodiscard]] any read_any();
    [[nodiscard]] any read_any(const tag& expected);

    /// Consumes a TLV, eagerly decodes it, and retains its original complete encoding.
    template <typename T, std::invocable<reader&> F>
    [[nodiscard]] value<T> read_with_original_encoding(F&& decode) {
        auto tlv = cursor_.read_tlv();
        T decoded{};
        with_encoded(tlv.encoded, [&](reader& r) {
            decoded = std::invoke(std::forward<F>(decode), r);
        });
        return value<T>::decoded(std::move(decoded), std::move(tlv.encoded));
    }

    /// Consumes a TLV without decoding content until `lazy::get()`.
    template <typename T, std::invocable<reader&> F>
    [[nodiscard]] lazy<T> read_lazy(F&& decode) {
        auto tlv = cursor_.read_tlv();
        return lazy<T>::from_encoded(
            std::move(tlv.encoded),
            encoding_rules(),
            options(),
            std::function<T(reader&)>(std::forward<F>(decode)));
    }

    /// Reads OCTET STRING contents as exactly one known ASN.1 value.
    template <typename T, std::invocable<reader&> F>
    [[nodiscard]] T read_octet_string_containing(
        const tag& expected,
        F&& decode) {
        const auto contents = read_octet_string(expected);
        T value{};
        with_encoded(contents, [&](reader& r) {
            value = std::invoke(std::forward<F>(decode), r);
        });
        return value;
    }

    /// Reads octet-aligned BIT STRING contents as exactly one known ASN.1 value.
    template <typename T, std::invocable<reader&> F>
    [[nodiscard]] T read_bit_string_containing(
        const tag& expected,
        F&& decode) {
        const auto bits = read_bit_string(expected);
        if (bits.unused_bits() != 0) {
            throw exception("ASN.1 contained content must be octet-aligned.");
        }
        T value{};
        with_encoded(bits.contents(), [&](reader& r) {
            value = std::invoke(std::forward<F>(decode), r);
        });
        return value;
    }

    [[nodiscard]] reader_scope enter_sequence(const tag& expected);
    [[nodiscard]] reader_scope enter_set(const tag& expected);
    [[nodiscard]] reader_scope enter_explicit(const tag& expected);
    [[nodiscard]] reader_scope enter_encoded(bytes encoded);

    /// enter → callback → throw_if_not_empty on success → pop.
    /// On exception from callback: pop only (error is not replaced).
    template <std::invocable<reader&> F>
    void with_sequence(const tag& expected, F&& fn) {
        with_constructed(enter_sequence(expected), std::forward<F>(fn));
    }

    template <std::invocable<reader&> F>
    void with_set(const tag& expected, F&& fn) {
        with_constructed(enter_set(expected), std::forward<F>(fn));
    }

    template <std::invocable<reader&> F>
    void with_explicit(const tag& expected, F&& fn) {
        with_constructed(enter_explicit(expected), std::forward<F>(fn));
    }

    template <std::invocable<reader&> F>
    void with_encoded(bytes encoded, F&& fn) {
        with_constructed(enter_encoded(std::move(encoded)), std::forward<F>(fn));
    }

    template <std::invocable<reader&> F>
    void for_each_sequence_of(const tag& expected, F&& decode_item) {
        with_sequence(expected, [&](reader& r) {
            while (!r.eof()) {
                const std::size_t before = r.remaining();
                decode_item(r);
                if (r.remaining() >= before) {
                    throw exception("Collection decoder did not consume an element.");
                }
            }
        });
    }

    template <std::invocable<reader&> F>
    void for_each_set_of(const tag& expected, F&& decode_item) {
        with_set(expected, [&](reader& r) {
            while (!r.eof()) {
                const std::size_t before = r.remaining();
                decode_item(r);
                if (r.remaining() >= before) {
                    throw exception("Collection decoder did not consume an element.");
                }
            }
        });
    }

    template <typename T, std::invocable<reader&> F>
    [[nodiscard]] std::vector<T> read_sequence_of(const tag& expected, F&& decode_item) {
        std::vector<T> result;
        for_each_sequence_of(expected, [&](reader& r) {
            result.push_back(static_cast<T>(decode_item(r)));
        });
        return result;
    }

    template <typename T, std::invocable<reader&> F>
    [[nodiscard]] std::vector<T> read_set_of(const tag& expected, F&& decode_item) {
        std::vector<T> result;
        for_each_set_of(expected, [&](reader& r) {
            result.push_back(static_cast<T>(decode_item(r)));
        });
        return result;
    }

    /// Type-driven decode for generated types and common primitives.
    template <detail::typed_readable T>
    [[nodiscard]] T read(const tag& expected) {
        return read_typed<T>(expected);
    }

    template <detail::typed_readable T>
    [[nodiscard]] T read() {
        if constexpr (asn1_decodable<T>) {
            return T::decode(*this);
        } else {
            return read_typed<T>(detail::default_tag_for<T>());
        }
    }

    /// EXPLICIT wrapper → typed value (generated type or primitive, e.g. `std::int32_t`).
    template <detail::typed_readable T>
    [[nodiscard]] T read_from_explicit(const tag& wrapper) {
        T value{};
        with_explicit(wrapper, [&](reader& r) { value = r.read<T>(); });
        return value;
    }

    /// SEQUENCE OF / SET OF without a per-item lambda; items use natural `read<T>()`.
    template <detail::typed_readable T>
    [[nodiscard]] std::vector<T> read_sequence_of(const tag& expected = tag::sequence) {
        return read_sequence_of<T>(expected, [](reader& r) { return r.read<T>(); });
    }

    template <detail::typed_readable T>
    [[nodiscard]] std::vector<T> read_set_of(const tag& expected = tag::set) {
        return read_set_of<T>(expected, [](reader& r) { return r.read<T>(); });
    }

    /// `[n] EXPLICIT SEQUENCE OF T` without lambdas.
    template <detail::typed_readable T>
    [[nodiscard]] std::vector<T> read_sequence_of_from_explicit(const tag& wrapper) {
        std::vector<T> value;
        with_explicit(wrapper, [&](reader& r) { value = r.read_sequence_of<T>(); });
        return value;
    }

    /// `[n] EXPLICIT SET OF T` without lambdas.
    template <detail::typed_readable T>
    [[nodiscard]] std::vector<T> read_set_of_from_explicit(const tag& wrapper) {
        std::vector<T> value;
        with_explicit(wrapper, [&](reader& r) { value = r.read_set_of<T>(); });
        return value;
    }

private:
    friend class reader_scope;

    template <std::invocable<reader&> F>
    void with_constructed(reader_scope scope, F&& fn) {
        fn(*this);
        throw_if_not_empty();
        scope.end();
    }

    template <detail::typed_readable T>
    [[nodiscard]] T read_typed(const tag& expected) {
        if constexpr (asn1_decodable<T>) {
            return T::decode(*this, expected);
        } else if constexpr (std::is_same_v<T, bool>) {
            return read_boolean(expected);
        } else if constexpr (std::is_same_v<T, std::int32_t>) {
            return read_int32(expected);
        } else if constexpr (std::is_same_v<T, std::uint32_t>) {
            return read_uint32(expected);
        } else if constexpr (std::is_same_v<T, std::int64_t>) {
            return read_int64(expected);
        } else if constexpr (std::is_same_v<T, std::uint64_t>) {
            return read_uint64(expected);
        } else if constexpr (std::is_same_v<T, integer>) {
            return read_integer_value(expected);
        } else if constexpr (std::is_same_v<T, bytes>) {
            return read_octet_string(expected);
        } else if constexpr (std::is_same_v<T, oid>) {
            return read_oid(expected);
        } else if constexpr (std::is_same_v<T, bit_string>) {
            return read_bit_string(expected);
        } else {
            static_assert(sizeof(T) == 0, "Unsupported typed ASN.1 value.");
        }
    }

    [[nodiscard]] reader_scope push_contents_window(bytes contents);
    void pop_contents_window(
        detail::decode_cursor saved_cursor,
        std::uint64_t expected_scope_token,
        std::uint64_t parent_scope_token);
    [[nodiscard]] bytes read_constructed_contents(const tag& expected);
    bytes read_primitive_contents(const tag& expected);
    void ensure_minimal_integer_contents(const bytes& contents) const;
    static void ensure_expected_tag(std::size_t tlv_start, const tag& actual, const tag& expected);

    detail::decode_cursor cursor_;
    std::uint64_t active_scope_token_{0};
    std::uint64_t next_scope_token_{0};
};

[[nodiscard]] tag default_string_tag(string_form form);
[[nodiscard]] tag default_time_tag(time_form form);

template <typename T>
void lazy<T>::materialize() const {
    if (cached_.has_value()) {
        return;
    }
    if (!decode_) {
        throw std::invalid_argument("Lazy value has no decoder.");
    }
    reader r(encoded_, encoding_, options_);
    auto decoded = decode_(r);
    r.throw_if_not_empty();
    cached_ = std::move(decoded);
}

} // namespace asn1kit

#include "asn1kit/writer.hpp"

namespace asn1kit {

template <typename T>
void lazy<T>::write_to(writer& w) const {
    if (!has_encoded_) {
        throw std::invalid_argument("Lazy value has no encoded TLV; encode get() explicitly.");
    }
    w.write_raw(encoded_.span());
}

} // namespace asn1kit
