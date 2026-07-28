using System.Security.Cryptography;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Infrastructure;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Tests;

public sealed class RsaQueryPackageSignatureVerifierTests
{
    [Fact]
    public void Verify_AcceptsAGenericPackageSignedWithTheConfiguredTrustKey()
    {
        using var rsa = RSA.Create(2048);
        var definition = new QueryPackageDefinition
        {
            QueryId = "marketing.future.custom_report",
            Version = 1,
            System = "marketing",
            DatabaseProfile = "Marketing",
            Sql = "SELECT TOP (@limit) ITEM_ID FROM dbo.ITEMS_VIEW",
            Parameters = new List<QueryPackageParameterDefinition>
            {
                new() { Name = "limit", Type = "int", Required = true },
            },
            TimeoutSeconds = 30,
            MaxRows = 100,
        };
        var signature = rsa.SignData(
            QueryPackageSignaturePayload.Build(definition),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        var verifier = new RsaQueryPackageSignatureVerifier(new AppSettings
        {
            QueryPackageSigningKeyId = "test-key",
            QueryPackageSigningPublicKeyPem = rsa.ExportRSAPublicKeyPem(),
        });

        var verified = verifier.Verify(
            new SignedQueryPackage
            {
                Definition = definition,
                KeyId = "test-key",
                SignatureBase64 = Convert.ToBase64String(signature),
            },
            out var error);

        Assert.True(verified, error);
    }

    [Fact]
    public void Verify_RejectsAPackageWhenSignedSqlWasChangedAfterSigning()
    {
        using var rsa = RSA.Create(2048);
        var signedDefinition = new QueryPackageDefinition
        {
            QueryId = "infinity.future.custom_report",
            Version = 1,
            System = "infinity",
            DatabaseProfile = "InfinityRetailDB",
            Sql = "SELECT TOP (@limit) ProductId FROM Inventory.Data_Products",
        };
        var signature = rsa.SignData(
            QueryPackageSignaturePayload.Build(signedDefinition),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        var verifier = new RsaQueryPackageSignatureVerifier(new AppSettings
        {
            QueryPackageSigningKeyId = "test-key",
            QueryPackageSigningPublicKeyPem = rsa.ExportRSAPublicKeyPem(),
        });

        var verified = verifier.Verify(
            new SignedQueryPackage
            {
                Definition = signedDefinition with { Sql = "SELECT 1" },
                KeyId = "test-key",
                SignatureBase64 = Convert.ToBase64String(signature),
            },
            out _);

        Assert.False(verified);
    }
}
