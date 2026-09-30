using Asn1Kit.Ir;

namespace Asn1Kit.Tests;

public sealed class DvcsTests
{
    [Fact]
    public void FullDependencyGraphCompilesAndValidatesSchema()
    {
        var document = DvcsTestData.Compile();

        Assert.Equal(10, document.Modules.Count);
        Assert.Equal(
            new[]
            {
                "PKIX1Explicit88", "PKIX1Implicit88", "CryptographicMessageSyntax2004",
                "PKCS10", "PKIXCRMF", "PKIXCMP", "OCSP", "ExtendedSecurityServices",
                "SecureMimeMessageV3", "PKIXDVCS"
            },
            document.Modules.Select(m => m.Name));
        IrSerializer.ValidateSchema(IrSerializer.ToJson(document));
    }

    [Fact]
    public void SpotChecksDvcsShapesImportsAndIdentifiers()
    {
        var module = DvcsTestData.Compile().Modules.Single(m => m.Name == "PKIXDVCS");

        Assert.Equal("1.3.6.1.5.5.7.0.15", module.Oid);
        Assert.Equal(TagDefaults.Implicit, module.TagDefault);
        Assert.Equal("Asn1Kit.Dvcs", IrOptions.CSharpNamespace(module.Options));
        Assert.Contains(module.Imports, i => i.Module == "PKIX1Implicit88" && i.Types.Contains("GeneralNames"));
        Assert.Contains(module.Imports, i =>
            i.Module == "CryptographicMessageSyntax2004" &&
            i.Types.Contains("DigestAlgorithmIdentifier") && i.Types.Contains("SignerInfos"));

        var types = module.Types.ToDictionary(t => t.Name);
        var request = Assert.IsType<SequenceType>(types["DVCSRequest"].Type);
        Assert.Equal(new[] { "requestInformation", "data", "transactionIdentifier" },
            request.Components.Select(c => c.Name));
        Assert.True(request.Components[2].Optional);

        var response = Assert.IsType<ChoiceType>(types["DVCSResponse"].Type);
        var error = response.Components.Single(c => c.Name == "dvErrorNote");
        Assert.Equal(0, error.Type.Tag!.Number);
        Assert.Equal(TagModes.Implicit, error.Type.Tag.Mode);

        var certInfo = Assert.IsType<SequenceType>(types["DVCSCertInfo"].Type);
        Assert.Equal(3, certInfo.Components.Single(c => c.Name == "certs").Type.Tag!.Number);
        Assert.NotNull(certInfo.Components.Single(c => c.Name == "version").Default);

        var values = module.Values.ToDictionary(v => v.Name);
        Assert.Equal("1.2.840.113549.1.9.16.1.7",
            Assert.IsType<IrOidValue>(values["id-ct-DVCSRequestData"].Value).Value);
        Assert.Equal("1.2.840.113549.1.9.16.1.8",
            Assert.IsType<IrOidValue>(values["id-ct-DVCSResponseData"].Value).Value);
    }

    [Fact]
    public void DependencyImportsResolveToExpectedModules()
    {
        var document = DvcsTestData.Compile();
        var imports = document.Modules.ToDictionary(
            m => m.Name,
            m => m.Imports.Select(i => i.Module).ToHashSet(StringComparer.Ordinal));

        Assert.Contains("CryptographicMessageSyntax2004", imports["PKIXCRMF"]);
        Assert.Contains("PKCS10", imports["PKIXCMP"]);
        Assert.Contains("PKIXCRMF", imports["PKIXCMP"]);
        Assert.Contains("PKIX1Explicit88", imports["OCSP"]);
        Assert.Contains("CryptographicMessageSyntax2004", imports["ExtendedSecurityServices"]);
        Assert.Contains("CryptographicMessageSyntax2004", imports["SecureMimeMessageV3"]);
        Assert.Contains("PKIXCMP", imports["PKIXDVCS"]);
        Assert.Contains("OCSP", imports["PKIXDVCS"]);
        Assert.Contains("ExtendedSecurityServices", imports["PKIXDVCS"]);
        Assert.Contains("SecureMimeMessageV3", imports["PKIXDVCS"]);
    }
}
