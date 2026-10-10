// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/detail/encode_buffer.hpp"

#include <cstdint>

namespace asn1kit {

class writer;

/// Move-only RAII scope returned by writer::enter_*.
///
/// Finalize with end() (may throw std::logic_error) or destructor (noexcept;
/// LIFO / double-end misuse terminates).
class writer_scope {
public:
    writer_scope(const writer_scope&) = delete;
    writer_scope& operator=(const writer_scope&) = delete;

    writer_scope(writer_scope&& other) noexcept;
    writer_scope& operator=(writer_scope&&) = delete;

    ~writer_scope() noexcept;

    /// Finalizes the constructed value. Throws std::logic_error on LIFO / double-end.
    void end();

private:
    friend class writer;

    writer_scope(
        writer* w,
        detail::encode_frame frame,
        std::uint64_t scope_token,
        std::uint64_t parent_scope_token,
        bool sort_der_set_of) noexcept;

    writer* writer_{nullptr};
    detail::encode_frame frame_{};
    std::uint64_t scope_token_{0};
    std::uint64_t parent_scope_token_{0};
    bool sort_der_set_of_{false};
    bool active_{false};
};

} // namespace asn1kit
