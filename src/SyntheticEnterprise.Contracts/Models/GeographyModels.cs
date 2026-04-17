namespace SyntheticEnterprise.Contracts.Models;

public record Office
{
    public string Id { get; init; } = "";
    public string CompanyId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Region { get; init; } = "";
    public string Country { get; init; } = "";
    public string StateOrProvince { get; init; } = "";
    public string City { get; init; } = "";
    public string PostalCode { get; init; } = "";
    public string TimeZone { get; init; } = "";
    public string AddressMode { get; init; } = "Hybrid";
    public string BuildingNumber { get; init; } = "";
    public string StreetName { get; init; } = "";
    public string FloorOrSuite { get; init; } = "";
    public string BusinessPhone { get; init; } = "";
    public bool IsHeadquarters { get; init; }
    public string? Latitude { get; init; }
    public string? Longitude { get; init; }
    public bool Geocoded { get; init; }
}
