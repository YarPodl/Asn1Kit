// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "M.hpp"

#include "asn1kit/codec.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/null.hpp"
#include "asn1kit/oid.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <vector>

using namespace asn1kit;
using namespace M;

TEST(OpenBinding, SetTryDecodeRoundTripNullAndInt32) {
    auto info = AlgorithmIdentifier::create(mixed_parameters_bindings::empty_entry, null_value::value());
    EXPECT_EQ(info.algorithm, oid::parse("1.2.3"));
    ASSERT_TRUE(info.parameters.has_value());

    const auto decoded_null = info.try_decode(mixed_parameters_bindings::empty_entry);
    ASSERT_TRUE(decoded_null.has_value());
    EXPECT_EQ(*decoded_null, null_value::value());

    EXPECT_FALSE(info.try_decode(mixed_parameters_bindings::data_entry).has_value());

    info.set(mixed_parameters_bindings::data_entry, 42);
    EXPECT_EQ(info.algorithm, oid::parse("1.2.4"));
    const auto decoded = info.try_decode(mixed_parameters_bindings::data_entry);
    ASSERT_TRUE(decoded.has_value());
    EXPECT_EQ(*decoded, 42);

    writer w(encoding::der);
    info.encode(w);
    reader r(bytes::from_vector(w.encode()), encoding::der);
    const auto round_trip = AlgorithmIdentifier::decode(r);
    EXPECT_EQ(round_trip.try_decode(mixed_parameters_bindings::data_entry), 42);
}

TEST(OpenBinding, UnknownKeyKeepsRaw) {
    AlgorithmIdentifier info{};
    info.algorithm = oid::parse("1.2.9");
    info.parameters = encode_open(std::int32_t{7});

    EXPECT_FALSE(info.try_decode(mixed_parameters_bindings::empty_entry).has_value());
    EXPECT_FALSE(info.try_decode(mixed_parameters_bindings::data_entry).has_value());
    ASSERT_TRUE(info.parameters.has_value());
    EXPECT_EQ(decode_open<std::int32_t>(*info.parameters, encoding::der), 7);
}

TEST(OpenBinding, KnownKeyWireMismatchRejects) {
    AlgorithmIdentifier info{};
    info.algorithm = oid::parse("1.2.3");
    info.parameters = encode_open(std::int32_t{1});

    EXPECT_THROW(
        (void)info.try_decode(mixed_parameters_bindings::empty_entry), exception);
}

TEST(OpenBinding, ContainedSetTryDecodeRoundTrip) {
    auto ext = Extension::create(mixed_extnValue_bindings::data_entry, std::int32_t{7});
    EXPECT_EQ(ext.extnID, oid::parse("1.2.4"));

    EXPECT_EQ(ext.try_decode(mixed_extnValue_bindings::data_entry), 7);

    EXPECT_FALSE(ext.try_decode(mixed_extnValue_bindings::empty_entry).has_value());

    writer w(encoding::der);
    ext.encode(w);
    reader r(bytes::from_vector(w.encode()), encoding::der);
    const auto round_trip = Extension::decode(r);
    EXPECT_EQ(round_trip.try_decode(mixed_extnValue_bindings::data_entry), 7);
}

TEST(OpenBinding, SetOfSetTryDecodeRoundTrip) {
    auto attr = Attribute::create(mixed_values_bindings::data_entry,
        std::vector<std::int32_t>{1, 2});
    EXPECT_EQ(attr.type, oid::parse("1.2.4"));
    ASSERT_EQ(attr.values.size(), 2u);

    const auto decoded = attr.try_decode(mixed_values_bindings::data_entry);
    ASSERT_TRUE(decoded.has_value());
    EXPECT_EQ(*decoded, (std::vector<std::int32_t>{1, 2}));

    attr.set(mixed_values_bindings::empty_entry, null_value::value());
    EXPECT_EQ(attr.type, oid::parse("1.2.3"));
    const auto nulls = attr.try_decode(mixed_values_bindings::empty_entry);
    ASSERT_TRUE(nulls.has_value());
    ASSERT_EQ(nulls->size(), 1u);
}

TEST(OpenBinding, AncestorSelectorUsesParentKey) {
    Outer outer{};
    outer.id = oid::parse("1.2.4");
    outer.nested.push_back(Outer_Nested_Item{});
    outer.nested.back().value_ = encode_open(std::int32_t{9});
    const auto decoded = outer.try_decode_nested(mixed_value_bindings::data_entry);
    ASSERT_TRUE(decoded.has_value());
    EXPECT_EQ(*decoded, 9);

    outer.id = oid::parse("1.2.3");
    EXPECT_FALSE(outer.try_decode_nested(mixed_value_bindings::data_entry).has_value());
}

TEST(OpenBinding, ContextualDecodeRejectsMultipleMatches) {
    Outer outer{};
    outer.id = oid::parse("1.2.4");
    outer.nested.resize(2);
    outer.nested[0].value_ = encode_open(std::int32_t{1});
    outer.nested[1].value_ = encode_open(std::int32_t{2});

    EXPECT_THROW(
        (void)outer.try_decode_nested(mixed_value_bindings::data_entry), exception);
}
