namespace JTI.DEEP.Reports.Domain.Entities;

public class ProgramOutlet : BaseEntity
{
    public string? OutletName { get; set; }
    public string? OutletCode { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string? Zone { get; set; }
    public long ProgramId { get; set; }
    public Program? Program { get; set; }
}
