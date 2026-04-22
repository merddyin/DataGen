using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;

namespace SyntheticEnterprise.Core.Tests;

public sealed class CatalogDrivenGenerationTests
{
    [Fact]
    public void WorldGenerator_Contextualizes_Duplicate_Department_And_Team_Names_Instead_Of_Numbering_Them()
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
                    Name = "Contextual Org Names Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Contextual Org Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 80,
                            BusinessUnitCount = 2,
                            DepartmentCountPerBusinessUnit = 1,
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
                    ["business_units"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Commercial")),
                        NewRow(("Name", "Technology"))
                    },
                    ["departments"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Planning"))
                    },
                    ["teams"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Operations"))
                    }
                }
            });

        Assert.Equal(2, result.World.Departments.Count);
        Assert.Equal(2, result.World.Teams.Count);
        Assert.DoesNotContain(result.World.Departments, department => Regex.IsMatch(department.Name, @"\s\d+$"));
        Assert.DoesNotContain(result.World.Teams, team => Regex.IsMatch(team.Name, @"\s\d+$"));
        Assert.Contains(result.World.Departments, department => department.Name.Contains("Commercial", StringComparison.OrdinalIgnoreCase) || department.Name.Contains("Technology", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.Teams, team => team.Name.Contains("Planning", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldGenerator_Uses_BusinessUnit_And_Department_Appropriate_Org_Structures_For_Manufacturing()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 17,
                Scenario = new ScenarioDefinition
                {
                    Name = "Manufacturing Org Realism Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Manufacturing Org Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 240,
                            BusinessUnitCount = 3,
                            DepartmentCountPerBusinessUnit = 3,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 2,
                            Countries = { "United States" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["business_units"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Commercial Operations")),
                        NewRow(("Name", "Product and Quality Engineering")),
                        NewRow(("Name", "Supply Chain and Manufacturing"))
                    }
                }
            });

        Assert.DoesNotContain(result.World.Departments, department => department.Name.Contains(" - ", StringComparison.Ordinal));
        Assert.Contains(result.World.Departments, department => department.Name == "Sales");
        Assert.Contains(result.World.Departments, department => department.Name == "Marketing");
        Assert.Contains(result.World.Departments, department => department.Name == "Customer Support");
        Assert.Contains(result.World.Departments, department => department.Name == "Product Engineering");
        Assert.Contains(result.World.Departments, department => department.Name == "Manufacturing Engineering");
        Assert.Contains(result.World.Departments, department => department.Name == "Quality Assurance");
        Assert.Contains(result.World.Departments, department => department.Name == "Production Operations");
        Assert.Contains(result.World.Departments, department => department.Name == "Procurement");
        Assert.Contains(result.World.Departments, department => department.Name == "Logistics and Planning");

        Assert.DoesNotContain(result.World.Teams, team => team.Name.Contains(" - Sales", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.World.Teams, team => team.Name.Contains(" - Marketing", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.World.Teams, team => Regex.IsMatch(team.Name, @"\s\d+$"));
        Assert.True(result.World.Teams.Count(team => team.Name.Equals("Commercial Planning", StringComparison.OrdinalIgnoreCase)) <= 1);
        Assert.Contains(result.World.Teams, team => team.Name == "Regional Sales");
        Assert.Contains(result.World.Teams, team => team.Name == "Account Management");
        Assert.Contains(result.World.Teams, team => team.Name == "Product Marketing");
        Assert.Contains(result.World.Teams, team => team.Name == "Demand Generation");
        Assert.Contains(result.World.Teams, team => team.Name == "Technical Support");
        Assert.Contains(result.World.Teams, team => team.Name == "Customer Care");
        Assert.Contains(result.World.Teams, team => team.Name == "Industrial Automation");
        Assert.Contains(result.World.Teams, team => team.Name == "Plant Systems");
        Assert.Contains(result.World.Teams, team => team.Name == "Strategic Sourcing");
        Assert.Contains(result.World.Teams, team => team.Name == "Supplier Management");
        Assert.Contains(result.World.Teams, team => team.Name == "Supply Planning");
        Assert.Contains(result.World.Teams, team => team.Name == "Demand Planning");
    }

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
    public void WorldGenerator_Prefers_PrimaryCountry_For_Headquarters_And_Distributes_People_Across_Offices()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 23,
                Scenario = new ScenarioDefinition
                {
                    Name = "Office Distribution Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Office Distribution Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 90,
                            BusinessUnitCount = 3,
                            DepartmentCountPerBusinessUnit = 3,
                            TeamCountPerDepartment = 2,
                            OfficeCount = 6,
                            Countries = { "United States", "Canada", "Mexico" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["business_units"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Commercial Operations")),
                        NewRow(("Name", "Product and Quality Engineering")),
                        NewRow(("Name", "Supply Chain and Manufacturing"))
                    },
                    ["locality_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("CountryCode", "US"), ("StateCode", "NY"), ("StateOrProvince", "New York"), ("City", "New York"), ("PostalCode", "10001"), ("TimeZone", "America/New_York"), ("Latitude", "40.7128"), ("Longitude", "-74.0060"), ("Population", "8804190"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "US"), ("StateCode", "CA"), ("StateOrProvince", "California"), ("City", "Los Angeles"), ("PostalCode", "90001"), ("TimeZone", "America/Los_Angeles"), ("Latitude", "34.0522"), ("Longitude", "-118.2437"), ("Population", "3898747"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "CA"), ("StateCode", "ON"), ("StateOrProvince", "Ontario"), ("City", "Toronto"), ("PostalCode", "M5H"), ("TimeZone", "America/Toronto"), ("Latitude", "43.6532"), ("Longitude", "-79.3832"), ("Population", "2731571"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "CA"), ("StateCode", "AB"), ("StateOrProvince", "Alberta"), ("City", "Calgary"), ("PostalCode", "T2P"), ("TimeZone", "America/Edmonton"), ("Latitude", "51.0447"), ("Longitude", "-114.0719"), ("Population", "1306784"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "MX"), ("StateCode", "BC"), ("StateOrProvince", "Baja California"), ("City", "Tijuana"), ("PostalCode", "22000"), ("TimeZone", "America/Tijuana"), ("Latitude", "32.5149"), ("Longitude", "-117.0382"), ("Population", "1810645"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "MX"), ("StateCode", "CMX"), ("StateOrProvince", "Ciudad de Mexico"), ("City", "Mexico City"), ("PostalCode", "06000"), ("TimeZone", "America/Mexico_City"), ("Latitude", "19.4326"), ("Longitude", "-99.1332"), ("Population", "9209944"), ("Accuracy", "6"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "United States"), ("Code", "US"), ("Continent", "North America")),
                        NewRow(("Name", "Canada"), ("Code", "CA"), ("Continent", "North America")),
                        NewRow(("Name", "Mexico"), ("Code", "MX"), ("Continent", "North America"))
                    }
                }
            });

        var headquarters = Assert.Single(result.World.Offices, office => office.IsHeadquarters);
        Assert.Equal("United States", headquarters.Country);

        var peopleByOffice = result.World.People
            .GroupBy(person => person.OfficeId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Count())
            .OrderByDescending(count => count)
            .ToArray();

        Assert.True(peopleByOffice.Length >= 3);
        Assert.True(peopleByOffice[0] > peopleByOffice[^1]);
        Assert.All(
            result.World.People.Where(person => !string.IsNullOrWhiteSpace(person.OfficeId)),
            person =>
            {
                var office = result.World.Offices.First(candidate => candidate.Id == person.OfficeId);
                Assert.Equal(office.Country, person.Country);
            });
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
        Assert.All(
            result.World.Offices,
            office => Assert.Contains(
                office.StreetName.Split(' ').Last(),
                new[] { "Avenue", "Boulevard", "Court", "Drive", "Road", "Street", "Way" }));
    }

    [Fact]
    public void WorldGenerator_Repairs_NorthAmerica_Localities_With_Missing_State_And_PostalCode()
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
                    Name = "North America Locality Repair Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Locality Repair Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 120,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 6,
                            Countries = { "Canada", "Mexico" }
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
                        NewRow(("CountryCode", "CA"), ("City", "Québec"), ("PostalCode", ""), ("StateOrProvince", ""), ("Latitude", "52.00017"), ("Longitude", "-71.99907"), ("Accuracy", "0"), ("TimeZone", "America/Halifax"), ("Population", "7730612")),
                        NewRow(("CountryCode", "CA"), ("City", "Montréal"), ("PostalCode", ""), ("StateOrProvince", ""), ("Latitude", "45.50884"), ("Longitude", "-73.58781"), ("Accuracy", "0"), ("TimeZone", "America/Halifax"), ("Population", "1600000")),
                        NewRow(("CountryCode", "MX"), ("City", "Mexico City"), ("PostalCode", ""), ("StateOrProvince", ""), ("Latitude", "19.42847"), ("Longitude", "-99.12766"), ("Accuracy", "0"), ("TimeZone", "America/Mexico_City"), ("Population", "12294193")),
                        NewRow(("CountryCode", "MX"), ("City", "Ecatepec"), ("PostalCode", ""), ("StateOrProvince", ""), ("Latitude", "19.60492"), ("Longitude", "-99.06064"), ("Accuracy", "0"), ("TimeZone", "America/Mexico_City"), ("Population", "1806226")),
                        NewRow(("CountryCode", "MX"), ("City", "Tijuana"), ("PostalCode", "22703"), ("StateOrProvince", "Baja California"), ("Latitude", "32.2528"), ("Longitude", "-116.8907"), ("Accuracy", "4"), ("TimeZone", "America/Mexico_City"), ("Population", "1376457")),
                        NewRow(("CountryCode", "MX"), ("City", "Naucalpan de Juárez"), ("PostalCode", "41666"), ("StateOrProvince", "Guerrero"), ("Latitude", "17.0764"), ("Longitude", "-98.4192"), ("Accuracy", "4"), ("TimeZone", "America/Mexico_City"), ("Population", "846185"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Canada"), ("Code", "CA"), ("Continent", "North America")),
                        NewRow(("Name", "Mexico"), ("Code", "MX"), ("Continent", "North America"))
                    }
                }
            });

        Assert.All(result.World.Offices, office => Assert.False(string.IsNullOrWhiteSpace(office.PostalCode)));
        Assert.Contains(result.World.Offices, office => office.City == "Québec" && office.StateOrProvince == "Quebec" && office.PostalCode == "G1A");
        Assert.Contains(result.World.Offices, office => office.City == "Montréal" && office.StateOrProvince == "Quebec" && office.PostalCode == "H2Y");
        Assert.Contains(result.World.Offices, office => office.City == "Mexico City" && office.StateOrProvince == "Ciudad de Mexico" && office.PostalCode == "06000");
        Assert.Contains(result.World.Offices, office => office.City == "Ecatepec" && office.StateOrProvince == "Estado de Mexico" && office.PostalCode == "55000");
        Assert.Contains(result.World.Offices, office => office.City == "Tijuana" && office.TimeZone == "America/Tijuana");
        Assert.Contains(result.World.Offices, office => office.City == "Naucalpan de Juárez" && office.StateOrProvince == "Estado de Mexico" && office.PostalCode == "53370");
    }

    [Fact]
    public void WorldGenerator_Prefers_Distinctive_Localities_Over_Ambiguous_Duplicate_City_Names()
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
                    Name = "Ambiguous Locality Ranking Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Distinctive Locality Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 120,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
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
                        NewRow(("CountryCode", "US"), ("StateOrProvince", "Kansas"), ("City", "Long Island"), ("PostalCode", "67647"), ("TimeZone", "America/Chicago"), ("Latitude", "39.9517"), ("Longitude", "-99.5391"), ("Population", "7838822"), ("Accuracy", "4")),
                        NewRow(("CountryCode", "US"), ("StateOrProvince", "Maine"), ("City", "Long Island"), ("PostalCode", "04050"), ("TimeZone", "America/New_York"), ("Latitude", "43.6920"), ("Longitude", "-70.1551"), ("Population", "7838822"), ("Accuracy", "4")),
                        NewRow(("CountryCode", "US"), ("StateOrProvince", "Virginia"), ("City", "Long Island"), ("PostalCode", "24569"), ("TimeZone", "America/New_York"), ("Latitude", "37.0644"), ("Longitude", "-79.1219"), ("Population", "7838822"), ("Accuracy", "4")),
                        NewRow(("CountryCode", "US"), ("StateOrProvince", "New York"), ("City", "New York"), ("PostalCode", "10001"), ("TimeZone", "America/New_York"), ("Latitude", "40.7484"), ("Longitude", "-73.9967"), ("Population", "8175133"), ("Accuracy", "4")),
                        NewRow(("CountryCode", "US"), ("StateOrProvince", "Illinois"), ("City", "Chicago"), ("PostalCode", "60601"), ("TimeZone", "America/Chicago"), ("Latitude", "41.8781"), ("Longitude", "-87.6298"), ("Population", "2746388"), ("Accuracy", "4")),
                        NewRow(("CountryCode", "US"), ("StateOrProvince", "Texas"), ("City", "Dallas"), ("PostalCode", "75201"), ("TimeZone", "America/Chicago"), ("Latitude", "32.7767"), ("Longitude", "-96.7970"), ("Population", "1304379"), ("Accuracy", "4"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "United States"), ("Code", "US"), ("Continent", "North America"))
                    }
                }
            });

        Assert.Equal(3, result.World.Offices.Count);
        Assert.DoesNotContain(result.World.Offices, office => office.City == "Long Island");
        Assert.Contains(result.World.Offices, office => office.City == "New York");
        Assert.Contains(result.World.Offices, office => office.City == "Chicago");
        Assert.Contains(result.World.Offices, office => office.City == "Dallas");
    }

    [Fact]
    public void WorldGenerator_Prefers_Major_International_Cities_Over_Subcity_Localities()
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
                    Name = "International Locality Quality Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "International Quality Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 160,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 4,
                            Countries = { "Canada", "Mexico" }
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
                        NewRow(("CountryCode", "CA"), ("StateOrProvince", "Quebec"), ("City", "Ahuntsic Central"), ("PostalCode", "H2C"), ("TimeZone", "America/Toronto"), ("Latitude", "45.5606"), ("Longitude", "-73.6584"), ("Population", "0"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "CA"), ("StateOrProvince", "Quebec"), ("City", "Ahuntsic East"), ("PostalCode", "H2M"), ("TimeZone", "America/Toronto"), ("Latitude", "45.5528"), ("Longitude", "-73.6411"), ("Population", "0"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "CA"), ("StateOrProvince", "Ontario"), ("City", "Toronto"), ("PostalCode", "M5H"), ("TimeZone", "America/Toronto"), ("Latitude", "43.6532"), ("Longitude", "-79.3832"), ("Population", "2731571"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "CA"), ("StateOrProvince", "Quebec"), ("City", "Montreal"), ("PostalCode", "H2Y"), ("TimeZone", "America/Toronto"), ("Latitude", "45.5019"), ("Longitude", "-73.5674"), ("Population", "1762949"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "MX"), ("StateOrProvince", "Guerrero"), ("City", "Naucalpan de Juárez"), ("PostalCode", "41666"), ("TimeZone", "America/Mexico_City"), ("Latitude", "17.0764"), ("Longitude", "-98.4192"), ("Population", "846185"), ("Accuracy", "4")),
                        NewRow(("CountryCode", "MX"), ("StateOrProvince", "México"), ("City", "Santa María Chimalhuacán"), ("PostalCode", "56330"), ("TimeZone", "America/Mexico_City"), ("Latitude", "19.4216"), ("Longitude", "-98.9504"), ("Population", "525389"), ("Accuracy", "4")),
                        NewRow(("CountryCode", "MX"), ("StateOrProvince", "Ciudad de Mexico"), ("City", "Mexico City"), ("PostalCode", "06000"), ("TimeZone", "America/Mexico_City"), ("Latitude", "19.4326"), ("Longitude", "-99.1332"), ("Population", "9209944"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "MX"), ("StateOrProvince", "Baja California"), ("City", "Tijuana"), ("PostalCode", "22000"), ("TimeZone", "America/Tijuana"), ("Latitude", "32.5149"), ("Longitude", "-117.0382"), ("Population", "1810645"), ("Accuracy", "6"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Canada"), ("Code", "CA"), ("Continent", "North America")),
                        NewRow(("Name", "Mexico"), ("Code", "MX"), ("Continent", "North America"))
                    }
                }
            });

        Assert.Equal(4, result.World.Offices.Count);
        Assert.DoesNotContain(result.World.Offices, office => office.City.StartsWith("Ahuntsic", StringComparison.Ordinal));
        Assert.DoesNotContain(result.World.Offices, office => office.City == "Cartier");
        Assert.DoesNotContain(result.World.Offices, office => office.City == "Old Montreal");
        Assert.Contains(result.World.Offices, office => office.City == "Toronto");
        Assert.Contains(result.World.Offices, office => office.City == "Montreal");
        Assert.Contains(result.World.Offices, office => office.City == "Mexico City");
        Assert.Contains(result.World.Offices, office => office.City == "Tijuana");
        Assert.Contains(result.World.Offices, office => office.City == "Tijuana" && office.TimeZone == "America/Tijuana");
    }

    [Fact]
    public void WorldGenerator_Normalizes_Street_Suffix_Abbreviations_And_Filters_Odd_Suffixes()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 7,
                Scenario = new ScenarioDefinition
                {
                    Name = "Street Suffix Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Street Suffix Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 40,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 1,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 2,
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
                        NewRow(("CountryCode", "US"), ("StateCode", "TX"), ("StateOrProvince", "Texas"), ("City", "Houston"), ("PostalCode", "77001"), ("TimeZone", "America/Chicago"), ("Population", "2300000"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "US"), ("StateCode", "OK"), ("StateOrProvince", "Oklahoma"), ("City", "Tulsa"), ("PostalCode", "74103"), ("TimeZone", "America/Chicago"), ("Population", "411000"), ("Accuracy", "6"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "United States"), ("Code", "US"), ("Continent", "North America"))
                    },
                    ["street_suffixes"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "blvd")),
                        NewRow(("Value", "clf"))
                    }
                }
            });

        var safeSuffixes = new[] { "Boulevard", "Drive", "Lane", "Road", "Street", "Way" };
        Assert.All(result.World.Offices, office => Assert.Contains(safeSuffixes, suffix => office.StreetName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(result.World.Offices, office => Regex.IsMatch(office.StreetName, "\\bclf\\b", RegexOptions.IgnoreCase));
        Assert.DoesNotContain(result.World.Offices, office => Regex.IsMatch(office.StreetName, "\\bblvd\\b", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void WorldGenerator_Uses_RegionalUs_Scenario_Description_To_Filter_States()
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
                    Name = "Regional US Hint Test",
                    Description = "Mid-size manufacturer operating in Texas, Oklahoma, and Arkansas.",
                    GeographyProfile = "Regional-US",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Hinted Geography Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 120,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
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
                        NewRow(("CountryCode", "US"), ("StateCode", "NY"), ("StateOrProvince", "New York"), ("City", "New York"), ("PostalCode", "10001"), ("TimeZone", "America/New_York"), ("Population", "8804190"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "US"), ("StateCode", "TX"), ("StateOrProvince", "Texas"), ("City", "Dallas"), ("PostalCode", "75201"), ("TimeZone", "America/Chicago"), ("Population", "1304379"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "US"), ("StateCode", "OK"), ("StateOrProvince", "Oklahoma"), ("City", "Oklahoma City"), ("PostalCode", "73102"), ("TimeZone", "America/Chicago"), ("Population", "681054"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "US"), ("StateCode", "AR"), ("StateOrProvince", "Arkansas"), ("City", "Little Rock"), ("PostalCode", "72201"), ("TimeZone", "America/Chicago"), ("Population", "202591"), ("Accuracy", "6"))
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

        Assert.Equal(new[] { "Dallas", "Oklahoma City", "Little Rock" }, result.World.Offices.Select(office => office.City).ToArray());
        Assert.DoesNotContain(result.World.Offices, office => office.StateOrProvince == "New York");
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
    public void WorldGenerator_Filters_LowFidelity_Uk_Locality_Rows_From_Merged_Runtime_Catalog()
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
                    Name = "UK Locality Filter Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Midland Fabrication Group",
                            Industry = "Manufacturing",
                            EmployeeCount = 120,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 3,
                            Countries = { "United Kingdom" }
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
                        NewRow(("CountryCode", "GB"), ("City", "London"), ("StateOrProvince", "England"), ("PostalCode", "W1B"), ("TimeZone", "Europe/London"), ("Population", "7556900"), ("Accuracy", "4")),
                        NewRow(("CountryCode", "GB"), ("City", "Kent"), ("Population", "1541893"), ("Accuracy", "0")),
                        NewRow(("CountryCode", "GB"), ("City", "City And Borough Of Birmingham"), ("Population", "1124569"), ("Accuracy", "0")),
                        NewRow(("CountryCode", "GB"), ("City", "Birmingham"), ("StateOrProvince", "England"), ("PostalCode", "B1"), ("TimeZone", "Europe/London"), ("Population", "984333"), ("Accuracy", "4")),
                        NewRow(("CountryCode", "GB"), ("City", "Manchester"), ("StateOrProvince", "England"), ("PostalCode", "M1"), ("TimeZone", "Europe/London"), ("Population", "552858"), ("Accuracy", "4"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "United Kingdom"), ("Code", "GB"), ("Continent", "Europe"))
                    },
                    ["street_suffixes"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "Road"))
                    }
                }
            });

        Assert.Equal(new[] { "London", "Birmingham", "Manchester" }, result.World.Offices.Select(office => office.City).ToArray());
        Assert.DoesNotContain(result.World.Offices, office => office.City == "Kent" || office.City == "City And Borough Of Birmingham");
        Assert.All(result.World.Offices, office => Assert.False(string.IsNullOrWhiteSpace(office.PostalCode)));
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
                        NewRow(("Name", "Sales")),
                        NewRow(("Name", "Finance"))
                    },
                    ["titles"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Department", "Sales"), ("Title", "Account Executive"), ("Level", "Experienced")),
                        NewRow(("Department", "Sales"), ("Title", "Sales Coordinator"), ("Level", "Entry")),
                        NewRow(("Department", "Finance"), ("Title", "Payroll Analyst"), ("Level", "Experienced")),
                        NewRow(("Department", "Finance"), ("Title", "Accounts Payable Clerk"), ("Level", "Entry"))
                    }
                }
            });

        var departments = result.World.Departments.ToDictionary(department => department.Id, department => department.Name, StringComparer.OrdinalIgnoreCase);
        var financeTitles = result.World.People
            .Where(person => departments.TryGetValue(person.DepartmentId, out var departmentName) && departmentName == "Finance")
            .Select(person => person.Title)
            .ToList();

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
                            Countries = { "Ireland" }
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
            Assert.Equal("Ireland", person.Country);
        });
    }

    [Fact]
    public void WorldGenerator_Filters_Synthetic_Affix_Name_Artifacts_From_Country_Catalogs()
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
                    Name = "Name Catalog Sanitization Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Name Sanitization Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 12,
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
                    ["first_names_country"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "DiDavid"), ("Gender", "Male"), ("Country", "United States")),
                        NewRow(("Name", "SanMary"), ("Gender", "Female"), ("Country", "United States"))
                    },
                    ["last_names_country"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Johnsonov"), ("Country", "United States")),
                        NewRow(("Name", "Williamsov"), ("Country", "United States"))
                    }
                }
            });

        Assert.All(result.World.People, person => Assert.DoesNotMatch("[A-Z][a-z]+[A-Z]", person.FirstName));
        Assert.All(result.World.People, person => Assert.DoesNotContain("ov", person.LastName, StringComparison.OrdinalIgnoreCase));
        Assert.All(result.World.People, person => Assert.False(LooksSyntheticFirstName(person.FirstName)));
        Assert.All(result.World.People, person => Assert.False(LooksSyntheticLastName(person.LastName)));
    }

    [Fact]
    public void WorldGenerator_Filters_First_Name_Leakage_From_Surname_Catalogs()
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
                    Name = "Surname Leakage Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Surname Leakage Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 12,
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
                    ["first_names_gendered"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "James"), ("Gender", "Male")),
                        NewRow(("Name", "Emma"), ("Gender", "Female")),
                        NewRow(("Name", "Abigail"), ("Gender", "Female"))
                    },
                    ["surnames_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "James")),
                        NewRow(("Value", "Abigail"))
                    }
                }
            });

        Assert.DoesNotContain(result.World.People, person => string.Equals(person.LastName, "James", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.World.People, person => string.Equals(person.LastName, "Abigail", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.World.People, person => person.LastName == "Smith" || person.LastName == "Johnson");
    }

    [Fact]
    public void WorldGenerator_Uses_Curated_Us_Name_Catalogs_For_Conservative_Name_Fallbacks()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 5,
                Scenario = new ScenarioDefinition
                {
                    Name = "Curated US Name Catalog Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Curated US Name Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 16,
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
                    ["first_names_country"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "DiDavid"), ("Gender", "Male"), ("Country", "United States")),
                        NewRow(("Name", "SanMary"), ("Gender", "Female"), ("Country", "United States"))
                    },
                    ["last_names_country"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Johnsonov"), ("Country", "United States")),
                        NewRow(("Name", "Williamsov"), ("Country", "United States"))
                    },
                    ["first_names_curated_us"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "James"), ("Gender", "Male"), ("Country", "United States"), ("CareerStage", "Experienced")),
                        NewRow(("Name", "Olivia"), ("Gender", "Female"), ("Country", "United States"), ("CareerStage", "Modern"))
                    },
                    ["surnames_curated_us"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "Smith"), ("Country", "United States")),
                        NewRow(("Value", "Brown"), ("Country", "United States"))
                    }
                }
            });

        Assert.All(result.World.People, person => Assert.Contains(person.FirstName, new[] { "James", "Olivia" }));
        Assert.All(result.World.People, person => Assert.Contains(person.LastName, new[] { "Smith", "Brown" }));
    }

    [Fact]
    public void WorldGenerator_Uses_Career_Stage_From_Curated_Us_Name_Catalogs()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 2,
                Scenario = new ScenarioDefinition
                {
                    Name = "Curated US Career Stage Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Curated Stage Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 20,
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
                    ["first_names_curated_us"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Edward"), ("Gender", "Male"), ("Country", "United States"), ("CareerStage", "Experienced")),
                        NewRow(("Name", "Emma"), ("Gender", "Female"), ("Country", "United States"), ("CareerStage", "Modern"))
                    },
                    ["surnames_curated_us"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "Smith"), ("Country", "United States"))
                    }
                }
            });

        Assert.Equal("Chief Executive Officer", result.World.People[0].Title);
        Assert.Contains(result.World.People.Take(15), person => person.FirstName == "Edward");
        Assert.Contains(result.World.People.Skip(15), person => person.FirstName == "Emma");
    }

    [Fact]
    public void WorldGenerator_Uses_Country_Specific_Curated_Name_Catalogs_For_United_Kingdom()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 4,
                Scenario = new ScenarioDefinition
                {
                    Name = "Curated UK Name Catalog Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Curated UK Name Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 18,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 1,
                            Countries = { "United Kingdom" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["first_names_curated_uk"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Harry"), ("Gender", "Male"), ("Country", "United Kingdom"), ("CareerStage", "Experienced")),
                        NewRow(("Name", "Olivia"), ("Gender", "Female"), ("Country", "United Kingdom"), ("CareerStage", "Modern"))
                    }
                }
            });

        Assert.All(result.World.People, person => Assert.Contains(person.FirstName, new[] { "Harry", "Olivia" }));
    }

    [Fact]
    public void WorldGenerator_Uses_Country_Specific_Curated_Name_Catalogs_For_Canada()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 6,
                Scenario = new ScenarioDefinition
                {
                    Name = "Curated Canada Name Catalog Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Curated Canada Name Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 18,
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
                    ["first_names_curated_ca"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Noah"), ("Gender", "Male"), ("Country", "Canada"), ("CareerStage", "Experienced")),
                        NewRow(("Name", "Olivia"), ("Gender", "Female"), ("Country", "Canada"), ("CareerStage", "Modern"))
                    }
                }
            });

        Assert.All(result.World.People, person => Assert.Contains(person.FirstName, new[] { "Noah", "Olivia" }));
    }

    [Fact]
    public void WorldGenerator_Uses_Country_Specific_Curated_Name_Catalogs_For_Australia()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 8,
                Scenario = new ScenarioDefinition
                {
                    Name = "Curated Australia Name Catalog Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Curated Australia Name Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 18,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 1,
                            Countries = { "Australia" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["first_names_curated_au"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Oliver"), ("Gender", "Male"), ("Country", "Australia"), ("CareerStage", "Experienced")),
                        NewRow(("Name", "Charlotte"), ("Gender", "Female"), ("Country", "Australia"), ("CareerStage", "Modern"))
                    }
                }
            });

        Assert.All(result.World.People, person => Assert.Contains(person.FirstName, new[] { "Oliver", "Charlotte" }));
    }

    [Fact]
    public void WorldGenerator_Uses_Country_Specific_Curated_Name_Catalogs_For_NewZealand()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 9,
                Scenario = new ScenarioDefinition
                {
                    Name = "Curated New Zealand Name Catalog Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Curated New Zealand Name Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 18,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 1,
                            Countries = { "New Zealand" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["first_names_curated_nz"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Noah"), ("Gender", "Male"), ("Country", "New Zealand"), ("CareerStage", "Experienced")),
                        NewRow(("Name", "Charlotte"), ("Gender", "Female"), ("Country", "New Zealand"), ("CareerStage", "Modern"))
                    }
                }
            });

        Assert.All(result.World.People, person => Assert.Contains(person.FirstName, new[] { "Noah", "Charlotte" }));
    }

    [Fact]
    public void WorldGenerator_Uses_Country_Specific_Curated_Surname_Catalogs_For_NewZealand()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 10,
                Scenario = new ScenarioDefinition
                {
                    Name = "Curated New Zealand Surname Catalog Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Curated New Zealand Surname Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 18,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 1,
                            Countries = { "New Zealand" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["first_names_curated_nz"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Noah"), ("Gender", "Male"), ("Country", "New Zealand"), ("CareerStage", "Experienced")),
                        NewRow(("Name", "Charlotte"), ("Gender", "Female"), ("Country", "New Zealand"), ("CareerStage", "Modern"))
                    },
                    ["surnames_curated_nz"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "Singh"), ("Country", "New Zealand")),
                        NewRow(("Value", "Taylor"), ("Country", "New Zealand"))
                    }
                }
            });

        Assert.All(result.World.People, person => Assert.Contains(person.LastName, new[] { "Singh", "Taylor" }));
    }

    [Fact]
    public void WorldGenerator_Uses_Country_Specific_Curated_Surname_Catalogs_For_UnitedKingdom()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 11,
                Scenario = new ScenarioDefinition
                {
                    Name = "Curated UK Surname Catalog Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Curated UK Surname Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 18,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 1,
                            Countries = { "United Kingdom" }
                        }
                    }
                }
            },
            new CatalogSet
            {
                CsvCatalogs = new Dictionary<string, IReadOnlyList<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["first_names_curated_uk"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Oliver"), ("Gender", "Male"), ("Country", "United Kingdom"), ("CareerStage", "Experienced")),
                        NewRow(("Name", "Amelia"), ("Gender", "Female"), ("Country", "United Kingdom"), ("CareerStage", "Modern"))
                    },
                    ["surnames_curated_uk"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "Smith"), ("Country", "United Kingdom")),
                        NewRow(("Value", "Khan"), ("Country", "United Kingdom"))
                    }
                }
            });

        Assert.All(result.World.People, person => Assert.Contains(person.LastName, new[] { "Smith", "Khan" }));
    }

    [Fact]
    public void WorldGenerator_Expands_Npa_And_Nxx_Phone_Tokens_To_Digits()
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
                    Name = "Phone Pattern Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Phone Pattern Co",
                            Industry = "Manufacturing",
                            EmployeeCount = 20,
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
                    ["locality_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("CountryCode", "US"), ("StateCode", "TX"), ("StateOrProvince", "Texas"), ("City", "Dallas"), ("PostalCode", "75201"), ("TimeZone", "America/Chicago"), ("Latitude", "32.7767"), ("Longitude", "-96.7970"), ("Population", "1304379"), ("Accuracy", "6"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "United States"), ("Code", "US"), ("Continent", "North America"), ("Phone", "1"))
                    },
                    ["country_identity_rules"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Country", "United States"), ("DialCode", "1"), ("PhonePattern", "+1 NPA-NXX-XXXX"))
                    }
                }
            });

        var phone = Assert.Single(result.World.Offices).BusinessPhone;
        Assert.Matches(new Regex(@"^\+1 [2-9]\d{2}-[2-9]\d{2}-\d{4}$"), phone);
    }

    [Fact]
    public void WorldGenerator_Uses_Plausible_Uk_Business_Phone_Format()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 11,
                Scenario = new ScenarioDefinition
                {
                    Name = "UK Phone Pattern Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Midland Fabrication Group",
                            Industry = "Manufacturing",
                            EmployeeCount = 20,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 1,
                            Countries = { "United Kingdom" }
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
                        NewRow(("CountryCode", "GB"), ("StateOrProvince", "England"), ("City", "London"), ("PostalCode", "W1B"), ("TimeZone", "Europe/London"), ("Latitude", "51.5072"), ("Longitude", "-0.1276"), ("Population", "7556900"), ("Accuracy", "4"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "United Kingdom"), ("Code", "GB"), ("Continent", "Europe"), ("Phone", "44"))
                    },
                    ["country_identity_rules"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Country", "United Kingdom"), ("DialCode", "44"), ("PhonePattern", "+44 XXXX XXXXXX"))
                    }
                }
            });

        var phone = Assert.Single(result.World.Offices).BusinessPhone;
        Assert.Matches(new Regex(@"^\+44 ((20 \d{4} \d{4})|((121|131|141|151|161|191|113|114|115|116|117|118) \d{3} \d{4}))$"), phone);
        Assert.DoesNotContain("+44 00", phone, StringComparison.Ordinal);
    }

    [Fact]
    public void WorldGenerator_Uses_Uk_City_Area_Codes_For_Common_Office_Cities()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 21,
                Scenario = new ScenarioDefinition
                {
                    Name = "UK City Phone Mapping Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Midland Fabrication Group",
                            Industry = "Manufacturing",
                            EmployeeCount = 40,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 3,
                            Countries = { "United Kingdom" }
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
                        NewRow(("CountryCode", "GB"), ("StateOrProvince", "England"), ("City", "London"), ("PostalCode", "W1B"), ("TimeZone", "Europe/London"), ("Population", "7556900"), ("Accuracy", "4")),
                        NewRow(("CountryCode", "GB"), ("StateOrProvince", "England"), ("City", "Birmingham"), ("PostalCode", "B1"), ("TimeZone", "Europe/London"), ("Population", "984333"), ("Accuracy", "4")),
                        NewRow(("CountryCode", "GB"), ("StateOrProvince", "England"), ("City", "Liverpool"), ("PostalCode", "L1"), ("TimeZone", "Europe/London"), ("Population", "864122"), ("Accuracy", "4"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "United Kingdom"), ("Code", "GB"), ("Continent", "Europe"), ("Phone", "44"))
                    },
                    ["country_identity_rules"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Country", "United Kingdom"), ("DialCode", "44"), ("PhonePattern", "+44 XXXX XXXXXX"))
                    }
                }
            });

        Assert.Contains(result.World.Offices, office => office.City == "London" && office.BusinessPhone.StartsWith("+44 20 ", StringComparison.Ordinal));
        Assert.Contains(result.World.Offices, office => office.City == "Birmingham" && office.BusinessPhone.StartsWith("+44 121 ", StringComparison.Ordinal));
        Assert.Contains(result.World.Offices, office => office.City == "Liverpool" && office.BusinessPhone.StartsWith("+44 151 ", StringComparison.Ordinal));
    }

    [Fact]
    public void WorldGenerator_Uses_City_Aware_Canadian_And_Mexican_Business_Phones()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 31,
                Scenario = new ScenarioDefinition
                {
                    Name = "Canada Mexico Phone Mapping Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "International Manufacturing Group",
                            Industry = "Manufacturing",
                            EmployeeCount = 60,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 4,
                            Countries = { "Canada", "Mexico" }
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
                        NewRow(("CountryCode", "CA"), ("StateOrProvince", "Ontario"), ("City", "Toronto"), ("PostalCode", "M5H"), ("TimeZone", "America/Toronto"), ("Population", "2731571"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "CA"), ("StateOrProvince", "Quebec"), ("City", "Montreal"), ("PostalCode", "H2Y"), ("TimeZone", "America/Toronto"), ("Population", "1762949"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "MX"), ("StateOrProvince", "Ciudad de Mexico"), ("City", "Mexico City"), ("PostalCode", "06000"), ("TimeZone", "America/Mexico_City"), ("Population", "9209944"), ("Accuracy", "6")),
                        NewRow(("CountryCode", "MX"), ("StateOrProvince", "Baja California"), ("City", "Tijuana"), ("PostalCode", "22000"), ("TimeZone", "America/Tijuana"), ("Population", "1810645"), ("Accuracy", "6"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "Canada"), ("Code", "CA"), ("Continent", "North America"), ("Phone", "1")),
                        NewRow(("Name", "Mexico"), ("Code", "MX"), ("Continent", "North America"), ("Phone", "52"))
                    },
                    ["country_identity_rules"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Country", "Canada"), ("DialCode", "1"), ("PhonePattern", "+1 NPA-NXX-XXXX")),
                        NewRow(("Country", "Mexico"), ("DialCode", "52"), ("PhonePattern", "+52 XX XXXX XXXX"))
                    }
                }
            });

        Assert.Contains(result.World.Offices, office => office.City == "Toronto" && Regex.IsMatch(office.BusinessPhone, @"^\+1 (416|437|647)-\d{3}-\d{4}$"));
        Assert.Contains(result.World.Offices, office => office.City == "Montreal" && Regex.IsMatch(office.BusinessPhone, @"^\+1 (438|514)-\d{3}-\d{4}$"));
        Assert.Contains(result.World.Offices, office => office.City == "Mexico City" && Regex.IsMatch(office.BusinessPhone, @"^\+52 55 \d{4} \d{4}$"));
        Assert.Contains(result.World.Offices, office => office.City == "Tijuana" && Regex.IsMatch(office.BusinessPhone, @"^\+52 664 \d{3} \d{4}$"));
    }

    [Fact]
    public void WorldGenerator_Uses_Uk_Style_Office_Address_Components()
    {
        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        var generator = services.GetRequiredService<IWorldGenerator>();
        var result = generator.Generate(
            new GenerationContext
            {
                Seed = 17,
                Scenario = new ScenarioDefinition
                {
                    Name = "UK Address Style Test",
                    Companies =
                    {
                        new ScenarioCompanyDefinition
                        {
                            Name = "Midland Fabrication Group",
                            Industry = "Manufacturing",
                            EmployeeCount = 20,
                            BusinessUnitCount = 1,
                            DepartmentCountPerBusinessUnit = 2,
                            TeamCountPerDepartment = 1,
                            OfficeCount = 1,
                            Countries = { "United Kingdom" }
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
                        NewRow(("CountryCode", "GB"), ("StateOrProvince", "England"), ("City", "London"), ("PostalCode", "W1B"), ("TimeZone", "Europe/London"), ("Latitude", "51.5072"), ("Longitude", "-0.1276"), ("Population", "7556900"), ("Accuracy", "4"))
                    },
                    ["countries_reference"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Name", "United Kingdom"), ("Code", "GB"), ("Continent", "Europe"), ("Phone", "44"))
                    },
                    ["country_identity_rules"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Country", "United Kingdom"), ("DialCode", "44"), ("PhonePattern", "+44 XXXX XXXXXX"))
                    },
                    ["street_suffixes"] = new List<Dictionary<string, string?>>
                    {
                        NewRow(("Value", "Boulevard")),
                        NewRow(("Value", "Road")),
                        NewRow(("Value", "Lane"))
                    }
                }
            });

        var office = Assert.Single(result.World.Offices);
        Assert.DoesNotContain("Boulevard", office.StreetName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("High Way", office.StreetName, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(new Regex(@"\b(Road|Lane|Street|Way|Close)$"), office.StreetName);
        Assert.Matches(new Regex(@"^(Bridge|Station|Market|Mill|Victoria|King|Queen|Albion|Manor|High) "), office.StreetName);
        Assert.StartsWith("Floor ", office.FloorOrSuite, StringComparison.Ordinal);
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
        var operationsDepartment = Assert.Single(result.World.Departments, department => department.Name is "Operations" or "Production Operations");
        var salesDepartment = Assert.Single(result.World.Departments, department => department.Name == "Sales");

        Assert.Equal("Supply Chain and Manufacturing", result.World.BusinessUnits.Single(unit => unit.Id == operationsDepartment.BusinessUnitId).Name);
        Assert.Equal("Commercial Operations", result.World.BusinessUnits.Single(unit => unit.Id == salesDepartment.BusinessUnitId).Name);

        Assert.Contains(result.World.Teams, team => team.Name == "Production Scheduling" && departmentsById[team.DepartmentId].Name == operationsDepartment.Name);
        Assert.Contains(result.World.Teams, team =>
            departmentsById[team.DepartmentId].Name == "Sales"
            && team.Name is "Commercial Planning" or "Regional Sales" or "Account Management");
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

    private static bool LooksSyntheticLastName(string value)
        => value.StartsWith("Di", StringComparison.OrdinalIgnoreCase)
           || value.StartsWith("San", StringComparison.OrdinalIgnoreCase)
           || value.EndsWith("ov", StringComparison.OrdinalIgnoreCase);

    private static bool LooksSyntheticFirstName(string value)
        => value.StartsWith("Di", StringComparison.OrdinalIgnoreCase)
           || value.StartsWith("San", StringComparison.OrdinalIgnoreCase)
           || Regex.IsMatch(value, "[A-Z][a-z]+[A-Z]");
}
