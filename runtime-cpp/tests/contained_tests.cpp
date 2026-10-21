// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/codec.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <stdexcept>
#include <string_view>
#include <type_traits>
#include <vector>

using namespace asn1kit;

namespace {

std::vector<std::uint8_t> hex_bytes(std::string_view hex) {
    std::vector<std::uint8_t> result;
    result.reserve(hex.size() / 2);
    auto nibble = [](char c) -> int {
        if (c >= '0' && c <= '9') {
            return c - '0';
        }
        if (c >= 'A' && c <= 'F') {
            return c - 'A' + 10;
        }
        if (c >= 'a' && c <= 'f') {
            return c - 'a' + 10;
        }
        throw std::invalid_argument("hex");
    };
    for (std::size_t i = 0; i + 1 < hex.size(); i += 2) {
        result.push_back(static_cast<std::uint8_t>((nibble(hex[i]) << 4) | nibble(hex[i + 1])));
    }
    return result;
}

} // namespace

TEST(Containing, OctetStringTypedRoundTrip) {
    writer w(encoding::der);
    w.write_octet_string_containing(
        tag::octet_string,
        std::int32_t{42},
        [](writer& inner, const std::int32_t& v) { inner.write_integer(tag::integer, v); });
    EXPECT_EQ(w.encode(), hex_bytes("040302012A"));

    reader r(w.encode(), encoding::der);
    const auto value = r.read_octet_string_containing<std::int32_t>(
        tag::octet_string,
        [](reader& inner) { return inner.read_int32(tag::integer); });
    EXPECT_EQ(value, 42);
    r.throw_if_not_empty();
}

TEST(Containing, TypeDrivenOctetStringContaining) {
    writer w(encoding::der);
    w.write_octet_string_containing(
        tag::octet_string,
        std::int32_t{42},
        [](writer& inner, const std::int32_t& v) { inner.write_integer(tag::integer, v); });

    reader r(w.encode(), encoding::der);
    EXPECT_EQ(r.read_octet_string_containing<std::int32_t>(tag::octet_string), 42);
    r.throw_if_not_empty();
}

TEST(Containing, TypeDrivenBitStringContaining) {
    writer w(encoding::der);
    w.write_bit_string_containing(
        tag::bit_string,
        std::int32_t{7},
        [](writer& inner, const std::int32_t& v) { inner.write_integer(tag::integer, v); });

    reader r(w.encode(), encoding::der);
    EXPECT_EQ(r.read_bit_string_containing<std::int32_t>(tag::bit_string), 7);
    r.throw_if_not_empty();
}

TEST(Containing, ReadsBerConstructedContent) {
    reader constructed(hex_bytes("24800401020402012A0000"), encoding::ber);
    EXPECT_EQ(
        constructed
            .read_octet_string_containing<std::int32_t>(
                tag::octet_string,
                [](reader& inner) { return inner.read_int32(tag::integer); }),
        42);
}

TEST(Containing, OpaqueOctetsDoNotRequireAsn1Content) {
    reader opaque_reader(hex_bytes("0402FFFF"), encoding::ber);
    const auto opaque = opaque_reader.read_octet_string(tag::octet_string);
    EXPECT_EQ(opaque.to_vector(), hex_bytes("FFFF"));
}

TEST(Containing, RejectsCorruptKnownContent) {
    for (const auto& hex : {"04020201", "040402010100", "040105"}) {
        reader r(hex_bytes(hex), encoding::ber);
        EXPECT_THROW(
            (void)r.read_octet_string_containing<std::int32_t>(
                tag::octet_string,
                [](reader& inner) { return inner.read_int32(tag::integer); }),
            exception);
    }
}

TEST(Containing, BitStringContentMustBeOctetAlignedWhenKnown) {
    writer w(encoding::der);
    w.write_bit_string_containing(
        tag::bit_string,
        std::int32_t{42},
        [](writer& inner, const std::int32_t& v) { inner.write_integer(tag::integer, v); });
    EXPECT_EQ(w.encode(), hex_bytes("03040002012A"));

    reader r(hex_bytes("030203A0"), encoding::ber);
    EXPECT_THROW(
        (void)r.read_bit_string_containing<std::int32_t>(
            tag::bit_string,
            [](reader& inner) { return inner.read_int32(tag::integer); }),
        exception);
}

TEST(Containing, OpaqueBitStringRoundTripsUnusedBits) {
    const auto opaque = bit_string::copy_from(hex_bytes("A0"), 2);
    writer w(encoding::der);
    w.write_bit_string(tag::bit_string, opaque);
    EXPECT_EQ(w.encode(), hex_bytes("030202A0"));
}

TEST(Containing, ContainedSequenceOfVectorIsOpenTyped) {
    static_assert(open_typed<std::vector<std::int32_t>>);
    static_assert(open_typed<std::vector<oid>>);

    const std::vector<std::int32_t> values{1, 2, 3};
    // Contained payload bytes are the SEQUENCE OF INTEGER TLV (extnValue contents).
    const auto octets = encode_contained_octets(values);
    EXPECT_EQ(octets.to_vector(), hex_bytes("3009020101020102020103"));

    const auto decoded = decode_contained<std::vector<std::int32_t>>(octets);
    EXPECT_EQ(decoded, values);
}
