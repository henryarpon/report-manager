using JTI.DEEP.Reports.Domain.Enums;

namespace JTI.DEEP.Reports.Domain.Entities;

public class Customer : BaseEntity
{
    public string? MobileNumber { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FullName { get; set; }
    public string? EmailAddress { get; set; }
    public PloomType PloomType { get; set; }
    public CustomerType CustomerType { get; set; }
    public string? ProvincePSGCCode { get; set; }
    public Province? Province { get; set; }
    public List<Transaction>? Transactions { get; set; }
}
