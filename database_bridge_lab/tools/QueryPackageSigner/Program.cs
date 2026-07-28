using System.Security.Cryptography;
using System.Text.Json;
using Almutamakkin.DatabaseBridge.Core;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: QueryPackageSigner <private-key.pem> <package-definition.json> <signed-package.json>");
    return 2;
}

var privateKeyPath = Path.GetFullPath(args[0]);
var inputPath = Path.GetFullPath(args[1]);
var outputPath = Path.GetFullPath(args[2]);
if (!File.Exists(privateKeyPath) || !File.Exists(inputPath))
{
    Console.Error.WriteLine("Private key or package definition file is missing.");
    return 3;
}

var definition = JsonSerializer.Deserialize<QueryPackageDefinition>(
    await File.ReadAllTextAsync(inputPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
if (definition is null)
{
    Console.Error.WriteLine("Unable to parse package definition.");
    return 4;
}

using var rsa = RSA.Create();
rsa.ImportFromPem(await File.ReadAllTextAsync(privateKeyPath));
var signature = rsa.SignData(
    QueryPackageSignaturePayload.Build(definition),
    HashAlgorithmName.SHA256,
    RSASignaturePadding.Pss);

var result = new
{
    definition,
    keyId = "amkq-2026-07-27",
    signatureBase64 = Convert.ToBase64String(signature),
};
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Signed query package written.");
return 0;
