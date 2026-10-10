// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include <stdexcept>
#include <string>

namespace asn1kit {

/// ASN.1 encoding or decoding error.
class exception : public std::runtime_error {
public:
    explicit exception(const std::string& message) : std::runtime_error(message) {}
    explicit exception(const char* message) : std::runtime_error(message) {}
};

} // namespace asn1kit
