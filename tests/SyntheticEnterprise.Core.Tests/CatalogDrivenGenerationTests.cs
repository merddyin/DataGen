using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class CatalogDrivenGenerationTests
{
    [Fact]
    public void WorldGenerator_Uses_CityReferenceCatalog_For_PopulationAware_OfficePlacement()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "City Reference Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "City Reference Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 120,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 3,
                            Countries = { "United States" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["locality_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("CountryCode", "US"), ("StateCode", "NY"), ("StateOrProvince", "New York"), ("City", "New York"), ("PostalCode", "10001"), ("TimeZone", "America/New_York"), ("Latitude", "40.7128"), ("Longitude", "-74.0060"), ("Population", "8804190"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "US"), ("StateCode", "CA"), ("StateOrProvince", "California"), ("City", "Los Angeles"), ("PostalCode", "90001"), ("TimeZone", "America/Los_Angeles"), ("Latitude", "34.0522"), ("Longitude", "-118.2437"), ("Population", "3898747"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "US"), ("StateCode", "IL"), ("StateOrProvince", "Illinois"), ("City", "Chicago"), ("PostalCode", "60601"), ("TimeZone", "America/Chicago"), ("Latitude", "41.8781"), ("Longitude", "-87.6298"), ("Population", "2746388"), ("Accuracy", "6"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "United States"), ("Code", "US"), ("Continent", "North America"))
                    },
                    ["street_suffixes"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "Boulevard"))
                    }
                }
            });

        Assert.Equal(new[] { "New York", "Los Angeles", "Chicago" }, result.World.Offices.Select(office => office.City).ToArray());
        Assert.All(result.World.Offices, office => Assert.False(string.IsNullOrWhiteSpace(office.PostalCode)));
        Assert.All(result.World.Offices, office => Assert.EndsWith("Boulevard", office.StreetName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldGenerator_Uses_CityPostalReference_When_PopulationCatalog_Is_Unavailable()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "City Postal Reference Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Postal Locality Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 80,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 2,
                            Countries = { "Canada" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["locality_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("CountryCode", "CA"), ("StateOrProvince", "Ontario"), ("StateCode", "ON"), ("City", "Toronto"), ("PostalCode", "M5H"), ("Latitude", "43.6532"), ("Longitude", "-79.3832"), ("Accuracy", "6"), ("TimeZone", "America/Toronto")),
                        NewRow(("CountryCode", "CA"), ("StateOrProvince", "Quebec"), ("StateCode", "QC"), ("City", "Montreal"), ("PostalCode", "H2Y"), ("Latitude", "45.5019"), ("Longitude", "-73.5674"), ("Accuracy", "6"), ("TimeZone", "America/Toronto"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Canada"), ("Code", "CA"), ("Continent", "North America"))
                    },
                    ["street_suffixes"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "Way"))
                    }
                }
            });

        Assert.Equal(new[] { "Montreal", "Toronto" }, result.World.Offices.Select(office => office.City).OrderBy(city => city, StringComparer.Ordinal).ToArray());
        Assert.All(result.World.Offices, office => Assert.False(string.IsNullOrWhiteSpace(office.PostalCode)));
        Assert.Contains(result.World.Offices, office => office.City == "Toronto" && office.StateOrProvince == "Ontario" && office.TimeZone == "America/Toronto");
        Assert.Contains(result.World.Offices, office => office.City == "Montreal" && office.StateOrProvince == "Quebec" && office.TimeZone == "America/Toronto");
        Assert.All(result.World.Offices, office => Assert.EndsWith("Way", office.StreetName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldGenerator_Filters_Administrative_Locality_Names_From_Merged_Runtime_Catalog()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Administrative Locality Filter Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Shanghai Trading",
                            Industry = "Technology",
                            EmployeeCount = 60,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 1,
                            Countries = { "China" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["locality_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("CountryCode", "CN"), ("City", "Banan Qu"), ("Population", "29914000"), ("Accuracy", "0")),
                        NewRow(("CountryCode", "CN"), ("City", "Shanghai"), ("Population", "22315474"), ("Latitude", "31.2222"), ("Longitude", "121.4581"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "China"), ("Code", "CN"), ("Continent", "Asia"))
                    }
                }
            });

        var office = Assert.Single(result.World.Offices);
        Assert.Equal("Shanghai", office.City);
    }

    [Fact]
    public void WorldGenerator_Uses_DepartmentAware_TitleCatalog_For_EmployeeTitles()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Title Catalog Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Title Catalog Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 180,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 1,
                            Countries = { "United States" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["departments"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Engineering")),
                        NewRow(("Name", "Finance"))
                    },
                    ["titles"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Department", "Engineering"), ("Title", "Platform Engineer"), ("Level", "Experienced")),
                        NewRow(("Department", "Engineering"), ("Title", "Junior QA Analyst"), ("Level", "Entry")),
                        NewRow(("Department", "Finance"), ("Title", "Payroll Analyst"), ("Level", "Experienced")),
                        NewRow(("Department", "Finance"), ("Title", "Accounts Payable Clerk"), ("Level", "Entry"))
                    }
                }
            });

        var departments = result.World.Departments.ToDictionary(department => department.Id, department => department.Name, StringComparer.OrdinalIgnoreCase);
        var engineeringTitles = result.World.People
            .Where(person => departments.TryGetValue(person.DepartmentId, out var departmentName) && departmentName == "Engineering")
            .Select(person => person.Title)
            .ToList();
        var financeTitles = result.World.People
            .Where(person => departments.TryGetValue(person.DepartmentId, out var departmentName) && departmentName == "Finance")
            .Select(person => person.Title)
            .ToList();

        Assert.Contains(engineeringTitles, title => title == "Platform Engineer" || title == "Junior QA Analyst");
        Assert.Contains(financeTitles, title => title == "Payroll Analyst" || title == "Accounts Payable Clerk");
        Assert.Contains(result.World.People, person => person.Title == "Director" || person.Title == "Manager" || person.Title == "Vice President");
    }

    [Fact]
    public void WorldGenerator_Uses_GivenName_And_Surname_Reference_Catalogs_When_CountrySpecific_NameCatalogs_Are_Missing()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Fallback Name Catalog Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Fallback Names Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 24,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 1,
                            Countries = { "Canada" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["given_names_male"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Liam")),
                        NewRow(("Name", "Noah"))
                    },
                    ["given_names_female"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Emma")),
                        NewRow(("Name", "Ava"))
                    },
                    ["surnames_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "Martin")),
                        NewRow(("Value", "Tremblay"))
                    }
                }
            });

        Assert.All(result.World.People, person =>
        {
            Assert.Contains(person.FirstName, new[] { "Liam", "Noah", "Emma", "Ava" });
            Assert.Contains(person.LastName, new[] { "Martin", "Tremblay" });
            Assert.Equal("Canada", person.Country);
        });
    }

    [Fact]
    public void WorldGenerator_Uses_Company_And_Country_Catalogs_For_Internal_Enterprise_Identity()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Company Identity Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Northern Tooling",
                            Industry = "Manufacturing",
                            EmployeeCount = 40,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 2,
                            Countries = { "Canada" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["company_suffixes"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "Ltd")),
                        NewRow(("Value", "Group"))
                    },
                    ["domain_suffixes"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "com")),
                        NewRow(("Value", "ca"))
                    },
                    ["taglines"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "optimize")),
                        NewRow(("Value", "transform"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Canada"), ("Code", "CA"), ("Phone", "1"), ("Capital", "Ottawa"), ("Continent", "North America"), ("PostalCode", "1"))
                    },
                    ["locality_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("CountryCode", "CA"), ("StateCode", "ON"), ("StateOrProvince", "Ontario"), ("City", "Toronto"), ("PostalCode", "M5H 2N2"), ("TimeZone", "America/Toronto"), ("Latitude", "43.6532"), ("Longitude", "-79.3832"), ("Population", "2731571"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "CA"), ("StateCode", "QC"), ("StateOrProvince", "Quebec"), ("City", "Montreal"), ("PostalCode", "H2Y 1C6"), ("TimeZone", "America/Toronto"), ("Latitude", "45.5019"), ("Longitude", "-73.5674"), ("Population", "1762949"), ("Accuracy", "6"))
                    }
                }
            });

        var company = Assert.Single(result.World.Companies);
        Assert.Equal("Canada", company.PrimaryCountry);
        Assert.EndsWith(".ca", company.PrimaryDomain, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"https://www.{company.PrimaryDomain}", company.Website);
        Assert.False(string.IsNullOrWhiteSpace(company.Tagline));
        Assert.False(string.IsNullOrWhiteSpace(company.LegalName));
        Assert.False(string.IsNullOrWhiteSpace(company.HeadquartersOfficeId));
        Assert.False(string.IsNullOrWhiteSpace(company.PrimaryPhoneNumber));

        var headquarters = Assert.Single(result.World.Offices, office => office.IsHeadquarters);
        Assert.Equal(company.HeadquartersOfficeId, headquarters.Id);
        Assert.Equal(company.PrimaryPhoneNumber, headquarters.BusinessPhone);
        Assert.StartsWith("+1 ", headquarters.BusinessPhone, StringComparison.Ordinal);
        Assert.All(
            result.World.People.Where(person => string.Equals(person.PersonType, "Internal", StringComparison.OrdinalIgnoreCase)),
            person => Assert.EndsWith($"@{company.PrimaryDomain}", person.UserPrincipalName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldGenerator_Uses_CountryIdentityRules_For_Domain_And_Phone_Formatting()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Country Rule Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Lagos Industrial",
                            Industry = "Manufacturing",
                            EmployeeCount = 30,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 1,
                            Countries = { "Nigeria" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["country_identity_rules"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Country", "Nigeria"), ("CountryCode", "NG"), ("DialCode", "234"), ("PostalCodeSupported", "false"), ("PrimaryDomainSuffix", "ng"), ("AlternateDomainSuffix", "com"), ("PhonePattern", "+234 XX XXXX XXXX"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Nigeria"), ("Code", "NG"), ("Phone", "234"), ("Capital", "Abuja"), ("Continent", "Africa"), ("PostalCodeSupported", "false"))
                    },
                    ["locality_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("CountryCode", "NG"), ("StateOrProvince", "Lagos"), ("StateCode", "LA"), ("City", "Lagos"), ("PostalCode", "100001"), ("Latitude", "6.5244"), ("Longitude", "3.3792"), ("Accuracy", "6"), ("TimeZone", "Africa/Lagos"))
                    }
                }
            });

        var company = Assert.Single(result.World.Companies);
        var office = Assert.Single(result.World.Offices);

        Assert.EndsWith(".ng", company.PrimaryDomain, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("+234 ", office.BusinessPhone, StringComparison.Ordinal);
        Assert.EndsWith($"@{company.PrimaryDomain}", result.World.People.First().UserPrincipalName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorldGenerator_Uses_Organization_Templates_For_Manufacturing_Composition()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Scenario = new ScenarioDefinition
                {
                    Name = "Organization Template Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Forge Systems",
                            Industry = "Manufacturing",
                            EmployeeCount = 400,
                            BusinessUnitCount = 3,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 1,
                            Countries = { "United States" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["organization_templates"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Layer", "BusinessUnit"), ("IndustryTags", "All"), ("Name", "Corporate Services"), ("ParentHints", ""), ("MinimumEmployees", "50")),
                        NewRow(("Layer", "BusinessUnit"), ("IndustryTags", "Manufacturing"), ("Name", "Supply Chain and Manufacturing"), ("ParentHints", ""), ("MinimumEmployees", "100")),
                        NewRow(("Layer", "BusinessUnit"), ("IndustryTags", "Manufacturing"), ("Name", "Commercial Operations"), ("ParentHints", ""), ("MinimumEmployees", "100")),
                        NewRow(("Layer", "Department"), ("IndustryTags", "Manufacturing"), ("Name", "Operations"), ("ParentHints", "Supply Chain and Manufacturing"), ("MinimumEmployees", "100")),
                        NewRow(("Layer", "Department"), ("IndustryTags", "Manufacturing"), ("Name", "Quality"), ("ParentHints", "Supply Chain and Manufacturing"), ("MinimumEmployees", "100")),
                        NewRow(("Layer", "Department"), ("IndustryTags", "Manufacturing"), ("Name", "Sales"), ("ParentHints", "Commercial Operations"), ("MinimumEmployees", "100")),
                        NewRow(("Layer", "Team"), ("IndustryTags", "Manufacturing"), ("Name", "Production Scheduling"), ("ParentHints", "Operations"), ("MinimumEmployees", "100")),
                        NewRow(("Layer", "Team"), ("IndustryTags", "Manufacturing"), ("Name", "Supplier Quality"), ("ParentHints", "Quality"), ("MinimumEmployees", "100")),
                        NewRow(("Layer", "Team"), ("IndustryTags", "Manufacturing"), ("Name", "Commercial Planning"), ("ParentHints", "Sales"), ("MinimumEmployees", "100"))
                    }
                }
            });

        Assert.Contains(result.World.BusinessUnits, unit => unit.Name == "Supply Chain and Manufacturing");
        Assert.Contains(result.World.BusinessUnits, unit => unit.Name == "Commercial Operations");

        var departmentsById = result.World.Departments.ToDictionary(department => department.Id, department => department, StringComparer.OrdinalIgnoreCase);
        var operationsDepartment = Assert.Single(result.World.Departments, department => department.Name == "Operations");
        var qualityDepartment = Assert.Single(result.World.Departments, department => department.Name == "Quality");
        var salesDepartment = Assert.Single(result.World.Departments, department => department.Name == "Sales");

        Assert.Equal("Supply Chain and Manufacturing", result.World.BusinessUnits.Single(unit => unit.Id == operationsDepartment.BusinessUnitId).Name);
        Assert.Equal("Supply Chain and Manufacturing", result.World.BusinessUnits.Single(unit => unit.Id == qualityDepartment.BusinessUnitId).Name);
        Assert.Equal("Commercial Operations", result.World.BusinessUnits.Single(unit => unit.Id == salesDepartment.BusinessUnitId).Name);

        Assert.Contains(result.World.Teams, team => team.Name == "Production Scheduling" && departmentsById[team.DepartmentId].Name == "Operations");
        Assert.Contains(result.World.Teams, team => team.Name == "Supplier Quality" && departmentsById[team.DepartmentId].Name == "Quality");
        Assert.Contains(result.World.Teams, team => team.Name == "Commercial Planning" && departmentsById[team.DepartmentId].Name == "Sales");
    }

    private static Dictionary<string, string?> NewRow(params (string Key, string? Value)[] entries)
    {
        var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            row[key] = value;
        }

        return row;
    }
}
