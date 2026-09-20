# BER/DER primitive vectors

Hex fixtures for Asn1Kit.Runtime encode/decode tests and cross-checks against `System.Formats.Asn1`.

## Format

Each JSON file is an array of cases:

```json
{
  "name": "unique-case-id",
  "rules": "der",
  "op": "boolean",
  "bytes": "0101FF",
  "value": true,
  "source": "optional provenance for external vectors"
}
```

| Field | Meaning |
| --- | --- |
| `rules` | `der` or `ber` — encoding rules for decode (and for DER also expected encode bytes) |
| `op` | Primitive kind: `boolean`, `integer`, `null`, `octetString`, `oid`, `bitString`, `string`, `time`, `sequence`, `setOf` |
| `bytes` | Hex of the full TLV (spaces optional) |
| `value` | Logical value for the op (shape depends on `op`) |
| `encode` | If `false`, fixture is decode-only (typical for non-canonical BER) |
| `reject` | If `true`, Asn1Kit must throw on decode under `rules` |
| `form` | For `string` / `time`: form name (`Utf8`, `Utc`, …) |
| `unusedBits` | For `bitString` |
| `fractionDigits` | For GeneralizedTime encode |
| `source` | Provenance for layer-3 external vectors |

## Layers

1. Own matrix — files in this directory (except `external/`).
2. Oracle — generated at runtime via `System.Formats.Asn1` (no fixture required).
3. External — `external/` with `source` provenance.
