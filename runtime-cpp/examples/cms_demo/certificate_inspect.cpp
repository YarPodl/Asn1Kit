// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "cms_demo.hpp"
#include "format.hpp"

#include "PKIX1Explicit2009.hpp"
#include "PKIX1Implicit2009.hpp"
#include "PKIXCommonTypes2009.hpp"

#include "asn1kit/encode_decode.hpp"
#include "asn1kit/exception.hpp"

#include <set>
#include <string>

namespace asn1kit::cms_demo {
namespace {

namespace Pkix = asn1kit::modern::PKIX1Explicit2009;
namespace Implicit = asn1kit::modern::PKIX1Implicit2009;
namespace Common = asn1kit::modern::PKIXCommonTypes2009;

using ImplicitOids = Implicit::PKIX1Implicit2009Oids;

[[nodiscard]] std::string format_general_name(const Implicit::GeneralName& name) {
    switch (name.kind) {
    case Implicit::GeneralName_kind::rfc822Name:
    case Implicit::GeneralName_kind::dNSName:
    case Implicit::GeneralName_kind::uniformResourceIdentifier:
        return std::get<1>(name.value);
    case Implicit::GeneralName_kind::directoryName:
        return format_name(std::get<3>(name.value));
    case Implicit::GeneralName_kind::registeredID:
        return std::get<6>(name.value).to_string();
    case Implicit::GeneralName_kind::iPAddress:
        return "#" + hex_bytes(std::get<5>(name.value).span(), 32);
    default:
        return "<" + std::to_string(static_cast<int>(name.kind)) + ">";
    }
}

void print_typed_extensions(const Pkix::TBSCertificate& tbs, std::ostream& out) {
    if (!tbs.extensions.has_value() || tbs.extensions->empty()) {
        out << "  extensions: <absent>\n";
        return;
    }

    out << "  X509v3 extensions:\n";
    std::set<std::string> printed;
    const auto view = tbs.cert_extensions();

    if (const auto ski = view.subject_key_identifier()) {
        write_indent(out, 2);
        out << format_oid(ImplicitOids::IdCeSubjectKeyIdentifier.get()) << ": non-critical\n";
        write_indent(out, 3);
        out << "keyIdentifier: " << hex_bytes(ski->span(), 64) << "\n";
        printed.insert(ImplicitOids::IdCeSubjectKeyIdentifier.get().to_string());
    }
    if (const auto aki = view.authority_key_identifier()) {
        write_indent(out, 2);
        out << format_oid(ImplicitOids::IdCeAuthorityKeyIdentifier.get()) << ": non-critical\n";
        if (aki->keyIdentifier.has_value()) {
            write_indent(out, 3);
            out << "keyIdentifier: " << hex_bytes(aki->keyIdentifier->span(), 64) << "\n";
        }
        if (aki->authorityCertSerialNumber.has_value()) {
            write_indent(out, 3);
            out << "authorityCertSerialNumber: "
                << aki->authorityCertSerialNumber->to_decimal() << "\n";
        }
        printed.insert(ImplicitOids::IdCeAuthorityKeyIdentifier.get().to_string());
    }
    if (const auto ku = view.key_usage()) {
        bool critical = false;
        for (const auto& ext : *tbs.extensions) {
            if (ext.extnID == ImplicitOids::IdCeKeyUsage.get()) {
                critical = ext.critical;
                break;
            }
        }
        write_indent(out, 2);
        out << format_oid(ImplicitOids::IdCeKeyUsage.get()) << ": "
            << (critical ? "critical" : "non-critical") << "\n";
        write_indent(out, 3);
        out << format_key_usage(ku->flags()) << "\n";
        printed.insert(ImplicitOids::IdCeKeyUsage.get().to_string());
    }
    if (const auto bc = view.basic_constraints()) {
        bool critical = false;
        for (const auto& ext : *tbs.extensions) {
            if (ext.extnID == ImplicitOids::IdCeBasicConstraints.get()) {
                critical = ext.critical;
                break;
            }
        }
        write_indent(out, 2);
        out << format_oid(ImplicitOids::IdCeBasicConstraints.get()) << ": "
            << (critical ? "critical" : "non-critical") << "\n";
        write_indent(out, 3);
        out << "cA: " << (bc->cA ? "true" : "false") << "\n";
        if (bc->pathLenConstraint.has_value()) {
            write_indent(out, 3);
            out << "pathLenConstraint: " << bc->pathLenConstraint->to_decimal() << "\n";
        }
        printed.insert(ImplicitOids::IdCeBasicConstraints.get().to_string());
    }
    if (const auto policies = view.certificate_policies()) {
        write_indent(out, 2);
        out << format_oid(ImplicitOids::IdCeCertificatePolicies.get()) << ": non-critical\n";
        for (const auto& policy : *policies) {
            write_indent(out, 3);
            out << "policyIdentifier: " << policy.policyIdentifier.to_string() << "\n";
        }
        printed.insert(ImplicitOids::IdCeCertificatePolicies.get().to_string());
    }
    if (const auto aia = view.authority_info_access()) {
        write_indent(out, 2);
        out << format_oid(ImplicitOids::IdPeAuthorityInfoAccess.get()) << ": non-critical\n";
        for (const auto& access : *aia) {
            write_indent(out, 3);
            out << "accessMethod: " << access.accessMethod.to_string() << "\n";
            write_indent(out, 3);
            out << "accessLocation: " << format_general_name(access.accessLocation) << "\n";
        }
        printed.insert(ImplicitOids::IdPeAuthorityInfoAccess.get().to_string());
    }

    for (const auto& ext : *tbs.extensions) {
        if (printed.contains(ext.extnID.to_string())) {
            continue;
        }
        write_indent(out, 2);
        out << format_oid(ext.extnID) << ": "
            << (ext.critical ? "critical" : "non-critical") << "\n";
        write_indent(out, 3);
        out << "extnValue: " << hex_bytes(ext.extnValue.span(), 64) << "\n";
    }
}

} // namespace

void print_certificate(std::span<const std::uint8_t> encoded, std::ostream& out) {
    const auto certificate = asn1kit::decode<Pkix::Certificate>(encoded, asn1kit::encoding::der);
    const auto& tbs = certificate.toBeSigned.get();
    out << "Certificate:\n";
    out << "  version: " << (tbs.version + 1) << " (0x" << std::hex << tbs.version << std::dec
        << ")\n";
    out << "  serialNumber: " << tbs.serialNumber.to_decimal() << "\n";
    out << "  signature: " << format_oid(tbs.signature.algorithm) << "\n";
    out << "  issuer: " << format_name(tbs.issuer.get()) << "\n";
    out << "  validity:\n";
    out << "    notBefore: " << format_time(tbs.validity.notBefore.value) << "\n";
    out << "    notAfter: " << format_time(tbs.validity.notAfter.value) << "\n";
    out << "  subject: " << format_name(tbs.subject.get()) << "\n";
    out << "  subjectPublicKeyInfo.algorithm: "
        << format_oid(tbs.subjectPublicKeyInfo.get().algorithm.algorithm) << "\n";
    print_typed_extensions(tbs, out);
    out << "  signatureAlgorithm: " << format_oid(certificate.algorithmIdentifier.algorithm)
        << "\n";
}

} // namespace asn1kit::cms_demo
