// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "cms_demo.hpp"
#include "format.hpp"
#include "sha256.hpp"

#include "OCSP2009.hpp"
#include "PKIX1Explicit2009.hpp"
#include "PKIX1PSSOAEPAlgorithms2009.hpp"
#include "PKIXCommonTypes2009.hpp"

#include "asn1kit/bit_string.hpp"
#include "asn1kit/bytes.hpp"
#include "asn1kit/encode_decode.hpp"
#include "asn1kit/null.hpp"
#include "asn1kit/utc_date_time.hpp"

#include <chrono>
#include <stdexcept>
#include <utility>

namespace asn1kit::cms_demo {
namespace {

namespace Ocsp = asn1kit::modern::OCSP2009;
namespace Pkix = asn1kit::modern::PKIX1Explicit2009;
namespace Common = asn1kit::modern::PKIXCommonTypes2009;
namespace PssOaep = asn1kit::modern::PKIX1PSSOAEPAlgorithms2009;

[[nodiscard]] bool cert_id_equal(const Ocsp::CertID& left, const Ocsp::CertID& right) {
    return left.hashAlgorithm.algorithm == right.hashAlgorithm.algorithm &&
           left.issuerNameHash == right.issuerNameHash &&
           left.issuerKeyHash == right.issuerKeyHash &&
           left.serialNumber == right.serialNumber;
}

[[nodiscard]] Ocsp::CertID make_cert_id(
    const Pkix::Certificate& ee,
    const Pkix::Certificate& issuer) {
    const auto& ee_tbs = ee.toBeSigned.get();
    const auto& issuer_tbs = issuer.toBeSigned.get();
    if (!ee_tbs.issuer.has_original_encoding()) {
        throw std::runtime_error("End-entity issuer Name lacks original DER encoding.");
    }

    Ocsp::CertID cert_id{};
    cert_id.hashAlgorithm.algorithm = PssOaep::PKIX1PSSOAEPAlgorithms2009Oids::IdSha256.get();
    // RFC 6960: hash over the DER encoding of the issuer's name in the certificate being checked.
    cert_id.issuerNameHash =
        asn1kit::bytes::from_vector(sha256(ee_tbs.issuer.original_encoding().span()));
    cert_id.issuerKeyHash = asn1kit::bytes::from_vector(
        sha256(issuer_tbs.subjectPublicKeyInfo.get().subjectPublicKey.contents().span()));
    cert_id.serialNumber = ee_tbs.serialNumber;
    return cert_id;
}

[[nodiscard]] asn1kit::bytes educational_nonce() {
    return asn1kit::bytes::from_vector(std::vector<std::uint8_t>{
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10});
}

void print_cert_id(const Ocsp::CertID& cert_id, std::ostream& out, int depth) {
    write_indent(out, depth);
    out << "CertID:\n";
    write_indent(out, depth + 1);
    out << "hashAlgorithm: " << format_oid(cert_id.hashAlgorithm.algorithm) << "\n";
    write_indent(out, depth + 1);
    out << "issuerNameHash: " << hex_bytes(cert_id.issuerNameHash.span(), 64) << "\n";
    write_indent(out, depth + 1);
    out << "issuerKeyHash: " << hex_bytes(cert_id.issuerKeyHash.span(), 64) << "\n";
    write_indent(out, depth + 1);
    out << "serialNumber: " << cert_id.serialNumber.to_decimal() << "\n";
}

[[nodiscard]] const char* response_status_name(Ocsp::OCSPResponseStatus status) {
    switch (status) {
    case Ocsp::OCSPResponseStatus::successful:
        return "successful";
    case Ocsp::OCSPResponseStatus::malformedRequest:
        return "malformedRequest";
    case Ocsp::OCSPResponseStatus::internalError:
        return "internalError";
    case Ocsp::OCSPResponseStatus::tryLater:
        return "tryLater";
    case Ocsp::OCSPResponseStatus::sigRequired:
        return "sigRequired";
    case Ocsp::OCSPResponseStatus::unauthorized:
        return "unauthorized";
    }
    return "unknown";
}

[[nodiscard]] const char* cert_status_name(Ocsp::CertStatus_kind kind) {
    switch (kind) {
    case Ocsp::CertStatus_kind::good:
        return "good";
    case Ocsp::CertStatus_kind::revoked:
        return "revoked";
    case Ocsp::CertStatus_kind::unknown:
        return "unknown";
    }
    return "unknown";
}

[[nodiscard]] Ocsp::BasicOCSPResponse require_basic_response(const Ocsp::OCSPResponse& response) {
    if (response.responseStatus != Ocsp::OCSPResponseStatus::successful) {
        throw std::runtime_error("OCSPResponseStatus is not successful.");
    }
    if (!response.responseBytes.has_value()) {
        throw std::runtime_error("Successful OCSPResponse requires responseBytes.");
    }
    auto basic = response.responseBytes->response_set().basic_response();
    if (!basic) {
        throw std::runtime_error("responseBytes must contain id-pkix-ocsp-basic.");
    }
    return std::move(*basic);
}

} // namespace

std::vector<std::uint8_t> build_ocsp_request(
    std::span<const std::uint8_t> ee_certificate_der,
    std::span<const std::uint8_t> issuer_certificate_der) {
    const auto ee = asn1kit::decode<Pkix::Certificate>(ee_certificate_der, asn1kit::encoding::der);
    const auto issuer =
        asn1kit::decode<Pkix::Certificate>(issuer_certificate_der, asn1kit::encoding::der);

    Ocsp::Request request{};
    request.reqCert = make_cert_id(ee, issuer);

    Ocsp::TBSRequest tbs{};
    tbs.requestList = {std::move(request)};
    tbs.requestExtensions = std::vector<Common::Extension>{
        Common::Extension::make_local_request_extensions().re_ocsp_nonce(educational_nonce()),
    };

    Ocsp::OCSPRequest ocsp_request{};
    ocsp_request.tbsRequest = std::move(tbs);
    return asn1kit::encode(ocsp_request, asn1kit::encoding::der);
}

void print_ocsp_request(std::span<const std::uint8_t> encoded, std::ostream& out) {
    const auto request = asn1kit::decode<Ocsp::OCSPRequest>(encoded, asn1kit::encoding::der);
    out << "OCSPRequest:\n";
    out << "  version: " << request.tbsRequest.version << "\n";
    out << "  requestList: " << request.tbsRequest.requestList.size() << "\n";
    for (std::size_t i = 0; i < request.tbsRequest.requestList.size(); ++i) {
        out << "    request[" << (i + 1) << "]:\n";
        print_cert_id(request.tbsRequest.requestList[i].reqCert, out, 3);
    }
    if (const auto nonce = request.tbsRequest.local().re_ocsp_nonce()) {
        out << "  requestExtensions.nonce: " << hex_bytes(nonce->span(), 64) << "\n";
    } else {
        out << "  requestExtensions.nonce: <absent>\n";
    }
    out << "  optionalSignature: "
        << (request.optionalSignature.has_value() ? "present" : "absent") << "\n";
}

std::vector<std::uint8_t> build_ocsp_response(std::span<const std::uint8_t> request_der) {
    const auto request = asn1kit::decode<Ocsp::OCSPRequest>(request_der, asn1kit::encoding::der);
    if (request.tbsRequest.requestList.empty()) {
        throw std::runtime_error("OCSPRequest has an empty requestList.");
    }
    const auto& cert_id = request.tbsRequest.requestList.front().reqCert;
    const auto nonce = request.tbsRequest.local().re_ocsp_nonce();

    const auto now = asn1kit::utc_date_time::from_system_clock(std::chrono::system_clock::now());

    Ocsp::SingleResponse single{};
    single.certID = cert_id;
    single.certStatus = Ocsp::CertStatus::from_good(asn1kit::null_value::value());
    single.thisUpdate = now;

    Ocsp::ResponseData response_data{};
    response_data.responderID = Ocsp::ResponderID::from_byKey(
        asn1kit::bytes::from_vector(std::vector<std::uint8_t>(20, 0xAB)));
    response_data.producedAt = now;
    response_data.responses = {std::move(single)};
    if (nonce.has_value()) {
        response_data.responseExtensions = std::vector<Common::Extension>{
            Common::Extension::make_local_response_extensions().re_ocsp_nonce(*nonce),
        };
    }

    Ocsp::BasicOCSPResponse basic{};
    basic.tbsResponseData = std::move(response_data);
    basic.signatureAlgorithm.algorithm =
        PssOaep::PKIX1PSSOAEPAlgorithms2009Oids::Sha256WithRSAEncryption.get();
    basic.signature = asn1kit::bit_string(
        asn1kit::bytes::from_vector(std::vector<std::uint8_t>(32, 0xCD)), 0);

    Ocsp::OCSPResponse response{};
    response.responseStatus = Ocsp::OCSPResponseStatus::successful;
    response.responseBytes = Ocsp::ResponseBytes::make_response_set().basic_response(basic);
    return asn1kit::encode(response, asn1kit::encoding::der);
}

void print_ocsp_response(std::span<const std::uint8_t> encoded, std::ostream& out) {
    const auto response = asn1kit::decode<Ocsp::OCSPResponse>(encoded, asn1kit::encoding::der);
    out << "OCSPResponse:\n";
    out << "  responseStatus: " << response_status_name(response.responseStatus) << "\n";
    if (!response.responseBytes.has_value()) {
        out << "  responseBytes: <absent>\n";
        return;
    }
    out << "  responseType: " << format_oid(response.responseBytes->responseType) << "\n";
    auto basic = require_basic_response(response);
    out << "  BasicOCSPResponse:\n";
    out << "    producedAt: " << format_time(basic.tbsResponseData.producedAt) << "\n";
    out << "    responses: " << basic.tbsResponseData.responses.size() << "\n";
    for (std::size_t i = 0; i < basic.tbsResponseData.responses.size(); ++i) {
        const auto& item = basic.tbsResponseData.responses[i];
        out << "      single[" << (i + 1) << "]:\n";
        print_cert_id(item.certID, out, 4);
        write_indent(out, 4);
        out << "certStatus: " << cert_status_name(item.certStatus.kind) << "\n";
        write_indent(out, 4);
        out << "thisUpdate: " << format_time(item.thisUpdate) << "\n";
    }
    if (const auto nonce = basic.tbsResponseData.local().re_ocsp_nonce()) {
        out << "    responseExtensions.nonce: " << hex_bytes(nonce->span(), 64) << "\n";
    } else {
        out << "    responseExtensions.nonce: <absent>\n";
    }
    out << "    signatureAlgorithm: " << format_oid(basic.signatureAlgorithm.algorithm) << "\n";
    out << "    signature: " << basic.signature.contents().size() << " octets (educational stub)\n";
}

OcspCheckResult check_ocsp(
    std::span<const std::uint8_t> request_der,
    std::span<const std::uint8_t> response_der) {
    OcspCheckResult result{};
    try {
        const auto request = asn1kit::decode<Ocsp::OCSPRequest>(request_der, asn1kit::encoding::der);
        const auto response =
            asn1kit::decode<Ocsp::OCSPResponse>(response_der, asn1kit::encoding::der);
        if (request.tbsRequest.requestList.empty()) {
            throw std::runtime_error("OCSPRequest has an empty requestList.");
        }
        const auto& expected_cert_id = request.tbsRequest.requestList.front().reqCert;
        const auto request_nonce = request.tbsRequest.local().re_ocsp_nonce();

        auto basic = require_basic_response(response);
        if (basic.tbsResponseData.responses.empty()) {
            throw std::runtime_error("BasicOCSPResponse has no SingleResponse entries.");
        }
        const auto& single = basic.tbsResponseData.responses.front();
        if (!cert_id_equal(single.certID, expected_cert_id)) {
            throw std::runtime_error("Response CertID does not match the request.");
        }
        if (request_nonce.has_value()) {
            const auto response_nonce = basic.tbsResponseData.local().re_ocsp_nonce();
            if (!response_nonce.has_value() || *response_nonce != *request_nonce) {
                throw std::runtime_error("Response nonce does not echo the request nonce.");
            }
        }
        if (basic.signature.contents().empty()) {
            throw std::runtime_error("Response signature was rejected by the educational stub.");
        }

        result.accepted = true;
        result.cert_status = cert_status_name(single.certStatus.kind);
        result.reason =
            "structure accepted; CertID and nonce matched; signature stub accepted";
    } catch (const std::exception& ex) {
        result.accepted = false;
        result.reason = ex.what();
    }
    return result;
}

} // namespace asn1kit::cms_demo
