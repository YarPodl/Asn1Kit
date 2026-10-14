// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "constructed_decoder.hpp"

#include "asn1kit/exception.hpp"
#include "text_codec.hpp"

#include <cstring>
#include <string>
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

void ensure_recursion_depth(
    const reader_options& options,
    std::size_t depth,
    std::size_t offset) {
    if (depth >= options.max_recursion_depth) {
        throw exception(
            "ASN.1 recursion depth exceeds limit " + std::to_string(options.max_recursion_depth) + ".",
            offset);
    }
}

void add_materialized_length(
    const reader_options& options,
    std::size_t& total,
    std::size_t length,
    std::size_t offset) {
    if (length > options.max_materialized_constructed_value_bytes - total) {
        throw exception(
            "Constructed value exceeds materialization limit "
                + std::to_string(options.max_materialized_constructed_value_bytes) + " bytes.",
            offset);
    }
    total += length;
}

void collect_octet_like_segments(
    decode_cursor& parent,
    bytes constructed_contents,
    const tag& segment_tag,
    std::vector<bytes>& segments,
    std::size_t& total_length,
    std::size_t depth) {
    auto nested = parent.create_nested(std::move(constructed_contents));
    while (!nested.eof()) {
        const auto tlv_start = nested.absolute_offset();
        auto tlv = nested.read_tlv();
        ensure_expected_tag(tlv_start, tlv.tag_value, segment_tag);
        if (tlv.tag_value.constructed()) {
            ensure_recursion_depth(parent.options(), depth, tlv_start);
            collect_octet_like_segments(
                parent,
                std::move(tlv.contents),
                segment_tag,
                segments,
                total_length,
                depth + 1);
            continue;
        }
        add_materialized_length(parent.options(), total_length, tlv.contents.size(), tlv_start);
        segments.push_back(std::move(tlv.contents));
    }
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
    std::vector<bytes> segments;
    std::size_t total = 0;
    collect_octet_like_segments(
        parent, std::move(constructed_contents), segment_tag, segments, total, 1);
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
    std::vector<bytes> segments;
    std::size_t total = 0;
    collect_octet_like_segments(
        parent, std::move(constructed_contents), segment_tag, segments, total, 1);
    if (destination.size() < total) {
        bytes_written = 0;
        return false;
    }
    copy_segments(segments, destination);
    bytes_written = total;
    return true;
}

void collect_bit_string_segments(
    decode_cursor& parent,
    bytes constructed_contents,
    bool reject_trailing_bits,
    std::vector<bit_string>& segments,
    std::size_t& total_length,
    std::size_t depth) {
    auto nested = parent.create_nested(std::move(constructed_contents));
    const auto initial_count = segments.size();
    while (!nested.eof()) {
        const auto tlv_start = nested.absolute_offset();
        auto tlv = nested.read_tlv();
        ensure_expected_tag(tlv_start, tlv.tag_value, tag::bit_string);
        if (tlv.tag_value.constructed()) {
            ensure_recursion_depth(parent.options(), depth, tlv_start);
            collect_bit_string_segments(
                parent,
                std::move(tlv.contents),
                reject_trailing_bits,
                segments,
                total_length,
                depth + 1);
            continue;
        }
        if (!segments.empty() && segments.back().unused_bits() != 0) {
            throw exception(
                "Only the last BIT STRING segment may have unused bits.",
                tlv_start);
        }
        auto segment = bit_string::parse_primitive(std::move(tlv.contents), reject_trailing_bits);
        add_materialized_length(parent.options(), total_length, segment.span().size(), tlv_start);
        segments.push_back(std::move(segment));
    }
    if (segments.size() == initial_count) {
        throw exception("Constructed BIT STRING has no segments.", nested.absolute_offset());
    }
}

bit_string read_constructed_bit_string(
    decode_cursor& parent,
    bytes constructed_contents,
    bool reject_trailing_bits) {
    std::vector<bit_string> segments;
    std::size_t total = 0;
    collect_bit_string_segments(
        parent, std::move(constructed_contents), reject_trailing_bits, segments, total, 1);

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

    bit_string value(bytes::from_vector(std::move(concatenated)), segments.back().unused_bits());
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
