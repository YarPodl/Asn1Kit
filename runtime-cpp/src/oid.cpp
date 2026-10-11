// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/oid.hpp"

#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <cstring>
#include <limits>

namespace asn1kit {
namespace {

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
