// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/bit_string.hpp"

#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"
#include "text_codec.hpp"

#include <cstring>

namespace asn1kit {

bit_string::bit_string(bytes data, int unused_bits) {
    if (unused_bits < 0 || unused_bits > 7) {
        throw exception("BIT STRING unusedBits must be in 0..7.");
    }
    if (data.empty()) {
        if (unused_bits != 0) {
            throw exception("Empty BIT STRING must have unusedBits = 0.");
        }
        bytes_ = {};
        unused_bits_ = 0;
        return;
    }
    bytes_ = std::move(data);
    unused_bits_ = unused_bits;
}

bit_string bit_string::copy_from(std::span<const std::uint8_t> data, int unused_bits) {
    if (data.empty()) {
        return bit_string({}, unused_bits);
    }
    return bit_string(bytes::copy_from(data), unused_bits);
}

bit_string bit_string::parse_primitive(bytes contents, bool reject_trailing_bits) {
    if (contents.empty()) {
        throw exception("BIT STRING contents must not be empty.");
    }
    const int unused = contents[0];
    if (unused > 7) {
        throw exception("BIT STRING unusedBits must be in 0..7.");
    }
    if (contents.size() == 1) {
        if (unused != 0) {
            throw exception("Empty BIT STRING must have unusedBits = 0.");
        }
        return {};
    }
    auto payload = contents.slice(1);
    if (reject_trailing_bits) {
        detail::text_codec::ensure_trailing_bits_zero(payload.span(), unused);
    }
    return bit_string(std::move(payload), unused);
}

void bit_string::encode(writer& w, const bit_string& value, const tag& t) {
    w.write_bit_string(t, value);
}

bit_string bit_string::decode(reader& r, const tag& t) {
    return r.read_bit_string(t);
}

bool operator==(const bit_string& a, const bit_string& b) {
    if (a.unused_bits() != b.unused_bits()) {
        return false;
    }
    const auto left = a.span();
    const auto right = b.span();
    if (left.size() != right.size()) {
        return false;
    }
    return std::memcmp(left.data(), right.data(), left.size()) == 0;
}

} // namespace asn1kit
