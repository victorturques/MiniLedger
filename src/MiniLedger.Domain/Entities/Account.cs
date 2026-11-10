namespace MiniLedger.Domain.Entities;

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string HolderName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Token de concorrência otimista. Incrementado automaticamente a cada alteração
    /// da conta (ver <c>AppDbContext.SaveChanges</c>).
    /// </summary>
    public long Version { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
