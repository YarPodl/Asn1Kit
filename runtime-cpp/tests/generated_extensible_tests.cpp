// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "ExtModule.hpp"

#include "asn1kit/any.hpp"
#include "asn1kit/bytes.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/null.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/tag.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <vector>

using asn1kit::any;
using asn1kit::bytes;
using asn1kit::encoding;
using asn1kit::exception;
using asn1kit::integer;
using asn1kit::null_value;
using asn1kit::reader;
using asn1kit::tag;
using asn1kit::writer;
using ExtModule::Bag;
using ExtModule::Box;
using ExtModule::Pick;
using ExtModule::Pick_kind;

namespace {

std::vector<std::uint8_t> encode_box(const Box& value) {
    writer w(encoding::der);
    value.encode(w);
    return w.encode();
}

Box decode_box(const std::vector<std::uint8_t>& der) {
    reader r(der, encoding::der);
    auto value = Box::decode(r);
    EXPECT_TRUE(r.eof());
    return value;
}

} // namespace

TEST(GeneratedExtensibleTests, SequenceRoundTripKnownExtensions) {
    Box box{};
    box.root = integer::from_int32(7);
    box.mid = true;
    box.flag = null_value::value();
    box.tail = bytes::copy_from(std::vector<std::uint8_t>{0xAB});

    const auto der = encode_box(box);
    const auto decoded = decode_box(der);
    EXPECT_EQ(decoded.root, integer::from_int32(7));
    ASSERT_TRUE(decoded.mid.has_value());
    EXPECT_TRUE(*decoded.mid);
    ASSERT_TRUE(decoded.flag.has_value());
    ASSERT_TRUE(decoded.tail.has_value());
    EXPECT_EQ(decoded.tail->to_vector(), (std::vector<std::uint8_t>{0xAB}));
    EXPECT_EQ(encode_box(decoded), der);
}

TEST(GeneratedExtensibleTests, SequenceSkipsUnknownExtension) {
    // root=1, unknown [5] INTEGER 9, mid=true
    const std::vector<std::uint8_t> der = {
        0x30, 0x09,
        0x80, 0x01, 0x01,
        0x85, 0x01, 0x09,
        0x81, 0x01, 0xFF};

    const auto decoded = decode_box(der);
    EXPECT_EQ(decoded.root, integer::from_int32(1));
    ASSERT_TRUE(decoded.mid.has_value());
    EXPECT_TRUE(*decoded.mid);
    EXPECT_FALSE(decoded.flag.has_value());
    EXPECT_FALSE(decoded.tail.has_value());
}

TEST(GeneratedExtensibleTests, SequenceRejectsIncompleteExtensionGroup) {
    Box box{};
    box.root = integer::from_int32(1);
    box.flag = null_value::value();
    EXPECT_THROW(encode_box(box), exception);
}

TEST(GeneratedExtensibleTests, SetSkipsUnknownComponent) {
    // SET { a=3, unknown [7] BOOLEAN true, b=false }
    const std::vector<std::uint8_t> der = {
        0x31, 0x09,
        0x80, 0x01, 0x03,
        0x87, 0x01, 0xFF,
        0x81, 0x01, 0x00};

    reader r(der, encoding::der);
    const auto decoded = Bag::decode(r);
    EXPECT_TRUE(r.eof());
    EXPECT_EQ(decoded.a, integer::from_int32(3));
    ASSERT_TRUE(decoded.b.has_value());
    EXPECT_FALSE(*decoded.b);
}

TEST(GeneratedExtensibleTests, ChoiceUnknownRoundTrip) {
    writer raw(encoding::der);
    raw.write_boolean(tag::context(9), true);
    const auto unknown_der = raw.encode();

    reader unknown_reader(unknown_der, encoding::der);
    const auto unknown = any::decode(unknown_reader);
    auto pick = Pick::from_unknown(unknown);
    EXPECT_EQ(pick.kind, Pick_kind::unknown);

    writer w(encoding::der);
    pick.encode(w);
    const auto der = w.encode();

    reader r(der, encoding::der);
    const auto decoded = Pick::decode(r);
    EXPECT_TRUE(r.eof());
    EXPECT_EQ(decoded.kind, Pick_kind::unknown);
    ASSERT_TRUE(decoded.unknown.has_value());

    writer again(encoding::der);
    decoded.encode(again);
    EXPECT_EQ(again.encode(), der);
}

TEST(GeneratedExtensibleTests, ChoiceKnownAlternative) {
    auto pick = Pick::from_left(integer::from_int32(5));
    writer w(encoding::der);
    pick.encode(w);
    const auto der = w.encode();

    reader r(der, encoding::der);
    const auto decoded = Pick::decode(r);
    EXPECT_EQ(decoded.kind, Pick_kind::left);
    EXPECT_EQ(decoded.value, integer::from_int32(5));
}
