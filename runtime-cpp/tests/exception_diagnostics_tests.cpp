// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/exception.hpp"
#include "asn1kit/oid.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/tag.hpp"

#include <gtest/gtest.h>

#include <functional>
#include <string>
#include <vector>

using namespace asn1kit;

namespace {

exception catch_exception(const std::function<void()>& action) {
    try {
        action();
    } catch (const exception& ex) {
        return ex;
    }
    ADD_FAILURE() << "Expected asn1kit::exception";
    return exception("missing exception");
}

} // namespace

TEST(ExceptionDiagnostics, LengthExceedsBuffer_ReportsAbsoluteOffset) {
    // OCTET STRING length 5, only 2 content bytes present.
    const std::vector<std::uint8_t> data{0x04, 0x05, 0x01, 0x02};
    reader r(data, encoding::der);
    const auto ex = catch_exception([&] { (void)r.read_octet_string(); });
    EXPECT_TRUE(ex.has_offset());
    EXPECT_EQ(2u, ex.offset());
    EXPECT_NE(std::string::npos, std::string(ex.what()).find("Length exceeds buffer."));
    EXPECT_NE(std::string::npos, std::string(ex.what()).find("offset=2"));
    EXPECT_EQ(std::string::npos, std::string(ex.what()).find(" at "));
}

TEST(ExceptionDiagnostics, TruncatedTlv_ReportsAbsoluteOffset) {
    const std::vector<std::uint8_t> data{0x04};
    reader r(data, encoding::der);
    const auto ex = catch_exception([&] { (void)r.read_octet_string(); });
    EXPECT_TRUE(ex.has_offset());
    EXPECT_EQ(1u, ex.offset());
    EXPECT_NE(std::string::npos, std::string(ex.what()).find("Unexpected end of ASN.1 data."));
    EXPECT_NE(std::string::npos, std::string(ex.what()).find("offset=1"));
}

TEST(ExceptionDiagnostics, TagMismatch_ReportsTlvStartOffset) {
    // INTEGER where NULL is expected.
    const std::vector<std::uint8_t> data{0x02, 0x01, 0x00};
    reader r(data, encoding::der);
    const auto ex = catch_exception([&] { r.read_null(); });
    EXPECT_TRUE(ex.has_offset());
    EXPECT_EQ(0u, ex.offset());
    EXPECT_NE(std::string::npos, std::string(ex.what()).find("Expected tag"));
    EXPECT_NE(std::string::npos, std::string(ex.what()).find("offset=0"));
}

TEST(ExceptionDiagnostics, NestedConstructedOctet_ReportsAbsoluteOffset) {
    // Constructed OCTET STRING with a second segment that is INTEGER (wrong tag).
    // Offsets: 0:24 1:07 2:04 3:02 4:01 5:02 6:02 <- mismatch
    const std::vector<std::uint8_t> data{0x24, 0x07, 0x04, 0x02, 0x01, 0x02, 0x02, 0x01, 0x00};
    reader r(data, encoding::ber);
    const auto ex = catch_exception([&] { (void)r.read_octet_string(); });
    EXPECT_TRUE(ex.has_offset());
    EXPECT_EQ(6u, ex.offset());
    EXPECT_NE(std::string::npos, std::string(ex.what()).find("Expected tag"));
    EXPECT_NE(std::string::npos, std::string(ex.what()).find("offset=6"));
}

TEST(ExceptionDiagnostics, TrailingData_ReportsOffset) {
    const std::vector<std::uint8_t> data{0x05, 0x00, 0xFF};
    reader r(data, encoding::der);
    r.read_null();
    const auto ex = catch_exception([&] { r.throw_if_not_empty(); });
    EXPECT_TRUE(ex.has_offset());
    EXPECT_EQ(2u, ex.offset());
    EXPECT_NE(std::string::npos, std::string(ex.what()).find("trailing data"));
}

TEST(ExceptionDiagnostics, EncodeSide_HasNoOffsetSuffix) {
    const auto ex = catch_exception([] { (void)oid::parse("1.2.not-a-number"); });
    EXPECT_FALSE(ex.has_offset());
    EXPECT_EQ(std::string::npos, std::string(ex.what()).find("offset="));
    EXPECT_EQ(std::string::npos, std::string(ex.what()).find(" at "));
}

TEST(ExceptionDiagnostics, ContentError_ReasonOnly) {
    // BOOLEAN with empty contents — content-level errors do not attach buffer offset.
    const std::vector<std::uint8_t> data{0x01, 0x00};
    reader r(data, encoding::der);
    const auto ex = catch_exception([&] { (void)r.read_boolean(); });
    EXPECT_FALSE(ex.has_offset());
    EXPECT_NE(std::string::npos, std::string(ex.what()).find("BOOLEAN must contain one octet."));
    EXPECT_EQ(std::string::npos, std::string(ex.what()).find("offset="));
}
