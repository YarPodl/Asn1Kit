// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/tag.hpp"

#include <cstdint>
#include <span>
#include <vector>

namespace asn1kit::detail {

struct encode_frame {
    int length_position{0};
    int content_start{0};
};

/// Mutable BER/DER encode buffer (internal).
class encode_buffer {
public:
    encode_buffer();

    [[nodiscard]] int length() const noexcept { return length_; }
    [[nodiscard]] std::span<const std::uint8_t> written_span() const noexcept;

    void ensure_capacity(int capacity);
    void reset() noexcept { length_ = 0; }
    [[nodiscard]] std::vector<std::uint8_t> to_vector() const;
    [[nodiscard]] bool try_copy_to(std::span<std::uint8_t> destination, int& bytes_written) const;

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
    void finish_definite_length(int length_position, int content_start, int content_length);
    static int encode_definite_length(int length, std::span<std::uint8_t> destination);
    void sort_tlv_contents(int content_start, int content_length);
    static int get_tlv_end(const std::vector<std::uint8_t>& data, int offset, int end);
    void write_tag(const tag& t);
    void write_length(int length);
    void ensure_additional_capacity(int additional);

    static constexpr int max_definite_length_bytes = 5;
    static constexpr int initial_constructed_length_bytes = 1;
    static constexpr int default_capacity = 256;

    std::vector<std::uint8_t> buffer_;
    int length_{0};
};

} // namespace asn1kit::detail
