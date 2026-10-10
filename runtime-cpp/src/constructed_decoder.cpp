// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "constructed_decoder.hpp"

#include "asn1kit/exception.hpp"
#include "text_codec.hpp"

#include <cstring>
#include <limits>
#include <vector>

namespace asn1kit::detail {
namespace {

void ensure_expected_tag(const tag& actual, const tag& expected) {
    if (!actual.matches_ignore_constructed(expected)) {
        throw exception("Expected tag " + expected.to_string() + ", found " + actual.to_string() + ".");
    }
}

std::vector<bytes> collect_octet_like_segments(
    decode_cursor& parent,
    bytes constructed_contents,
    const tag& segment_tag,
    int& total_length) {
    auto nested = parent.create_nested(std::move(constructed_contents));
    std::vector<bytes> segments;
    int length = 0;
    while (!nested.eof()) {
        auto segment = constructed_decoder::read_octet_like(nested, segment_tag);
        if (segment.size() > static_cast<std::size_t>(std::numeric_limits<int>::max() - length)) {
            throw exception("Constructed value exceeds Int32.");
        }
        length += static_cast<int>(segment.size());
        segments.push_back(std::move(segment));
    }
    total_length = length;
    return segments;
}

void copy_segments(const std::vector<bytes>& segments, std::span<std::uint8_t> destination) {
    std::size_t offset = 0;
    for (const auto& segment : segments) {
        if (!segment.empty()) {
            std::memcpy(destination.data() + offset, segment.data(), segment.size());
            offset += segment.size();
        }
    }
}

bytes concat_octet_like(decode_cursor& parent, bytes constructed_contents, const tag& segment_tag) {
    int total = 0;
    auto segments = collect_octet_like_segments(parent, std::move(constructed_contents), segment_tag, total);
    if (total == 0) {
        return bytes::from_vector({});
    }
    std::vector<std::uint8_t> result(static_cast<std::size_t>(total));
    copy_segments(segments, result);
    return bytes::from_vector(std::move(result));
}

bool try_copy_concat_octet_like(
    decode_cursor& parent,
    bytes constructed_contents,
    const tag& segment_tag,
    std::span<std::uint8_t> destination,
    int& bytes_written) {
    int total = 0;
    auto segments = collect_octet_like_segments(parent, std::move(constructed_contents), segment_tag, total);
    if (static_cast<int>(destination.size()) < total) {
        bytes_written = 0;
        return false;
    }
    copy_segments(segments, destination);
    bytes_written = total;
    return true;
}

bit_string read_constructed_bit_string(
    decode_cursor& parent,
    bytes constructed_contents,
    bool reject_trailing_bits) {
    auto nested = parent.create_nested(std::move(constructed_contents));
    std::vector<bit_string> segments;
    int unused_bits = 0;
    while (!nested.eof()) {
        auto segment = constructed_decoder::read_bit_string(nested, tag::bit_string, reject_trailing_bits);
        if (!segments.empty() && unused_bits != 0) {
            throw exception("Only the last BIT STRING segment may have unused bits.");
        }
        unused_bits = segment.unused_bits();
        segments.push_back(std::move(segment));
    }
    if (segments.empty()) {
        throw exception("Constructed BIT STRING has no segments.");
    }

    int total = 0;
    for (const auto& segment : segments) {
        if (segment.span().size() > static_cast<std::size_t>(std::numeric_limits<int>::max() - total)) {
            throw exception("Constructed BIT STRING exceeds Int32.");
        }
        total += static_cast<int>(segment.span().size());
    }

    std::vector<std::uint8_t> concatenated;
    if (total > 0) {
        concatenated.resize(static_cast<std::size_t>(total));
        int offset = 0;
        for (const auto& segment : segments) {
            if (!segment.span().empty()) {
                std::memcpy(
                    concatenated.data() + offset,
                    segment.span().data(),
                    segment.span().size());
                offset += static_cast<int>(segment.span().size());
            }
        }
    }

    bit_string value(bytes::from_vector(std::move(concatenated)), unused_bits);
    if (reject_trailing_bits) {
        text_codec::ensure_trailing_bits_zero(value.span(), value.unused_bits());
    }
    return value;
}

} // namespace

bytes constructed_decoder::read_octet_like(decode_cursor& cursor, const tag& expected) {
    auto tlv = cursor.read_tlv();
    ensure_expected_tag(tlv.tag_value, expected);
    if (!tlv.tag_value.constructed()) {
        return tlv.contents;
    }
    return concat_octet_like(cursor, std::move(tlv.contents), expected.as_primitive());
}

bool constructed_decoder::try_read_octet_like(
    decode_cursor& cursor,
    const tag& expected,
    std::span<std::uint8_t> destination,
    int& bytes_written) {
    auto tlv = cursor.read_tlv();
    ensure_expected_tag(tlv.tag_value, expected);
    if (!tlv.tag_value.constructed()) {
        if (destination.size() < tlv.contents.size()) {
            bytes_written = 0;
            return false;
        }
        if (!tlv.contents.empty()) {
            std::memcpy(destination.data(), tlv.contents.data(), tlv.contents.size());
        }
        bytes_written = static_cast<int>(tlv.contents.size());
        return true;
    }
    return try_copy_concat_octet_like(
        cursor,
        std::move(tlv.contents),
        expected.as_primitive(),
        destination,
        bytes_written);
}

bit_string constructed_decoder::read_bit_string(
    decode_cursor& cursor,
    const tag& expected,
    bool reject_trailing_bits) {
    auto tlv = cursor.read_tlv();
    ensure_expected_tag(tlv.tag_value, expected);
    if (!tlv.tag_value.constructed()) {
        return bit_string::parse_primitive(std::move(tlv.contents), reject_trailing_bits);
    }
    return read_constructed_bit_string(cursor, std::move(tlv.contents), reject_trailing_bits);
}

} // namespace asn1kit::detail
