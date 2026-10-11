// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "PKIX1Explicit2009.hpp"

#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <fstream>
#include <string>
#include <vector>

using asn1kit::encoding;
using asn1kit::reader;
using asn1kit::writer;
using Certificate = asn1kit::modern::PKIX1Explicit2009::Certificate;
using PkixOids = asn1kit::modern::PKIX1Explicit2009::PKIX1Explicit2009Oids;

static_assert(std::string_view(PkixOids::IdPkix.text()) == "1.3.6.1.5.5.7");
static_assert(PkixOids::IdPkix.contents == std::array<std::uint8_t, 6>{0x2b, 6, 1, 5, 5, 7});

TEST(GeneratedModernCertificate, OidCatalogUsesStaticContents) {
    const auto value = PkixOids::IdPkix.get();
    EXPECT_EQ(value.span().data(), PkixOids::IdPkix.span().data());
    EXPECT_EQ(value.to_string(), PkixOids::IdPkix.text());
}

#ifndef ASN1KIT_PKIX_FIXTURES_DIR
#error ASN1KIT_PKIX_FIXTURES_DIR must be defined
#endif

namespace {

std::vector<std::uint8_t> read_file(const std::string& name) {
    const auto path = std::string(ASN1KIT_PKIX_FIXTURES_DIR) + "/" + name;
    std::ifstream input(path, std::ios::binary);
    EXPECT_TRUE(input.good()) << "missing fixture " << path;
    return std::vector<std::uint8_t>(
        (std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
}

void round_trip_certificate(const std::string& file) {
    const auto bytes = read_file(file);
    ASSERT_FALSE(bytes.empty());

    reader r(bytes, encoding::der);
    const auto decoded = Certificate::decode(r);
    EXPECT_TRUE(r.eof());

    writer w(encoding::der);
    decoded.encode(w);
    EXPECT_EQ(w.encode(), bytes);
}

} // namespace

TEST(GeneratedModernCertificateTests, TrustAnchorRootCertificateRoundTrip) {
    round_trip_certificate("TrustAnchorRootCertificate.crt");
}

TEST(GeneratedModernCertificateTests, ValidCertificatePathTest1EERoundTrip) {
    round_trip_certificate("ValidCertificatePathTest1EE.crt");
}
