// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "cms_demo.hpp"
#include "sha256.hpp"

#include "CryptographicMessageSyntax2009.hpp"
#include "PKIX1Explicit2009.hpp"

#include "asn1kit/bytes.hpp"
#include "asn1kit/codec.hpp"
#include "asn1kit/encode_decode.hpp"
#include "asn1kit/exception.hpp"
#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

#include <iomanip>
#include <sstream>
#include <stdexcept>
#include <utility>

namespace asn1kit::cms_demo {
namespace {

namespace Cms = asn1kit::modern::CryptographicMessageSyntax2009;
namespace Pkix = asn1kit::modern::PKIX1Explicit2009;
namespace Common = asn1kit::modern::PKIXCommonTypes2009;
namespace Alg = asn1kit::modern::AlgorithmInformation2009;

using CmsOids = Cms::CryptographicMessageSyntax2009Oids;
using PkixOids = Pkix::PKIX1Explicit2009Oids;

[[nodiscard]] asn1kit::oid sha256_oid() {
    return asn1kit::oid::parse("2.16.840.1.101.3.4.2.1");
}

[[nodiscard]] asn1kit::oid rsa_encryption_oid() {
    return asn1kit::oid::parse("1.2.840.113549.1.1.1");
}

[[nodiscard]] std::string hex_bytes(std::span<const std::uint8_t> data, std::size_t max = 16) {
    std::ostringstream out;
    const std::size_t n = data.size() < max ? data.size() : max;
    for (std::size_t i = 0; i < n; ++i) {
        out << std::hex << std::setw(2) << std::setfill('0') << static_cast<int>(data[i]);
    }
    if (data.size() > max) {
        out << "...";
    }
    return out.str();
}

[[nodiscard]] const char* short_attr_name(const asn1kit::oid& type) {
    if (type == PkixOids::IdAtCommonName.get()) return "CN";
    if (type == PkixOids::IdAtCountryName.get()) return "C";
    if (type == PkixOids::IdAtLocalityName.get()) return "L";
    if (type == PkixOids::IdAtStateOrProvinceName.get()) return "ST";
    if (type == PkixOids::IdAtOrganizationName.get()) return "O";
    if (type == PkixOids::IdAtOrganizationalUnitName.get()) return "OU";
    return nullptr;
}

[[nodiscard]] bool looks_like_text(std::span<const std::uint8_t> bytes) {
    if (bytes.empty()) {
        return false;
    }
    for (const auto b : bytes) {
        if (b < 0x20 || b > 0x7e) {
            return false;
        }
    }
    return true;
}

[[nodiscard]] std::string format_attribute(const Common::SingleAttribute& attr) {
    const char* short_name = short_attr_name(attr.type);
    const std::string label = short_name != nullptr ? short_name : attr.type.to_string();

    try {
        if (attr.type == PkixOids::IdAtCountryName.get()
            || attr.type == PkixOids::IdAtSerialNumber.get()) {
            const auto printable =
                asn1kit::decode_open<asn1kit::asn_string<asn1kit::string_form::printable>>(attr.value_);
            return label + "=" + printable.value;
        }
        const auto directory = asn1kit::decode_open<Pkix::DirectoryString>(attr.value_);
        return label + "=" + directory.value;
    } catch (const asn1kit::exception&) {
        // Wire may use IA5String (or other forms) outside DirectoryString; show ASCII when obvious.
        const auto contents = attr.value_.contents().span();
        if (looks_like_text(contents)) {
            return label + "=" + std::string(contents.begin(), contents.end());
        }
        return label + "=#" + hex_bytes(contents, 32);
    }
}

[[nodiscard]] std::string format_name(
    const std::vector<std::vector<Common::SingleAttribute>>& name) {
    std::ostringstream out;
    bool first_rdn = true;
    for (auto it = name.rbegin(); it != name.rend(); ++it) {
        if (!first_rdn) {
            out << ',';
        }
        first_rdn = false;
        bool first_attr = true;
        for (const auto& attr : *it) {
            if (!first_attr) {
                out << '+';
            }
            first_attr = false;
            out << format_attribute(attr);
        }
    }
    return out.str();
}

[[nodiscard]] Cms::SignedData decode_signed_data(const Cms::ContentInfo& info) {
    if (info.contentType != CmsOids::IdSignedData.get()) {
        throw std::runtime_error("ContentInfo must be id-signedData.");
    }
    auto signed_data = info.try_decode(Cms::contentSet_content_bindings::ct_signed_data);
    if (!signed_data) {
        throw std::runtime_error("ContentInfo must contain SignedData.");
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

    const auto types = signer.try_decode_signed_attrs(
        Cms::signedAttributesSet_bindings::content_type);
    const auto digests = signer.try_decode_signed_attrs(
        Cms::signedAttributesSet_bindings::message_digest);
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

[[nodiscard]] bool stub_accept_digest(const asn1kit::bytes& claimed) {
    return !claimed.empty();
}

[[nodiscard]] bool stub_accept_signature(const asn1kit::bytes& signature) {
    return !signature.empty();
}

} // namespace

std::vector<SignerResult> verify_attached(std::span<const std::uint8_t> encoded) {
    const auto content_info = asn1kit::decode<Cms::ContentInfo>(encoded, asn1kit::encoding::der);
    const auto signed_data = decode_signed_data(content_info);
    const auto content = require_econtent(signed_data);
    if (signed_data.signerInfos.empty()) {
        throw std::runtime_error("SignedData has no signers.");
    }

    std::vector<SignerResult> results;
    results.reserve(signed_data.signerInfos.size());
    for (std::size_t index = 0; index < signed_data.signerInfos.size(); ++index) {
        SignerResult result{};
        result.signer_number = static_cast<int>(index + 1);
        try {
            const auto& signer = signed_data.signerInfos[index];
            if (!digest_algorithm_listed(signed_data, signer.digestAlgorithm.algorithm)) {
                throw std::runtime_error("Signer digest algorithm is absent from SignedData.digestAlgorithms.");
            }

            const auto* certificate = find_certificate(signed_data.certificates, signer.sid);
            if (certificate == nullptr) {
                throw std::runtime_error("No unique embedded certificate matches the signer identifier.");
            }

            if (signer.signedAttrs.has_value()) {
                const auto claimed = read_message_digest(signer, signed_data.encapContentInfo.eContentType);
                if (!stub_accept_digest(claimed)) {
                    throw std::runtime_error("Digest was rejected by the educational stub.");
                }
                (void)content;
            }

            if (!stub_accept_signature(signer.signature)) {
                throw std::runtime_error("Signature was rejected by the educational stub.");
            }

            const auto& tbs = certificate->toBeSigned.get();
            result.accepted = true;
            result.reason = "structure accepted; stub accepted digest and signature";
            result.issuer = format_name(tbs.issuer.get());
            result.subject = format_name(tbs.subject.get());
        } catch (const std::exception& ex) {
            result.accepted = false;
            result.reason = ex.what();
        }
        results.push_back(std::move(result));
    }
    return results;
}

void print_certificate(std::span<const std::uint8_t> encoded, std::ostream& out) {
    const auto certificate = asn1kit::decode<Pkix::Certificate>(encoded, asn1kit::encoding::der);
    const auto& tbs = certificate.toBeSigned.get();
    out << "Certificate:\n";
    out << "  version: " << tbs.version << "\n";
    out << "  serialNumber: " << tbs.serialNumber.to_decimal() << "\n";
    out << "  signature: " << tbs.signature.algorithm.to_string() << "\n";
    out << "  issuer: " << format_name(tbs.issuer.get()) << "\n";
    out << "  subject: " << format_name(tbs.subject.get()) << "\n";
    out << "  subjectPublicKeyInfo.algorithm: "
        << tbs.subjectPublicKeyInfo.get().algorithm.algorithm.to_string() << "\n";
    out << "  signatureAlgorithm: " << certificate.algorithmIdentifier.algorithm.to_string() << "\n";
}

void print_cms(std::span<const std::uint8_t> encoded, std::ostream& out) {
    const auto content_info = asn1kit::decode<Cms::ContentInfo>(encoded, asn1kit::encoding::der);
    out << "CMS_ContentInfo:\n";
    out << "  contentType: " << content_info.contentType.to_string() << "\n";

    const auto signed_data = decode_signed_data(content_info);
    out << "  d.signedData:\n";
    out << "    version: " << signed_data.version << "\n";
    out << "    digestAlgorithms: " << signed_data.digestAlgorithms.size() << "\n";
    for (const auto& alg : signed_data.digestAlgorithms) {
        out << "      - " << alg.algorithm.to_string() << "\n";
    }
    out << "    encapContentInfo:\n";
    out << "      eContentType: " << signed_data.encapContentInfo.eContentType.to_string() << "\n";
    if (signed_data.encapContentInfo.eContent.has_value()) {
        out << "      eContent: " << signed_data.encapContentInfo.eContent->size()
            << " octets\n";
    } else {
        out << "      eContent: <absent>\n";
    }
    const std::size_t cert_count =
        signed_data.certificates.has_value() ? signed_data.certificates->size() : 0;
    out << "    certificates: " << cert_count << "\n";
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
            out << "          ski: " << hex_bytes(std::get<1>(signer.sid.value).span()) << "\n";
            break;
        default:
            out << "unknown\n";
            break;
        }
        out << "        digestAlgorithm: " << signer.digestAlgorithm.algorithm.to_string() << "\n";
        out << "        signedAttrs: " << (signer.signedAttrs.has_value() ? "present" : "absent")
            << "\n";
        out << "        signatureAlgorithm: " << signer.signatureAlgorithm.algorithm.to_string()
            << "\n";
        out << "        signature: " << signer.signature.size() << " octets\n";
    }
}

std::vector<std::uint8_t> build_attached_signed_data(
    std::span<const std::uint8_t> content,
    std::span<const std::uint8_t> certificate_der) {
    const auto certificate = asn1kit::decode<Pkix::Certificate>(certificate_der, asn1kit::encoding::der);
    const auto& tbs = certificate.toBeSigned.get();

    const auto digest_vec = sha256(content);
    const auto digest = asn1kit::bytes::copy_from(digest_vec);

    Alg::AlgorithmIdentifier digest_algorithm{};
    digest_algorithm.algorithm = sha256_oid();

    Alg::AlgorithmIdentifier signature_algorithm{};
    signature_algorithm.algorithm = rsa_encryption_oid();

    Cms::IssuerAndSerialNumber ias{};
    ias.issuer = tbs.issuer;
    ias.serialNumber = tbs.serialNumber;

    Cms::SignerInfo signer{};
    signer.version = Cms::CMSVersion::V1;
    signer.sid = Cms::SignerIdentifier::from_issuerAndSerialNumber(std::move(ias));
    signer.digestAlgorithm = digest_algorithm;
    signer.signedAttrs = asn1kit::value<std::vector<Cms::Attribute>>::from_value({
        Cms::Attribute::create(Cms::signedAttributesSet_bindings::content_type, CmsOids::IdData.get()),
        Cms::Attribute::create(Cms::signedAttributesSet_bindings::message_digest, digest),
    });
    signer.signatureAlgorithm = signature_algorithm;
    signer.signature = asn1kit::bytes::from_vector(std::vector<std::uint8_t>(32, 0x01));

    Cms::SignedData signed_data{};
    signed_data.version = Cms::CMSVersion::V1;
    signed_data.digestAlgorithms = {digest_algorithm};
    signed_data.encapContentInfo.eContentType = CmsOids::IdData.get();
    signed_data.encapContentInfo.eContent = asn1kit::bytes::copy_from(content);
    signed_data.certificates = std::vector<Cms::CertificateChoices>{
        Cms::CertificateChoices::from_certificate(certificate)};
    signed_data.signerInfos = {std::move(signer)};

    const auto content_info = Cms::ContentInfo::create(
        Cms::contentSet_content_bindings::ct_signed_data, signed_data);
    return asn1kit::encode(content_info, asn1kit::encoding::der);
}

} // namespace asn1kit::cms_demo
