// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/exception.hpp"

#include <cstddef>
#include <cstdint>
#include <limits>
#include <span>
#include <string>
#include <string_view>

namespace asn1kit::detail {

constexpr std::string_view normalize_oid(std::string_view oid_text) {
    while (!oid_text.empty() && (oid_text.front() == ' ' || oid_text.front() == '\t')) {
        oid_text.remove_prefix(1);
    }
    while (!oid_text.empty() && (oid_text.back() == ' ' || oid_text.back() == '\t')) {
        oid_text.remove_suffix(1);
    }
    if (oid_text.empty()) {
        throw exception("OID is empty.");
    }
    return oid_text;
}

constexpr int count_arcs(std::string_view span) {
    int count = 1;
    for (char c : span) {
        if (c == '.') {
            ++count;
        }
    }
    return count;
}

constexpr int parse_next_arc(std::string_view span, std::size_t& index, std::string_view oid_text) {
    if (index >= span.size()) {
        throw exception("OID '" + std::string(oid_text) + "' has an invalid component.");
    }
    const std::size_t start = index;
    while (index < span.size() && span[index] != '.') {
        ++index;
    }
    if (index == start) {
        throw exception("OID '" + std::string(oid_text) + "' has an invalid component.");
    }
    int arc = 0;
    for (std::size_t i = start; i < index; ++i) {
        if (span[i] < '0' || span[i] > '9') {
            throw exception("OID '" + std::string(oid_text) + "' has an invalid component.");
        }
        if (arc > (std::numeric_limits<int>::max() - (span[i] - '0')) / 10) {
            throw exception("OID '" + std::string(oid_text) + "' has an invalid component.");
        }
        arc = arc * 10 + (span[i] - '0');
    }
    if (index < span.size()) {
        ++index; // skip '.'
    }
    return arc;
}

constexpr std::size_t encode_base128(std::span<std::uint8_t> destination, int value) {
    if (value < 0) {
        throw exception("OID arc must not be negative.");
    }
    std::uint8_t temp[5]{};
    int count = 0;
    temp[count++] = static_cast<std::uint8_t>(value & 0x7F);
    value >>= 7;
    while (value > 0) {
        temp[count++] = static_cast<std::uint8_t>((value & 0x7F) | 0x80);
        value >>= 7;
    }
    for (int i = 0; i < count; ++i) {
        destination[static_cast<std::size_t>(i)] = temp[count - 1 - i];
    }
    return static_cast<std::size_t>(count);
}

constexpr std::size_t get_encode_contents_max_length(std::string_view dotted) {
    const auto span = normalize_oid(dotted);
    const int arc_count = count_arcs(span);
    if (arc_count < 2) {
        throw exception("OID '" + std::string(dotted) + "' is too short.");
    }
    return static_cast<std::size_t>(arc_count) * 5;
}

constexpr std::size_t encode_contents(std::string_view dotted, std::span<std::uint8_t> destination) {
    const auto span = normalize_oid(dotted);
    const int arc_count = count_arcs(span);
    if (arc_count < 2) {
        throw exception("OID '" + std::string(dotted) + "' is too short.");
    }
    const std::size_t max_bytes = static_cast<std::size_t>(arc_count) * 5;
    if (destination.size() < max_bytes) {
        throw exception("OID encode destination is too small.");
    }
    std::size_t index = 0;
    const int arc0 = parse_next_arc(span, index, dotted);
    const int arc1 = parse_next_arc(span, index, dotted);
    if (arc0 > 2) {
        throw exception("OID '" + std::string(dotted) + "' first arc must be 0, 1, or 2.");
    }
    if (arc0 < 2 && arc1 >= 40) {
        throw exception(
            "OID '" + std::string(dotted) + "' second arc must be in 0..39 when first arc is "
            + std::to_string(arc0) + ".");
    }
    const long long first = 40LL * arc0 + arc1;
    if (first > std::numeric_limits<int>::max()) {
        throw exception("OID '" + std::string(dotted) + "' first subidentifier is too large.");
    }
    std::size_t written = encode_base128(destination, static_cast<int>(first));
    for (int a = 2; a < arc_count; ++a) {
        written += encode_base128(destination.subspan(written), parse_next_arc(span, index, dotted));
    }
    return written;
}

} // namespace asn1kit::detail
