using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public interface IConnectionStringBuilder
{
    string Build(DatabaseProfile profile, string? plainPassword);
}
