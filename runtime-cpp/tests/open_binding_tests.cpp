// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "M.hpp"

#ifdef ASN1KIT_DETAIL_OPEN_VIEW_ENTRY
#error "generated open-view helper macro leaked into consumer code"
#endif

#include "asn1kit/codec.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/null.hpp"
#include "asn1kit/oid.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <type_traits>
#include <utility>
#include <vector>

using namespace asn1kit;
using namespace M;

template <typename Owner, typename Binding>
concept supports_nested_open_decode = requires(const Owner& owner, const Binding& binding) {
    owner.try_decode_nested(binding);
};

template <typename Owner>
concept supports_rvalue_mixed_view = requires(Owner&& owner) {
    std::move(owner).mixed();
};

template <typename View>
concept supports_data_entry_set = requires(View view) {
    view.data_entry(std::int32_t{42});
};

static_assert(supports_rvalue_mixed_view<AlgorithmIdentifier>);
static_assert(sizeof(decltype(std::declval<AlgorithmIdentifier&>().mixed())) ==
              sizeof(void*));
static_assert(sizeof(decltype(AlgorithmIdentifier::make_mixed())) == 1);
static_assert(supports_data_entry_set<
    decltype(std::declval<AlgorithmIdentifier&>().mixed())>);
static_assert(!supports_data_entry_set<
    decltype(std::declval<const AlgorithmIdentifier&>().mixed())>);
static_assert(!supports_data_entry_set<
    decltype(std::declval<AlgorithmIdentifier&&>().mixed())>);

TEST(OpenBinding, SetTryDecodeRoundTripNullAndInt32) {
    auto info = AlgorithmIdentifier::make_mixed().empty_entry(null_value::value());
    EXPECT_EQ(info.algorithm, oid::parse("1.2.3"));
    ASSERT_TRUE(info.parameters.has_value());

    const auto decoded_null = std::as_const(info).mixed().empty_entry();
    ASSERT_TRUE(decoded_null.has_value());
    EXPECT_EQ(*decoded_null, null_value::value());

    EXPECT_FALSE(info.mixed().data_entry().has_value());

    info.mixed().data_entry(42);
    EXPECT_EQ(info.algorithm, oid::parse("1.2.4"));
    const auto decoded = info.mixed().data_entry();
    ASSERT_TRUE(decoded.has_value());
    EXPECT_EQ(*decoded, 42);

    writer w(encoding::der);
    info.encode(w);
    reader r(bytes::from_vector(w.encode()), encoding::der);
    const auto round_trip = AlgorithmIdentifier::decode(r);
    EXPECT_EQ(round_trip.mixed().data_entry(), 42);
}

TEST(OpenBinding, RvalueViewDecodesOwnedCarrier) {
    auto info = AlgorithmIdentifier::make_mixed().data_entry(42);
    writer w(encoding::der);
    info.encode(w);
    reader r(bytes::from_vector(w.encode()), encoding::der);
    EXPECT_EQ(AlgorithmIdentifier::decode(r).mixed().data_entry(), 42);
}

TEST(OpenBinding, UnknownKeyKeepsRaw) {
    AlgorithmIdentifier info{};
    info.algorithm = oid::parse("1.2.9");
    info.parameters = encode_open(std::int32_t{7});

    EXPECT_FALSE(info.mixed().empty_entry().has_value());
    EXPECT_FALSE(info.mixed().data_entry().has_value());
    ASSERT_TRUE(info.parameters.has_value());
    EXPECT_EQ(decode_open<std::int32_t>(*info.parameters, encoding::der), 7);
}

TEST(OpenBinding, KnownKeyWireMismatchRejects) {
    AlgorithmIdentifier info{};
    info.algorithm = oid::parse("1.2.3");
    info.parameters = encode_open(std::int32_t{1});

    EXPECT_THROW(
        (void)info.mixed().empty_entry(), exception);
}

TEST(OpenBinding, ContainedSetTryDecodeRoundTrip) {
    auto ext = Extension::make_mixed().data_entry(std::int32_t{7});
    EXPECT_EQ(ext.extnID, oid::parse("1.2.4"));

    EXPECT_EQ(ext.mixed().data_entry(), 7);

    EXPECT_FALSE(ext.mixed().empty_entry().has_value());

    writer w(encoding::der);
    ext.encode(w);
    reader r(bytes::from_vector(w.encode()), encoding::der);
    const auto round_trip = Extension::decode(r);
    EXPECT_EQ(round_trip.mixed().data_entry(), 7);
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
    auto attr = Attribute::make_mixed().data_entry(
        std::vector<std::int32_t>{1, 2});
    EXPECT_EQ(attr.type, oid::parse("1.2.4"));
    ASSERT_EQ(attr.values.size(), 2u);

    const auto decoded = attr.mixed().data_entry();
    ASSERT_TRUE(decoded.has_value());
    EXPECT_EQ(*decoded, (std::vector<std::int32_t>{1, 2}));

    attr.mixed().empty_entry(null_value::value());
    EXPECT_EQ(attr.type, oid::parse("1.2.3"));
    const auto nulls = attr.mixed().empty_entry();
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
    const auto decoded = outer.mixed().data_entry();
    ASSERT_TRUE(decoded.has_value());
    EXPECT_EQ(*decoded, 9);

    outer.id = oid::parse("1.2.3");
    EXPECT_FALSE(outer.mixed().data_entry().has_value());
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
        (void)outer.mixed().data_entry(), exception);
}
