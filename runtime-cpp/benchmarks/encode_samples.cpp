// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "encode_samples.hpp"

#include "PKIX1Implicit2009.hpp"
#include "PKIXCommonTypes2009.hpp"

#include "asn1kit/bit_string.hpp"
#include "asn1kit/bytes.hpp"
#include "asn1kit/codec.hpp"
#include "asn1kit/integer.hpp"
#include "asn1kit/null.hpp"
#include "asn1kit/writer.hpp"

#include <cstdint>
#include <vector>

namespace asn1kit::benchmarks {
namespace {

namespace Pkix = asn1kit::modern::PKIX1Explicit2009;
namespace Implicit = asn1kit::modern::PKIX1Implicit2009;
namespace Common = asn1kit::modern::PKIXCommonTypes2009;
namespace Alg = asn1kit::modern::AlgorithmInformation2009;

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
    Alg::AlgorithmIdentifier alg{};
    alg.algorithm = asn1kit::oid_literal<"1.2.840.113549.1.1.11">().get();
    alg.parameters = asn1kit::encode_open(asn1kit::null_value::value());
    return alg;
}

[[nodiscard]] Alg::AlgorithmIdentifier rsa_encryption() {
    Alg::AlgorithmIdentifier alg{};
    alg.algorithm = asn1kit::oid_literal<"1.2.840.113549.1.1.1">().get();
    alg.parameters = asn1kit::encode_open(asn1kit::null_value::value());
    return alg;
}

[[nodiscard]] Name make_name(const char* common_name) {
    Name name;
    name.push_back({Common::supportedAttributes_value_bindings::x520country_name.create(
        asn1kit::asn_string<asn1kit::string_form::printable>{"US"})});
    name.push_back({Common::supportedAttributes_value_bindings::x520_organization_name.create(
        Pkix::DirectoryString::from_printableString("Test Certificates 2011"))});
    name.push_back({Common::supportedAttributes_value_bindings::x520_common_name.create(
        Pkix::DirectoryString::from_printableString(common_name))});
    return name;
}

[[nodiscard]] Common::Extension with_critical(Common::Extension ext, bool critical) {
    ext.critical = critical;
    return ext;
}

[[nodiscard]] Common::Extension crl_reason_extension() {
    asn1kit::writer w(asn1kit::encoding::der);
    Implicit::encode_CRLReason(w, Implicit::CRLReason::keyCompromise);
    Common::Extension ext{};
    ext.extnID = Implicit::PKIX1Implicit2009Oids::IdCeCRLReasons.get();
    ext.critical = false;
    ext.extnValue = asn1kit::bytes::from_vector(w.encode());
    return ext;
}

[[nodiscard]] Pkix::SubjectPublicKeyInfo create_spki() {
    Pkix::SubjectPublicKeyInfo spki{};
    spki.algorithm = rsa_encryption();
    spki.subjectPublicKey =
        asn1kit::bit_string::copy_from(filled(270, 0x30), 0);
    return spki;
}

[[nodiscard]] Pkix::TBSCertList_RevokedCertificates_Item revoked(std::int32_t serial) {
    Pkix::TBSCertList_RevokedCertificates_Item item{};
    item.userCertificate = asn1kit::integer::from_int32(serial);
    item.revocationDate = Pkix::Time::from_utcTime(not_before());
    item.crlEntryExtensions = std::vector<Common::Extension>{crl_reason_extension()};
    return item;
}

} // namespace

Pkix::Certificate create_certificate() {
    const auto name = make_name("Trust Anchor");
    const auto signature_algorithm = sha256_with_rsa();

    Pkix::TBSCertificate tbs{};
    tbs.version = Pkix::Version::V3;
    tbs.serialNumber = asn1kit::integer::from_int32(1);
    tbs.signature = signature_algorithm;
    tbs.issuer = asn1kit::value<Name>::from_value(name);
    tbs.validity.notBefore = Pkix::Time::from_utcTime(not_before());
    tbs.validity.notAfter = Pkix::Time::from_utcTime(not_after());
    tbs.subject = asn1kit::value<Name>::from_value(make_name("Trust Anchor"));
    tbs.subjectPublicKeyInfo = asn1kit::value<Pkix::SubjectPublicKeyInfo>::from_value(create_spki());

    const auto ski_bytes = filled(20, 0xE4);
    std::vector<Common::Extension> extensions{
        Common::certExtensions_bindings::subject_key_identifier.create(
            asn1kit::bytes::copy_from(ski_bytes)),
        with_critical(
            Common::certExtensions_bindings::key_usage.create(Implicit::KeyUsage::from_flags(
                Implicit::KeyUsageFlags::keyCertSign | Implicit::KeyUsageFlags::cRLSign)),
            true),
        with_critical(
            Common::certExtensions_bindings::basic_constraints.create(
                Implicit::BasicConstraints{true, std::nullopt}),
            true),
    };
    tbs.extensions = std::move(extensions);

    Pkix::Certificate cert{};
    cert.toBeSigned = asn1kit::value<Pkix::TBSCertificate>::from_value(std::move(tbs));
    cert.algorithmIdentifier = signature_algorithm;
    cert.signature = asn1kit::bit_string::copy_from(filled(256, 0xC5), 0);
    return cert;
}

Pkix::CertificateList create_certificate_list() {
    const auto signature_algorithm = sha256_with_rsa();

    Pkix::TBSCertList tbs{};
    tbs.version = Pkix::Version::V2;
    tbs.signature = signature_algorithm;
    tbs.issuer = make_name("Good CA");
    tbs.thisUpdate = Pkix::Time::from_utcTime(not_before());
    tbs.nextUpdate = Pkix::Time::from_utcTime(not_after());
    tbs.revokedCertificates = std::vector<Pkix::TBSCertList_RevokedCertificates_Item>{
        revoked(0x0E),
        revoked(0x0F),
    };

    Implicit::AuthorityKeyIdentifier aki{};
    aki.keyIdentifier = asn1kit::bytes::copy_from(filled(20, 0x58));
    tbs.crlExtensions = std::vector<Common::Extension>{
        Common::crlExtensions_bindings::authority_key_identifier.create(aki),
        Common::crlExtensions_bindings::c_r_l_number.create(asn1kit::integer::from_int32(1)),
    };

    Pkix::CertificateList crl{};
    crl.toBeSigned = std::move(tbs);
    crl.algorithmIdentifier = signature_algorithm;
    crl.signature = asn1kit::bit_string::copy_from(filled(256, 0xC5), 0);
    return crl;
}

} // namespace asn1kit::benchmarks
