// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/any.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/reader_options.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <vector>

using namespace asn1kit;

TEST(Any, ReadAnyExposesPrimitiveContents) {
    writer w(encoding::der);
    w.write_integer(tag::integer, 1);
    const auto bytes = w.encode();

    reader r(bytes, encoding::der);
    const any value = r.read_any();
    EXPECT_EQ(value.tag_value(), tag::integer);
    EXPECT_EQ(value.contents().to_vector(), (std::vector<std::uint8_t>{0x01}));
    EXPECT_EQ(value.to_vector(), bytes);
    EXPECT_TRUE(r.eof());
}

TEST(Any, DerRoundTripsTagAndContents) {
    const any value = any::from_tag_and_contents(tag::integer, std::vector<std::uint8_t>{0x05});
    writer w(encoding::der);
    w.write_any(value);
    const auto bytes = w.encode();
    EXPECT_EQ(bytes, (std::vector<std::uint8_t>{0x02, 0x01, 0x05}));

    reader r(bytes, encoding::der);
    const any decoded = r.read_any();
    EXPECT_EQ(decoded.tag_value(), tag::integer);
    EXPECT_EQ(decoded.to_vector(), bytes);
    EXPECT_EQ(decoded.contents().to_vector(), (std::vector<std::uint8_t>{0x05}));
    EXPECT_TRUE(r.eof());
    EXPECT_EQ(decoded, value);

    writer rewrite(encoding::der);
    rewrite.write_any(decoded);
    EXPECT_EQ(rewrite.encode(), bytes);
}

TEST(Any, ReadAnyExpectedTagRejectsMismatch) {
    const std::vector<std::uint8_t> bytes{0x02, 0x01, 0x05};
    reader r(bytes, encoding::der);
    EXPECT_THROW(r.read_any(tag::null), exception);
}

TEST(Any, ImplicitTagRoundTrips) {
    const any value = any::from_tag_and_contents(tag::null, {});
    const tag context = tag::context(0);
    writer w(encoding::der);
    w.write_any(context, value);
    const auto bytes = w.encode();
    EXPECT_EQ(bytes, (std::vector<std::uint8_t>{0x80, 0x00}));

    reader r(bytes, encoding::der);
    const any decoded = r.read_any(context);
    EXPECT_EQ(decoded.tag_value(), context);
    EXPECT_EQ(decoded.to_vector(), bytes);
    EXPECT_TRUE(decoded.contents().empty());
}

TEST(Any, BerReadsIndefiniteLength) {
    const std::vector<std::uint8_t> ber{0x30, 0x80, 0x02, 0x01, 0x01, 0x00, 0x00};
    reader r(ber, encoding::ber);
    const any decoded = r.read_any();
    EXPECT_EQ(decoded.tag_value(), tag::sequence);
    EXPECT_EQ(decoded.to_vector(), ber);
    EXPECT_EQ(decoded.contents().to_vector(), (std::vector<std::uint8_t>{0x02, 0x01, 0x01}));
    EXPECT_TRUE(r.eof());

    writer rewrite(encoding::ber);
    rewrite.write_any(decoded);
    EXPECT_EQ(rewrite.encode(), ber);
}

TEST(Any, PreservesNonMinimalLengthOnRewrite) {
    const std::vector<std::uint8_t> encoded{0x02, 0x81, 0x01, 0x05};
    reader r(encoded, encoding::ber, reader_options::allow_non_minimal_length());
    const any decoded = r.read_any();
    EXPECT_EQ(decoded.to_vector(), encoded);

    writer rewrite(encoding::der);
    rewrite.write_any(decoded);
    EXPECT_EQ(rewrite.encode(), encoded);
}

TEST(Any, CtorRejectsTrailingOctetsAndEmpty) {
    const std::vector<std::uint8_t> trailing{0x05, 0x00, 0x05, 0x00};
    EXPECT_THROW(any(bytes::copy_from(trailing)), exception);
    EXPECT_THROW(any(bytes{}), exception);
}

TEST(Any, FromValueAndDecodeValue) {
    const any value = any::from_value(42, [](writer& w, int v) {
        w.write_integer(tag::integer, v);
    });
    EXPECT_EQ(value.to_vector(), (std::vector<std::uint8_t>{0x02, 0x01, 0x2A}));
    EXPECT_EQ(
        value.decode_value([](reader& r) { return r.read_int32(tag::integer); }, encoding::der),
        42);
    EXPECT_THROW(
        value.decode_value([](reader& r) { return r.read_oid(tag::object_identifier); }),
        exception);
    EXPECT_THROW(value.decode_value([](reader&) { return 0; }), exception);
}

TEST(Any, ToString) {
    EXPECT_EQ(any{}.to_string(), "<empty>");
    const any value = any::from_tag_and_contents(tag::integer, std::vector<std::uint8_t>{0x05});
    EXPECT_EQ(value.to_string(), "Universal-2P (3 bytes)");
}
