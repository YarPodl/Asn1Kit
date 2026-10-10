// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/bytes.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <stdexcept>
#include <vector>

using asn1kit::bytes;
using asn1kit::encoding;
using asn1kit::exception;
using asn1kit::reader;
using asn1kit::tag;
using asn1kit::tag_class;
using asn1kit::writer;

namespace {

std::vector<std::uint8_t> hex(std::initializer_list<std::uint8_t> bytes) {
    return std::vector<std::uint8_t>(bytes);
}

} // namespace

TEST(ConstructedScopeTests, WithSequence_NestedRoundTrip) {
    writer w(encoding::der);
    {
        auto outer = w.enter_sequence(tag::sequence);
        w.write_integer(tag::integer, 1);
        {
            auto inner = w.enter_sequence(tag::sequence);
            w.write_integer(tag::integer, 2);
        }
    }
    const auto bytes = w.encode();
    EXPECT_EQ(bytes, hex({0x30, 0x08, 0x02, 0x01, 0x01, 0x30, 0x03, 0x02, 0x01, 0x02}));

    reader r(bytes, encoding::der);
    int a = 0;
    int b = 0;
    r.with_sequence(tag::sequence, [&](reader& inner) {
        a = inner.read_int32();
        inner.with_sequence(tag::sequence, [&](reader& nested) { b = nested.read_int32(); });
    });
    EXPECT_EQ(a, 1);
    EXPECT_EQ(b, 2);
    EXPECT_TRUE(r.eof());
}

TEST(ConstructedScopeTests, WithSequence_TrailingDataThrows) {
    const auto bytes = hex({0x30, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02});
    reader r(bytes, encoding::der);
    EXPECT_THROW(
        r.with_sequence(tag::sequence, [&](reader& inner) { (void)inner.read_int32(); }),
        exception);
}

TEST(ConstructedScopeTests, EnterSequence_ManualThrowIfNotEmpty) {
    const auto bytes = hex({0x30, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02});
    reader r(bytes, encoding::der);
    {
        auto scope = r.enter_sequence(tag::sequence);
        EXPECT_EQ(r.read_int32(), 1);
        EXPECT_THROW(r.throw_if_not_empty(), exception);
        EXPECT_EQ(r.remaining(), 3u);
        EXPECT_EQ(r.read_int32(), 2);
        r.throw_if_not_empty();
    }
    EXPECT_TRUE(r.eof());
}

TEST(ConstructedScopeTests, WithSequence_ExceptionDoesNotBecomeNotEmpty) {
    writer w(encoding::der);
    {
        auto scope = w.enter_sequence(tag::sequence);
        w.write_integer(tag::integer, 1);
        w.write_integer(tag::integer, 2);
    }
    const auto bytes = w.encode();

    reader r(bytes, encoding::der);
    try {
        r.with_sequence(tag::sequence, [&](reader& inner) {
            (void)inner.read_int32();
            throw std::runtime_error("boom");
        });
        FAIL() << "expected runtime_error";
    } catch (const std::runtime_error& ex) {
        EXPECT_STREQ(ex.what(), "boom");
    }
    EXPECT_TRUE(r.eof());
}

TEST(ConstructedScopeTests, EnterExplicit_RoundTripsWrappedInteger) {
    const tag explicit_tag(tag_class::context_specific, 0, true);
    writer w(encoding::der);
    {
        auto scope = w.enter_explicit(explicit_tag);
        w.write_integer(tag::integer, 42);
    }
    const auto bytes = w.encode();

    reader r(bytes, encoding::der);
    int value = 0;
    r.with_explicit(explicit_tag, [&](reader& inner) { value = inner.read_int32(); });
    EXPECT_EQ(value, 42);
    EXPECT_TRUE(r.eof());
}

TEST(ConstructedScopeTests, WriteSetOf_DerSortsElementEncodings) {
    writer w(encoding::der);
    {
        auto scope = w.enter_set_of(tag::set);
        w.write_integer(tag::integer, 2);
        w.write_integer(tag::integer, 1);
    }
    const auto bytes = w.encode();
    EXPECT_EQ(bytes, hex({0x31, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02}));

    reader r(bytes, encoding::der);
    const auto decoded = r.read_set_of<std::int32_t>(tag::set, [](reader& inner) {
        return inner.read_int32();
    });
    ASSERT_EQ(decoded.size(), 2u);
    EXPECT_EQ(decoded[0], 1);
    EXPECT_EQ(decoded[1], 2);
}

TEST(ConstructedScopeTests, WriteSetOf_BerPreservesElementOrder) {
    writer w(encoding::ber);
    {
        auto scope = w.enter_set_of(tag::set);
        w.write_integer(tag::integer, 2);
        w.write_integer(tag::integer, 1);
    }
    EXPECT_EQ(w.encode(), hex({0x31, 0x06, 0x02, 0x01, 0x02, 0x02, 0x01, 0x01}));
}

TEST(ConstructedScopeTests, EnterSet_DerPreservesFieldOrder) {
    writer w(encoding::der);
    {
        auto scope = w.enter_set(tag::set);
        w.write_integer(tag::integer, 2);
        w.write_integer(tag::integer, 1);
    }
    EXPECT_EQ(w.encode(), hex({0x31, 0x06, 0x02, 0x01, 0x02, 0x02, 0x01, 0x01}));
}

TEST(ConstructedScopeTests, WriteSequenceOf_RoundTripsItems) {
    const std::vector<std::int32_t> items{1, 2};
    writer w(encoding::der);
    w.write_sequence_of(tag::sequence, items, [](writer& inner, std::int32_t item) {
        inner.write_integer(tag::integer, item);
    });
    const auto bytes = w.encode();
    EXPECT_EQ(bytes, hex({0x30, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02}));

    reader r(bytes, encoding::der);
    const auto decoded = r.read_sequence_of<std::int32_t>(tag::sequence, [](reader& inner) {
        return inner.read_int32();
    });
    ASSERT_EQ(decoded.size(), 2u);
    EXPECT_EQ(decoded[0], 1);
    EXPECT_EQ(decoded[1], 2);
    EXPECT_TRUE(r.eof());
}

TEST(ConstructedScopeTests, ReadSequenceOf_Empty) {
    writer w(encoding::der);
    {
        auto scope = w.enter_sequence_of(tag::sequence);
    }
    const auto bytes = w.encode();
    EXPECT_EQ(bytes, hex({0x30, 0x00}));

    reader r(bytes, encoding::der);
    const auto decoded = r.read_sequence_of<std::int32_t>(tag::sequence, [](reader& inner) {
        return inner.read_int32();
    });
    EXPECT_TRUE(decoded.empty());
    EXPECT_TRUE(r.eof());
}

TEST(ConstructedScopeTests, ForEachSequenceOf) {
    const auto bytes = hex({0x30, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02});
    reader r(bytes, encoding::der);
    std::vector<std::int32_t> values;
    r.for_each_sequence_of(tag::sequence, [&](reader& inner) {
        values.push_back(inner.read_int32());
    });
    ASSERT_EQ(values.size(), 2u);
    EXPECT_EQ(values[0], 1);
    EXPECT_EQ(values[1], 2);
}

TEST(ConstructedScopeTests, WriterScope_LifoAndDoubleEnd) {
    writer w(encoding::der);
    auto outer = w.enter_sequence(tag::sequence);
    auto inner = w.enter_explicit(tag(tag_class::context_specific, 0, true));

    EXPECT_THROW(outer.end(), std::logic_error);

    inner.end();
    outer.end();
    EXPECT_THROW(outer.end(), std::logic_error);
    EXPECT_EQ(w.encode(), hex({0x30, 0x02, 0xA0, 0x00}));
}

TEST(ConstructedScopeTests, WriterScope_BlocksEncodeAndReset) {
    writer w(encoding::der);
    {
        auto scope = w.enter_sequence(tag::sequence);
        w.write_null(tag::null);
        EXPECT_THROW((void)w.encode(), std::logic_error);
        std::uint8_t buf[16];
        std::size_t written = 0;
        EXPECT_THROW((void)w.try_encode(buf, written), std::logic_error);
        EXPECT_THROW(w.reset(), std::logic_error);
    }
    EXPECT_EQ(w.encode(), hex({0x30, 0x02, 0x05, 0x00}));
}

TEST(ConstructedScopeTests, WriterScope_ExceptionFinalizesPartialValue) {
    writer w(encoding::der);
    try {
        auto scope = w.enter_sequence(tag::sequence);
        w.write_integer(tag::integer, 1);
        throw std::runtime_error("boom");
    } catch (const std::runtime_error& ex) {
        EXPECT_STREQ(ex.what(), "boom");
    }
    EXPECT_EQ(w.encode(), hex({0x30, 0x03, 0x02, 0x01, 0x01}));
    w.reset();
    w.write_null(tag::null);
    EXPECT_EQ(w.encode(), hex({0x05, 0x00}));
}

TEST(ConstructedScopeTests, EnterEncoded_RestoresWindow) {
    const auto retained_inner = hex({0x30, 0x03, 0x02, 0x01, 0x02});
    writer w(encoding::der);
    {
        auto outer = w.enter_sequence(tag::sequence);
        w.write_integer(tag::integer, 1);
        w.write_raw(retained_inner);
        w.write_integer(tag::integer, 3);
    }
    const auto encoded = w.encode();

    reader r(encoded, encoding::der);
    int a = 0;
    int b = 0;
    int c = 0;
    r.with_sequence(tag::sequence, [&](reader& outer) {
        a = outer.read_int32();
        {
            auto skip = outer.enter_sequence(tag::sequence);
            (void)outer.read_int32();
        }
        outer.with_encoded(bytes::copy_from(retained_inner), [&](reader& window) {
            window.with_sequence(tag::sequence, [&](reader& inner) { b = inner.read_int32(); });
        });
        c = outer.read_int32();
    });
    EXPECT_EQ(a, 1);
    EXPECT_EQ(b, 2);
    EXPECT_EQ(c, 3);
}

TEST(ConstructedScopeTests, EnterSequence_RejectsWrongTagAndPrimitive) {
    const auto wrong_tag = hex({0x31, 0x00});
    reader r1(wrong_tag, encoding::der);
    EXPECT_THROW(r1.enter_sequence(tag::sequence).end(), exception);

    const auto primitive = hex({0x02, 0x01, 0x01});
    reader r2(primitive, encoding::der);
    EXPECT_THROW(r2.enter_sequence(tag::integer).end(), exception);
}

TEST(ConstructedScopeTests, WriteSequence_DeepNesting_RoundTrips) {
    writer w(encoding::der);
    {
        auto a = w.enter_sequence(tag::sequence);
        auto b = w.enter_sequence(tag::sequence);
        auto c = w.enter_sequence(tag::sequence);
        w.write_integer(tag::integer, 7);
        w.write_integer(tag::integer, 9);
    }
    const auto bytes = w.encode();
    EXPECT_EQ(
        bytes,
        hex({0x30, 0x0A, 0x30, 0x08, 0x30, 0x06, 0x02, 0x01, 0x07, 0x02, 0x01, 0x09}));

    reader r(bytes, encoding::der);
    r.with_sequence(tag::sequence, [&](reader& a) {
        a.with_sequence(tag::sequence, [&](reader& b) {
            b.with_sequence(tag::sequence, [&](reader& c) {
                EXPECT_EQ(c.read_int32(), 7);
                EXPECT_EQ(c.read_int32(), 9);
            });
        });
    });
    EXPECT_TRUE(r.eof());
}

TEST(ConstructedScopeTests, ReaderScope_LifoViolation) {
    const auto bytes = hex({0x30, 0x08, 0x30, 0x03, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02});
    reader r(bytes, encoding::der);
    auto outer = r.enter_sequence(tag::sequence);
    auto inner = r.enter_sequence(tag::sequence);
    EXPECT_THROW(outer.end(), std::logic_error);
    inner.end();
    (void)r.read_int32();
    r.throw_if_not_empty();
    outer.end();
}

namespace {

/// Minimal asn1_decodable / asn1_encodable stand-in for generated types.
struct int_box {
    std::int32_t value{0};

    static tag default_tag() { return tag::integer; }

    static int_box decode(reader& r, const tag& t) {
        return int_box{r.read_int32(t)};
    }

    void encode(writer& w, const tag& t) const {
        w.write_integer(t, value);
    }

    friend bool operator==(const int_box& a, const int_box& b) noexcept {
        return a.value == b.value;
    }
};

struct pair_box {
    int_box a{};
    int_box b{};

    static tag default_tag() { return tag::sequence; }

    static pair_box decode(reader& r, const tag& t) {
        pair_box value{};
        r.with_sequence(t, [&](reader& inner) {
            value.a = inner.read<int_box>();
            value.b = inner.read<int_box>();
        });
        return value;
    }

    void encode(writer& w, const tag& t) const {
        auto scope = w.enter_sequence(t);
        w.write(a);
        w.write(b);
    }

    friend bool operator==(const pair_box& x, const pair_box& y) noexcept {
        return x.a == y.a && x.b == y.b;
    }
};

} // namespace

TEST(ConstructedScopeTests, TypeDriven_ReadWriteWithoutLambdas) {
    const std::vector<int_box> items{{2}, {1}};
    writer w(encoding::der);
    w.write_sequence_of(tag::sequence, items);
    w.write_to_explicit(tag{tag_class::context_specific, 0, true}, pair_box{{7}, {9}});
    w.write_sequence_of_to_explicit(
        tag{tag_class::context_specific, 3, true}, std::span<const int_box>(items));

    const auto encoded = w.encode();
    reader r(encoded, encoding::der);

    const auto of_items = r.read_sequence_of<int_box>();
    ASSERT_EQ(of_items.size(), 2u);
    EXPECT_EQ(of_items[0].value, 2);
    EXPECT_EQ(of_items[1].value, 1);

    const auto pair = r.read_from_explicit<pair_box>(tag{tag_class::context_specific, 0, true});
    EXPECT_EQ(pair, (pair_box{{7}, {9}}));

    const auto explicit_of =
        r.read_sequence_of_from_explicit<int_box>(tag{tag_class::context_specific, 3, true});
    ASSERT_EQ(explicit_of.size(), 2u);
    EXPECT_EQ(explicit_of[0].value, 2);
    EXPECT_EQ(explicit_of[1].value, 1);
    EXPECT_TRUE(r.eof());
}

TEST(ConstructedScopeTests, NextIs_And_ReadFromExplicit_Primitive) {
    const tag ver_tag{tag_class::context_specific, 0, true};
    writer w(encoding::der);
    {
        auto outer = w.enter_sequence(tag::sequence);
        w.write_to_explicit(ver_tag, std::int32_t{2});
        w.write_integer(tag::integer, 42);
    }

    reader r(w.encode(), encoding::der);
    std::int32_t version = 0;
    std::int32_t serial = 0;
    r.with_sequence(tag::sequence, [&](reader& inner) {
        if (inner.next_is(ver_tag)) {
            version = inner.read_from_explicit<std::int32_t>(ver_tag);
        }
        serial = inner.read<std::int32_t>();
    });
    EXPECT_EQ(version, 2);
    EXPECT_EQ(serial, 42);
}

TEST(ConstructedScopeTests, TypeDriven_SetOfDerSorts) {
    const std::vector<int_box> items{{2}, {1}};
    writer w(encoding::der);
    w.write_set_of(tag::set, items);
    EXPECT_EQ(w.encode(), hex({0x31, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x02}));

    reader r(w.encode(), encoding::der);
    const auto decoded = r.read_set_of<int_box>();
    ASSERT_EQ(decoded.size(), 2u);
    EXPECT_EQ(decoded[0].value, 1);
    EXPECT_EQ(decoded[1].value, 2);
}
