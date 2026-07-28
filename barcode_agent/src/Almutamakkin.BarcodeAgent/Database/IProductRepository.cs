using Almutamakkin.BarcodeAgent.Models;

namespace Almutamakkin.BarcodeAgent.Database;

public interface IProductRepository
{
    Task<IReadOnlyList<ProductDto>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductDto>> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken);
    Task<ProductDto?> GetByBarIdAsync(long barId, CancellationToken cancellationToken);
    Task<string?> GetBusinessNameAsync(CancellationToken cancellationToken);
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}
