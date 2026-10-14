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

std::vector<std::uint8_t> g_crl_der;
Pkix::CertificateList g_encode_sample{};
asn1kit::writer g_encode_writer{asn1kit::encoding::der};

void SetupCertificateList(const benchmark::State&) {
    g_crl_der = asn1kit::benchmarks::read_pkix_fixture("GoodCACRL.crl");
    g_encode_sample = asn1kit::benchmarks::create_certificate_list();
    g_encode_writer.ensure_capacity(4096);
}

void CertificateList_Decode(benchmark::State& state) {
    for (auto _ : state) {
        auto value = asn1kit::decode<Pkix::CertificateList>(g_crl_der, asn1kit::encoding::der);
        benchmark::DoNotOptimize(value);
        benchmark::ClobberMemory();
    }
}

void CertificateList_Encode(benchmark::State& state) {
    for (auto _ : state) {
        g_encode_writer.reset();
        g_encode_sample.encode(g_encode_writer);
        auto encoded = g_encode_writer.encode();
        benchmark::DoNotOptimize(encoded);
        benchmark::ClobberMemory();
    }
}

} // namespace

BENCHMARK(CertificateList_Decode)->Unit(benchmark::kMicrosecond)->Setup(SetupCertificateList);
BENCHMARK(CertificateList_Encode)->Unit(benchmark::kMicrosecond)->Setup(SetupCertificateList);
