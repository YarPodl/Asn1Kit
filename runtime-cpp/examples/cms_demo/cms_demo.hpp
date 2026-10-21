// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include <cstdint>
#include <ostream>
#include <span>
#include <string>
#include <vector>

namespace asn1kit::cms_demo {

struct SignerResult {
    int signer_number{};
    bool accepted{};
    std::string reason;
    std::string issuer;
    std::string subject;
    std::string ski;
    std::string key_usage;
};

[[nodiscard]] std::vector<SignerResult> verify_attached(std::span<const std::uint8_t> encoded);

void print_certificate(std::span<const std::uint8_t> encoded, std::ostream& out);

void print_cms(std::span<const std::uint8_t> encoded, std::ostream& out);

[[nodiscard]] std::vector<std::uint8_t> build_attached_signed_data(
    std::span<const std::uint8_t> content,
    std::span<const std::uint8_t> certificate_der);

[[nodiscard]] std::vector<std::uint8_t> build_ocsp_request(
    std::span<const std::uint8_t> ee_certificate_der,
    std::span<const std::uint8_t> issuer_certificate_der);

void print_ocsp_request(std::span<const std::uint8_t> encoded, std::ostream& out);

[[nodiscard]] std::vector<std::uint8_t> build_ocsp_response(
    std::span<const std::uint8_t> request_der);

void print_ocsp_response(std::span<const std::uint8_t> encoded, std::ostream& out);

struct OcspCheckResult {
    bool accepted{};
    std::string reason;
    std::string cert_status;
};

[[nodiscard]] OcspCheckResult check_ocsp(
    std::span<const std::uint8_t> request_der,
    std::span<const std::uint8_t> response_der);

} // namespace asn1kit::cms_demo
