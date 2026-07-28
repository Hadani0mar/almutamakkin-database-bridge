using Almutamakkin.DatabaseBridge.Core;

namespace Almutamakkin.DatabaseBridge.Tests;

internal sealed class TestBridgeLogger : IBridgeLogger
{
    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
}
