using System.Net.Http.Headers;
using System.Text.Json;

namespace Almutamakkin.DatabaseBridge.App;

/// <summary>
/// Reads the latest public release metadata. It never downloads or installs an
/// update; the desktop UI remains in control of that action.
/// </summary>
public sealed class GitHubReleaseUpdateChecker
{
    private const string ReleasesApi = "https://api.github.com/repos/Hadani0mar/almutamakkin-database-bridge/releases/latest";

    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateChecker(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GitHubReleaseUpdate?> GetLatestAsync(Version installedVersion, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AlmutamakkinDatabaseBridge", installedVersion.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() : null;
        var pageUrl = root.TryGetProperty("html_url", out var urlValue) ? urlValue.GetString() : null;

        if (!TryParseVersion(tag, out var latestVersion) || latestVersion <= installedVersion || string.IsNullOrWhiteSpace(pageUrl))
        {
            return null;
        }

        return new GitHubReleaseUpdate(latestVersion, new Uri(pageUrl));
    }

    internal static bool TryParseVersion(string? value, out Version version)
    {
        var normalized = value?.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out version!);
    }
}

public sealed record GitHubReleaseUpdate(Version Version, Uri ReleasePageUri);
