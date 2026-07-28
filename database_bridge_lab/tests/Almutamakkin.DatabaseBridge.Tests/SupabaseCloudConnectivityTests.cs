using System.Net.Sockets;
using Almutamakkin.DatabaseBridge.Infrastructure;

namespace Almutamakkin.DatabaseBridge.Tests;

public sealed class SupabaseCloudConnectivityTests
{
    [Fact]
    public void Default_functions_base_url_is_production_project()
    {
        Assert.Equal(
            "https://mapfattjpsuizvlklddl.supabase.co/functions/v1",
            SupabaseBridgeTransport.DefaultSupabaseFunctionsBaseUrl);
    }

    [Fact]
    public void ExtractHost_uses_default_when_url_missing()
    {
        Assert.Equal(
            SupabaseCloudConnectivity.DefaultHost,
            SupabaseCloudConnectivity.ExtractHost(null));
    }

    [Fact]
    public void ExtractHost_parses_functions_base()
    {
        Assert.Equal(
            "mapfattjpsuizvlklddl.supabase.co",
            SupabaseCloudConnectivity.ExtractHost(
                "https://mapfattjpsuizvlklddl.supabase.co/functions/v1"));
    }

    [Fact]
    public void ResolveFunctionsBaseUrl_trims_trailing_slash()
    {
        Assert.Equal(
            "https://mapfattjpsuizvlklddl.supabase.co/functions/v1",
            SupabaseCloudConnectivity.ResolveFunctionsBaseUrl(
                "https://mapfattjpsuizvlklddl.supabase.co/functions/v1/"));
    }

    [Fact]
    public void FormatUserMessage_maps_no_such_host_to_arabic_dns_guidance()
    {
        var ex = new HttpRequestException(
            "No such host is known. (mapfattjpsuizvlklddl.supabase.co:443)",
            new SocketException((int)SocketError.HostNotFound));

        var message = SupabaseCloudConnectivity.FormatUserMessage(ex);

        Assert.Contains("DNS", message, StringComparison.Ordinal);
        Assert.Contains("mapfattjpsuizvlklddl.supabase.co", message, StringComparison.Ordinal);
        Assert.DoesNotContain("SocketException", message, StringComparison.Ordinal);
        Assert.Contains("No such host is known", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsDnsFailure_detects_socket_host_not_found()
    {
        var ex = new SocketException((int)SocketError.HostNotFound);
        Assert.True(SupabaseCloudConnectivity.IsDnsFailure(ex));
    }
}
