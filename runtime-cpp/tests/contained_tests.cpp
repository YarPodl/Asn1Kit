// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/contained.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <stdexcept>
#include <string_view>
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

TEST(Contained, WritesTypedContentAndRetainsDecodedOctets) {
    writer w(encoding::der);
    w.write_contained(
        tag::octet_string,
        false,
        contained<std::int32_t>::from_value(42),
        [](writer& inner, const std::int32_t& v) { inner.write_integer(tag::integer, v); });
    EXPECT_EQ(w.encode(), hex_bytes("040302012A"));

    reader r(w.encode(), encoding::der);
    const auto value = r.read_contained<std::int32_t>(
        tag::octet_string,
        false,
        true,
        0,
        [](reader& inner, int) { return inner.read_int32(tag::integer); });
    EXPECT_EQ(value.value(), 42);
    EXPECT_EQ(value.contents().to_vector(), hex_bytes("02012A"));
    r.throw_if_not_empty();
}

TEST(Contained, ReadsBerConstructedContentAndOpaqueOctets) {
    reader constructed(hex_bytes("24800401020402012A0000"), encoding::ber);
    EXPECT_EQ(
        constructed
            .read_contained<std::int32_t>(
                tag::octet_string,
                false,
                true,
                0,
                [](reader& inner, int) { return inner.read_int32(tag::integer); })
            .value(),
        42);

    reader opaque_reader(hex_bytes("0402FFFF"), encoding::ber);
    const auto opaque = opaque_reader.read_contained<std::int32_t>(
        tag::octet_string,
        false,
        false,
        0,
        [](reader& inner, int) { return inner.read_int32(tag::integer); });
    EXPECT_FALSE(opaque.has_value());
    EXPECT_EQ(opaque.contents().to_vector(), hex_bytes("FFFF"));
}

TEST(Contained, RejectsCorruptKnownContent) {
    for (const auto& hex : {"04020201", "040402010100", "040105"}) {
        reader r(hex_bytes(hex), encoding::ber);
        EXPECT_THROW(
            (void)r.read_contained<std::int32_t>(
                tag::octet_string,
                false,
                true,
                0,
                [](reader& inner, int) { return inner.read_int32(tag::integer); }),
            exception);
    }
}

TEST(Contained, BitStringContentMustBeOctetAlignedWhenKnown) {
    writer w(encoding::der);
    w.write_contained(
        tag::bit_string,
        true,
        contained<std::int32_t>::from_value(42),
        [](writer& inner, const std::int32_t& v) { inner.write_integer(tag::integer, v); });
    EXPECT_EQ(w.encode(), hex_bytes("03040002012A"));

    reader r(hex_bytes("030203A0"), encoding::ber);
    EXPECT_THROW(
        (void)r.read_contained<std::int32_t>(
            tag::bit_string,
            true,
            true,
            0,
            [](reader& inner, int) { return inner.read_int32(tag::integer); }),
        exception);
}

TEST(Contained, OpaqueBitStringRoundTripsUnusedBits) {
    const auto opaque = contained<std::int32_t>::from_encoded(bytes::copy_from(hex_bytes("A0")), 2);
    writer w(encoding::der);
    w.write_contained(
        tag::bit_string,
        true,
        opaque,
        [](writer&, const std::int32_t&) { FAIL() << "opaque encode must not invoke callback"; });
    EXPECT_EQ(w.encode(), hex_bytes("030202A0"));
}
