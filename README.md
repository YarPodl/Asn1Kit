# Asn1Kit

English | [Русский](README.ru.md)

A toolkit for working with ASN.1: a module compiler that produces an intermediate JSON representation, a code generator, and a BER/DER runtime.

The repository is organized into the following directories:

| Directory | Contents |
| --- | --- |
| [compiler/](compiler/) | IR, compiler, C# code generator, CLI, and their tests |
| [runtime-csharp/](runtime-csharp/) | C# BER/DER runtime and its tests |
| [runtime-cpp/](runtime-cpp/) | C++20 BER/DER runtime (primitives + SEQUENCE/SET scopes) |

## Components

1. **Compiler** — converts ASN.1 text into JSON IR (`irVersion` plus the [schemas/asn1kit-ir-v1.json](schemas/asn1kit-ir-v1.json) schema). The resulting file can be edited to configure `options.csharp.namespace`, type names, and `generate: false`.
2. **Generator** — converts IR into source code. C# is currently supported, with C++ planned for the future.
3. **Runtime** — provides BER and DER primitives for the target language. Generated code calls this library.

## Get started

Asn1Kit uses a two-stage workflow: compile one or more ASN.1 modules into JSON IR, then generate C# sources from that IR. Keeping the IR as a separate artifact lets you review it, change code-generation options, or start from one of the IR files already included in this repository.

### 1. Produce or select an IR file

To compile your own ASN.1 module, run the CLI from the repository root. Use `-O csharp.namespace=...` to choose the namespace of the generated types:

```powershell
dotnet run --project compiler/src/Asn1Kit.Cli -- compile `
  -i path/to/MyProtocol.asn `
  -o artifacts/MyProtocol.json `
  -O csharp.namespace=MyCompany.MyProtocol
```

Pass multiple `-i` files when the module imports definitions from other ASN.1 modules.

Alternatively, start with a ready-made IR file from [compiler/fixtures/ir](compiler/fixtures/ir/). Copy it into your project if you want to customize its `options`, such as the C# namespace, generated names, or whether individual definitions are emitted. The IR format is documented in [docs/ir-schema.md](docs/ir-schema.md).

### 2. Generate C# sources

Create a class library for the generated module and emit the `.g.cs` files into it:

```powershell
dotnet new classlib --framework net6.0 -o generated/MyProtocol

dotnet run --project compiler/src/Asn1Kit.Cli -- generate `
  -i artifacts/MyProtocol.json `
  --lang csharp `
  -o generated/MyProtocol
```

You can pass an `.asn` file directly to `generate` for a shorter workflow, but keeping the IR is useful when its options need to be reviewed or maintained.

### 3. Reference the runtime

Generated C# sources call `Asn1Writer`, `Asn1Reader`, and other APIs from `Asn1Kit.Runtime`, so the generated class library must reference the runtime. While working from this repository, add a project reference:

```powershell
dotnet add generated/MyProtocol/MyProtocol.csproj reference `
  runtime-csharp/src/Asn1Kit.Runtime/Asn1Kit.Runtime.csproj
```

After `Asn1Kit.Runtime` is published to NuGet, consumers will instead use:

```powershell
dotnet add generated/MyProtocol/MyProtocol.csproj package Asn1Kit.Runtime
```

Finally, reference the generated class library from your application:

```powershell
dotnet add path/to/MyApplication.csproj reference `
  generated/MyProtocol/MyProtocol.csproj
```

Generated composite types expose an instance `Encode(Asn1Writer)` method and a static `Decode(Asn1Reader)` method. A typical DER round trip looks like this:

```csharp
using Asn1Kit.Runtime;
using MyCompany.MyProtocol;

var value = new MyMessage
{
    // Initialize fields generated from the ASN.1 definition.
};

var writer = new Asn1Writer(Asn1Encoding.Der);
value.Encode(writer);
byte[] encoded = writer.Encode();

var reader = new Asn1Reader(encoded, Asn1Encoding.Der);
MyMessage decoded = MyMessage.Decode(reader);
```

Replace `MyMessage` with a type defined by your ASN.1 module. Consult the current [support status](docs/status.md) before using language constructs outside the supported compiler and runtime profile.

## Build

.NET 6 SDK is required.

```text
dotnet build Asn1Kit.sln
dotnet test Asn1Kit.sln
```

## CLI

```text
dotnet run --project compiler/src/Asn1Kit.Cli -- compile -i compiler/fixtures/asn1/example.asn -o out.json
dotnet run --project compiler/src/Asn1Kit.Cli -- generate -i compiler/fixtures/ir/example.json --lang csharp -o ./generated
```

`generate` also accepts `.asn` files directly and compiles them in memory. The repeatable `-O` / `--option path=value` argument applies `options` to every module, for example `-O csharp.namespace=Asn1Kit.Pkix` for the PKIX golden output.

The reference generated C# code for PKIX, CMS, DVCS, and their dependent protocols is located in [runtime-csharp/generated/Asn1Kit.Pkix](runtime-csharp/generated/Asn1Kit.Pkix/).

## Compiler profile

Modules with `DEFINITIONS`, `EXPLICIT` / `IMPLICIT` / `AUTOMATIC` tags, and `IMPORTS` / `EXPORTS` across the supplied files are supported.

Supported types include `BOOLEAN`, `INTEGER`, `ENUMERATED`, `BIT STRING`, `OCTET STRING`, `NULL`, `OBJECT IDENTIFIER`, character string types (`UTF8String`, `PrintableString`, `IA5String`, etc.), `UTCTime` / `GeneralizedTime`, `SEQUENCE` / `SEQUENCE OF`, `SET` / `SET OF`, `CHOICE`, `ANY` / `ANY DEFINED BY`, `OPTIONAL`, `DEFAULT`, and `SIZE` / range constraints, including resolved `ub-*` references.

Value assignments such as `id-pkix OBJECT IDENTIFIER ::= { … }` and `ub-name INTEGER ::= 32768` are stored in `module.values`.

The reference RFC 5280 fixtures are [compiler/fixtures/asn1/pkix1-explicit88.asn](compiler/fixtures/asn1/pkix1-explicit88.asn) (Appendix A.1) and [compiler/fixtures/asn1/pkix1-implicit88.asn](compiler/fixtures/asn1/pkix1-implicit88.asn) (Appendix A.2, with `IMPORTS` from Explicit88).

The complete DVCS graph is based on the ASN.1:1988 module from RFC 3029 and the original CMP, CRMF, OCSP, ESS, and S/MIME RFCs. Legacy X.509 and CMS imports are normalized to the local RFC 5280 modules and `CryptographicMessageSyntax2004`. Public namespaces include `Asn1Kit.Dvcs`, `.Cmp`, `.Crmf`, `.Ocsp`, `.Ess`, `.Smime`, and `.Pkcs10`.

Two profiles share the same IR and C# backend:

- **Legacy** (PKCS/PKIX Explicit88/Implicit88, curated CMS 2004, DVCS graph) — ASN.1:1988 with `ANY` / overlay bindings; no `CLASS` or parameterization in those modules.
- **Modern** — RFC 5911/5912/6268/8410 corpus with `CLASS`, objects/sets, `WITH SYNTAX`, and parameterized types, emitted as [Asn1Kit.Modern](runtime-csharp/generated/Asn1Kit.Modern/).

Always rejected with an explicit error: `COMPONENTS OF`, `REAL`, `EXTERNAL`, and IOC/parameterization forms outside the documented modern profile. Details and gaps: [docs/status.md](docs/status.md).

The C# backend generates `sequence`, `choice`, `sequenceOf`, `set`, `setOf`, and `enumerated` as C# `enum`, as well as primitives (`boolean`, `integer`, `octetString`, `oid`, `bitString`, `string`, `time`, and `any` as `Asn1Any`).

The runtime supports BER, including indefinite-length decoding, and DER with canonical encoding.

For more information, see [docs/architecture.md](docs/architecture.md), [docs/ir-schema.md](docs/ir-schema.md), and the current [support status](docs/status.md).

## License

Asn1Kit is distributed under the [Apache License 2.0](LICENSE). It permits commercial use, modification, and distribution, including as part of proprietary products, subject to the terms of the license.

Code generated by Asn1Kit from user-provided ASN.1 modules or IR may be used without additional restrictions imposed by Asn1Kit. Third-party materials with an explicitly identified source or separate license remain subject to their respective copyright holders' terms.
