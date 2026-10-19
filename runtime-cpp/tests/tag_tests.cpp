// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/null.hpp"
#include "asn1kit/reader_options.hpp"
#include "asn1kit/tag.hpp"
#include "asn1kit/utc_date_time.hpp"

#include <gtest/gtest.h>

using namespace asn1kit;

static_assert(tag::context(2) == tag(tag_class::context_specific, 2, false));
static_assert(tag::context(0, true) == tag(tag_class::context_specific, 0, true));
static_assert(tag::application(1) == tag(tag_class::application, 1, false));
static_assert(tag::private_(3, true) == tag(tag_class::private_, 3, true));
static_assert(tag::context(5).as_constructed() == tag::context(5, true));

static_assert(null_value::value() == null_value{});

constexpr auto default_reader_options = reader_options::default_profile();
static_assert(!default_reader_options.reject_non_minimal_integer);
static_assert(!default_reader_options.reject_bit_string_trailing_bits);
static_assert(default_reader_options.reject_non_minimal_length);
static_assert(default_reader_options.reject_overlong_oid_base128);
static_assert(default_reader_options.max_recursion_depth == 64);
static_assert(default_reader_options.max_materialized_constructed_value_bytes == 100u * 1024u * 1024u);
static_assert(default_reader_options.max_constructed_segments == 4096);

constexpr auto strict_reader_options = reader_options::strict();
static_assert(strict_reader_options.reject_non_minimal_integer);
static_assert(strict_reader_options.reject_bit_string_trailing_bits);
static_assert(strict_reader_options.reject_non_minimal_length);
static_assert(strict_reader_options.reject_overlong_oid_base128);

constexpr auto relaxed_length_options = reader_options::allow_non_minimal_length();
static_assert(!relaxed_length_options.reject_non_minimal_length);

constexpr auto relaxed_oid_options = reader_options::allow_overlong_oid_base128();
static_assert(!relaxed_oid_options.reject_overlong_oid_base128);

constexpr utc_date_time epoch{1970, 1, 1, 0, 0, 0, time_fraction{}};
static_assert(epoch == utc_date_time{1970, 1, 1, 0, 0, 0, time_fraction{}});
static_assert(epoch != utc_date_time{1970, 1, 1, 0, 0, 1, time_fraction{}});

TEST(TagFactories, MatchConstructor) {
    EXPECT_EQ(tag::context(2), tag(tag_class::context_specific, 2, false));
    EXPECT_EQ(tag::context(0, true), tag(tag_class::context_specific, 0, true));
    EXPECT_EQ(tag::application(1), tag(tag_class::application, 1, false));
    EXPECT_EQ(tag::private_(3, true), tag(tag_class::private_, 3, true));
}
