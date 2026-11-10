using MiniLedger.Application.DTOs;

namespace MiniLedger.Application.Interfaces;

public interface ITransferService
{
    Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default);
}
