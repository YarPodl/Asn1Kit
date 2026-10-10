// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/tag.hpp"

#include <gtest/gtest.h>

using namespace asn1kit;

static_assert(tag::context(2) == tag(tag_class::context_specific, 2, false));
static_assert(tag::context(0, true) == tag(tag_class::context_specific, 0, true));
static_assert(tag::application(1) == tag(tag_class::application, 1, false));
static_assert(tag::private_(3, true) == tag(tag_class::private_, 3, true));
static_assert(tag::context(5).as_constructed() == tag::context(5, true));

TEST(TagFactories, MatchConstructor) {
    EXPECT_EQ(tag::context(2), tag(tag_class::context_specific, 2, false));
    EXPECT_EQ(tag::context(0, true), tag(tag_class::context_specific, 0, true));
    EXPECT_EQ(tag::application(1), tag(tag_class::application, 1, false));
    EXPECT_EQ(tag::private_(3, true), tag(tag_class::private_, 3, true));
}
