// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/oid.hpp"

#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <cstring>
#include <limits>

namespace asn1kit {
namespace {

std::string_view normalize_oid(std::string_view oid_text) {
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

int count_arcs(std::string_view span) {
    int count = 1;
    for (char c : span) {
        if (c == '.') {
            ++count;
        }
    }
    return count;
}

int parse_next_arc(std::string_view span, std::size_t& index, std::string_view oid_text) {
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

std::size_t encode_base128(std::span<std::uint8_t> destination, int value) {
    if (value < 0) {
        throw exception("OID arc must not be negative.");
    }
    std::uint8_t temp[5];
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

std::string format_dotted(std::span<const std::uint8_t> contents) {
    if (contents.empty()) {
        throw exception("OBJECT IDENTIFIER is empty.");
    }
    std::size_t i = 0;
    const int first = oid::read_arc(contents, i, false);
    int arc0 = 0;
    int arc1 = 0;
    if (first < 40) {
        arc0 = 0;
        arc1 = first;
    } else if (first < 80) {
        arc0 = 1;
        arc1 = first - 40;
    } else {
        arc0 = 2;
        arc1 = first - 80;
    }
    std::string result = std::to_string(arc0) + "." + std::to_string(arc1);
    while (i < contents.size()) {
        result.push_back('.');
        result += std::to_string(oid::read_arc(contents, i, false));
    }
    return result;
}

} // namespace

oid oid::from_contents(bytes contents) {
    if (contents.empty()) {
        throw exception("OBJECT IDENTIFIER is empty.");
    }
    return oid(std::move(contents));
}

oid oid::copy_from(std::span<const std::uint8_t> contents) {
    if (contents.empty()) {
        throw exception("OBJECT IDENTIFIER is empty.");
    }
    return oid(bytes::copy_from(contents));
}

oid oid::parse(std::string_view dotted) {
    return copy_from(encode_contents(dotted));
}

std::size_t oid::get_encode_contents_max_length(std::string_view dotted) {
    const auto span = normalize_oid(dotted);
    const int arc_count = count_arcs(span);
    if (arc_count < 2) {
        throw exception("OID '" + std::string(dotted) + "' is too short.");
    }
    return static_cast<std::size_t>(arc_count) * 5;
}

std::size_t oid::encode_contents(std::string_view dotted, std::span<std::uint8_t> destination) {
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

std::vector<std::uint8_t> oid::encode_contents(std::string_view dotted) {
    const std::size_t max_bytes = get_encode_contents_max_length(dotted);
    std::vector<std::uint8_t> scratch(max_bytes);
    const std::size_t written = encode_contents(dotted, scratch);
    scratch.resize(written);
    return scratch;
}

std::string oid::to_string() const {
    return format_dotted(span());
}

int oid::read_arc(std::span<const std::uint8_t> contents, std::size_t& offset, bool reject_overlong) {
    if (offset >= contents.size()) {
        throw exception("Truncated OBJECT IDENTIFIER.");
    }
    int value = 0;
    bool first = true;
    std::uint8_t b = 0;
    do {
        if (offset >= contents.size()) {
            throw exception("Truncated OBJECT IDENTIFIER.");
        }
        b = contents[offset++];
        if (first) {
            if (reject_overlong && b == 0x80) {
                throw exception("OID base-128 encoding is overlong.");
            }
            first = false;
        }
        if (value > (std::numeric_limits<int>::max() >> 7)) {
            throw exception("OBJECT IDENTIFIER arc is too large.");
        }
        value = (value << 7) | (b & 0x7F);
    } while ((b & 0x80) != 0);
    return value;
}

void oid::encode(writer& w, const oid& value, const tag& t) {
    w.write_object_identifier(t, value);
}

void oid::encode(writer& w, std::string_view dotted, const tag& t) {
    w.write_object_identifier(t, dotted);
}

oid oid::decode(reader& r, const tag& t) {
    return r.read_oid(t);
}

std::string oid::decode_string(reader& r, const tag& t) {
    return r.read_object_identifier(t);
}

bool operator==(const oid& a, const oid& b) {
    const auto left = a.span();
    const auto right = b.span();
    if (left.size() != right.size()) {
        return false;
    }
    return std::memcmp(left.data(), right.data(), left.size()) == 0;
}

} // namespace asn1kit
