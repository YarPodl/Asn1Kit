// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "ExampleModule.hpp"

#include "asn1kit/encode_decode.hpp"
#include "asn1kit/encoding.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/integer.hpp"

#include <gtest/gtest.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <initializer_list>
#include <span>
#include <vector>

using asn1kit::bytes;
using asn1kit::encoding;
using example::asn1::Person;

namespace {

std::vector<std::uint8_t> hex(std::initializer_list<std::uint8_t> values) {
    return std::vector<std::uint8_t>(values);
}

Person make_person() {
    Person person{};
    person.id = asn1kit::integer::from_int32(42);
    person.name = bytes::copy_from(std::vector<std::uint8_t>{'A', 'n', 'n'});
    person.nick = bytes::copy_from(std::vector<std::uint8_t>{'A'});
    return person;
}

void expect_person(const Person& person) {
    EXPECT_EQ(person.id, asn1kit::integer::from_int32(42));
    EXPECT_EQ(person.name.to_vector(), (std::vector<std::uint8_t>{'A', 'n', 'n'}));
    ASSERT_TRUE(person.nick.has_value());
    EXPECT_EQ(person.nick->to_vector(), (std::vector<std::uint8_t>{'A'}));
}

// Same DER as C# RoundTripTests Person(id=42, name="Ann", nick="A").
std::vector<std::uint8_t> person_der() {
    return hex({0x30, 0x0B, 0x80, 0x01, 0x2A, 0x81, 0x03, 0x41, 0x6E, 0x6E, 0x82, 0x01, 0x41});
}

} // namespace

TEST(EncodeDecodeTests, EncodeMatchesCsharpVector) {
    const auto expected = person_der();
    EXPECT_EQ(asn1kit::encode(make_person(), encoding::der), expected);
    EXPECT_EQ(asn1kit::encode(make_person(), encoding::ber), expected);
}

TEST(EncodeDecodeTests, TryEncodeIntoBuffer) {
    const auto expected = person_der();
    std::array<std::uint8_t, 32> storage{};
    std::size_t written = 0;

    ASSERT_TRUE(asn1kit::try_encode(std::span<std::uint8_t>(storage), make_person(), written, encoding::der));
    EXPECT_EQ(written, expected.size());
    EXPECT_EQ((std::vector<std::uint8_t>(storage.begin(), storage.begin() + static_cast<std::ptrdiff_t>(written))), expected);
}

TEST(EncodeDecodeTests, TryEncodeTooSmallBufferFails) {
    std::array<std::uint8_t, 4> storage{};
    EXPECT_FALSE(asn1kit::try_encode(std::span<std::uint8_t>(storage), make_person(), encoding::der));
}

TEST(EncodeDecodeTests, DecodeRoundTrip) {
    const auto encoded = asn1kit::encode(make_person(), encoding::der);
    expect_person(asn1kit::decode<Person>(encoded, encoding::der));
}

TEST(EncodeDecodeTests, DecodeFromSpanAndBytes) {
    const auto encoded = asn1kit::encode(make_person(), encoding::der);
    const std::span<const std::uint8_t> view(encoded.data(), encoded.size());
    expect_person(asn1kit::decode<Person>(view, encoding::der));
    expect_person(asn1kit::decode<Person>(bytes::copy_from(view), encoding::der));
}

TEST(EncodeDecodeTests, DecodeBer) {
    const auto encoded = asn1kit::encode(make_person(), encoding::ber);
    expect_person(asn1kit::decode<Person>(encoded, encoding::ber));
    expect_person(asn1kit::decode<Person>(bytes::copy_from(encoded), encoding::ber));
}

TEST(EncodeDecodeTests, StrictDecodeRejectsTrailingOctets) {
    auto data = asn1kit::encode(make_person(), encoding::der);
    data.push_back(0x00);
    EXPECT_THROW(static_cast<void>(asn1kit::decode<Person>(data, encoding::der)), asn1kit::exception);
}

TEST(EncodeDecodeTests, DecodePrefixIgnoresTrailingOctets) {
    auto data = asn1kit::encode(make_person(), encoding::der);
    data.push_back(0x00);
    expect_person(asn1kit::decode_prefix<Person>(data, encoding::der));
}
