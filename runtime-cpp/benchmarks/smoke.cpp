// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "smoke.hpp"

#include "encode_samples.hpp"
#include "fixture_loader.hpp"
#include "openssl_support.hpp"

#include "CryptographicMessageSyntax2009.hpp"
#include "PKIX1Explicit2009.hpp"

#include "asn1kit/encode_decode.hpp"

#include <algorithm>
#include <stdexcept>
#include <string>

namespace asn1kit::benchmarks {
namespace {

namespace Cms = asn1kit::modern::CryptographicMessageSyntax2009;
namespace Pkix = asn1kit::modern::PKIX1Explicit2009;

void require(bool ok, const char* message) {
    if (!ok) {
        throw std::runtime_error(message);
    }
}

} // namespace

void verify_fixtures() {
    const auto cert_der = read_pkix_fixture("TrustAnchorRootCertificate.crt");
    require(!cert_der.empty(), "Certificate fixture is empty.");
    const auto cert = asn1kit::decode<Pkix::Certificate>(cert_der, asn1kit::encoding::der);
    require(cert.toBeSigned.has_original_encoding(), "Certificate TBS should retain original encoding.");
    const auto cert_roundtrip = asn1kit::encode(cert, asn1kit::encoding::der);
    require(cert_roundtrip == cert_der, "Certificate encode must round-trip fixture bytes.");
    const auto openssl_cert = openssl_support::decode_certificate(cert_der);
    const auto openssl_cert_roundtrip = openssl_support::encode_certificate(*openssl_cert);
    require(
        std::equal(
            openssl_cert_roundtrip.bytes().begin(),
            openssl_cert_roundtrip.bytes().end(),
            cert_der.begin(),
            cert_der.end()),
        "OpenSSL Certificate encode must round-trip fixture bytes.");
    auto cert_with_trailing_data = cert_der;
    cert_with_trailing_data.push_back(0);
    bool rejected_trailing_data = false;
    try {
        static_cast<void>(openssl_support::decode_certificate(cert_with_trailing_data));
    } catch (const std::runtime_error& ex) {
        rejected_trailing_data =
            std::string(ex.what()).find("trailing DER data") != std::string::npos;
    }
    require(rejected_trailing_data, "OpenSSL Certificate decode must reject trailing data.");

    const auto crl_der = read_pkix_fixture("GoodCACRL.crl");
    require(!crl_der.empty(), "CRL fixture is empty.");
    const auto crl = asn1kit::decode<Pkix::CertificateList>(crl_der, asn1kit::encoding::der);
    const auto crl_roundtrip = asn1kit::encode(crl, asn1kit::encoding::der);
    require(crl_roundtrip == crl_der, "CRL encode must round-trip fixture bytes.");
    const auto openssl_crl = openssl_support::decode_certificate_list(crl_der);
    const auto openssl_crl_roundtrip = openssl_support::encode_certificate_list(*openssl_crl);
    require(
        std::equal(
            openssl_crl_roundtrip.bytes().begin(),
            openssl_crl_roundtrip.bytes().end(),
            crl_der.begin(),
            crl_der.end()),
        "OpenSSL CRL encode must round-trip fixture bytes.");

    const auto cms_der = read_cms_fixture("attached-signeddata.p7m");
    require(!cms_der.empty(), "CMS fixture is empty.");
    const auto info = asn1kit::decode<Cms::ContentInfo>(cms_der, asn1kit::encoding::der);
    auto decoded_signed_data = info.content_set().signed_data();
    require(decoded_signed_data.has_value(), "CMS fixture must contain SignedData.");
    auto signed_data = std::move(*decoded_signed_data);
    const auto openssl_cms = openssl_support::decode_cms(cms_der);
    const auto openssl_cms_roundtrip = openssl_support::encode_cms(*openssl_cms);
    require(
        std::equal(
            openssl_cms_roundtrip.bytes().begin(),
            openssl_cms_roundtrip.bytes().end(),
            cms_der.begin(),
            cms_der.end()),
        "OpenSSL CMS encode must round-trip fixture bytes.");
    require(signed_data.certificates.has_value(), "CMS SignedData must carry certificates.");
    bool found_retained_tbs = false;
    for (const auto& choice : *signed_data.certificates) {
        if (choice.kind != Cms::CertificateChoices_kind::certificate) {
            continue;
        }
        const auto& nested = std::get<0>(choice.value);
        if (nested.toBeSigned.has_original_encoding()) {
            found_retained_tbs = true;
            break;
        }
    }
    require(found_retained_tbs, "CMS certificate should retain TBS original encoding after shell decode.");

    const auto hand_cert = create_certificate();
    require(!asn1kit::encode(hand_cert, asn1kit::encoding::der).empty(), "Hand-built Certificate encode failed.");
    const auto hand_crl = create_certificate_list();
    require(!asn1kit::encode(hand_crl, asn1kit::encoding::der).empty(), "Hand-built CRL encode failed.");

    Cms::ContentInfo rebound = info;
    rebound.content_set().signed_data(signed_data);
    require(!asn1kit::encode(rebound, asn1kit::encoding::der).empty(), "CMS Lazy Encode smoke failed.");
}

} // namespace asn1kit::benchmarks
