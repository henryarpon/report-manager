namespace JTI.DEEP.Reports.Domain.Entities;

/// <summary>
/// Trimmed read-only copy of JTI-DEEP-API's BaseEntity. Only the columns reports need are mapped.
/// </summary>
public abstract class BaseEntity
{
    public long Id { get; set; }
    public DateTime DateCreated { get; set; }
    public bool IsDeleted { get; set; }
}
