// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "PKIX1Explicit2009.hpp"

namespace asn1kit::benchmarks {

/// Hand-built encode graphs (PKITS Trust Anchor / GoodCACRL scale). No Decode.
[[nodiscard]] asn1kit::modern::PKIX1Explicit2009::Certificate create_certificate();
[[nodiscard]] asn1kit::modern::PKIX1Explicit2009::CertificateList create_certificate_list();

} // namespace asn1kit::benchmarks
