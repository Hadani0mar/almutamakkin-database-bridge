namespace Almutamakkin.DatabaseBridge.Protocol;

/// <summary>
/// Public verification key for the offline package publisher.  This is safe to
/// ship; the matching private key is kept outside the repository and outside
/// every bridge/mobile installation.
/// </summary>
public static class QueryPackageTrustAnchor
{
    public const string KeyId = "amkq-2026-07-27";

    public const string PublicKeyPem = """
        -----BEGIN RSA PUBLIC KEY-----
        MIIBigKCAYEA23cQLBGk6b/SAofNSOVT+3CwbgvtZqd/bT2VpcluX0oZZYa1xJEL
        fPxP6A4bsSwod1zuvjBe94A2huvW7B4eO/SXH6jEm60Ab0iCJRVlfY/LjPULDFXS
        77/XN9AqnBf23097Kjumz0Hr8VQwFq+P48ZtqI5ovgG23/XUGzMfQ6JQ6QR8Pm/V
        1B+S0wNFpw6mEWuwTPRElaplyoBrFAzh3ItFFbqAKoSv5reGxjRrTcvV7a3s3mNw
        G2Yy/HzJz7ZCUQ7NQqRT9lX5fp9bkdtVpZoTo8Codlgxqww4FhutfO1WLOzpWUr/
        pyo6ReUS7K+kCYOSIpsgeJks9jNiNPdKjxNlfFLNb8Iyo9nEPtriv1rAYkXHRYSh
        ZA04bZMUN4JeYL/o0ZzLSGfqRM6ppwGhPnq8qbH5CNert9a2p/XYBC5P+Hs/ch4j
        2d2Jhg7YfyBcH8e+4kYBNu8QZySp0MxBtNPocf7iAd/y93qJwRspIrq3LGQbHTK5
        24lJ2Lk7QeI9AgMBAAE=
        -----END RSA PUBLIC KEY-----
        """;
}
