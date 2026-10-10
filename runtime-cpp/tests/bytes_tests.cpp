// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/bytes.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/tag.hpp"

#include <gtest/gtest.h>

#include <vector>

using namespace asn1kit;

TEST(BytesKeepAlive, SliceSurvivesReaderDestruction) {
    bytes owned;
    {
        const std::vector<std::uint8_t> tlv{0x04, 0x03, 0xAA, 0xBB, 0xCC};
        reader r(tlv, encoding::der);
        owned = r.read_octet_string(tag::octet_string);
        EXPECT_EQ(3u, owned.size());
        EXPECT_EQ(0xAA, owned[0]);
    }
    // Reader destroyed; bytes must still be valid via keep-alive.
    EXPECT_EQ(3u, owned.size());
    EXPECT_EQ(0xAA, owned[0]);
    EXPECT_EQ(0xBB, owned[1]);
    EXPECT_EQ(0xCC, owned[2]);
}

TEST(BytesKeepAlive, BorrowDoesNotExtendLifetime) {
    std::vector<std::uint8_t> storage{1, 2, 3, 4};
    auto view = bytes::borrow(storage);
    auto sliced = view.slice(1, 2);
    EXPECT_EQ(2u, sliced.size());
    EXPECT_EQ(2, sliced[0]);
    storage[1] = 9;
    EXPECT_EQ(9, sliced[0]);
}

TEST(BytesKeepAlive, CopyFromDetaches) {
    std::vector<std::uint8_t> storage{1, 2, 3};
    auto copied = bytes::copy_from(storage);
    storage[0] = 9;
    EXPECT_EQ(1, copied[0]);
}
