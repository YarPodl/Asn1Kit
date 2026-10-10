// Copyright Asn1Kit contributors
// SPDX-License-Identifier: Apache-2.0

#include "asn1kit/null.hpp"

#include "asn1kit/reader.hpp"
#include "asn1kit/writer.hpp"

namespace asn1kit {

void null_value::encode(writer& w, const tag& t) {
    w.write_null(t);
}

null_value null_value::decode(reader& r, const tag& t) {
    r.read_null(t);
    return {};
}

} // namespace asn1kit
