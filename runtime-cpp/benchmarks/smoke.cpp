// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "smoke.hpp"

#include "encode_samples.hpp"
#include "fixture_loader.hpp"

#include "CryptographicMessageSyntax2009.hpp"
#include "PKIX1Explicit2009.hpp"

#include "asn1kit/encode_decode.hpp"

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

    const auto crl_der = read_pkix_fixture("GoodCACRL.crl");
    require(!crl_der.empty(), "CRL fixture is empty.");
    const auto crl = asn1kit::decode<Pkix::CertificateList>(crl_der, asn1kit::encoding::der);
    const auto crl_roundtrip = asn1kit::encode(crl, asn1kit::encoding::der);
    require(crl_roundtrip == crl_der, "CRL encode must round-trip fixture bytes.");

    const auto cms_der = read_cms_fixture("attached-signeddata.p7m");
    require(!cms_der.empty(), "CMS fixture is empty.");
    const auto info = asn1kit::decode<Cms::ContentInfo>(cms_der, asn1kit::encoding::der);
    using content_open_types = Cms::ContentInfo::open_types::content_set::content;
    auto decoded_signed_data = info.try_decode(content_open_types::ct_signed_data);
    require(decoded_signed_data.has_value(), "CMS fixture must contain SignedData.");
    auto signed_data = std::move(*decoded_signed_data);
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
    rebound.set(content_open_types::ct_signed_data, signed_data);
    require(!asn1kit::encode(rebound, asn1kit::encoding::der).empty(), "CMS Lazy Encode smoke failed.");
}

} // namespace asn1kit::benchmarks
