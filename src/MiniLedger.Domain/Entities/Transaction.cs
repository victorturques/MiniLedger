using MiniLedger.Domain.Enums;

namespace MiniLedger.Domain.Entities;

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public Guid TransferId { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string? CounterpartyAccountNumber { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
