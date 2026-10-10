// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include <cstddef>
#include <stdexcept>
#include <string>

namespace asn1kit {

/// ASN.1 encoding or decoding error.
///
/// Decode failures may carry an absolute byte offset into the root input buffer.
/// Source file/line is intentionally omitted; the reason text identifies the site.
class exception : public std::runtime_error {
public:
    explicit exception(const std::string& message)
        : std::runtime_error(message), has_offset_(false), offset_(0) {}

    explicit exception(const char* message)
        : std::runtime_error(message), has_offset_(false), offset_(0) {}

    exception(std::string reason, std::size_t offset)
        : std::runtime_error(format_with_offset(reason, offset))
        , has_offset_(true)
        , offset_(offset) {}

    exception(const exception&) = default;
    exception& operator=(const exception&) = default;
    exception(exception&&) noexcept = default;
    exception& operator=(exception&&) noexcept = default;

    [[nodiscard]] bool has_offset() const noexcept { return has_offset_; }

    /// Absolute byte index in the root decode buffer. Valid only when has_offset().
    [[nodiscard]] std::size_t offset() const noexcept { return offset_; }

private:
    static std::string format_with_offset(const std::string& reason, std::size_t offset) {
        return reason + " (offset=" + std::to_string(offset) + ")";
    }

    bool has_offset_;
    std::size_t offset_;
};

} // namespace asn1kit
