// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/exception.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/lazy.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/tag.hpp"
#include "asn1kit/value.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <vector>

using namespace asn1kit;

namespace {

struct Inner {
    integer n{};

    static tag default_tag() { return tag::sequence; }

    static Inner decode(reader& r, const tag& t = default_tag()) {
        Inner value{};
        r.with_sequence(t, [&](reader& inner) {
            value.n = inner.read_integer_value(tag::integer);
        });
        return value;
    }

    void encode(writer& w, const tag& t = default_tag()) const {
        auto scope = w.enter_sequence(t);
        w.write_integer(tag::integer, n);
    }
};

std::vector<std::uint8_t> encode_inner(std::int32_t n) {
    Inner value{};
    value.n = integer::from_int32(n);
    writer w(encoding::der);
    value.encode(w);
    return w.encode();
}

} // namespace

TEST(ValueLazy, ReadWithOriginalEncodingRetainsTlv) {
    const auto der = encode_inner(42);
    reader r(der, encoding::der);
    const auto retained = r.read_with_original_encoding<Inner>([](reader& nested) {
        return Inner::decode(nested);
    });
    EXPECT_TRUE(r.eof());
    EXPECT_EQ(retained.get().n, integer::from_int32(42));
    ASSERT_TRUE(retained.has_original_encoding());
    EXPECT_EQ(retained.original_encoding().to_vector(), der);
}

TEST(ValueLazy, FromValueHasEmptyOriginalEncoding) {
    Inner inner{};
    inner.n = integer::from_int32(1);
    const auto wrapped = value<Inner>::from_value(inner);
    EXPECT_FALSE(wrapped.has_original_encoding());
    EXPECT_TRUE(wrapped.original_encoding().empty());
    EXPECT_EQ(wrapped.get().n, integer::from_int32(1));
}

TEST(ValueLazy, LazyDefersDecodeUntilGet) {
    const auto der = encode_inner(7);
    int decode_count = 0;
    reader r(der, encoding::der);
    auto deferred = r.read_lazy<Inner>([&](reader& nested) {
        ++decode_count;
        return Inner::decode(nested);
    });
    EXPECT_TRUE(r.eof());
    EXPECT_EQ(decode_count, 0);
    EXPECT_TRUE(deferred.has_encoded());
    EXPECT_FALSE(deferred.is_materialized());
    EXPECT_EQ(deferred.get().n, integer::from_int32(7));
    EXPECT_EQ(decode_count, 1);
    EXPECT_EQ(deferred.get().n, integer::from_int32(7));
    EXPECT_EQ(decode_count, 1);
}

TEST(ValueLazy, LazyWriteToIsBitExact) {
    const auto der = encode_inner(3);
    reader r(der, encoding::der);
    auto deferred = r.read_lazy<Inner>([](reader& nested) { return Inner::decode(nested); });
    writer w(encoding::der);
    deferred.write_to(w);
    EXPECT_EQ(w.encode(), der);
}

TEST(ValueLazy, LazyFromValueRequiresExplicitEncode) {
    Inner inner{};
    inner.n = integer::from_int32(9);
    auto deferred = lazy<Inner>::from_value(inner);
    EXPECT_FALSE(deferred.has_encoded());
    EXPECT_TRUE(deferred.is_materialized());
    writer w(encoding::der);
    EXPECT_THROW(deferred.write_to(w), std::invalid_argument);
    deferred.get().encode(w);
    EXPECT_EQ(w.encode(), encode_inner(9));
}

TEST(ValueLazy, LazyCorruptTlvFailsOnGet) {
    // Complete SEQUENCE TLV whose contents are BOOLEAN, not INTEGER.
    const std::vector<std::uint8_t> der = {0x30, 0x03, 0x01, 0x01, 0xFF};
    reader r(der, encoding::der);
    auto deferred = r.read_lazy<Inner>([](reader& nested) { return Inner::decode(nested); });
    EXPECT_TRUE(r.eof());
    EXPECT_THROW(deferred.get(), exception);
}
