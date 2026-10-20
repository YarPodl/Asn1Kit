// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include <openssl/cms.h>
#include <openssl/x509.h>

#include <cstddef>
#include <cstdint>
#include <memory>
#include <span>

namespace asn1kit::benchmarks::openssl_support {

struct x509_deleter {
    void operator()(X509* value) const noexcept;
};

struct x509_crl_deleter {
    void operator()(X509_CRL* value) const noexcept;
};

struct cms_content_info_deleter {
    void operator()(CMS_ContentInfo* value) const noexcept;
};

struct buffer_deleter {
    void operator()(unsigned char* value) const noexcept;
};

using x509_ptr = std::unique_ptr<X509, x509_deleter>;
using x509_crl_ptr = std::unique_ptr<X509_CRL, x509_crl_deleter>;
using cms_content_info_ptr = std::unique_ptr<CMS_ContentInfo, cms_content_info_deleter>;

class encoded_buffer {
public:
    encoded_buffer(unsigned char* data, std::size_t size) noexcept;

    [[nodiscard]] const unsigned char* data() const noexcept;
    [[nodiscard]] std::size_t size() const noexcept;
    [[nodiscard]] std::span<const std::uint8_t> bytes() const noexcept;

private:
    std::unique_ptr<unsigned char, buffer_deleter> data_;
    std::size_t size_;
};

[[nodiscard]] x509_ptr decode_certificate(std::span<const std::uint8_t> der);
[[nodiscard]] x509_crl_ptr decode_certificate_list(std::span<const std::uint8_t> der);
[[nodiscard]] cms_content_info_ptr decode_cms(std::span<const std::uint8_t> der);

[[nodiscard]] encoded_buffer encode_certificate(const X509& value);
[[nodiscard]] encoded_buffer encode_certificate_list(const X509_CRL& value);
[[nodiscard]] encoded_buffer encode_cms(const CMS_ContentInfo& value);

} // namespace asn1kit::benchmarks::openssl_support
