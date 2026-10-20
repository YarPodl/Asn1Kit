// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "openssl_support.hpp"

#include <openssl/crypto.h>
#include <openssl/err.h>

#include <limits>
#include <stdexcept>
#include <string>
#include <utility>

namespace asn1kit::benchmarks::openssl_support {
namespace {

[[nodiscard]] std::runtime_error openssl_error(const char* operation) {
    std::string message(operation);
    bool found = false;
    while (const auto code = ERR_get_error()) {
        char detail[256]{};
        ERR_error_string_n(code, detail, sizeof(detail));
        message += found ? "; " : ": ";
        message += detail;
        found = true;
    }
    if (!found) {
        message += ": OpenSSL error queue is empty.";
    }
    return std::runtime_error(std::move(message));
}

[[nodiscard]] long checked_length(std::size_t size) {
    if (size == 0) {
        throw std::invalid_argument("DER input must not be empty.");
    }
    if (size > static_cast<std::size_t>((std::numeric_limits<long>::max)())) {
        throw std::length_error("DER input is too large for OpenSSL d2i API.");
    }
    return static_cast<long>(size);
}

template <typename T, typename Deleter>
[[nodiscard]] std::unique_ptr<T, Deleter> decode(
    std::span<const std::uint8_t> der,
    T* (*decoder)(T**, const unsigned char**, long),
    const char* operation) {
    ERR_clear_error();
    const auto* cursor = reinterpret_cast<const unsigned char*>(der.data());
    const auto* const end = cursor + der.size();
    auto* decoded = decoder(nullptr, &cursor, checked_length(der.size()));
    if (decoded == nullptr) {
        throw openssl_error(operation);
    }

    std::unique_ptr<T, Deleter> result(decoded);
    if (cursor != end) {
        throw std::runtime_error(std::string(operation) + ": trailing DER data.");
    }
    return result;
}

template <typename T>
[[nodiscard]] encoded_buffer encode(
    const T& value,
    int (*encoder)(const T*, unsigned char**),
    const char* operation) {
    ERR_clear_error();
    unsigned char* data = nullptr;
    const int size = encoder(&value, &data);
    if (size <= 0 || data == nullptr) {
        if (data != nullptr) {
            OPENSSL_free(data);
        }
        throw openssl_error(operation);
    }
    return encoded_buffer(data, static_cast<std::size_t>(size));
}

} // namespace

void x509_deleter::operator()(X509* value) const noexcept { X509_free(value); }

void x509_crl_deleter::operator()(X509_CRL* value) const noexcept { X509_CRL_free(value); }

void cms_content_info_deleter::operator()(CMS_ContentInfo* value) const noexcept {
    CMS_ContentInfo_free(value);
}

void buffer_deleter::operator()(unsigned char* value) const noexcept { OPENSSL_free(value); }

encoded_buffer::encoded_buffer(unsigned char* data, std::size_t size) noexcept
    : data_(data), size_(size) {}

const unsigned char* encoded_buffer::data() const noexcept { return data_.get(); }

std::size_t encoded_buffer::size() const noexcept { return size_; }

std::span<const std::uint8_t> encoded_buffer::bytes() const noexcept {
    return {reinterpret_cast<const std::uint8_t*>(data_.get()), size_};
}

x509_ptr decode_certificate(std::span<const std::uint8_t> der) {
    return decode<X509, x509_deleter>(der, d2i_X509, "d2i_X509 failed");
}

x509_crl_ptr decode_certificate_list(std::span<const std::uint8_t> der) {
    return decode<X509_CRL, x509_crl_deleter>(der, d2i_X509_CRL, "d2i_X509_CRL failed");
}

cms_content_info_ptr decode_cms(std::span<const std::uint8_t> der) {
    return decode<CMS_ContentInfo, cms_content_info_deleter>(
        der, d2i_CMS_ContentInfo, "d2i_CMS_ContentInfo failed");
}

encoded_buffer encode_certificate(const X509& value) {
    return encode(value, i2d_X509, "i2d_X509 failed");
}

encoded_buffer encode_certificate_list(const X509_CRL& value) {
    return encode(value, i2d_X509_CRL, "i2d_X509_CRL failed");
}

encoded_buffer encode_cms(const CMS_ContentInfo& value) {
    return encode(value, i2d_CMS_ContentInfo, "i2d_CMS_ContentInfo failed");
}

} // namespace asn1kit::benchmarks::openssl_support
