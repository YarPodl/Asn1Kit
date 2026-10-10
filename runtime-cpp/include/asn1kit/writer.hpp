// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/bit_string.hpp"
#include "asn1kit/concepts.hpp"
#include "asn1kit/detail/encode_buffer.hpp"
#include "asn1kit/detail/typed_value.hpp"
#include "asn1kit/encoding.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/oid.hpp"
#include "asn1kit/tag.hpp"
#include "asn1kit/utc_date_time.hpp"
#include "asn1kit/writer_scope.hpp"

#include <concepts>
#include <cstddef>
#include <cstdint>
#include <ranges>
#include <span>
#include <string_view>
#include <type_traits>
#include <utility>
#include <vector>

namespace asn1kit {

/// Public BER/DER encode facade.
class writer {
public:
    explicit writer(encoding enc = encoding::der);

    writer(const writer&) = delete;
    writer& operator=(const writer&) = delete;
    writer(writer&&) noexcept = default;
    writer& operator=(writer&&) noexcept = default;
    ~writer() = default;

    [[nodiscard]] encoding encoding_rules() const noexcept;
    [[nodiscard]] std::size_t encoded_length() const noexcept;

    void ensure_capacity(std::size_t capacity);
    void reset();

    [[nodiscard]] std::vector<std::uint8_t> encode() const;
    [[nodiscard]] bool try_encode(std::span<std::uint8_t> destination, std::size_t& bytes_written) const;

    void write_boolean(const tag& t, bool value);
    void write_integer(const tag& t, std::int32_t value);
    void write_integer(const tag& t, std::uint32_t value);
    void write_integer(const tag& t, std::int64_t value);
    void write_integer(const tag& t, std::uint64_t value);
    void write_integer(const tag& t, const integer& value);
    /// Encode INTEGER from decimal ASCII (no external bigint library).
    void write_integer_decimal(const tag& t, std::string_view decimal);
    void write_enumerated_decimal(const tag& t, std::string_view decimal);

    void write_octet_string(const tag& t, std::span<const std::uint8_t> value);
    void write_null(const tag& t);
    void write_object_identifier(const tag& t, std::string_view oid_text);
    void write_object_identifier(const tag& t, const oid& value);
    void write_bit_string(const tag& t, const bit_string& value);
    void write_string(const tag& t, std::string_view value, string_form form);
    void write_time(const tag& t, const utc_date_time& value, time_form form, int fraction_digits = 3);

    void write_raw(std::span<const std::uint8_t> tlv);

    [[nodiscard]] writer_scope enter_sequence(const tag& t);
    [[nodiscard]] writer_scope enter_set(const tag& t);
    [[nodiscard]] writer_scope enter_sequence_of(const tag& t);
    [[nodiscard]] writer_scope enter_set_of(const tag& t);
    [[nodiscard]] writer_scope enter_explicit(const tag& t);

    template <std::ranges::input_range R, std::invocable<writer&, std::ranges::range_reference_t<R>> F>
    void write_sequence_of(const tag& t, R&& items, F&& encode_item) {
        [[maybe_unused]] auto scope = enter_sequence_of(t);
        for (auto&& item : items) {
            encode_item(*this, item);
        }
    }

    template <std::ranges::input_range R, std::invocable<writer&, std::ranges::range_reference_t<R>> F>
    void write_set_of(const tag& t, R&& items, F&& encode_item) {
        [[maybe_unused]] auto scope = enter_set_of(t);
        for (auto&& item : items) {
            encode_item(*this, item);
        }
    }

    /// Type-driven encode for generated types and common primitives.
    template <detail::typed_writable T>
    void write(const T& value, const tag& expected) {
        write_typed(value, expected);
    }

    template <detail::typed_writable T>
    void write(const T& value) {
        write_typed(value, detail::default_tag_for<T>());
    }

    /// EXPLICIT wrapper around a typed value (generated type or primitive).
    template <detail::typed_writable T>
    void write_to_explicit(const tag& wrapper, const T& value) {
        [[maybe_unused]] auto scope = enter_explicit(wrapper);
        write(value);
    }

    /// SEQUENCE OF / SET OF without a per-item lambda.
    template <std::ranges::input_range R>
        requires asn1_encodable<std::ranges::range_value_t<R>>
    void write_sequence_of(const tag& t, R&& items) {
        using element = std::ranges::range_value_t<R>;
        write_sequence_of(t, std::forward<R>(items), [](writer& w, const element& item) {
            item.encode(w, element::default_tag());
        });
    }

    template <std::ranges::input_range R>
        requires asn1_encodable<std::ranges::range_value_t<R>>
    void write_set_of(const tag& t, R&& items) {
        using element = std::ranges::range_value_t<R>;
        write_set_of(t, std::forward<R>(items), [](writer& w, const element& item) {
            item.encode(w, element::default_tag());
        });
    }

    template <asn1_encodable T>
    void write_sequence_of_to_explicit(const tag& wrapper, std::span<const T> items) {
        [[maybe_unused]] auto scope = enter_explicit(wrapper);
        write_sequence_of(tag::sequence, items);
    }

    [[nodiscard]] std::span<const std::uint8_t> written_span() const;

private:
    friend class writer_scope;

    template <detail::typed_writable T>
    void write_typed(const T& value, const tag& expected) {
        if constexpr (asn1_encodable<T>) {
            value.encode(*this, expected);
        } else if constexpr (std::is_same_v<T, bool>) {
            write_boolean(expected, value);
        } else if constexpr (
            std::is_same_v<T, std::int32_t> || std::is_same_v<T, std::uint32_t>
            || std::is_same_v<T, std::int64_t> || std::is_same_v<T, std::uint64_t>
            || std::is_same_v<T, integer>) {
            write_integer(expected, value);
        } else if constexpr (std::is_same_v<T, bytes>) {
            write_octet_string(expected, value.span());
        } else if constexpr (std::is_same_v<T, oid>) {
            write_object_identifier(expected, value);
        } else if constexpr (std::is_same_v<T, bit_string>) {
            write_bit_string(expected, value);
        } else {
            static_assert(sizeof(T) == 0, "Unsupported typed ASN.1 value.");
        }
    }

    [[nodiscard]] writer_scope begin_scope(const tag& t, bool sort_der_set_of);
    void end_scope(
        const detail::encode_frame& frame,
        std::uint64_t scope_token,
        std::uint64_t parent_scope_token,
        bool sort_der_set_of);
    void ensure_no_active_scope(const char* operation) const;

    encoding encoding_;
    detail::encode_buffer buffer_;
    std::uint64_t active_scope_token_{0};
    std::uint64_t next_scope_token_{0};
};

} // namespace asn1kit
