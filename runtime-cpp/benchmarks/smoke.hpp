// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

namespace asn1kit::benchmarks {

/// Loads Cert/CRL/CMS fixtures and verifies decode/encode + retained TBS TLV on CMS certs.
void verify_fixtures();

} // namespace asn1kit::benchmarks
