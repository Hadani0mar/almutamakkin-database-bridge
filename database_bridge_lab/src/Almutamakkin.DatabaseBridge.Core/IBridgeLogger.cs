namespace Almutamakkin.DatabaseBridge.Core;

public interface IBridgeLogger
{
    void Info(string message);

    void Warning(string message);

    void Error(string message, Exception? exception = null);
}
