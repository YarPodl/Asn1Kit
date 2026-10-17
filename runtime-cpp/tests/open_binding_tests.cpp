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
#include <type_traits>
#include <vector>

using namespace asn1kit;
using namespace M;

using algorithm_open_types = AlgorithmIdentifier::open_types::mixed::parameters;
using attribute_open_types = Attribute::open_types::mixed::values;
using extension_open_types = Extension::open_types::mixed::extn_value;
using nested_open_types = Outer::open_types::mixed::value_;

template <typename Owner, typename Binding>
concept supports_nested_open_decode = requires(const Owner& owner, const Binding& binding) {
    owner.try_decode_nested(binding);
};

TEST(OpenBinding, SetTryDecodeRoundTripNullAndInt32) {
    auto info = AlgorithmIdentifier::create(algorithm_open_types::empty_entry, null_value::value());
    EXPECT_EQ(info.algorithm, oid::parse("1.2.3"));
    ASSERT_TRUE(info.parameters.has_value());

    const auto decoded_null = info.try_decode(algorithm_open_types::empty_entry);
    ASSERT_TRUE(decoded_null.has_value());
    EXPECT_EQ(*decoded_null, null_value::value());

    EXPECT_FALSE(info.try_decode(algorithm_open_types::data_entry).has_value());

    info.set(algorithm_open_types::data_entry, 42);
    EXPECT_EQ(info.algorithm, oid::parse("1.2.4"));
    const auto decoded = info.try_decode(algorithm_open_types::data_entry);
    ASSERT_TRUE(decoded.has_value());
    EXPECT_EQ(*decoded, 42);

    writer w(encoding::der);
    info.encode(w);
    reader r(bytes::from_vector(w.encode()), encoding::der);
    const auto round_trip = AlgorithmIdentifier::decode(r);
    EXPECT_EQ(round_trip.try_decode(algorithm_open_types::data_entry), 42);
}

TEST(OpenBinding, UnknownKeyKeepsRaw) {
    AlgorithmIdentifier info{};
    info.algorithm = oid::parse("1.2.9");
    info.parameters = encode_open(std::int32_t{7});

    EXPECT_FALSE(info.try_decode(algorithm_open_types::empty_entry).has_value());
    EXPECT_FALSE(info.try_decode(algorithm_open_types::data_entry).has_value());
    ASSERT_TRUE(info.parameters.has_value());
    EXPECT_EQ(decode_open<std::int32_t>(*info.parameters, encoding::der), 7);
}

TEST(OpenBinding, KnownKeyWireMismatchRejects) {
    AlgorithmIdentifier info{};
    info.algorithm = oid::parse("1.2.3");
    info.parameters = encode_open(std::int32_t{1});

    EXPECT_THROW(
        (void)info.try_decode(algorithm_open_types::empty_entry), exception);
}

TEST(OpenBinding, ContainedSetTryDecodeRoundTrip) {
    auto ext = Extension::create(extension_open_types::data_entry, std::int32_t{7});
    EXPECT_EQ(ext.extnID, oid::parse("1.2.4"));

    EXPECT_EQ(ext.try_decode(extension_open_types::data_entry), 7);

    EXPECT_FALSE(ext.try_decode(extension_open_types::empty_entry).has_value());

    writer w(encoding::der);
    ext.encode(w);
    reader r(bytes::from_vector(w.encode()), encoding::der);
    const auto round_trip = Extension::decode(r);
    EXPECT_EQ(round_trip.try_decode(extension_open_types::data_entry), 7);
}

TEST(OpenBinding, UserDefinedContainedBindingSupportsLocalCarrierOperations) {
    const auto binding = Extension::open_binding<std::int32_t>(oid::parse("1.2.99"));

    auto ext = Extension::create(binding, std::int32_t{7});
    EXPECT_EQ(ext.extnID, oid::parse("1.2.99"));
    EXPECT_EQ(ext.try_decode(binding), 7);

    ext.set(binding, std::int32_t{42});
    EXPECT_EQ(ext.try_decode(binding), 42);

    const auto other = Extension::open_binding<std::int32_t>(oid::parse("1.2.100"));
    EXPECT_FALSE(ext.try_decode(other).has_value());
}

TEST(OpenBinding, UserDefinedBindingRejectsMatchingKeyWithInvalidWireValue) {
    const auto binding = Extension::open_binding<std::int32_t>(oid::parse("1.2.99"));
    Extension ext{};
    ext.extnID = binding.key;
    ext.extnValue = encode_contained_octets(null_value::value());

    EXPECT_THROW((void)ext.try_decode(binding), exception);
}

TEST(OpenBinding, SetOfSetTryDecodeRoundTrip) {
    auto attr = Attribute::create(attribute_open_types::data_entry,
        std::vector<std::int32_t>{1, 2});
    EXPECT_EQ(attr.type, oid::parse("1.2.4"));
    ASSERT_EQ(attr.values.size(), 2u);

    const auto decoded = attr.try_decode(attribute_open_types::data_entry);
    ASSERT_TRUE(decoded.has_value());
    EXPECT_EQ(*decoded, (std::vector<std::int32_t>{1, 2}));

    attr.set(attribute_open_types::empty_entry, null_value::value());
    EXPECT_EQ(attr.type, oid::parse("1.2.3"));
    const auto nulls = attr.try_decode(attribute_open_types::empty_entry);
    ASSERT_TRUE(nulls.has_value());
    ASSERT_EQ(nulls->size(), 1u);
}

TEST(OpenBinding, UserDefinedCollectionBindingUsesElementType) {
    const auto binding = Attribute::open_binding<std::int32_t>(oid::parse("1.2.99"));
    static_assert(std::is_same_v<typename std::remove_cv_t<decltype(binding)>::value_type,
        std::vector<std::int32_t>>);

    auto attr = Attribute::create(binding, std::int32_t{7});
    ASSERT_EQ(attr.try_decode(binding), (std::vector<std::int32_t>{7}));
}

TEST(OpenBinding, AncestorSelectorUsesParentKey) {
    Outer outer{};
    outer.id = oid::parse("1.2.4");
    outer.nested.push_back(Outer_Nested_Item{});
    outer.nested.back().value_ = encode_open(std::int32_t{9});
    const auto decoded = outer.try_decode_nested(nested_open_types::data_entry);
    ASSERT_TRUE(decoded.has_value());
    EXPECT_EQ(*decoded, 9);

    outer.id = oid::parse("1.2.3");
    EXPECT_FALSE(outer.try_decode_nested(nested_open_types::data_entry).has_value());
}

TEST(OpenBinding, DefaultContextDoesNotBypassContextualOwnerProtection) {
    const auto binding = Extension::open_binding<std::int32_t>(oid::parse("1.2.4"));
    static_assert(!supports_nested_open_decode<Outer, decltype(binding)>);
}

TEST(OpenBinding, ContextualDecodeRejectsMultipleMatches) {
    Outer outer{};
    outer.id = oid::parse("1.2.4");
    outer.nested.resize(2);
    outer.nested[0].value_ = encode_open(std::int32_t{1});
    outer.nested[1].value_ = encode_open(std::int32_t{2});

    EXPECT_THROW(
        (void)outer.try_decode_nested(nested_open_types::data_entry), exception);
}
