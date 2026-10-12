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
};

[[nodiscard]] std::vector<SignerResult> verify_attached(std::span<const std::uint8_t> encoded);

void print_certificate(std::span<const std::uint8_t> encoded, std::ostream& out);

void print_cms(std::span<const std::uint8_t> encoded, std::ostream& out);

[[nodiscard]] std::vector<std::uint8_t> build_attached_signed_data(
    std::span<const std::uint8_t> content,
    std::span<const std::uint8_t> certificate_der);

} // namespace asn1kit::cms_demo
