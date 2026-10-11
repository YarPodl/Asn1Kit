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

using namespace asn1kit;
using namespace M;

TEST(OpenBinding, SetTryDecodeRoundTripNullAndInt32) {
    auto info = mixed_parameters_bindings::empty_entry.create(null_value::value());
    EXPECT_EQ(info.algorithm, oid::parse("1.2.3"));
    ASSERT_TRUE(info.parameters.has_value());

    null_value decoded_null{};
    EXPECT_TRUE(mixed_parameters_bindings::empty_entry.try_decode(info, decoded_null));
    EXPECT_EQ(decoded_null, null_value::value());

    std::int32_t ignored = 0;
    EXPECT_FALSE(mixed_parameters_bindings::data_entry.try_decode(info, ignored));

    mixed_parameters_bindings::data_entry.set(info, 42);
    EXPECT_EQ(info.algorithm, oid::parse("1.2.4"));
    std::int32_t decoded = 0;
    EXPECT_TRUE(mixed_parameters_bindings::data_entry.try_decode(info, decoded));
    EXPECT_EQ(decoded, 42);

    writer w(encoding::der);
    info.encode(w);
    reader r(bytes::from_vector(w.encode()), encoding::der);
    const auto round_trip = AlgorithmIdentifier::decode(r);
    EXPECT_TRUE(mixed_parameters_bindings::data_entry.try_decode(round_trip, decoded));
    EXPECT_EQ(decoded, 42);
}

TEST(OpenBinding, UnknownKeyKeepsRaw) {
    AlgorithmIdentifier info{};
    info.algorithm = oid::parse("1.2.9");
    info.parameters = encode_open(std::int32_t{7});

    null_value n{};
    std::int32_t i = 0;
    EXPECT_FALSE(mixed_parameters_bindings::empty_entry.try_decode(info, n));
    EXPECT_FALSE(mixed_parameters_bindings::data_entry.try_decode(info, i));
    ASSERT_TRUE(info.parameters.has_value());
    EXPECT_EQ(decode_open<std::int32_t>(*info.parameters, encoding::der), 7);
}

TEST(OpenBinding, KnownKeyWireMismatchRejects) {
    AlgorithmIdentifier info{};
    info.algorithm = oid::parse("1.2.3");
    info.parameters = encode_open(std::int32_t{1});

    null_value n{};
    EXPECT_THROW(
        (void)mixed_parameters_bindings::empty_entry.try_decode(info, n), exception);
}
