using JTI.DEEP.Reports.Domain.Enums;

namespace JTI.DEEP.Reports.Domain.Entities;

public class Transaction : BaseEntity
{
    public DateTime StartTransactionDate { get; set; }
    public DateTime EndTransactionDate { get; set; }
    public TransactionPloomType PloomType { get; set; }
    public long CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public long ProgramId { get; set; }
    public Program? Program { get; set; }
    public long ProgramOutletId { get; set; }
    public ProgramOutlet? Outlet { get; set; }
}
