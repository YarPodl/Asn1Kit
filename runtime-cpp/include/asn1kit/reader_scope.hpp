// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/detail/decode_cursor.hpp"

#include <cstdint>

namespace asn1kit {

class reader;

/// Move-only RAII scope returned by reader::enter_*.
///
/// Restores the outer decode window on end() / destructor. Double-end is
/// idempotent (like C# Asn1ReaderScope). Explicit end() throws std::logic_error
/// on LIFO violation; destructor terminates on the same misuse.
class reader_scope {
public:
    reader_scope(const reader_scope&) = delete;
    reader_scope& operator=(const reader_scope&) = delete;

    reader_scope(reader_scope&& other) noexcept;
    reader_scope& operator=(reader_scope&&) = delete;

    ~reader_scope() noexcept;

    /// Restores the outer window. Idempotent if already ended.
    void end();

private:
    friend class reader;

    reader_scope(
        reader* r,
        detail::decode_cursor saved_cursor,
        std::uint64_t expected_scope_token,
        std::uint64_t parent_scope_token) noexcept;

    reader* reader_{nullptr};
    detail::decode_cursor saved_cursor_;
    std::uint64_t expected_scope_token_{0};
    std::uint64_t parent_scope_token_{0};
    bool active_{false};
};

} // namespace asn1kit
