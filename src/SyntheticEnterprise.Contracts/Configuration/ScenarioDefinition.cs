namespace SyntheticEnterprise.Contracts.Configuration;

public record ScenarioDefinition
{
    public string Name { get; init; } = "Default";
    public List<ScenarioCompanyDefinition> Companies { get; init; } = new();
    public List<AnomalyProfile> Anomalies { get; init; } = new();
}

public record ScenarioCompanyDefinition
{
    public string Name { get; init; } = "Contoso Dynamics";
    public string Industry { get; init; } = "Technology";
    public int EmployeeCount { get; init; } = 250;
    public int BusinessUnitCount { get; init; } = 3;
    public int DepartmentCountPerBusinessUnit { get; init; } = 3;
    public int TeamCountPerDepartment { get; init; } = 2;
    public int OfficeCount { get; init; } = 3;
    public string AddressMode { get; init; } = "Hybrid";
    public bool IncludeGeocodes { get; init; } = false;
    public int SharedMailboxCount { get; init; } = 5;
    public int ServiceAccountCount { get; init; } = 8;
    public bool IncludePrivilegedAccounts { get; init; } = true;
    public double WorkstationCoverageRatio { get; init; } = 0.92;
    public int ServerCount { get; init; } = 24;
    public int NetworkAssetCountPerOffice { get; init; } = 6;
    public int TelephonyAssetCountPerOffice { get; init; } = 20;
    public int DatabaseCount { get; init; } = 18;
    public int FileShareCount { get; init; } = 12;
    public int CollaborationSiteCount { get; init; } = 20;
    public List<string> Countries { get; init; } = new();
}

public record AnomalyProfile
{
    public string Name { get; init; } = "None";
    public string Category { get; init; } = "General";
    public double Intensity { get; init; } = 1.0;
}
