using JTI.DEEP.Reports.Domain.Enums;

namespace JTI.DEEP.Reports.Domain.Entities;

public class Program : BaseEntity
{
    public string? ProgramName { get; set; }
    public ProgramStatus Status { get; set; }
    public ProgramType ProgramType { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? FocusZone { get; set; }
    public ICollection<ProgramOutlet>? Outlets { get; set; }

    /// <summary>Not a navigation in JTI-DEEP-API; added here (FK lives on Transaction) so reports can use EXISTS.</summary>
    public ICollection<Transaction>? Transactions { get; set; }
}
