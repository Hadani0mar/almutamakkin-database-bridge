namespace Almutamakkin.DatabaseBridge.Protocol;

/// <summary>
/// Public verification key for the offline package publisher.  This is safe to
/// ship; the matching private key is kept outside the repository and outside
/// every bridge/mobile installation.
/// </summary>
public static class QueryPackageTrustAnchor
{
    public const string KeyId = "amkq-2026-07-28";

    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAt0zqy2dv7mpvWqkpOzQm
        HBnz3TU0Xe464ElIvUNdWllixmhfpBg5y6AjD30llPqbCETnaBtrYXNCBb5yI7HU
        yKDC8u3HVxsJtucmYdX3s8wr5Igsr453cZgUBtceeIwHNH7cFok5XbzyZTjKw6dY
        S3pyHDMEnwniCzz2nvUjVNc5abfikOHsTw+T7jUWB+c1uBi+LsZkANHAKmN0LZu3
        1yQsNUjo68wv93bR5hU3q3YilI9mmGr77u4Aw9BSeUGoMME08T4ICaSITVEUqpGd
        o3rU/BnVExkQjy9GSjkbLLLWrpcbLaGNkTzmkoHGTiGvoY07xhQzYFDBj5Ij5tjC
        GYfOa105ixjXrXmExgk79d70PTtUmiX3xliNcDPOqzJwktQWd3t75Gj2F6r1qOXE
        5orygtqeTqFnJNMXUITf0udHJddiwBqQBPZtLyPl+lzGZoFbijgqCDRWdoyI1VwV
        o+UTT41u8M8SlGdO0DY7WzYlVdtm9OhC8S7WLZjiL+YdAgMBAAE=
        -----END PUBLIC KEY-----
        """;
}
