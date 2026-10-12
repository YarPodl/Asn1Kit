// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "PKIX1Explicit2009.hpp"
#include "PKIX1Implicit2009.hpp"

#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <gtest/gtest.h>

#include <cstdint>
#include <fstream>
#include <string>
#include <type_traits>
#include <vector>

using asn1kit::encoding;
using asn1kit::reader;
using asn1kit::writer;
using Certificate = asn1kit::modern::PKIX1Explicit2009::Certificate;
using PkixOids = asn1kit::modern::PKIX1Explicit2009::PKIX1Explicit2009Oids;
using Time = asn1kit::modern::PKIX1Explicit2009::Time;
using TimeKind = asn1kit::modern::PKIX1Explicit2009::Time_kind;
using DirectoryString = asn1kit::modern::PKIX1Explicit2009::DirectoryString;
using GeneralName = asn1kit::modern::PKIX1Implicit2009::GeneralName;
using GeneralNameKind = asn1kit::modern::PKIX1Implicit2009::GeneralName_kind;

static_assert(std::is_same_v<decltype(Time::value), asn1kit::utc_date_time>);
static_assert(std::is_same_v<decltype(DirectoryString::value), std::string>);

TEST(GeneratedModernCertificateTests, DirectoryStringPreservesStringForm) {
    const std::vector<std::vector<std::uint8_t>> vectors{
        {0x0c, 1, 'A'}, {0x13, 1, 'A'}, {0x1e, 2, 0, 'A'}};
    for (const auto& bytes : vectors) {
        reader r(bytes, encoding::der);
        const auto value = DirectoryString::decode(r);
        EXPECT_TRUE(r.eof());
        EXPECT_EQ(value.value, "A");
        writer w(encoding::der);
        value.encode(w);
        EXPECT_EQ(w.encode(), bytes);
    }
    const std::vector<std::uint8_t> invalid{0x02, 1, 1};
    reader r(invalid, encoding::der);
    EXPECT_THROW(DirectoryString::decode(r), asn1kit::exception);
}

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

TEST(GeneratedModernCertificateTests, TimeUsesOneValueForBothWireForms) {
    const auto date = asn1kit::utc_date_time::parse_iso("2000-01-02T03:04:05Z");
    for (const auto& time : {Time::from_utcTime(date), Time::from_generalTime(date)}) {
        const bool utc = time.kind == TimeKind::utcTime;
        const std::string text = utc ? "000102030405Z" : "20000102030405Z";
        std::vector<std::uint8_t> expected{
            static_cast<std::uint8_t>(utc ? 0x17 : 0x18), static_cast<std::uint8_t>(text.size())};
        expected.insert(expected.end(), text.begin(), text.end());
        writer w(encoding::der);
        time.encode(w);
        EXPECT_EQ(w.encode(), expected);
        reader r(expected, encoding::der);
        const auto decoded = Time::decode(r);
        EXPECT_TRUE(r.eof());
        EXPECT_EQ(decoded.kind, time.kind);
        EXPECT_EQ(decoded.value, date);
        writer again(encoding::der);
        decoded.encode(again);
        EXPECT_EQ(again.encode(), expected);
    }
    const std::vector<std::uint8_t> unknown_tag{0x01, 0x01, 0xff};
    reader unknown(unknown_tag, encoding::der);
    EXPECT_THROW(Time::decode(unknown), asn1kit::exception);
    const std::vector<std::uint8_t> truncated_time{0x17, 0x0d, '0'};
    reader truncated(truncated_time, encoding::der);
    EXPECT_THROW(Time::decode(truncated), asn1kit::exception);
}

TEST(GeneratedModernCertificateTests, GeneralNameSharesStringSlotAndKeepsAlternativeTag) {
    for (const auto& name : {GeneralName::from_rfc822Name("a"), GeneralName::from_dNSName("a"),
                            GeneralName::from_uniformResourceIdentifier("a")}) {
        const std::uint8_t tag = name.kind == GeneralNameKind::rfc822Name ? 0x81
            : name.kind == GeneralNameKind::dNSName ? 0x82 : 0x86;
        const std::vector<std::uint8_t> expected{tag, 1, 'a'};
        EXPECT_EQ(std::get<std::string>(name.value), "a");
        writer w(encoding::der);
        name.encode(w);
        EXPECT_EQ(w.encode(), expected);
        reader r(expected, encoding::der);
        const auto decoded = GeneralName::decode(r);
        EXPECT_TRUE(r.eof());
        EXPECT_EQ(decoded.kind, name.kind);
        EXPECT_EQ(decoded.value.index(), name.value.index());
        EXPECT_EQ(std::get<std::string>(decoded.value), "a");
        writer again(encoding::der);
        decoded.encode(again);
        EXPECT_EQ(again.encode(), expected);
    }
    const auto oid = asn1kit::oid::parse("1.2.3");
    const auto name = GeneralName::from_registeredID(oid);
    writer w(encoding::der);
    name.encode(w);
    const std::vector<std::uint8_t> expected{0x88, 2, 0x2a, 3};
    EXPECT_EQ(w.encode(), expected);
    reader r(expected, encoding::der);
    const auto decoded = GeneralName::decode(r);
    EXPECT_EQ(decoded.kind, GeneralNameKind::registeredID);
    EXPECT_EQ(std::get<asn1kit::oid>(decoded.value), oid);
    EXPECT_TRUE(r.eof());
}
