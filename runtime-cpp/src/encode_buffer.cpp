// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/detail/encode_buffer.hpp"

#include "asn1kit/exception.hpp"

#include <algorithm>
#include <cstring>

namespace asn1kit::detail {

encode_buffer::encode_buffer() : buffer_(default_capacity) {}

std::span<const std::uint8_t> encode_buffer::written_span() const noexcept {
    return std::span<const std::uint8_t>(buffer_.data(), length_);
}

void encode_buffer::ensure_capacity(std::size_t capacity) {
    if (capacity > buffer_.size()) {
        buffer_.resize(capacity);
    }
}

std::vector<std::uint8_t> encode_buffer::to_vector() const {
    return std::vector<std::uint8_t>(buffer_.begin(), buffer_.begin() + static_cast<std::ptrdiff_t>(length_));
}

bool encode_buffer::try_copy_to(std::span<std::uint8_t> destination, std::size_t& bytes_written) const {
    if (destination.size() < length_) {
        bytes_written = 0;
        return false;
    }
    std::memcpy(destination.data(), buffer_.data(), length_);
    bytes_written = length_;
    return true;
}

void encode_buffer::write_primitive(const tag& t, std::span<const std::uint8_t> contents) {
    write_tlv(t.as_primitive(), contents);
}

void encode_buffer::write_primitive(
    const tag& t,
    std::uint8_t first_contents_octet,
    std::span<const std::uint8_t> remaining_contents) {
    write_tag(t.as_primitive());
    write_length(1 + remaining_contents.size());
    ensure_additional_capacity(1 + remaining_contents.size());
    buffer_[length_++] = first_contents_octet;
    if (!remaining_contents.empty()) {
        std::memcpy(buffer_.data() + length_, remaining_contents.data(), remaining_contents.size());
        length_ += remaining_contents.size();
    }
}

void encode_buffer::write_tlv(const tag& t, std::span<const std::uint8_t> contents) {
    write_tag(t);
    write_length(contents.size());
    write_raw(contents);
}

void encode_buffer::write_raw(std::span<const std::uint8_t> value) {
    ensure_additional_capacity(value.size());
    if (!value.empty()) {
        std::memcpy(buffer_.data() + length_, value.data(), value.size());
        length_ += value.size();
    }
}

encode_frame encode_buffer::begin_constructed(const tag& t) {
    return begin_value(t.as_constructed());
}

encode_frame encode_buffer::begin_value(const tag& t) {
    write_tag(t);
    const std::size_t length_position = length_;
    ensure_additional_capacity(initial_constructed_length_bytes);
    buffer_[length_++] = 0;
    return encode_frame{length_position, length_};
}

void encode_buffer::end_constructed(const encode_frame& frame, bool sort_contents) {
    const std::size_t content_length = length_ - frame.content_start;
    if (sort_contents && content_length != 0) {
        sort_tlv_contents(frame.content_start, content_length);
    }
    finish_definite_length(frame.length_position, frame.content_start, content_length);
}

void encode_buffer::finish_definite_length(
    std::size_t length_position,
    std::size_t content_start,
    std::size_t content_length) {
    std::uint8_t encoded[max_definite_length_bytes];
    const std::size_t length_size = encode_definite_length(content_length, encoded);
    const std::size_t reserved_length_size = content_start - length_position;
    const std::size_t content_end = content_start + content_length;

    if (length_size > reserved_length_size) {
        const std::size_t shift = length_size - reserved_length_size;
        ensure_additional_capacity(shift);
        if (content_length > 0) {
            std::memmove(
                buffer_.data() + content_start + shift,
                buffer_.data() + content_start,
                content_length);
        }
        length_ = content_end + shift;
    } else {
        length_ = content_end;
    }

    std::memcpy(buffer_.data() + length_position, encoded, length_size);
}

std::size_t encode_buffer::encode_definite_length(std::size_t length, std::span<std::uint8_t> destination) {
    if (length < 128) {
        destination[0] = static_cast<std::uint8_t>(length);
        return 1;
    }
    if (length > 0xFFFFFFFFu) {
        throw exception("Length exceeds supported definite form.");
    }

    const auto length32 = static_cast<std::uint32_t>(length);
    std::uint8_t bytes[4] = {
        static_cast<std::uint8_t>((length32 >> 24) & 0xFF),
        static_cast<std::uint8_t>((length32 >> 16) & 0xFF),
        static_cast<std::uint8_t>((length32 >> 8) & 0xFF),
        static_cast<std::uint8_t>(length32 & 0xFF),
    };
    std::size_t start = 0;
    while (start < 3 && bytes[start] == 0) {
        ++start;
    }
    const std::size_t count = 4 - start;
    destination[0] = static_cast<std::uint8_t>(0x80 | count);
    for (std::size_t i = 0; i < count; ++i) {
        destination[1 + i] = bytes[start + i];
    }
    return 1 + count;
}

void encode_buffer::sort_tlv_contents(std::size_t content_start, std::size_t content_length) {
    const std::size_t end = content_start + content_length;
    std::vector<std::pair<std::size_t, std::size_t>> ranges;
    ranges.reserve(8);
    std::size_t offset = content_start;
    while (offset < end) {
        const std::size_t tlv_start = offset;
        offset = get_tlv_end(buffer_, offset, end);
        ranges.emplace_back(tlv_start, offset - tlv_start);
    }
    if (ranges.size() <= 1) {
        return;
    }

    std::sort(ranges.begin(), ranges.end(), [this](const auto& left, const auto& right) {
        const auto* a = buffer_.data() + left.first;
        const auto* b = buffer_.data() + right.first;
        const std::size_t n = std::min(left.second, right.second);
        const int cmp = std::memcmp(a, b, n);
        if (cmp != 0) {
            return cmp < 0;
        }
        return left.second < right.second;
    });

    bool already_sorted = true;
    for (std::size_t i = 1; i < ranges.size(); ++i) {
        if (ranges[i].first < ranges[i - 1].first) {
            already_sorted = false;
            break;
        }
    }
    if (already_sorted) {
        return;
    }

    std::vector<std::uint8_t> rented(content_length);
    std::size_t write_offset = 0;
    for (const auto& [tlv_start, tlv_length] : ranges) {
        std::memcpy(rented.data() + write_offset, buffer_.data() + tlv_start, tlv_length);
        write_offset += tlv_length;
    }
    std::memcpy(buffer_.data() + content_start, rented.data(), content_length);
}

std::size_t encode_buffer::get_tlv_end(
    const std::vector<std::uint8_t>& data,
    std::size_t offset,
    std::size_t end) {
    if (offset >= end) {
        throw exception("Unexpected end of ASN.1 data.");
    }
    const std::uint8_t first = data[offset++];
    if ((first & 0x1F) == 0x1F) {
        std::uint8_t current = 0;
        do {
            if (offset >= end) {
                throw exception("Unexpected end of ASN.1 data.");
            }
            current = data[offset++];
        } while ((current & 0x80) != 0);
    }
    if (offset >= end) {
        throw exception("Unexpected end of ASN.1 data.");
    }
    const std::uint8_t first_length_octet = data[offset++];
    if (first_length_octet == 0x80) {
        throw exception("Indefinite length is not allowed when sorting SET OF for DER.");
    }
    std::size_t length = 0;
    if ((first_length_octet & 0x80) == 0) {
        length = first_length_octet;
    } else {
        const std::size_t count = first_length_octet & 0x7F;
        if (count == 0 || count > 4 || offset + count > end) {
            throw exception("Unsupported length form.");
        }
        for (std::size_t i = 0; i < count; ++i) {
            length = (length << 8) | data[offset++];
        }
    }
    if (length > end - offset) {
        throw exception("Length exceeds buffer.");
    }
    return offset + length;
}

void encode_buffer::write_tag(const tag& t) {
    std::uint8_t first = static_cast<std::uint8_t>(
        (static_cast<int>(t.tag_class_value()) << 6) | (t.constructed() ? 0x20 : 0));
    if (t.number() < 31) {
        ensure_additional_capacity(1);
        buffer_[length_++] = static_cast<std::uint8_t>(first | t.number());
        return;
    }

    ensure_additional_capacity(6);
    buffer_[length_++] = static_cast<std::uint8_t>(first | 0x1F);
    int number = t.number();
    std::uint8_t temporary[5];
    int count = 0;
    temporary[count++] = static_cast<std::uint8_t>(number & 0x7F);
    number >>= 7;
    while (number > 0) {
        temporary[count++] = static_cast<std::uint8_t>((number & 0x7F) | 0x80);
        number >>= 7;
    }
    for (int i = count - 1; i >= 0; --i) {
        buffer_[length_++] = temporary[i];
    }
}

void encode_buffer::write_length(std::size_t length) {
    std::uint8_t encoded[max_definite_length_bytes];
    const std::size_t size = encode_definite_length(length, encoded);
    write_raw(std::span<const std::uint8_t>(encoded, size));
}

void encode_buffer::ensure_additional_capacity(std::size_t additional) {
    const std::size_t required = length_ + additional;
    if (required <= buffer_.size()) {
        return;
    }
    std::size_t new_size = buffer_.size();
    while (new_size < required) {
        new_size = new_size < 1024 ? new_size * 2 : new_size + (new_size / 2);
    }
    buffer_.resize(new_size);
}

} // namespace asn1kit::detail
