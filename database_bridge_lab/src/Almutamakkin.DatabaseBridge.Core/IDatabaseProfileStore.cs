using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.Core;

public interface IDatabaseProfileStore
{
    DatabaseProfile? GetByName(string profileName);

    DatabaseProfile? GetById(Guid id);

    IReadOnlyList<DatabaseProfile> GetAll();

    void Save(DatabaseProfile profile);

    bool Delete(Guid id);

    void Reload();
}

