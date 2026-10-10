// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/any.hpp"

#include "asn1kit/detail/encode_buffer.hpp"
#include "asn1kit/reader.hpp"

#include <string>

namespace asn1kit {

any::any(bytes encoded) {
    if (encoded.empty()) {
        throw exception("ANY encoded TLV must not be empty.");
    }

    reader r(std::move(encoded), encoding::ber);
    auto parsed = r.read_any();
    if (!r.eof()) {
        throw exception("Encoded value is not a single complete TLV.");
    }

    tag_ = parsed.tag_;
    encoded_ = std::move(parsed.encoded_);
    contents_ = std::move(parsed.contents_);
}

any any::copy_from(std::span<const std::uint8_t> encoded) {
    if (encoded.empty()) {
        throw exception("ANY encoded TLV must not be empty.");
    }
    return any(bytes::copy_from(encoded));
}

any any::from_tag_and_contents(const tag& t, std::span<const std::uint8_t> contents) {
    detail::encode_buffer buffer;
    buffer.write_tlv(t, contents);
    auto encoded_vec = buffer.to_vector();
    const std::size_t contents_len = contents.size();
    auto owned = bytes::from_vector(std::move(encoded_vec));
    auto contents_view = owned.slice(owned.size() - contents_len, contents_len);
    return wrap(t, std::move(owned), std::move(contents_view));
}

std::string any::to_string() const {
    if (encoded_.empty()) {
        return "<empty>";
    }
    return tag_.to_string() + " (" + std::to_string(encoded_.size()) + " bytes)";
}

} // namespace asn1kit
