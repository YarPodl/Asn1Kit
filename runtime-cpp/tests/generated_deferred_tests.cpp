// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "Deferred.hpp"

#include "asn1kit/exception.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <vector>

using asn1kit::encoding;
using asn1kit::exception;
using asn1kit::integer;
using asn1kit::reader;
using asn1kit::writer;
using Deferred::Box;
using Deferred::Inner;

namespace {

Inner make_inner(std::int32_t n) {
    Inner value{};
    value.n = integer::from_int32(n);
    return value;
}

std::vector<std::uint8_t> encode_box(const Box& box) {
    writer w(encoding::der);
    box.encode(w);
    return w.encode();
}

} // namespace

TEST(GeneratedDeferredTests, RetainEncodedPreservesOriginalTlv) {
    Box box{};
    box.retained = asn1kit::value<Inner>::from_value(make_inner(5));
    box.deferred = asn1kit::lazy<Inner>::from_value(make_inner(9));
    const auto der = encode_box(box);

    reader r(der, encoding::der);
    const auto decoded = Box::decode(r);
    EXPECT_TRUE(r.eof());
    EXPECT_EQ(decoded.retained.get().n, integer::from_int32(5));
    ASSERT_TRUE(decoded.retained.has_original_encoding());
    EXPECT_FALSE(decoded.retained.original_encoding().empty());
    EXPECT_EQ(decoded.deferred.get().n, integer::from_int32(9));
    EXPECT_TRUE(decoded.deferred.has_encoded());

    // Mutate retained value; encode uses current value, not original TLV.
    auto mutated = decoded;
    mutated.retained.mutate().n = integer::from_int32(6);
    EXPECT_FALSE(mutated.retained.has_original_encoding());
    EXPECT_NE(encode_box(mutated), der);
}

TEST(GeneratedDeferredTests, LazyRewriteIsBitExact) {
    Box box{};
    box.retained = asn1kit::value<Inner>::from_value(make_inner(1));
    box.deferred = asn1kit::lazy<Inner>::from_value(make_inner(2));
    const auto der = encode_box(box);

    reader r(der, encoding::der);
    const auto decoded = Box::decode(r);
    writer again(encoding::der);
    decoded.encode(again);
    EXPECT_EQ(again.encode(), der);
}

TEST(GeneratedDeferredTests, LazyDefersUntilGet) {
    Box box{};
    box.retained = asn1kit::value<Inner>::from_value(make_inner(1));
    box.deferred = asn1kit::lazy<Inner>::from_value(make_inner(2));
    const auto der = encode_box(box);

    reader r(der, encoding::der);
    const auto decoded = Box::decode(r);
    EXPECT_TRUE(decoded.deferred.has_encoded());
    EXPECT_FALSE(decoded.deferred.is_materialized());
    EXPECT_EQ(decoded.deferred.get().n, integer::from_int32(2));
    EXPECT_TRUE(decoded.deferred.is_materialized());
}
