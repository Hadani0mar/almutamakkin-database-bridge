using System.Net.Http.Headers;
using System.Text.Json;

namespace Almutamakkin.DatabaseBridge.App;

/// <summary>
/// Reads the latest public release metadata and downloads only the installer
/// asset selected by the release workflow. The desktop UI remains in control
/// of launching the downloaded installer.
/// </summary>
public sealed class GitHubReleaseUpdateChecker
{
    private const string ReleasesApi = "https://api.github.com/repos/Hadani0mar/almutamakkin-database-bridge/releases/latest";
    public static readonly Uri ReleasesPageUri = new("https://github.com/Hadani0mar/almutamakkin-database-bridge/releases");

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
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() : null;
        var pageUrl = root.TryGetProperty("html_url", out var urlValue) ? urlValue.GetString() : null;

        if (!TryParseVersion(tag, out var latestVersion) || latestVersion <= installedVersion || string.IsNullOrWhiteSpace(pageUrl))
        {
            return null;
        }

        var installerUrl = FindInstallerUrl(root);
        return new GitHubReleaseUpdate(
            latestVersion,
            new Uri(pageUrl),
            installerUrl is null ? null : new Uri(installerUrl));
    }

    public async Task DownloadInstallerAsync(
        GitHubReleaseUpdate update,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (update.InstallerUri is null)
        {
            throw new InvalidOperationException("ملف تثبيت التحديث غير متاح في الإصدار المنشور.");
        }

        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("مسار تنزيل التحديث غير صالح.");
        Directory.CreateDirectory(directory);

        var temporaryPath = destinationPath + ".downloading";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.InstallerUri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AlmutamakkinDatabaseBridge", update.Version.ToString()));
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private static string? FindInstallerUrl(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() : null;
            if (!string.IsNullOrWhiteSpace(name) &&
                name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }

        return null;
    }

    internal static bool TryParseVersion(string? value, out Version version)
    {
        var normalized = value?.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out version!);
    }
}

public sealed record GitHubReleaseUpdate(Version Version, Uri ReleasePageUri, Uri? InstallerUri);
