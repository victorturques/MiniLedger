using MiniLedger.Application.DTOs;
using MiniLedger.Application.Interfaces;

namespace MiniLedger.Tests.Infrastructure;

public sealed class FakeTransferService : ITransferService
{
    public Func<TransferRequest, Task<TransferResponse>> Transfer { get; set; } =
        _ => throw new NotImplementedException();

    public Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default)
        => Transfer(request);
}
