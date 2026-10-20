// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "encode_samples.hpp"
#include "fixture_loader.hpp"
#include "openssl_support.hpp"

#include "PKIX1Explicit2009.hpp"
#include "PKIX1Implicit2009.hpp"
#include "PKIXCommonTypes2009.hpp"

#include "asn1kit/encode_decode.hpp"
#include "asn1kit/writer.hpp"

#include <benchmark/benchmark.h>

#include <openssl/x509.h>

#include <cstdint>
#include <span>
#include <stdexcept>
#include <vector>

namespace {

namespace Pkix = asn1kit::modern::PKIX1Explicit2009;
namespace Implicit = asn1kit::modern::PKIX1Implicit2009;

constexpr std::size_t large_crl_entry_count = 1'165'000;
constexpr std::size_t mebibyte = 1024 * 1024;
constexpr std::size_t large_crl_min_size = 39 * mebibyte;
constexpr std::size_t large_crl_max_size = 41 * mebibyte;

std::vector<std::uint8_t> g_crl_der;
std::span<const std::uint8_t> g_large_crl_der;
Pkix::CertificateList g_encode_sample{};
asn1kit::writer g_encode_writer{asn1kit::encoding::der};
asn1kit::benchmarks::openssl_support::x509_crl_ptr g_openssl_certificate_list;

void require(bool ok, const char* message) {
    if (!ok) {
        throw std::runtime_error(message);
    }
}

void verify_asn1kit_large_crl(std::span<const std::uint8_t> der) {
    const auto crl = asn1kit::decode<Pkix::CertificateList>(der, asn1kit::encoding::der);
    require(
        crl.toBeSigned.revokedCertificates.has_value(),
        "Generated large CRL must contain revoked certificates.");
    const auto& entries = *crl.toBeSigned.revokedCertificates;
    require(
        entries.size() == large_crl_entry_count,
        "Asn1Kit decoded an unexpected large CRL entry count.");

    const auto reason_oid = Implicit::PKIX1Implicit2009Oids::IdCeCRLReasons.get();
    for (const auto& entry : entries) {
        require(
            entry.crlEntryExtensions.has_value() && entry.crlEntryExtensions->size() == 1,
            "Every generated large CRL entry must contain exactly one extension.");
        require(
            entry.crlEntryExtensions->front().extnID == reason_oid,
            "Every generated large CRL entry extension must be CRLReason.");
    }

    require(
        entries.front().crlEntryExtensions->front().crl_entry_extensions().crl_reason() ==
            Implicit::CRLReason::keyCompromise,
        "First generated large CRL reason must be keyCompromise.");
    require(
        entries.back().crlEntryExtensions->front().crl_entry_extensions().crl_reason() ==
            Implicit::CRLReason::keyCompromise,
        "Last generated large CRL reason must be keyCompromise.");
}

void verify_openssl_large_crl(std::span<const std::uint8_t> der) {
    const auto crl = asn1kit::benchmarks::openssl_support::decode_certificate_list(der);
    const auto* entries = X509_CRL_get_REVOKED(crl.get());
    require(entries != nullptr, "OpenSSL decoded no entries from the generated large CRL.");
    require(
        sk_X509_REVOKED_num(entries) == static_cast<int>(large_crl_entry_count),
        "OpenSSL decoded an unexpected large CRL entry count.");
    for (int i = 0; i < sk_X509_REVOKED_num(entries); ++i) {
        const auto* entry = sk_X509_REVOKED_value(entries, i);
        require(
            X509_REVOKED_get_ext_count(entry) == 1 &&
                X509_REVOKED_get_ext_by_NID(entry, NID_crl_reason, -1) == 0,
            "Every OpenSSL large CRL entry must contain exactly one CRLReason extension.");
    }
}

struct large_crl_fixture {
    large_crl_fixture()
        : der(asn1kit::encode(
              asn1kit::benchmarks::create_certificate_list(large_crl_entry_count),
              asn1kit::encoding::der)) {
        require(
            der.size() >= large_crl_min_size && der.size() <= large_crl_max_size,
            "Generated large CRL size must be between 39 and 41 MiB.");
        verify_asn1kit_large_crl(der);
        verify_openssl_large_crl(der);
    }

    std::vector<std::uint8_t> der;
};

[[nodiscard]] const large_crl_fixture& get_large_crl_fixture() {
    static const large_crl_fixture fixture;
    return fixture;
}

void SetupCertificateList(const benchmark::State&) {
    g_crl_der = asn1kit::benchmarks::read_pkix_fixture("GoodCACRL.crl");
    g_encode_sample = asn1kit::benchmarks::create_certificate_list();
    g_encode_writer.ensure_capacity(4096);
    g_openssl_certificate_list =
        asn1kit::benchmarks::openssl_support::decode_certificate_list(g_crl_der);
}

void SetupLargeCertificateList(const benchmark::State&) {
    g_large_crl_der = std::span<const std::uint8_t>{get_large_crl_fixture().der};
}

void CertificateList_Asn1Kit_Decode(benchmark::State& state) {
    for (auto _ : state) {
        auto value = asn1kit::decode<Pkix::CertificateList>(g_crl_der, asn1kit::encoding::der);
        benchmark::DoNotOptimize(value);
        benchmark::ClobberMemory();
    }
}

void CertificateList_Asn1Kit_Encode(benchmark::State& state) {
    for (auto _ : state) {
        g_encode_writer.reset();
        g_encode_sample.encode(g_encode_writer);
        auto encoded = g_encode_writer.encode();
        benchmark::DoNotOptimize(encoded);
        benchmark::ClobberMemory();
    }
}

void CertificateList_OpenSSL_Decode(benchmark::State& state) {
    for (auto _ : state) {
        auto value = asn1kit::benchmarks::openssl_support::decode_certificate_list(g_crl_der);
        benchmark::DoNotOptimize(value.get());
        benchmark::ClobberMemory();
    }
}

void CertificateList_OpenSSL_Encode(benchmark::State& state) {
    for (auto _ : state) {
        auto encoded = asn1kit::benchmarks::openssl_support::encode_certificate_list(
            *g_openssl_certificate_list);
        benchmark::DoNotOptimize(encoded.data());
        benchmark::DoNotOptimize(encoded.size());
        benchmark::ClobberMemory();
    }
}

void LargeCertificateList_Asn1Kit_Decode(benchmark::State& state) {
    for (auto _ : state) {
        auto value =
            asn1kit::decode<Pkix::CertificateList>(g_large_crl_der, asn1kit::encoding::der);
        benchmark::DoNotOptimize(value);
        benchmark::ClobberMemory();
    }
    state.SetBytesProcessed(
        state.iterations() * static_cast<std::int64_t>(g_large_crl_der.size()));
}

void LargeCertificateList_OpenSSL_Decode(benchmark::State& state) {
    for (auto _ : state) {
        auto value =
            asn1kit::benchmarks::openssl_support::decode_certificate_list(g_large_crl_der);
        benchmark::DoNotOptimize(value.get());
        benchmark::ClobberMemory();
    }
    state.SetBytesProcessed(
        state.iterations() * static_cast<std::int64_t>(g_large_crl_der.size()));
}

} // namespace

BENCHMARK(CertificateList_Asn1Kit_Decode)
    ->Unit(benchmark::kMicrosecond)
    ->Setup(SetupCertificateList);
BENCHMARK(CertificateList_Asn1Kit_Encode)
    ->Unit(benchmark::kMicrosecond)
    ->Setup(SetupCertificateList);
BENCHMARK(CertificateList_OpenSSL_Decode)
    ->Unit(benchmark::kMicrosecond)
    ->Setup(SetupCertificateList);
BENCHMARK(CertificateList_OpenSSL_Encode)
    ->Unit(benchmark::kMicrosecond)
    ->Setup(SetupCertificateList);
BENCHMARK(LargeCertificateList_Asn1Kit_Decode)
    ->Unit(benchmark::kMillisecond)
    ->Setup(SetupLargeCertificateList);
BENCHMARK(LargeCertificateList_OpenSSL_Decode)
    ->Unit(benchmark::kMillisecond)
    ->Setup(SetupLargeCertificateList);
