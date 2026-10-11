// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/oid.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

using namespace asn1kit;

namespace {

constexpr auto pkix = oid_literal<"1.3.6.1.5.5.7">();
static_assert(pkix.contents == std::array<std::uint8_t, 6>{0x2b, 6, 1, 5, 5, 7});
static_assert(pkix.span().size() == 6 && pkix.span()[0] == 0x2b);
static_assert(std::string_view(pkix.text()) == "1.3.6.1.5.5.7");
static_assert(oid_literal<"0.0">().contents == std::array<std::uint8_t, 1>{0});
static_assert(oid_literal<"1.39">().contents == std::array<std::uint8_t, 1>{79});
static_assert(oid_literal<"2.999.128">().contents == std::array<std::uint8_t, 4>{0x88, 0x37, 0x81, 0});
static_assert(oid_literal<"2.2147483567.2147483647">().contents ==
    std::array<std::uint8_t, 10>{0x87, 0xff, 0xff, 0xff, 0x7f, 0x87, 0xff, 0xff, 0xff, 0x7f});
static_assert(oid_literal<" \t1.3.6.1.5.5.7\t ">().contents == pkix.contents);

TEST(OidLiteral, StaticStorageAndDerRoundTrip) {
    const auto value = pkix();
    EXPECT_EQ(value.span().data(), pkix.span().data());
    EXPECT_EQ(value.span().data(), pkix().span().data());
    EXPECT_FALSE(value.contents().owner());
    EXPECT_EQ(value, oid::parse("1.3.6.1.5.5.7"));
    EXPECT_EQ(value.to_string(), "1.3.6.1.5.5.7");

    writer w(encoding::der);
    w.write_object_identifier(tag::object_identifier, value);
    EXPECT_EQ(w.encode(), (std::vector<std::uint8_t>{6, 6, 0x2b, 6, 1, 5, 5, 7}));
    reader r(bytes::from_vector(w.encode()), encoding::der);
    EXPECT_EQ(r.read_oid(), value);
    r.throw_if_not_empty();
}

TEST(OidLiteral, TemporaryConstantReturnsStaticStorage) {
    const auto value = oid_literal<"2.999.128">().get();
    EXPECT_EQ(value.to_string(), "2.999.128");
    EXPECT_EQ(value, oid::parse("2.999.128"));
    EXPECT_EQ(value.span().data(), oid_literal<"2.999.128">().span().data());
    const auto copy = value.clone();
    EXPECT_EQ(copy, value);
    EXPECT_NE(copy.span().data(), value.span().data());
}

TEST(OidLiteral, SharedEncoderRejectsInvalidArcsAndShortBuffer) {
    std::array<std::uint8_t, 128> buffer{};
    for (const auto text : {"", "1", "3.0", "1.40", "1.2.", "1..2", "1.-2", "1.x",
                            "2.2147483568", "2.0.2147483648"}) {
        EXPECT_THROW((void)oid::encode_contents(text, buffer), exception) << text;
    }
    std::array<std::uint8_t, 1> short_buffer{};
    EXPECT_THROW((void)oid::encode_contents("1.2.3", short_buffer), exception);
    EXPECT_EQ(oid_literal<"2.2147483567.2147483647">().get(), oid::parse("2.2147483567.2147483647"));
}

} // namespace
