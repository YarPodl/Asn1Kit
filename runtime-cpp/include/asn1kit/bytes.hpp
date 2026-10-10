// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#pragma once

#include "asn1kit/exception.hpp"

#include <cstdint>
#include <memory>
#include <span>
#include <utility>
#include <vector>

namespace asn1kit {

/// Octet window: `span` view plus type-erased keep-alive of the backing store.
///
/// - `borrow` — no keep-alive (caller guarantees lifetime).
/// - `from_shared` / `from_vector` — extend lifetime of the backing store.
/// - `copy_from` — owned copy of the octets.
/// - `slice` keeps the same owner.
class bytes {
public:
    bytes() = default;

    [[nodiscard]] std::span<const std::uint8_t> span() const noexcept { return span_; }
    [[nodiscard]] const std::uint8_t* data() const noexcept { return span_.data(); }
    [[nodiscard]] std::size_t size() const noexcept { return span_.size(); }
    [[nodiscard]] bool empty() const noexcept { return span_.empty(); }
    [[nodiscard]] const std::shared_ptr<void>& owner() const noexcept { return owner_; }

    [[nodiscard]] std::uint8_t operator[](std::size_t index) const { return span_[index]; }

    [[nodiscard]] bytes slice(std::size_t offset, std::size_t length) const {
        if (offset > span_.size() || length > span_.size() - offset) {
            throw exception("bytes slice is out of range.");
        }
        return bytes(span_.subspan(offset, length), owner_);
    }

    [[nodiscard]] bytes slice(std::size_t offset) const {
        if (offset > span_.size()) {
            throw exception("bytes slice is out of range.");
        }
        return bytes(span_.subspan(offset), owner_);
    }

    [[nodiscard]] std::vector<std::uint8_t> to_vector() const {
        return std::vector<std::uint8_t>(span_.begin(), span_.end());
    }

    [[nodiscard]] static bytes borrow(std::span<const std::uint8_t> view) {
        return bytes(view, nullptr);
    }

    template <typename T>
    [[nodiscard]] static bytes from_shared(std::shared_ptr<T> owner, std::span<const std::uint8_t> view) {
        std::shared_ptr<void> keep(owner, static_cast<void*>(owner.get()));
        return bytes(view, std::move(keep));
    }

    [[nodiscard]] static bytes from_vector(std::vector<std::uint8_t> value) {
        auto owned = std::make_shared<std::vector<std::uint8_t>>(std::move(value));
        auto view = std::span<const std::uint8_t>(owned->data(), owned->size());
        std::shared_ptr<void> keep(owned, static_cast<void*>(owned.get()));
        return bytes(view, std::move(keep));
    }

    [[nodiscard]] static bytes copy_from(std::span<const std::uint8_t> view) {
        return from_vector(std::vector<std::uint8_t>(view.begin(), view.end()));
    }

    friend bool operator==(const bytes& a, const bytes& b) noexcept {
        if (a.span_.size() != b.span_.size()) {
            return false;
        }
        for (std::size_t i = 0; i < a.span_.size(); ++i) {
            if (a.span_[i] != b.span_[i]) {
                return false;
            }
        }
        return true;
    }
    friend bool operator!=(const bytes& a, const bytes& b) noexcept { return !(a == b); }

private:
    bytes(std::span<const std::uint8_t> view, std::shared_ptr<void> owner)
        : span_(view), owner_(std::move(owner)) {}

    std::span<const std::uint8_t> span_{};
    std::shared_ptr<void> owner_{};
};

} // namespace asn1kit
