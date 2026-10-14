// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "encode_samples.hpp"
#include "fixture_loader.hpp"

#include "PKIX1Explicit2009.hpp"

#include "asn1kit/encode_decode.hpp"
#include "asn1kit/writer.hpp"

#include <benchmark/benchmark.h>

#include <cstdint>
#include <vector>

namespace {

namespace Pkix = asn1kit::modern::PKIX1Explicit2009;

std::vector<std::uint8_t> g_cert_der;
Pkix::Certificate g_encode_sample{};
asn1kit::writer g_encode_writer{asn1kit::encoding::der};

void SetupCertificate(const benchmark::State&) {
    g_cert_der = asn1kit::benchmarks::read_pkix_fixture("TrustAnchorRootCertificate.crt");
    g_encode_sample = asn1kit::benchmarks::create_certificate();
    g_encode_writer.ensure_capacity(4096);
}

void Certificate_Decode(benchmark::State& state) {
    for (auto _ : state) {
        auto value = asn1kit::decode<Pkix::Certificate>(g_cert_der, asn1kit::encoding::der);
        benchmark::DoNotOptimize(value);
        benchmark::ClobberMemory();
    }
}

void Certificate_Encode(benchmark::State& state) {
    for (auto _ : state) {
        g_encode_writer.reset();
        g_encode_sample.encode(g_encode_writer);
        auto encoded = g_encode_writer.encode();
        benchmark::DoNotOptimize(encoded);
        benchmark::ClobberMemory();
    }
}

} // namespace

BENCHMARK(Certificate_Decode)->Unit(benchmark::kMicrosecond)->Setup(SetupCertificate);
BENCHMARK(Certificate_Encode)->Unit(benchmark::kMicrosecond)->Setup(SetupCertificate);
