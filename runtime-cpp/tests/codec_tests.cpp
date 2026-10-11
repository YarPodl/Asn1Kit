// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/codec.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <type_traits>
#include <vector>

using namespace asn1kit;

TEST(OpenTyped, Int32RoundTripThroughAny) {
    const any encoded = encode_open(std::int32_t{42});
    EXPECT_EQ(encoded.to_vector(), (std::vector<std::uint8_t>{0x02, 0x01, 0x2A}));
    EXPECT_EQ(decode_open<std::int32_t>(encoded, encoding::der), 42);
}

TEST(OpenTyped, NullRoundTrip) {
    const any encoded = encode_open(null_value::value());
    EXPECT_EQ(encoded.to_vector(), (std::vector<std::uint8_t>{0x05, 0x00}));
    EXPECT_EQ(decode_open<null_value>(encoded, encoding::der), null_value::value());
}

struct skip_value {
    static tag default_tag() { return tag::integer; }
    static skip_value decode(reader&, const tag&) { return {}; }
    void encode(writer&, const tag&) const {}
};

TEST(OpenTyped, RejectsTrailingOctets) {
    const any encoded = encode_open(std::int32_t{1});
    EXPECT_THROW((void)decode_open<skip_value>(encoded, encoding::der), exception);
}

TEST(OpenTyped, KnownMismatchRejects) {
    const any encoded = encode_open(null_value::value());
    EXPECT_THROW((void)decode_open<std::int32_t>(encoded, encoding::der), exception);
}

TEST(OpenTyped, ContainedTypedRoundTrip) {
    const auto wrapped = encode_contained(std::int32_t{7});
    EXPECT_TRUE(wrapped.has_value());
    EXPECT_EQ(decode_contained<std::int32_t>(wrapped), 7);

    const any inner = encode_open(std::int32_t{9});
    const auto opaque = contained<any>::from_encoded(bytes::copy_from(inner.span()));
    EXPECT_FALSE(opaque.has_value());
    EXPECT_EQ(decode_contained<std::int32_t>(opaque), 9);
}

TEST(OpenTyped, ContainedRejectsNonAligned) {
    const any inner = encode_open(std::int32_t{1});
    const auto misaligned = contained<any>::from_encoded(bytes::copy_from(inner.span()), 1);
    EXPECT_THROW((void)decode_contained<std::int32_t>(misaligned), exception);
}

TEST(OpenTyped, DecodeEachEmptyAndValues) {
    EXPECT_TRUE(decode_each<std::int32_t>(std::vector<any>{}).empty());
    const std::vector<any> raw{encode_open(std::int32_t{1}), encode_open(std::int32_t{2})};
    EXPECT_EQ(decode_each<std::int32_t>(raw), (std::vector<std::int32_t>{1, 2}));
    EXPECT_EQ(encode_each(std::vector<std::int32_t>{3, 4}).size(), 2u);
}

TEST(OpenTyped, Utf8StringFormInType) {
    const any encoded = encode_open(asn_string<string_form::utf8>{"hi"});
    EXPECT_EQ(decode_open<asn_string<string_form::utf8>>(encoded, encoding::der).value, "hi");
    EXPECT_THROW(
        (void)decode_open<asn_string<string_form::printable>>(encoded, encoding::der), exception);
}

TEST(OpenTyped, PrintableVsIa5AreDifferentTypes) {
    static_assert(!std::is_same_v<
                  asn_string<string_form::printable>, asn_string<string_form::ia5>>);
    const any printable = encode_open(asn_string<string_form::printable>{"AB"});
    EXPECT_EQ(
        decode_open<asn_string<string_form::printable>>(printable, encoding::der).value, "AB");
}
