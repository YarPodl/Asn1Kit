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

void ensure_expected_tag(std::size_t tlv_start, const tag& actual, const tag& expected) {
    if (!actual.matches_ignore_constructed(expected)) {
        throw exception(
            "Expected tag " + expected.to_string() + ", found " + actual.to_string() + ".",
            tlv_start);
    }
}

std::vector<bytes> collect_octet_like_segments(
    decode_cursor& parent,
    bytes constructed_contents,
    const tag& segment_tag,
    std::size_t& total_length) {
    auto nested = parent.create_nested(std::move(constructed_contents));
    std::vector<bytes> segments;
    std::size_t length = 0;
    while (!nested.eof()) {
        auto segment = constructed_decoder::read_octet_like(nested, segment_tag);
        if (length > (std::numeric_limits<std::size_t>::max() - segment.size())) {
            throw exception("Constructed value is too large.", nested.absolute_offset());
        }
        length += segment.size();
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
    std::size_t total = 0;
    auto segments = collect_octet_like_segments(parent, std::move(constructed_contents), segment_tag, total);
    if (total == 0) {
        return bytes::from_vector({});
    }
    std::vector<std::uint8_t> result(total);
    copy_segments(segments, result);
    return bytes::from_vector(std::move(result));
}

bool try_copy_concat_octet_like(
    decode_cursor& parent,
    bytes constructed_contents,
    const tag& segment_tag,
    std::span<std::uint8_t> destination,
    std::size_t& bytes_written) {
    std::size_t total = 0;
    auto segments = collect_octet_like_segments(parent, std::move(constructed_contents), segment_tag, total);
    if (destination.size() < total) {
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
            throw exception(
                "Only the last BIT STRING segment may have unused bits.",
                nested.absolute_offset());
        }
        unused_bits = segment.unused_bits();
        segments.push_back(std::move(segment));
    }
    if (segments.empty()) {
        throw exception("Constructed BIT STRING has no segments.", nested.absolute_offset());
    }

    std::size_t total = 0;
    for (const auto& segment : segments) {
        if (total > (std::numeric_limits<std::size_t>::max() - segment.span().size())) {
            throw exception("Constructed BIT STRING is too large.", nested.absolute_offset());
        }
        total += segment.span().size();
    }

    std::vector<std::uint8_t> concatenated;
    if (total > 0) {
        concatenated.resize(total);
        std::size_t offset = 0;
        for (const auto& segment : segments) {
            if (!segment.span().empty()) {
                std::memcpy(concatenated.data() + offset, segment.span().data(), segment.span().size());
                offset += segment.span().size();
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
    const std::size_t tlv_start = cursor.absolute_offset();
    auto tlv = cursor.read_tlv();
    ensure_expected_tag(tlv_start, tlv.tag_value, expected);
    if (!tlv.tag_value.constructed()) {
        return tlv.contents;
    }
    return concat_octet_like(cursor, std::move(tlv.contents), expected.as_primitive());
}

bool constructed_decoder::try_read_octet_like(
    decode_cursor& cursor,
    const tag& expected,
    std::span<std::uint8_t> destination,
    std::size_t& bytes_written) {
    const std::size_t tlv_start = cursor.absolute_offset();
    auto tlv = cursor.read_tlv();
    ensure_expected_tag(tlv_start, tlv.tag_value, expected);
    if (!tlv.tag_value.constructed()) {
        if (destination.size() < tlv.contents.size()) {
            bytes_written = 0;
            return false;
        }
        if (!tlv.contents.empty()) {
            std::memcpy(destination.data(), tlv.contents.data(), tlv.contents.size());
        }
        bytes_written = tlv.contents.size();
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
    const std::size_t tlv_start = cursor.absolute_offset();
    auto tlv = cursor.read_tlv();
    ensure_expected_tag(tlv_start, tlv.tag_value, expected);
    if (!tlv.tag_value.constructed()) {
        return bit_string::parse_primitive(std::move(tlv.contents), reject_trailing_bits);
    }
    return read_constructed_bit_string(cursor, std::move(tlv.contents), reject_trailing_bits);
}

} // namespace asn1kit::detail
