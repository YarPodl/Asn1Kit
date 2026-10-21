// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "format.hpp"

#include "CryptographicMessageSyntax2009.hpp"
#include "OCSP2009.hpp"
#include "PKIX1Implicit2009.hpp"
#include "PKIX1PSSOAEPAlgorithms2009.hpp"

#include "asn1kit/exception.hpp"

#include <iomanip>
#include <sstream>

namespace asn1kit::cms_demo {
namespace {

namespace Implicit = asn1kit::modern::PKIX1Implicit2009;
namespace Cms = asn1kit::modern::CryptographicMessageSyntax2009;
namespace Ocsp = asn1kit::modern::OCSP2009;
namespace PssOaep = asn1kit::modern::PKIX1PSSOAEPAlgorithms2009;

using ImplicitOids = Implicit::PKIX1Implicit2009Oids;
using CmsOids = Cms::CryptographicMessageSyntax2009Oids;
using OcspOids = Ocsp::OCSP2009Oids;
using DigestOids = PssOaep::PKIX1PSSOAEPAlgorithms2009Oids;

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

} // namespace

std::string hex_bytes(std::span<const std::uint8_t> data, std::size_t max) {
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

std::string format_oid(const asn1kit::oid& value) {
    const auto labeled = [&](const char* name) {
        return std::string(name) + " (" + value.to_string() + ")";
    };
    if (value == ImplicitOids::IdCeSubjectKeyIdentifier) {
        return labeled("subjectKeyIdentifier");
    }
    if (value == ImplicitOids::IdCeKeyUsage) {
        return labeled("keyUsage");
    }
    if (value == ImplicitOids::IdCeBasicConstraints) {
        return labeled("basicConstraints");
    }
    if (value == ImplicitOids::IdCeCertificatePolicies) {
        return labeled("certificatePolicies");
    }
    if (value == ImplicitOids::IdCeAuthorityKeyIdentifier) {
        return labeled("authorityKeyIdentifier");
    }
    if (value == ImplicitOids::IdPeAuthorityInfoAccess) {
        return labeled("authorityInfoAccess");
    }
    if (value == CmsOids::IdContentType) {
        return labeled("contentType");
    }
    if (value == CmsOids::IdMessageDigest) {
        return labeled("messageDigest");
    }
    if (value == CmsOids::IdSigningTime) {
        return labeled("signingTime");
    }
    if (value == OcspOids::IdPkixOcspNonce) {
        return labeled("id-pkix-ocsp-nonce");
    }
    if (value == OcspOids::IdPkixOcspBasic) {
        return labeled("id-pkix-ocsp-basic");
    }
    if (value == DigestOids::IdSha256) {
        return labeled("id-sha256");
    }
    return value.to_string();
}

std::string format_time(const asn1kit::utc_date_time& value) {
    return value.to_iso();
}

std::string format_key_usage(Implicit::KeyUsageFlags flags) {
    std::ostringstream out;
    bool first = true;
    const auto append = [&](Implicit::KeyUsageFlags bit, const char* name) {
        if (!has_key_usage_flag(flags, bit)) {
            return;
        }
        if (!first) {
            out << ", ";
        }
        first = false;
        out << name;
    };
    append(Implicit::KeyUsageFlags::digitalSignature, "digitalSignature");
    append(Implicit::KeyUsageFlags::nonRepudiation, "nonRepudiation");
    append(Implicit::KeyUsageFlags::keyEncipherment, "keyEncipherment");
    append(Implicit::KeyUsageFlags::dataEncipherment, "dataEncipherment");
    append(Implicit::KeyUsageFlags::keyAgreement, "keyAgreement");
    append(Implicit::KeyUsageFlags::keyCertSign, "keyCertSign");
    append(Implicit::KeyUsageFlags::cRLSign, "cRLSign");
    append(Implicit::KeyUsageFlags::encipherOnly, "encipherOnly");
    append(Implicit::KeyUsageFlags::decipherOnly, "decipherOnly");
    if (first) {
        return "none";
    }
    return out.str();
}

bool has_key_usage_flag(Implicit::KeyUsageFlags flags, Implicit::KeyUsageFlags bit) {
    return (static_cast<std::uint32_t>(flags) & static_cast<std::uint32_t>(bit)) != 0;
}

std::string format_attribute(
    const asn1kit::modern::PKIXCommonTypes2009::SingleAttribute& attr) {
    try {
        const auto values = attr.supported_attributes();
        if (const auto value = values.x520_common_name()) return "CN=" + value->value;
        if (const auto value = values.x520country_name()) return "C=" + value->value;
        if (const auto value = values.x520_locality_name()) return "L=" + value->value;
        if (const auto value = values.x520_state_or_province_name()) return "ST=" + value->value;
        if (const auto value = values.x520_organization_name()) return "O=" + value->value;
        if (const auto value = values.x520_organizational_unit_name()) return "OU=" + value->value;
        if (const auto value = values.name()) return attr.type.to_string() + "=" + value->value;
        if (const auto value = values.surname()) return attr.type.to_string() + "=" + value->value;
        if (const auto value = values.given_name()) return attr.type.to_string() + "=" + value->value;
        if (const auto value = values.initials()) return attr.type.to_string() + "=" + value->value;
        if (const auto value = values.generation_qualifier()) {
            return attr.type.to_string() + "=" + value->value;
        }
        if (const auto value = values.x520_title()) return attr.type.to_string() + "=" + value->value;
        if (const auto value = values.x520_pseudonym()) {
            return attr.type.to_string() + "=" + value->value;
        }
        if (const auto value = values.x520dn_qualifier()) {
            return attr.type.to_string() + "=" + value->value;
        }
        if (const auto value = values.x520_serial_number()) {
            return attr.type.to_string() + "=" + value->value;
        }
        if (const auto value = values.domain_component()) {
            return "DC=" + value->value;
        }
        if (const auto value = values.email_address()) {
            return "emailAddress=" + value->value;
        }
    } catch (const asn1kit::exception&) {
        // Fall through to the opaque representation for a malformed known value.
    }

    const auto label = attr.type.to_string();
    const auto contents = attr.value_.contents().span();
    if (looks_like_text(contents)) {
        return label + "=" + std::string(contents.begin(), contents.end());
    }
    return label + "=#" + hex_bytes(contents, 32);
}

std::string format_name(
    const std::vector<std::vector<asn1kit::modern::PKIXCommonTypes2009::SingleAttribute>>& name) {
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

void write_indent(std::ostream& out, int depth) {
    for (int i = 0; i < depth; ++i) {
        out << "  ";
    }
}

} // namespace asn1kit::cms_demo
