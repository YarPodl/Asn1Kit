// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "CryptographicMessageSyntax2009.hpp"

#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <fstream>
#include <string>
#include <vector>

#ifndef ASN1KIT_CMS_RFC4134_FIXTURES_DIR
#error ASN1KIT_CMS_RFC4134_FIXTURES_DIR must be defined
#endif

using asn1kit::encoding;
using asn1kit::reader;
using asn1kit::writer;
namespace Cms = asn1kit::modern::CryptographicMessageSyntax2009;
using CmsOids = Cms::CryptographicMessageSyntax2009Oids;

namespace {

std::vector<std::uint8_t> read_file(const std::string& name) {
    const auto path = std::string(ASN1KIT_CMS_RFC4134_FIXTURES_DIR) + "/" + name;
    std::ifstream input(path, std::ios::binary);
    EXPECT_TRUE(input.good()) << "missing fixture " << path;
    return std::vector<std::uint8_t>(
        (std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
}

void round_trip_content_info(const std::string& file) {
    const auto bytes = read_file(file);
    ASSERT_FALSE(bytes.empty());

    reader r(bytes, encoding::der);
    const auto decoded = Cms::ContentInfo::decode(r);
    EXPECT_TRUE(r.eof());

    writer w(encoding::der);
    decoded.encode(w);
    EXPECT_EQ(w.encode(), bytes);
}

} // namespace

TEST(GeneratedModernCmsTests, DataContentInfo_3_2_RoundTrip) {
    const auto bytes = read_file("3.2.bin");
    ASSERT_FALSE(bytes.empty());

    reader r(bytes, encoding::der);
    const auto info = Cms::ContentInfo::decode(r);
    EXPECT_TRUE(r.eof());
    EXPECT_EQ(info.contentType, CmsOids::IdData.get());

    // id-data is not in ContentSet typed bindings; payload stays asn1kit::any / OCTET STRING.
    reader content_reader(info.content.encoded(), encoding::der);
    const auto octets = content_reader.read_octet_string(asn1kit::tag::octet_string);
    EXPECT_TRUE(content_reader.eof());
    EXPECT_EQ(octets.size(), 28u);

    writer w(encoding::der);
    info.encode(w);
    EXPECT_EQ(w.encode(), bytes);
}

TEST(GeneratedModernCmsTests, SignedData_4_2_RoundTrip) {
    round_trip_content_info("4.2.bin");

    const auto bytes = read_file("4.2.bin");
    reader r(bytes, encoding::der);
    auto info = Cms::ContentInfo::decode(r);
    EXPECT_EQ(info.contentType, CmsOids::IdSignedData.get());
    auto signed_data = std::move(info).content_set().ct_signed_data();
    ASSERT_TRUE(signed_data.has_value());
    EXPECT_EQ(signed_data->signerInfos.size(), 1u);
    EXPECT_TRUE(signed_data->encapContentInfo.eContent.has_value());
}

TEST(GeneratedModernCmsTests, SignedData_4_3_DetachedRoundTrip) {
    round_trip_content_info("4.3.bin");

    const auto bytes = read_file("4.3.bin");
    reader r(bytes, encoding::der);
    auto info = Cms::ContentInfo::decode(r);
    auto signed_data = std::move(info).content_set().ct_signed_data();
    ASSERT_TRUE(signed_data.has_value());
    EXPECT_FALSE(signed_data->encapContentInfo.eContent.has_value());
    EXPECT_EQ(signed_data->encapContentInfo.eContentType, CmsOids::IdData.get());
}

TEST(GeneratedModernCmsTests, SignedData_4_6_MultipleSignersRoundTrip) {
    round_trip_content_info("4.6.bin");

    const auto bytes = read_file("4.6.bin");
    reader r(bytes, encoding::der);
    auto info = Cms::ContentInfo::decode(r);
    auto signed_data = std::move(info).content_set().ct_signed_data();
    ASSERT_TRUE(signed_data.has_value());
    EXPECT_EQ(signed_data->signerInfos.size(), 2u);
}

TEST(GeneratedModernCmsTests, SignedData_4_7_SkiRoundTrip) {
    round_trip_content_info("4.7.bin");

    const auto bytes = read_file("4.7.bin");
    reader r(bytes, encoding::der);
    auto info = Cms::ContentInfo::decode(r);
    auto signed_data = std::move(info).content_set().ct_signed_data();
    ASSERT_TRUE(signed_data.has_value());
    ASSERT_EQ(signed_data->signerInfos.size(), 1u);
    EXPECT_EQ(
        signed_data->signerInfos[0].sid.kind,
        Cms::SignerIdentifier_kind::subjectKeyIdentifier);
    EXPECT_EQ(signed_data->version, 3);
}

TEST(GeneratedModernCmsTests, SignedData_4_10_SignedAttrsRoundTrip) {
    round_trip_content_info("4.10.bin");

    const auto bytes = read_file("4.10.bin");
    reader r(bytes, encoding::der);
    auto info = Cms::ContentInfo::decode(r);
    auto signed_data = std::move(info).content_set().ct_signed_data();
    ASSERT_TRUE(signed_data.has_value());
    ASSERT_EQ(signed_data->signerInfos.size(), 1u);
    ASSERT_TRUE(signed_data->signerInfos[0].signedAttrs.has_value());
    EXPECT_TRUE(signed_data->signerInfos[0].signedAttrs->has_original_encoding());
    const auto retained = signed_data->signerInfos[0].signedAttrs->original_encoding().span();
    ASSERT_FALSE(retained.empty());
    EXPECT_EQ(retained[0], 0xA0);
}
