// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "encode_samples.hpp"

#include "AlgorithmInformation2009.hpp"
#include "PKIX1Implicit2009.hpp"
#include "PKIXCommonTypes2009.hpp"

#include "asn1kit/bit_string.hpp"
#include "asn1kit/bytes.hpp"
#include "asn1kit/codec.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/null.hpp"
#include "asn1kit/writer.hpp"

#include <cstdint>
#include <limits>
#include <stdexcept>
#include <vector>

namespace asn1kit::benchmarks {
namespace {

namespace Pkix = asn1kit::modern::PKIX1Explicit2009;
namespace Implicit = asn1kit::modern::PKIX1Implicit2009;
namespace Common = asn1kit::modern::PKIXCommonTypes2009;
namespace Alg = asn1kit::modern::AlgorithmInformation2009;
namespace PssOaep = asn1kit::modern::PKIX1PSSOAEPAlgorithms2009;

using Attr = Common::SingleAttribute;
using Name = std::vector<std::vector<Attr>>;

[[nodiscard]] std::vector<std::uint8_t> filled(std::size_t length, std::uint8_t seed) {
    std::vector<std::uint8_t> bytes(length);
    for (std::size_t i = 0; i < length; ++i) {
        bytes[i] = static_cast<std::uint8_t>(seed + static_cast<std::uint8_t>(i));
    }
    return bytes;
}

[[nodiscard]] asn1kit::utc_date_time not_before() {
    return asn1kit::utc_date_time{2010, 1, 1, 8, 30, 0, {}};
}

[[nodiscard]] asn1kit::utc_date_time not_after() {
    return asn1kit::utc_date_time{2030, 12, 31, 8, 30, 0, {}};
}

[[nodiscard]] Alg::AlgorithmIdentifier sha256_with_rsa() {
    const auto binding = Alg::AlgorithmIdentifier::open_binding<asn1kit::null_value>(
        PssOaep::PKIX1PSSOAEPAlgorithms2009Oids::Sha256WithRSAEncryption);
    return Alg::AlgorithmIdentifier::create(binding, asn1kit::null_value::value());
}

[[nodiscard]] Alg::AlgorithmIdentifier rsa_public_key() {
    return Alg::AlgorithmIdentifier::make_public_key_algorithms().rsa(
        asn1kit::null_value::value());
}

[[nodiscard]] Name make_name(const char* common_name) {
    Name name;
    name.push_back({Common::SingleAttribute::make_supported_attributes().x520country_name(
        asn1kit::asn_string<asn1kit::string_form::printable>{"US"})});
    name.push_back({Common::SingleAttribute::make_supported_attributes().x520_organization_name(
        Pkix::DirectoryString::from_printableString("Test Certificates 2011"))});
    name.push_back({Common::SingleAttribute::make_supported_attributes().x520_common_name(
        Pkix::DirectoryString::from_printableString(common_name))});
    return name;
}

[[nodiscard]] Common::Extension with_critical(Common::Extension ext, bool critical) {
    ext.critical = critical;
    return ext;
}

[[nodiscard]] Pkix::SubjectPublicKeyInfo create_spki() {
    Pkix::SubjectPublicKeyInfo spki{};
    spki.algorithm = rsa_public_key();
    spki.subjectPublicKey = asn1kit::bit_string(
        asn1kit::bytes::from_vector(filled(270, 0x30)), 0);
    return spki;
}

[[nodiscard]] Pkix::TBSCertList_RevokedCertificates_Item revoked(
    std::int32_t serial,
    const Common::Extension& reason_extension) {
    Pkix::TBSCertList_RevokedCertificates_Item item{};
    item.userCertificate = asn1kit::integer::from_int32(serial);
    item.revocationDate = Pkix::Time::from_utcTime(not_before());
    item.crlEntryExtensions = std::vector<Common::Extension>{reason_extension};
    return item;
}

} // namespace

Pkix::Certificate create_certificate() {
    const auto signature_algorithm = sha256_with_rsa();

    Pkix::TBSCertificate tbs{};
    tbs.version = Pkix::Version::V3;
    tbs.serialNumber = asn1kit::integer::from_int32(1);
    tbs.signature = signature_algorithm;
    tbs.issuer = asn1kit::value<Name>::from_value(make_name("Trust Anchor"));
    tbs.validity.notBefore = Pkix::Time::from_utcTime(not_before());
    tbs.validity.notAfter = Pkix::Time::from_utcTime(not_after());
    tbs.subject = asn1kit::value<Name>::from_value(make_name("Trust Anchor"));
    tbs.subjectPublicKeyInfo = asn1kit::value<Pkix::SubjectPublicKeyInfo>::from_value(create_spki());

    std::vector<Common::Extension> extensions{
        Common::Extension::make_cert_extensions().subject_key_identifier(
            asn1kit::bytes::from_vector(filled(20, 0xE4))),
        with_critical(
            Common::Extension::make_cert_extensions().key_usage(Implicit::KeyUsage::from_flags(
                Implicit::KeyUsageFlags::keyCertSign | Implicit::KeyUsageFlags::cRLSign)),
            true),
        with_critical(
            Common::Extension::make_cert_extensions().basic_constraints(
                Implicit::BasicConstraints{true, std::nullopt}),
            true),
    };
    tbs.extensions = std::move(extensions);

    Pkix::Certificate cert{};
    cert.toBeSigned = asn1kit::value<Pkix::TBSCertificate>::from_value(std::move(tbs));
    cert.algorithmIdentifier = signature_algorithm;
    cert.signature = asn1kit::bit_string(
        asn1kit::bytes::from_vector(filled(256, 0xC5)), 0);
    return cert;
}

Pkix::CertificateList create_certificate_list(std::size_t revoked_certificate_count) {
    if (revoked_certificate_count >
        static_cast<std::size_t>((std::numeric_limits<std::int32_t>::max)() - 0x0D)) {
        throw std::length_error("CRL entry count exceeds the supported serial number range.");
    }

    const auto signature_algorithm = sha256_with_rsa();

    Pkix::TBSCertList tbs{};
    tbs.version = Pkix::Version::V2;
    tbs.signature = signature_algorithm;
    tbs.issuer = make_name("Good CA");
    tbs.thisUpdate = Pkix::Time::from_utcTime(not_before());
    tbs.nextUpdate = Pkix::Time::from_utcTime(not_after());
    const auto reason_extension =
        Common::Extension::make_crl_entry_extensions().crl_reason(
            Implicit::CRLReason::keyCompromise);
    std::vector<Pkix::TBSCertList_RevokedCertificates_Item> revoked_certificates;
    revoked_certificates.reserve(revoked_certificate_count);
    for (std::size_t i = 0; i < revoked_certificate_count; ++i) {
        revoked_certificates.push_back(
            revoked(static_cast<std::int32_t>(i + 0x0E), reason_extension));
    }
    tbs.revokedCertificates = std::move(revoked_certificates);

    Implicit::AuthorityKeyIdentifier aki{};
    aki.keyIdentifier = asn1kit::bytes::from_vector(filled(20, 0x58));
    tbs.crlExtensions = std::vector<Common::Extension>{
        Common::Extension::make_crl_extensions().authority_key_identifier(aki),
        Common::Extension::make_crl_extensions().crl_number(asn1kit::integer::from_int32(1)),
    };

    Pkix::CertificateList crl{};
    crl.toBeSigned = std::move(tbs);
    crl.algorithmIdentifier = signature_algorithm;
    crl.signature = asn1kit::bit_string(
        asn1kit::bytes::from_vector(filled(256, 0xC5)), 0);
    return crl;
}

} // namespace asn1kit::benchmarks
