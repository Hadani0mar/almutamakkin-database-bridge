using System.Security.Cryptography;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: QueryPackageKeygen <private-key-output-path>");
    return 2;
}

var privatePath = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!);
if (File.Exists(privatePath))
{
    Console.Error.WriteLine("Refusing to replace an existing private key.");
    return 3;
}

using var rsa = RSA.Create(3072);
await File.WriteAllTextAsync(privatePath, rsa.ExportRSAPrivateKeyPem());
// SubjectPublicKeyInfo is understood by both .NET and WebCrypto.  The latter
// verifies package signatures inside the secure publisher Edge Function.
Console.Write(rsa.ExportSubjectPublicKeyInfoPem());
return 0;
