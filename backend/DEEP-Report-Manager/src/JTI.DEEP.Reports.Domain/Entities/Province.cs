namespace JTI.DEEP.Reports.Domain.Entities;

public class Province : BaseEntity
{
    public required string PSGCCode { get; set; }
    public required string Name { get; set; }
    public string? Zone { get; set; }
}
