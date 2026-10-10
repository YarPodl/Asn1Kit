// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/tag.hpp"

#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace asn1kit::detail {

struct encode_frame {
    std::size_t length_position{0};
    std::size_t content_start{0};
};

/// Mutable BER/DER encode buffer (internal).
class encode_buffer {
public:
    encode_buffer();

    [[nodiscard]] std::size_t length() const noexcept { return length_; }
    [[nodiscard]] std::span<const std::uint8_t> written_span() const noexcept;

    void ensure_capacity(std::size_t capacity);
    void reset() noexcept { length_ = 0; }
    [[nodiscard]] std::vector<std::uint8_t> to_vector() const;
    [[nodiscard]] bool try_copy_to(std::span<std::uint8_t> destination, std::size_t& bytes_written) const;

    void write_primitive(const tag& t, std::span<const std::uint8_t> contents);
    void write_primitive(
        const tag& t,
        std::uint8_t first_contents_octet,
        std::span<const std::uint8_t> remaining_contents);
    void write_tlv(const tag& t, std::span<const std::uint8_t> contents);
    void write_raw(std::span<const std::uint8_t> value);

    [[nodiscard]] encode_frame begin_constructed(const tag& t);
    [[nodiscard]] encode_frame begin_value(const tag& t);
    void end_constructed(const encode_frame& frame, bool sort_contents);

private:
    void finish_definite_length(
        std::size_t length_position,
        std::size_t content_start,
        std::size_t content_length);
    static std::size_t encode_definite_length(std::size_t length, std::span<std::uint8_t> destination);
    void sort_tlv_contents(std::size_t content_start, std::size_t content_length);
    static std::size_t get_tlv_end(const std::vector<std::uint8_t>& data, std::size_t offset, std::size_t end);
    void write_tag(const tag& t);
    void write_length(std::size_t length);
    void ensure_additional_capacity(std::size_t additional);

    static constexpr std::size_t max_definite_length_bytes = 5;
    static constexpr std::size_t initial_constructed_length_bytes = 1;
    static constexpr std::size_t default_capacity = 256;

    std::vector<std::uint8_t> buffer_;
    std::size_t length_{0};
};

} // namespace asn1kit::detail
