// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0
//
// Deep debug formatting: runtime primitives plus generated to_string overloads.

#include "ExampleModule.format.hpp"
#include "ExtModule.format.hpp"

#include "asn1kit/format.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <optional>
#include <sstream>
#include <string>
#include <vector>

namespace {

template <typename T>
std::string render(const T& value) {
    std::ostringstream out;
    asn1kit::to_string(value, out);
    return out.str();
}

} // namespace

TEST(FormatTests, PrimitivesAndContainers) {
    EXPECT_EQ(render(asn1kit::integer::from_int32(42)), "42");
    EXPECT_EQ(render(std::int32_t{-7}), "-7");
    EXPECT_EQ(render(true), "true");
    EXPECT_EQ(render(asn1kit::null_value{}), "null");

    const auto octets = asn1kit::bytes::copy_from(std::vector<std::uint8_t>{'A', 'n', 'n'});
    EXPECT_EQ(render(octets), "3 bytes");

    const auto bits = asn1kit::bit_string::copy_from(std::vector<std::uint8_t>{0xFF}, 4);
    EXPECT_EQ(render(bits), "4 bits");

    const std::optional<std::int32_t> absent{};
    EXPECT_EQ(render(absent), "<absent>");

    const std::optional<std::int32_t> present{5};
    EXPECT_EQ(render(present), "5");

    EXPECT_EQ(
        render(std::vector<asn1kit::integer>{
            asn1kit::integer::from_int32(1), asn1kit::integer::from_int32(42)}),
        "[2] {\n    1\n    42\n}");
}

TEST(FormatTests, GeneratedSequenceRecurses) {
    example::asn1::Person person{};
    person.id = asn1kit::integer::from_int32(42);
    person.name = asn1kit::bytes::copy_from(std::vector<std::uint8_t>{'A', 'n', 'n'});
    person.nick = asn1kit::bytes::copy_from(std::vector<std::uint8_t>{'A'});

    EXPECT_EQ(render(person), "Person {\n    id: 42\n    name: 3 bytes\n    nick: 1 bytes\n}");
}

TEST(FormatTests, GeneratedOptionalAbsentIsMarked) {
    example::asn1::Person person{};
    person.id = asn1kit::integer::from_int32(1);
    person.name = asn1kit::bytes::copy_from(std::vector<std::uint8_t>{'x'});

    EXPECT_EQ(render(person), "Person {\n    id: 1\n    name: 1 bytes\n    nick: <absent>\n}");
}

TEST(FormatTests, GeneratedSetAndExtensionFields) {
    ExtModule::Bag bag{};
    bag.a = asn1kit::integer::from_int32(5);
    bag.b = true;

    EXPECT_EQ(render(bag), "Bag {\n    a: 5\n    b: true\n}");

    ExtModule::Box box{};
    box.root = asn1kit::integer::from_int32(9);
    box.flag = asn1kit::null_value{};
    box.tail = asn1kit::bytes::copy_from(std::vector<std::uint8_t>{0x01, 0x02});

    EXPECT_EQ(
        render(box),
        "Box {\n    root: 9\n    mid: <absent>\n    flag: null\n    tail: 2 bytes\n}");
}

TEST(FormatTests, GeneratedChoiceShowsAlternativeName) {
    const auto pick = ExtModule::Pick::from_left(asn1kit::integer::from_int32(7));
    EXPECT_EQ(render(pick), "Pick {\n    kind: left\n    value: 7\n}");
}
