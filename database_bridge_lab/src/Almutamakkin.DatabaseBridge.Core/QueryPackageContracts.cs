using System.Text;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

/// <summary>
/// A versioned, server-owned SQL contract. It is fetched by the Windows bridge
/// after device authentication; neither the Flutter app nor bridge_commands
/// persist its SQL text.
/// </summary>
public sealed record QueryPackageDefinition
{
    public required string QueryId { get; init; }

    public required int Version { get; init; }

    public required string System { get; init; }

    public required string DatabaseProfile { get; init; }

    public required string Sql { get; init; }

    public List<QueryPackageParameterDefinition> Parameters { get; init; } = new();

    public int TimeoutSeconds { get; init; } = 30;

    public int MaxRows { get; init; } = 1000;
}
public sealed record QueryPackageParameterDefinition
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public bool Required { get; init; } = true;
}

public sealed record SignedQueryPackage
{
    public required QueryPackageDefinition Definition { get; init; }

    public required string KeyId { get; init; }

    public required string SignatureBase64 { get; init; }
}

public interface IQueryPackageCatalogClient
{
    Task<SignedQueryPackage?> GetAsync(string queryId, CancellationToken cancellationToken);
}

public interface IQueryPackageSignatureVerifier
{
    bool Verify(SignedQueryPackage package, out string? errorMessage);
}

/// <summary>
/// Canonical bytes signed by the offline publisher and verified by the bridge.
/// Keep this format stable: changing it intentionally invalidates old packages.
/// </summary>
public static class QueryPackageSignaturePayload
{
    public static byte[] Build(QueryPackageDefinition package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var parameterPart = string.Join(
            "\n",
            package.Parameters
                .OrderBy(parameter => parameter.Name, StringComparer.Ordinal)
                .Select(parameter =>
                    $"{parameter.Name}|{parameter.Type}|{(parameter.Required ? "1" : "0")}"));

        var canonical = string.Join(
            "\n",
            "AMKQ1",
            package.QueryId,
            package.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            package.System,
            package.DatabaseProfile,
            package.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            package.MaxRows.ToString(System.Globalization.CultureInfo.InvariantCulture),
            parameterPart,
            package.Sql);

        return Encoding.UTF8.GetBytes(canonical);
    }
}
