// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "cms_demo.hpp"
#include "format.hpp"
#include "sha256.hpp"

#include "CryptographicMessageSyntax2009.hpp"
#include "PKIX1Explicit2009.hpp"
#include "PKIX1Implicit2009.hpp"
#include "PKIXAlgs2009.hpp"
#include "PKIX1PSSOAEPAlgorithms2009.hpp"
#include "PKIXCommonTypes2009.hpp"

#include "asn1kit/bytes.hpp"
#include "asn1kit/codec.hpp"
#include "asn1kit/encode_decode.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/utc_date_time.hpp"

#include <algorithm>
#include <chrono>
#include <optional>
#include <stdexcept>
#include <utility>

namespace asn1kit::cms_demo {
namespace {

namespace Cms = asn1kit::modern::CryptographicMessageSyntax2009;
namespace Pkix = asn1kit::modern::PKIX1Explicit2009;
namespace Implicit = asn1kit::modern::PKIX1Implicit2009;
namespace Alg = asn1kit::modern::AlgorithmInformation2009;
namespace PssOaep = asn1kit::modern::PKIX1PSSOAEPAlgorithms2009;
namespace PkixAlgs = asn1kit::modern::PKIXAlgs2009;

using CmsOids = Cms::CryptographicMessageSyntax2009Oids;

[[nodiscard]] Cms::SignedData decode_signed_data(Cms::ContentInfo&& info) {
    auto signed_data = std::move(info).content_set().ct_signed_data();
    if (!signed_data) {
        throw std::runtime_error("ContentInfo must be id-signedData and contain SignedData.");
    }
    return std::move(*signed_data);
}

[[nodiscard]] std::span<const std::uint8_t> require_econtent(const Cms::SignedData& signed_data) {
    if (!signed_data.encapContentInfo.eContent.has_value()) {
        throw std::runtime_error("Attached SignedData requires eContent.");
    }
    return signed_data.encapContentInfo.eContent->span();
}

[[nodiscard]] bool digest_algorithm_listed(
    const Cms::SignedData& signed_data,
    const asn1kit::oid& algorithm) {
    for (const auto& item : signed_data.digestAlgorithms) {
        if (item.algorithm == algorithm) {
            return true;
        }
    }
    return false;
}

[[nodiscard]] bool matches_issuer_and_serial(
    const Pkix::Certificate& certificate,
    const Cms::IssuerAndSerialNumber& sid) {
    const auto& tbs = certificate.toBeSigned.get();
    if (tbs.serialNumber != sid.serialNumber) {
        return false;
    }
    if (!tbs.issuer.has_original_encoding() || !sid.issuer.has_original_encoding()) {
        return false;
    }
    return tbs.issuer.original_encoding() == sid.issuer.original_encoding();
}

[[nodiscard]] std::optional<asn1kit::bytes> read_subject_key_identifier(
    const Pkix::Certificate& certificate) {
    return certificate.toBeSigned.get().cert_extensions().subject_key_identifier();
}

[[nodiscard]] bool matches_subject_key_identifier(
    const Pkix::Certificate& certificate,
    const asn1kit::bytes& key_identifier) {
    const auto ski = read_subject_key_identifier(certificate);
    return ski.has_value() && *ski == key_identifier;
}

[[nodiscard]] const Pkix::Certificate* find_certificate(
    const std::optional<std::vector<Cms::CertificateChoices>>& choices,
    const Cms::SignerIdentifier& sid) {
    if (!choices.has_value()) {
        return nullptr;
    }
    const Pkix::Certificate* match = nullptr;
    for (const auto& choice : *choices) {
        if (choice.kind != Cms::CertificateChoices_kind::certificate) {
            continue;
        }
        const auto& certificate = std::get<0>(choice.value);
        bool matches = false;
        if (sid.kind == Cms::SignerIdentifier_kind::issuerAndSerialNumber) {
            matches = matches_issuer_and_serial(certificate, std::get<0>(sid.value));
        } else if (sid.kind == Cms::SignerIdentifier_kind::subjectKeyIdentifier) {
            matches = matches_subject_key_identifier(certificate, std::get<1>(sid.value));
        }
        if (!matches) {
            continue;
        }
        if (match != nullptr) {
            return nullptr;
        }
        match = &certificate;
    }
    return match;
}

[[nodiscard]] asn1kit::bytes read_message_digest(
    const Cms::SignerInfo& signer,
    const asn1kit::oid& econtent_type) {
    if (!signer.signedAttrs.has_value()) {
        throw std::runtime_error("SignedAttrs requires contentType and messageDigest.");
    }

    const auto types = signer.signed_attributes_set().content_type();
    const auto digests = signer.signed_attributes_set().message_digest();
    if (!types || !digests) {
        throw std::runtime_error("SignedAttrs requires contentType and messageDigest.");
    }
    if (types->size() != 1) {
        throw std::runtime_error("SignedAttrs must contain exactly one contentType value.");
    }
    if (digests->size() != 1) {
        throw std::runtime_error("SignedAttrs must contain exactly one messageDigest value.");
    }
    if (types->front() != econtent_type) {
        throw std::runtime_error("SignedAttrs contentType differs from eContentType.");
    }
    return digests->front();
}

[[nodiscard]] bool stub_accept_signature(const asn1kit::bytes& signature) {
    return !signature.empty();
}

[[nodiscard]] std::string describe_key_usage(const Pkix::Certificate& certificate) {
    const auto ku = certificate.toBeSigned.get().cert_extensions().key_usage();
    if (!ku) {
        return {};
    }
    return format_key_usage(ku->flags());
}

void check_signer_key_usage(const Pkix::Certificate& certificate) {
    const auto ku = certificate.toBeSigned.get().cert_extensions().key_usage();
    if (!ku) {
        return;
    }
    const auto flags = ku->flags();
    if (!has_key_usage_flag(flags, Implicit::KeyUsageFlags::digitalSignature) &&
        !has_key_usage_flag(flags, Implicit::KeyUsageFlags::nonRepudiation)) {
        throw std::runtime_error(
            "Signer KeyUsage lacks digitalSignature and nonRepudiation.");
    }
}

void check_embedded_ca_constraints(
    const std::optional<std::vector<Cms::CertificateChoices>>& choices) {
    if (!choices.has_value()) {
        return;
    }
    for (const auto& choice : *choices) {
        if (choice.kind != Cms::CertificateChoices_kind::certificate) {
            continue;
        }
        const auto& certificate = std::get<0>(choice.value);
        const auto bc = certificate.toBeSigned.get().cert_extensions().basic_constraints();
        const auto ku = certificate.toBeSigned.get().cert_extensions().key_usage();
        if (!bc.has_value() || !bc->cA) {
            continue;
        }
        if (ku.has_value() &&
            !has_key_usage_flag(ku->flags(), Implicit::KeyUsageFlags::keyCertSign)) {
            throw std::runtime_error(
                "Embedded CA certificate has BasicConstraints.cA but lacks keyCertSign.");
        }
    }
}

void print_signed_attributes(const Cms::SignerInfo& signer, std::ostream& out, int depth) {
    if (!signer.signedAttrs.has_value()) {
        write_indent(out, depth);
        out << "signedAttrs: <absent>\n";
        return;
    }
    write_indent(out, depth);
    out << "signedAttrs:\n";
    const auto attrs = signer.signed_attributes_set();
    if (const auto types = attrs.content_type()) {
        write_indent(out, depth + 1);
        out << "contentType:\n";
        for (const auto& type : *types) {
            write_indent(out, depth + 2);
            out << format_oid(type) << "\n";
        }
    }
    if (const auto digests = attrs.message_digest()) {
        write_indent(out, depth + 1);
        out << "messageDigest:\n";
        for (const auto& digest : *digests) {
            write_indent(out, depth + 2);
            out << hex_bytes(digest.span(), 64) << "\n";
        }
    }
    if (const auto times = attrs.signing_time()) {
        write_indent(out, depth + 1);
        out << "signingTime:\n";
        for (const auto& time : *times) {
            write_indent(out, depth + 2);
            out << format_time(time.value) << "\n";
        }
    }
    for (const auto& attr : signer.signedAttrs->get()) {
        if (attr.attrType == CmsOids::IdContentType.get() ||
            attr.attrType == CmsOids::IdMessageDigest.get() ||
            attr.attrType == CmsOids::IdSigningTime.get()) {
            continue;
        }
        write_indent(out, depth + 1);
        out << format_oid(attr.attrType) << ": " << attr.attrValues.size() << " value(s)\n";
    }
}

void print_embedded_certificate_summary(
    const Pkix::Certificate& certificate,
    std::ostream& out,
    int depth) {
    const auto& tbs = certificate.toBeSigned.get();
    write_indent(out, depth);
    out << "subject: " << format_name(tbs.subject.get()) << "\n";
    if (const auto ski = tbs.cert_extensions().subject_key_identifier()) {
        write_indent(out, depth);
        out << "subjectKeyIdentifier: " << hex_bytes(ski->span(), 64) << "\n";
    }
    if (const auto aki = tbs.cert_extensions().authority_key_identifier()) {
        if (aki->keyIdentifier.has_value()) {
            write_indent(out, depth);
            out << "authorityKeyIdentifier: "
                << hex_bytes(aki->keyIdentifier->span(), 64) << "\n";
        }
    }
    if (const auto ku = tbs.cert_extensions().key_usage()) {
        write_indent(out, depth);
        out << "keyUsage: " << format_key_usage(ku->flags()) << "\n";
    }
}

} // namespace

std::vector<SignerResult> verify_attached(std::span<const std::uint8_t> encoded) {
    auto signed_data = decode_signed_data(
        asn1kit::decode<Cms::ContentInfo>(encoded, asn1kit::encoding::der));
    const auto content = require_econtent(signed_data);
    if (signed_data.signerInfos.empty()) {
        throw std::runtime_error("SignedData has no signers.");
    }

    check_embedded_ca_constraints(signed_data.certificates);

    std::vector<SignerResult> results;
    results.reserve(signed_data.signerInfos.size());
    for (std::size_t index = 0; index < signed_data.signerInfos.size(); ++index) {
        SignerResult result{};
        result.signer_number = static_cast<int>(index + 1);
        try {
            const auto& signer = signed_data.signerInfos[index];
            if (!digest_algorithm_listed(signed_data, signer.digestAlgorithm.algorithm)) {
                throw std::runtime_error(
                    "Signer digest algorithm is absent from SignedData.digestAlgorithms.");
            }

            const auto* certificate = find_certificate(signed_data.certificates, signer.sid);
            if (certificate == nullptr) {
                throw std::runtime_error(
                    "No unique embedded certificate matches the signer identifier.");
            }

            if (signer.signedAttrs.has_value()) {
                const auto claimed =
                    read_message_digest(signer, signed_data.encapContentInfo.eContentType);
                if (signer.digestAlgorithm.algorithm ==
                    PssOaep::PKIX1PSSOAEPAlgorithms2009Oids::IdSha256.get()) {
                    const auto actual = sha256(content);
                    if (claimed.span().size() != actual.size() ||
                        !std::equal(
                            claimed.span().begin(),
                            claimed.span().end(),
                            actual.begin())) {
                        throw std::runtime_error(
                            "SignedAttrs messageDigest does not match SHA-256(eContent).");
                    }
                } else if (claimed.empty()) {
                    throw std::runtime_error("Digest was rejected by the educational stub.");
                }
            }

            check_signer_key_usage(*certificate);

            if (!stub_accept_signature(signer.signature)) {
                throw std::runtime_error("Signature was rejected by the educational stub.");
            }

            const auto& tbs = certificate->toBeSigned.get();
            result.accepted = true;
            result.reason =
                "structure accepted; digest checked when SHA-256; signature stub accepted";
            result.issuer = format_name(tbs.issuer.get());
            result.subject = format_name(tbs.subject.get());
            if (const auto ski = read_subject_key_identifier(*certificate)) {
                result.ski = hex_bytes(ski->span(), 64);
            }
            result.key_usage = describe_key_usage(*certificate);
        } catch (const std::exception& ex) {
            result.accepted = false;
            result.reason = ex.what();
        }
        results.push_back(std::move(result));
    }
    return results;
}

void print_cms(std::span<const std::uint8_t> encoded, std::ostream& out) {
    auto content_info = asn1kit::decode<Cms::ContentInfo>(encoded, asn1kit::encoding::der);
    out << "CMS_ContentInfo:\n";
    out << "  contentType: " << format_oid(content_info.contentType) << "\n";

    auto signed_data = decode_signed_data(std::move(content_info));
    out << "  d.signedData:\n";
    out << "    version: " << signed_data.version << "\n";
    out << "    digestAlgorithms: " << signed_data.digestAlgorithms.size() << "\n";
    for (const auto& alg : signed_data.digestAlgorithms) {
        out << "      - " << format_oid(alg.algorithm) << "\n";
    }
    out << "    encapContentInfo:\n";
    out << "      eContentType: " << format_oid(signed_data.encapContentInfo.eContentType)
        << "\n";
    if (signed_data.encapContentInfo.eContent.has_value()) {
        out << "      eContent: " << signed_data.encapContentInfo.eContent->size()
            << " octets\n";
    } else {
        out << "      eContent: <absent>\n";
    }

    const std::size_t cert_count =
        signed_data.certificates.has_value() ? signed_data.certificates->size() : 0;
    out << "    certificates: " << cert_count << "\n";
    if (signed_data.certificates.has_value()) {
        for (std::size_t i = 0; i < signed_data.certificates->size(); ++i) {
            const auto& choice = (*signed_data.certificates)[i];
            out << "      certificate[" << (i + 1) << "]:\n";
            if (choice.kind == Cms::CertificateChoices_kind::certificate) {
                print_embedded_certificate_summary(std::get<0>(choice.value), out, 4);
            } else {
                out << "        kind: other\n";
            }
        }
    }

    out << "    signerInfos: " << signed_data.signerInfos.size() << "\n";
    for (std::size_t i = 0; i < signed_data.signerInfos.size(); ++i) {
        const auto& signer = signed_data.signerInfos[i];
        out << "      signer[" << (i + 1) << "]:\n";
        out << "        version: " << signer.version << "\n";
        out << "        sid.kind: ";
        switch (signer.sid.kind) {
        case Cms::SignerIdentifier_kind::issuerAndSerialNumber:
            out << "issuerAndSerialNumber\n";
            {
                const auto& ias = std::get<0>(signer.sid.value);
                out << "          issuer: " << format_name(ias.issuer.get()) << "\n";
                out << "          serialNumber: " << ias.serialNumber.to_decimal() << "\n";
            }
            break;
        case Cms::SignerIdentifier_kind::subjectKeyIdentifier:
            out << "subjectKeyIdentifier\n";
            out << "          ski: " << hex_bytes(std::get<1>(signer.sid.value).span(), 64)
                << "\n";
            break;
        default:
            out << "unknown\n";
            break;
        }
        out << "        digestAlgorithm: " << format_oid(signer.digestAlgorithm.algorithm)
            << "\n";
        print_signed_attributes(signer, out, 4);
        out << "        signatureAlgorithm: "
            << format_oid(signer.signatureAlgorithm.algorithm) << "\n";
        out << "        signature: " << signer.signature.size() << " octets\n";
    }
}

std::vector<std::uint8_t> build_attached_signed_data(
    std::span<const std::uint8_t> content,
    std::span<const std::uint8_t> certificate_der) {
    const auto certificate = asn1kit::decode<Pkix::Certificate>(certificate_der, asn1kit::encoding::der);
    const auto& tbs = certificate.toBeSigned.get();

    auto digest_vec = sha256(content);
    const auto digest = asn1kit::bytes::from_vector(std::move(digest_vec));

    Alg::AlgorithmIdentifier digest_algorithm{};
    digest_algorithm.algorithm = PssOaep::PKIX1PSSOAEPAlgorithms2009Oids::IdSha256.get();

    Alg::AlgorithmIdentifier signature_algorithm{};
    signature_algorithm.algorithm = PkixAlgs::PKIXAlgs2009Oids::RsaEncryption.get();

    Cms::IssuerAndSerialNumber ias{};
    ias.issuer = tbs.issuer;
    ias.serialNumber = tbs.serialNumber;

    const auto now = asn1kit::utc_date_time::from_system_clock(std::chrono::system_clock::now());

    Cms::SignerInfo signer{};
    signer.version = Cms::CMSVersion::V1;
    signer.sid = Cms::SignerIdentifier::from_issuerAndSerialNumber(std::move(ias));
    signer.digestAlgorithm = digest_algorithm;
    signer.signedAttrs = asn1kit::value<std::vector<Cms::Attribute>>::from_value({
        Cms::Attribute::make_signed_attributes_set().content_type(CmsOids::IdData.get()),
        Cms::Attribute::make_signed_attributes_set().message_digest(digest),
        Cms::Attribute::make_signed_attributes_set().signing_time(
            Cms::Time::from_utcTime(now)),
    });
    signer.signatureAlgorithm = signature_algorithm;
    signer.signature = asn1kit::bytes::from_vector(std::vector<std::uint8_t>(32, 0x01));

    Cms::SignedData signed_data{};
    signed_data.version = Cms::CMSVersion::V1;
    signed_data.digestAlgorithms = {digest_algorithm};
    signed_data.encapContentInfo.eContentType = CmsOids::IdData.get();
    signed_data.encapContentInfo.eContent = asn1kit::bytes::borrow(content);
    signed_data.certificates = std::vector<Cms::CertificateChoices>{
        Cms::CertificateChoices::from_certificate(certificate)};
    signed_data.signerInfos = {std::move(signer)};

    const auto content_info =
        Cms::ContentInfo::make_content_set().ct_signed_data(signed_data);
    return asn1kit::encode(content_info, asn1kit::encoding::der);
}

} // namespace asn1kit::cms_demo
