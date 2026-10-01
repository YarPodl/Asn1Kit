# Asn1Kit.Runtime

Runtime library for encoding and decoding ASN.1 values using BER and DER. It provides `Asn1Writer`, `Asn1Reader`, ASN.1 tags, and primitive types for use with both Asn1Kit-generated code and handwritten types.

## Installation

```powershell
dotnet add package Asn1Kit.Runtime
```

## Example

```csharp
using Asn1Kit.Runtime;

var writer = new Asn1Writer(Asn1Encoding.Der);
writer.WriteInteger(Asn1Tag.Integer, 42);
byte[] encoded = writer.Encode();

var reader = new Asn1Reader(encoded, Asn1Encoding.Der);
int value = reader.ReadInt32(Asn1Tag.Integer);
```

The writer always produces canonical definite-length encodings. The reader supports DER and BER, including indefinite-length constructed values in the supported forms.

For API documentation and the current feature matrix, see the [Asn1Kit repository](https://github.com/YarPodl/Asn1Kit/tree/main/runtime-csharp).

This package is distributed under the [Apache License 2.0](https://github.com/YarPodl/Asn1Kit/blob/main/LICENSE).
