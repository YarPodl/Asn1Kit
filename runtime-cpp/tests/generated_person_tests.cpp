// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "ExampleModule.hpp"

#include "asn1kit/bytes.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <string>
#include <vector>

using asn1kit::bytes;
using asn1kit::encoding;
using asn1kit::integer;
using asn1kit::reader;
using asn1kit::writer;
using example::asn1::Person;

namespace {

std::vector<std::uint8_t> hex(std::initializer_list<std::uint8_t> values) {
    return std::vector<std::uint8_t>(values);
}

} // namespace

// Same DER as C# RoundTripTests Person(id=42, name="Ann", nick="A").
TEST(GeneratedPersonTests, EncodeDecodeRoundTripMatchesCsharpVector) {
    const auto expected = hex({
        0x30, 0x0B, 0x80, 0x01, 0x2A, 0x81, 0x03, 0x41, 0x6E, 0x6E, 0x82, 0x01, 0x41});

    Person person{};
    person.id = integer::from_int32(42);
    person.name = bytes::copy_from(std::vector<std::uint8_t>{'A', 'n', 'n'});
    person.nick = bytes::copy_from(std::vector<std::uint8_t>{'A'});

    writer w(encoding::der);
    person.encode(w);
    const auto encoded = w.encode();
    EXPECT_EQ(encoded, expected);

    reader r(encoded, encoding::der);
    const auto decoded = Person::decode(r);
    EXPECT_TRUE(r.eof());
    EXPECT_EQ(decoded.id, integer::from_int32(42));
    EXPECT_EQ(decoded.name.to_vector(), (std::vector<std::uint8_t>{'A', 'n', 'n'}));
    ASSERT_TRUE(decoded.nick.has_value());
    EXPECT_EQ(decoded.nick->to_vector(), (std::vector<std::uint8_t>{'A'}));

    writer again(encoding::der);
    decoded.encode(again);
    EXPECT_EQ(again.encode(), expected);
}

TEST(GeneratedPersonTests, OptionalNickAbsent) {
    Person person{};
    person.id = integer::from_int32(1);
    person.name = bytes::copy_from(std::vector<std::uint8_t>{'x'});

    writer w(encoding::der);
    person.encode(w);
    const auto encoded = w.encode();
    EXPECT_EQ(encoded, hex({0x30, 0x06, 0x80, 0x01, 0x01, 0x81, 0x01, 0x78}));

    reader r(encoded, encoding::der);
    const auto decoded = Person::decode(r);
    EXPECT_FALSE(decoded.nick.has_value());
}
