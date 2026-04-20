namespace SyntheticEnterprise.Core.Generation.Geography;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public sealed class BasicGeographyGenerator : IGeographyGenerator
{
    private readonly IIdFactory _idFactory;
    private readonly IRandomSource _randomSource;

    public BasicGeographyGenerator(IIdFactory idFactory, IRandomSource randomSource)
    {
        _idFactory = idFactory;
        _randomSource = randomSource;
    }

    public void GenerateOffices(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        foreach (var companyDefinition in context.Scenario.Companies)
        {
            var company = world.Companies.FirstOrDefault(c => string.Equals(c.Name, companyDefinition.Name, StringComparison.OrdinalIgnoreCase));
            if (company is null)
            {
                continue;
            }

            var cityRows = GetCityRows(catalogs, companyDefinition.Countries);
            cityRows = ApplyScenarioGeographyHints(cityRows, context.Scenario, companyDefinition, Math.Max(1, companyDefinition.OfficeCount));
            if (cityRows.Count == 0)
            {
                cityRows = GetFallbackCities(companyDefinition.Countries);
            }

            var officeCount = Math.Max(1, companyDefinition.OfficeCount);
            var selectedCities = SelectCities(cityRows, officeCount);
            var streetSuffixes = ReadCatalogValues(catalogs, "street_suffixes", "Value", new[] { "Blvd", "Way", "Drive", "Street" });
            var countryDialCodes = ReadCountryDialCodes(catalogs);
            var countryIdentityRules = ReadCountryIdentityRules(catalogs);

            var createdOffices = new List<Office>();
            for (var i = 0; i < selectedCities.Count; i++)
            {
                var city = selectedCities[i];
                var office = new Office
                {
                    Id = _idFactory.Next("OFF"),
                    CompanyId = company.Id,
                    Name = BuildOfficeName(city.City, i),
                    Region = city.Region,
                    Country = city.Country,
                    StateOrProvince = city.StateOrProvince,
                    City = city.City,
                    PostalCode = city.PostalCode,
                    TimeZone = city.TimeZone,
                    AddressMode = companyDefinition.AddressMode,
                    BuildingNumber = (100 + _randomSource.Next(1, 999)).ToString(),
                    StreetName = BuildStreetName(city.Country, city.City, streetSuffixes),
                    FloorOrSuite = BuildFloorOrSuite(city.Country),
                    BusinessPhone = BuildBusinessPhone(city.Country, city.City, countryIdentityRules, countryDialCodes),
                    IsHeadquarters = i == 0,
                    Latitude = companyDefinition.IncludeGeocodes ? city.Latitude : null,
                    Longitude = companyDefinition.IncludeGeocodes ? city.Longitude : null,
                    Geocoded = companyDefinition.IncludeGeocodes
                };

                createdOffices.Add(office);
                world.Offices.Add(office);
            }

            AssignPeopleToOffices(world, company.Id, createdOffices);
            UpdateCompanyHeadquarters(world, company, createdOffices);
        }
    }

    private void AssignPeopleToOffices(SyntheticEnterpriseWorld world, string companyId, IReadOnlyList<Office> offices)
    {
        if (offices.Count == 0)
        {
            return;
        }

        for (var i = 0; i < world.People.Count; i++)
        {
            var person = world.People[i];
            if (!string.Equals(person.CompanyId, companyId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Office? office = null;

            if (!string.IsNullOrWhiteSpace(person.Country))
            {
                office = offices.FirstOrDefault(o => string.Equals(o.Country, person.Country, StringComparison.OrdinalIgnoreCase));
            }

            office ??= offices[i % offices.Count];

            world.People[i] = person with
            {
                OfficeId = office.Id,
                Country = string.IsNullOrWhiteSpace(person.Country) ? office.Country : person.Country
            };
        }
    }

    private static string BuildOfficeName(string city, int index)
    {
        if (index == 0) return $"{city} Headquarters";
        return $"{city} Office";
    }

    private string BuildStreetName(string country, string city, IReadOnlyList<string> suffixes)
    {
        var stems = GetStreetStems(country);
        var stem = stems[_randomSource.Next(stems.Count)];
        var preferredSuffixes = GetPreferredStreetSuffixes(country, suffixes);
        var allowedSuffixes = GetAllowedStreetSuffixes(country, stem, preferredSuffixes);
        var suffix = allowedSuffixes.Count == 0
            ? FallbackStreetSuffix(country)
            : allowedSuffixes[_randomSource.Next(allowedSuffixes.Count)];
        return $"{stem} {suffix}";
    }

    private string NormalizeStreetSuffix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FallbackStreetSuffix();
        }

        var normalized = value.Trim().TrimEnd('.').ToLowerInvariant();
        return normalized switch
        {
            "ave" or "av" or "avn" or "avnue" => "Avenue",
            "blvd" or "boul" => "Boulevard",
            "cir" => "Circle",
            "cl" or "close" => "Close",
            "ct" or "crt" => "Court",
            "dr" or "drs" => "Drive",
            "hwy" => "Highway",
            "ln" => "Lane",
            "pkwy" => "Parkway",
            "pl" => "Place",
            "rd" => "Road",
            "sq" => "Square",
            "st" or "str" => "Street",
            "ter" => "Terrace",
            "trl" => "Trail",
            "way" => "Way",
            _ when IsSafeStreetSuffix(normalized) => TitleCaseStreetSuffix(normalized),
            _ => FallbackStreetSuffix()
        };
    }

    private static bool IsSafeStreetSuffix(string value)
    {
        return value is
            "avenue" or
            "boulevard" or
            "circle" or
            "close" or
            "court" or
            "drive" or
            "highway" or
            "lane" or
            "parkway" or
            "place" or
            "road" or
            "square" or
            "street" or
            "terrace" or
            "trail" or
            "way";
    }

    private string FallbackStreetSuffix()
    {
        var safeSuffixes = new[] { "Boulevard", "Drive", "Lane", "Road", "Street", "Way" };
        return safeSuffixes[_randomSource.Next(safeSuffixes.Length)];
    }

    private string FallbackStreetSuffix(string country)
    {
        var safeSuffixes = country switch
        {
            "United Kingdom" => new[] { "Lane", "Road", "Street", "Way" },
            "Canada" => new[] { "Avenue", "Boulevard", "Drive", "Road", "Street", "Way" },
            "Mexico" => new[] { "Avenue", "Boulevard", "Road", "Street" },
            _ => new[] { "Boulevard", "Drive", "Lane", "Road", "Street", "Way" }
        };
        return safeSuffixes[_randomSource.Next(safeSuffixes.Length)];
    }

    private List<string> GetPreferredStreetSuffixes(string country, IReadOnlyList<string> suffixes)
    {
        var normalized = suffixes
            .Select(NormalizeStreetSuffix)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        if (string.Equals(country, "United Kingdom", StringComparison.OrdinalIgnoreCase))
        {
            var ukPreferred = normalized
                .Where(value => value is "Lane" or "Road" or "Street" or "Way" or "Close")
                .ToList();
            if (ukPreferred.Count > 0)
            {
                return ukPreferred;
            }
        }

        if (string.Equals(country, "Canada", StringComparison.OrdinalIgnoreCase))
        {
            var canadaPreferred = normalized
                .Where(value => value is "Avenue" or "Boulevard" or "Court" or "Drive" or "Road" or "Street" or "Way")
                .ToList();
            if (canadaPreferred.Count > 0)
            {
                return canadaPreferred;
            }
        }

        if (string.Equals(country, "Mexico", StringComparison.OrdinalIgnoreCase))
        {
            var mexicoPreferred = normalized
                .Where(value => value is "Avenue" or "Boulevard" or "Road" or "Street")
                .ToList();
            if (mexicoPreferred.Count > 0)
            {
                return mexicoPreferred;
            }
        }

        return normalized.ToList();
    }

    private string BuildFloorOrSuite(string country)
    {
        return string.Equals(country, "United Kingdom", StringComparison.OrdinalIgnoreCase)
            ? $"Floor {_randomSource.Next(1, 7)}"
            : $"Suite {100 + _randomSource.Next(1, 30)}";
    }

    private static IReadOnlyList<string> GetStreetStems(string country)
    {
        if (string.Equals(country, "United Kingdom", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                "Bridge", "Station", "Market", "Mill", "Victoria", "King", "Queen", "Albion", "Manor", "High"
            };
        }

        if (string.Equals(country, "Canada", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                "Bay", "Front", "Granville", "Harbour", "King", "Main", "Queen", "Richmond", "Wellington", "Yonge"
            };
        }

        if (string.Equals(country, "Mexico", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                "Constitucion", "Hidalgo", "Independencia", "Insurgentes", "Juarez", "Madero", "Morelos", "Reforma"
            };
        }

        return new[]
        {
            "Main", "Commerce", "Industrial", "Market", "Enterprise", "Technology", "Foundry", "Logistics"
        };
    }

    private List<string> GetAllowedStreetSuffixes(string country, string stem, IReadOnlyList<string> preferredSuffixes)
    {
        if (string.Equals(country, "Canada", StringComparison.OrdinalIgnoreCase))
        {
            var canadaPairings = stem switch
            {
                "Bay" => new[] { "Street" },
                "Front" => new[] { "Street", "Road" },
                "Granville" => new[] { "Street" },
                "Harbour" => new[] { "Street", "Way" },
                "King" => new[] { "Street", "Road" },
                "Main" => new[] { "Street", "Road" },
                "Queen" => new[] { "Street", "Road" },
                "Richmond" => new[] { "Street", "Road" },
                "Wellington" => new[] { "Street", "Road" },
                "Yonge" => new[] { "Street" },
                _ => Array.Empty<string>()
            };

            if (canadaPairings.Length == 0)
            {
                return preferredSuffixes.ToList();
            }

            var canadaMatched = preferredSuffixes
                .Where(value => canadaPairings.Contains(value, StringComparer.OrdinalIgnoreCase))
                .ToList();

            return canadaMatched.Count > 0 ? canadaMatched : canadaPairings.ToList();
        }

        if (string.Equals(country, "Mexico", StringComparison.OrdinalIgnoreCase))
        {
            var mexicoPairings = stem switch
            {
                "Constitucion" => new[] { "Boulevard", "Avenue" },
                "Hidalgo" => new[] { "Avenue", "Boulevard" },
                "Independencia" => new[] { "Avenue", "Boulevard" },
                "Insurgentes" => new[] { "Avenue", "Boulevard" },
                "Juarez" => new[] { "Avenue", "Boulevard" },
                "Madero" => new[] { "Street", "Avenue" },
                "Morelos" => new[] { "Street", "Avenue" },
                "Reforma" => new[] { "Avenue", "Boulevard" },
                _ => Array.Empty<string>()
            };

            if (mexicoPairings.Length == 0)
            {
                return preferredSuffixes.ToList();
            }

            var mexicoMatched = preferredSuffixes
                .Where(value => mexicoPairings.Contains(value, StringComparer.OrdinalIgnoreCase))
                .ToList();

            return mexicoMatched.Count > 0 ? mexicoMatched : mexicoPairings.ToList();
        }

        if (!string.Equals(country, "United Kingdom", StringComparison.OrdinalIgnoreCase))
        {
            return preferredSuffixes.ToList();
        }

        var ukPairings = stem switch
        {
            "High" => new[] { "Street", "Road" },
            "Station" => new[] { "Road", "Street" },
            "Victoria" => new[] { "Road", "Street" },
            "Bridge" => new[] { "Street", "Road" },
            "Market" => new[] { "Street", "Road", "Lane" },
            "Mill" => new[] { "Lane", "Road", "Street" },
            "King" => new[] { "Street", "Road" },
            "Queen" => new[] { "Street", "Road" },
            "Albion" => new[] { "Street", "Road", "Close" },
            "Manor" => new[] { "Road", "Lane", "Close" },
            _ => Array.Empty<string>()
        };

        if (ukPairings.Length == 0)
        {
            return preferredSuffixes.ToList();
        }

        var matched = preferredSuffixes
            .Where(value => ukPairings.Contains(value, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return matched.Count > 0 ? matched : ukPairings.ToList();
    }

    private static string TitleCaseStreetSuffix(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Way";
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }

    private string BuildBusinessPhone(string country, IReadOnlyDictionary<string, string> countryDialCodes)
    {
        var dialCode = countryDialCodes.TryGetValue(country, out var code) && !string.IsNullOrWhiteSpace(code)
            ? code
            : "1";
        dialCode = new string(dialCode.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(dialCode))
        {
            dialCode = "1";
        }

        if (dialCode == "1")
        {
            return $"+1 {_randomSource.Next(210, 989):000}-{_randomSource.Next(200, 999):000}-{_randomSource.Next(1000, 9999):0000}";
        }

        return $"+{dialCode} {_randomSource.Next(10, 99):00} {_randomSource.Next(1000, 9999):0000} {_randomSource.Next(1000, 9999):0000}";
    }

    private string BuildBusinessPhone(
        string country,
        string city,
        IReadOnlyDictionary<string, CountryIdentityRule> countryRules,
        IReadOnlyDictionary<string, string> countryDialCodes)
    {
        if (string.Equals(country, "United Kingdom", StringComparison.OrdinalIgnoreCase))
        {
            return BuildUnitedKingdomBusinessPhone(city);
        }

        if (string.Equals(country, "Canada", StringComparison.OrdinalIgnoreCase))
        {
            return BuildCanadaBusinessPhone(city);
        }

        if (string.Equals(country, "Mexico", StringComparison.OrdinalIgnoreCase))
        {
            return BuildMexicoBusinessPhone(city);
        }

        if (countryRules.TryGetValue(country, out var rule))
        {
            var dialCode = string.IsNullOrWhiteSpace(rule.DialCode)
                ? (countryDialCodes.TryGetValue(country, out var fallbackDialCode) ? fallbackDialCode : "1")
                : rule.DialCode;
            return ApplyPhonePattern(rule.PhonePattern, dialCode);
        }

        return BuildBusinessPhone(country, countryDialCodes);
    }

    private string BuildCanadaBusinessPhone(string city)
    {
        var areaCodes = NormalizeCityKey(city) switch
        {
            "toronto" => new[] { "416", "437", "647" },
            "montreal" => new[] { "438", "514" },
            "vancouver" => new[] { "236", "604", "778" },
            "calgary" => new[] { "403", "587", "825" },
            "ottawa" => new[] { "343", "613" },
            "edmonton" => new[] { "587", "780", "825" },
            "quebec" => new[] { "418", "581" },
            "mississauga" => new[] { "289", "365", "905" },
            _ => new[] { "204", "236", "249", "306", "343", "365", "403", "416", "431", "437", "438", "506", "514", "587", "604", "613", "647", "705", "778", "780", "825", "867", "902", "905" }
        };

        var areaCode = areaCodes[_randomSource.Next(areaCodes.Length)];
        return $"+1 {areaCode}-{_randomSource.Next(200, 1000):000}-{_randomSource.Next(1000, 10000):0000}";
    }

    private string BuildMexicoBusinessPhone(string city)
    {
        var normalizedCity = NormalizeCityKey(city);
        if (normalizedCity is "mexico" or "iztapalapa" or "ecatepec" or "naucalpan" or "naucalpan de juarez" or "santa maria chimalhuacan")
        {
            return $"+52 55 {_randomSource.Next(1000, 10000):0000} {_randomSource.Next(1000, 10000):0000}";
        }

        var areaCode = normalizedCity switch
        {
            "guadalajara" => "33",
            "monterrey" => "81",
            "tijuana" => "664",
            "mexicali" => "686",
            "puebla" => "222",
            "queretaro" => "442",
            "leon" => "477",
            "merida" => "999",
            "chihuahua" => "614",
            "toluca" => "722",
            _ => null
        };

        if (string.IsNullOrWhiteSpace(areaCode))
        {
            areaCode = new[] { "222", "228", "241", "442", "477", "614", "656", "664", "686", "722", "818", "833", "999" }[_randomSource.Next(13)];
        }

        return areaCode.Length == 2
            ? $"+52 {areaCode} {_randomSource.Next(1000, 10000):0000} {_randomSource.Next(1000, 10000):0000}"
            : $"+52 {areaCode} {_randomSource.Next(100, 1000):000} {_randomSource.Next(1000, 10000):0000}";
    }

    private static IReadOnlyDictionary<string, string> ReadCountryDialCodes(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("countries_reference", out var rows))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return rows
            .Where(row => row.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name))
            .GroupBy(row => row["Name"] ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.FirstOrDefault(row => row.TryGetValue("Phone", out var phone) && !string.IsNullOrWhiteSpace(phone))?.GetValueOrDefault("Phone") ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, CountryIdentityRule> ReadCountryIdentityRules(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("country_identity_rules", out var rows))
        {
            return new Dictionary<string, CountryIdentityRule>(StringComparer.OrdinalIgnoreCase);
        }

        return rows
            .Where(row => row.TryGetValue("Country", out var country) && !string.IsNullOrWhiteSpace(country))
            .GroupBy(row => row["Country"] ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var row = group.First();
                    return new CountryIdentityRule(
                        Read(row, "DialCode"),
                        Read(row, "PrimaryDomainSuffix"),
                        Read(row, "AlternateDomainSuffix"),
                        FirstNonEmpty(Read(row, "PhonePattern"), "+1 NPA-NXX-XXXX"));
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static void UpdateCompanyHeadquarters(SyntheticEnterpriseWorld world, Company company, IReadOnlyList<Office> createdOffices)
    {
        if (createdOffices.Count == 0)
        {
            return;
        }

        var headquarters = createdOffices[0];
        var companyIndex = world.Companies.FindIndex(existing => string.Equals(existing.Id, company.Id, StringComparison.OrdinalIgnoreCase));
        if (companyIndex < 0)
        {
            return;
        }

        var updated = world.Companies[companyIndex] with
        {
            HeadquartersOfficeId = headquarters.Id,
            PrimaryCountry = string.IsNullOrWhiteSpace(world.Companies[companyIndex].PrimaryCountry) ? headquarters.Country : world.Companies[companyIndex].PrimaryCountry,
            PrimaryPhoneNumber = string.IsNullOrWhiteSpace(headquarters.BusinessPhone) ? world.Companies[companyIndex].PrimaryPhoneNumber : headquarters.BusinessPhone
        };

        world.Companies[companyIndex] = updated;
    }

    private static List<CityRow> SelectCities(IReadOnlyList<CityRow> rows, int officeCount)
    {
        if (rows.Count == 0 || officeCount <= 0)
        {
            return new List<CityRow>();
        }

        var duplicateCityCounts = rows
            .GroupBy(row => $"{row.Country}|{NormalizeCityKey(row.City)}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var grouped = rows
            .GroupBy(row => row.Country, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .GroupBy(row => $"{row.Country}|{row.StateOrProvince}|{NormalizeCityKey(row.City)}", StringComparer.OrdinalIgnoreCase)
                .Select(candidate => candidate
                    .OrderByDescending(row => row.HasPostalDetails)
                    .ThenByDescending(GetPreferredOfficeCityRank)
                    .ThenByDescending(row => row.Accuracy)
                    .ThenBy(row => IsLikelySubCityLocality(row) ? 1 : 0)
                    .ThenByDescending(row => row.Population)
                    .First())
                .OrderBy(row => IsAmbiguousLocality(row, duplicateCityCounts) ? 1 : 0)
                .ThenByDescending(row => row.HasPostalDetails)
                .ThenByDescending(GetPreferredOfficeCityRank)
                .ThenByDescending(row => row.Accuracy)
                .ThenBy(row => IsLikelySubCityLocality(row) ? 1 : 0)
                .ThenByDescending(row => row.Population)
                .ThenBy(row => row.City, StringComparer.OrdinalIgnoreCase)
                .ToList())
            .Where(group => group.Count > 0)
            .ToList();

        var selected = new List<CityRow>();
        var indices = new int[grouped.Count];

        while (selected.Count < officeCount && grouped.Count > 0)
        {
            var addedThisPass = false;
            for (var groupIndex = 0; groupIndex < grouped.Count && selected.Count < officeCount; groupIndex++)
            {
                if (indices[groupIndex] >= grouped[groupIndex].Count)
                {
                    continue;
                }

                selected.Add(grouped[groupIndex][indices[groupIndex]]);
                indices[groupIndex]++;
                addedThisPass = true;
            }

            if (!addedThisPass)
            {
                break;
            }
        }

        while (selected.Count < officeCount)
        {
            selected.Add(rows[selected.Count % rows.Count]);
        }

        return selected;
    }

    private static bool IsAmbiguousLocality(CityRow row, IReadOnlyDictionary<string, int> duplicateCityCounts)
    {
        var key = $"{row.Country}|{NormalizeCityKey(row.City)}";
        return duplicateCityCounts.TryGetValue(key, out var count) && count > 1;
    }

    private static int GetPreferredOfficeCityRank(CityRow row)
    {
        return PreferredOfficeCityRanks.TryGetValue($"{row.Country}|{NormalizeCityKey(row.City)}", out var rank)
            ? rank
            : 0;
    }

    private static List<CityRow> GetCityRows(CatalogSet catalogs, IReadOnlyCollection<string> countries)
    {
        var mergedLocalities = GetLocalityReferenceRows(catalogs, countries);
        if (mergedLocalities.Count > 0)
        {
            return mergedLocalities;
        }

        var curatedCities = GetCuratedCityRows(catalogs, countries);
        if (curatedCities.Count > 0)
        {
            return curatedCities;
        }

        var postalReferenceCities = GetPostalReferenceRows(catalogs, countries);
        if (postalReferenceCities.Count > 0)
        {
            return postalReferenceCities;
        }

        if (!catalogs.CsvCatalogs.TryGetValue("cities", out var rows))
        {
            return new();
        }

        return rows
            .Where(r => countries.Count == 0 || (r.TryGetValue("Country", out var c) && c is not null && countries.Contains(c)))
            .Select(r => new CityRow
            {
                Region = Read(r, "Region"),
                Country = Read(r, "Country"),
                StateOrProvince = Read(r, "State"),
                City = Read(r, "City"),
                PostalCode = Read(r, "PostalCode"),
                TimeZone = Read(r, "TimeZone"),
                Latitude = Read(r, "Latitude"),
                Longitude = Read(r, "Longitude")
            })
            .Where(r => !string.IsNullOrWhiteSpace(r.Country) && !string.IsNullOrWhiteSpace(r.City))
            .ToList();
    }

    private static List<CityRow> ApplyScenarioGeographyHints(
        List<CityRow> cityRows,
        ScenarioDefinition scenario,
        ScenarioCompanyDefinition companyDefinition,
        int officeCount)
    {
        if (cityRows.Count == 0
            || !string.Equals(scenario.GeographyProfile, "Regional-US", StringComparison.OrdinalIgnoreCase)
            || !companyDefinition.Countries.Contains("United States", StringComparer.OrdinalIgnoreCase))
        {
            return cityRows;
        }

        var hintedStates = ExtractUsStatesFromScenarioText(scenario.Description);
        if (hintedStates.Count == 0)
        {
            return cityRows;
        }

        var allowedStates = hintedStates
            .Take(Math.Max(1, officeCount))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var filtered = cityRows
            .Where(row => allowedStates.Contains(NormalizeStateName(row.StateOrProvince)))
            .ToList();

        if (filtered.Count == 0)
        {
            return cityRows;
        }

        return filtered
            .GroupBy(row => NormalizeStateName(row.StateOrProvince), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(row => row.HasPostalDetails)
                .ThenByDescending(row => row.Population)
                .ThenByDescending(row => row.Accuracy)
                .ThenBy(row => row.City, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(row => hintedStates.IndexOf(NormalizeStateName(row.StateOrProvince)))
            .ToList();
    }

    private static List<CityRow> GetLocalityReferenceRows(CatalogSet catalogs, IReadOnlyCollection<string> countries)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("locality_reference", out var rows))
        {
            return new();
        }

        var countryLookup = ReadCountryReferenceRows(catalogs);

        return rows
            .Where(r =>
            {
                var countryCode = Read(r, "CountryCode");
                countryLookup.TryGetValue(countryCode, out var countryReference);
                return countries.Count == 0 || CountryMatches(countries, countryReference?.Country ?? countryCode, countryCode);
            })
            .Select(r =>
            {
                var countryCode = Read(r, "CountryCode");
                countryLookup.TryGetValue(countryCode, out var countryReference);
                return new CityRow
                {
                    Region = countryReference?.Region ?? string.Empty,
                    Country = countryReference?.Country ?? countryCode,
                    StateOrProvince = FirstNonEmpty(Read(r, "StateOrProvince"), Read(r, "State")),
                    City = Read(r, "City"),
                    PostalCode = Read(r, "PostalCode"),
                    TimeZone = FirstNonEmpty(Read(r, "TimeZone"), InferTimeZone(countryReference?.Country ?? string.Empty)),
                    Latitude = Read(r, "Latitude"),
                    Longitude = Read(r, "Longitude"),
                    Population = ParsePopulation(Read(r, "Population")),
                    Accuracy = ParseInteger(Read(r, "Accuracy")),
                    HasPostalDetails = !string.IsNullOrWhiteSpace(Read(r, "PostalCode"))
                };
            })
            .Select(RepairCityRow)
            .Where(r => !string.IsNullOrWhiteSpace(r.Country) && !string.IsNullOrWhiteSpace(r.City))
            .Where(r => !IsLikelyLowFidelityLocality(r))
            .OrderByDescending(r => r.HasPostalDetails)
            .ThenByDescending(r => r.Population)
            .ThenByDescending(r => r.Accuracy)
            .ThenBy(r => r.Country, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.StateOrProvince, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.City, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CityRow> GetCuratedCityRows(CatalogSet catalogs, IReadOnlyCollection<string> countries)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("city_reference", out var rows))
        {
            return new();
        }

        var postalLookup = BuildPostalLookup(catalogs);

        return rows
            .Where(r => countries.Count == 0 || CountryMatches(countries, Read(r, "Country"), Read(r, "CountryCode")))
            .Select(r =>
            {
                var country = Read(r, "Country");
                var city = Read(r, "City");
                postalLookup.TryGetValue($"{country}|{NormalizeCityKey(city)}", out var postalMatch);

                return new CityRow
                {
                    Region = FirstNonEmpty(Read(r, "Region"), postalMatch?.Region),
                    Country = country,
                    StateOrProvince = FirstNonEmpty(postalMatch?.StateOrProvince, string.Empty),
                    City = FirstNonEmpty(postalMatch?.City, city),
                    PostalCode = FirstNonEmpty(postalMatch?.PostalCode, string.Empty),
                    TimeZone = FirstNonEmpty(postalMatch?.TimeZone, InferTimeZone(country)),
                    Latitude = FirstNonEmpty(Read(r, "Latitude"), postalMatch?.Latitude),
                    Longitude = FirstNonEmpty(Read(r, "Longitude"), postalMatch?.Longitude),
                    Population = ParsePopulation(Read(r, "Population")),
                    HasPostalDetails = postalMatch is not null && !string.IsNullOrWhiteSpace(postalMatch.PostalCode)
                };
            })
            .Select(RepairCityRow)
            .Where(r => !string.IsNullOrWhiteSpace(r.Country) && !string.IsNullOrWhiteSpace(r.City))
            .Where(r => !IsLikelyAdministrativeArea(r.City))
            .GroupBy(r => $"{r.Country}|{NormalizeCityKey(r.City)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(r => r.HasPostalDetails)
            .ThenByDescending(r => r.Population)
            .ThenBy(r => r.Country, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.City, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CityRow> GetPostalReferenceRows(CatalogSet catalogs, IReadOnlyCollection<string> countries)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("city_postal_reference", out var rows))
        {
            return new();
        }

        return rows
            .Where(r => countries.Count == 0 || CountryMatches(countries, Read(r, "Country"), Read(r, "CountryCode")))
            .Select(r => new CityRow
            {
                Region = Read(r, "Region"),
                Country = Read(r, "Country"),
                StateOrProvince = FirstNonEmpty(Read(r, "StateOrProvince"), Read(r, "State")),
                City = Read(r, "City"),
                PostalCode = Read(r, "PostalCode"),
                TimeZone = FirstNonEmpty(Read(r, "TimeZone"), InferTimeZone(Read(r, "Country"))),
                Latitude = Read(r, "Latitude"),
                Longitude = Read(r, "Longitude"),
                Accuracy = ParseInteger(Read(r, "Accuracy")),
                HasPostalDetails = !string.IsNullOrWhiteSpace(Read(r, "PostalCode"))
            })
            .Select(RepairCityRow)
            .Where(r => !string.IsNullOrWhiteSpace(r.Country) && !string.IsNullOrWhiteSpace(r.City))
            .Where(r => !IsLikelyAdministrativeArea(r.City))
            .OrderByDescending(r => r.Accuracy)
            .ThenBy(r => r.Country, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.StateOrProvince, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.City, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CityRow> GetFallbackCities(IReadOnlyCollection<string> countries)
    {
        var all = new List<CityRow>
        {
            new() { Region = "North America", Country = "United States", StateOrProvince = "Texas", City = "Dallas", PostalCode = "75201", TimeZone = "America/Chicago", Latitude = "32.7767", Longitude = "-96.7970", Population = 1304379 },
            new() { Region = "North America", Country = "United States", StateOrProvince = "California", City = "San Diego", PostalCode = "92101", TimeZone = "America/Los_Angeles", Latitude = "32.7157", Longitude = "-117.1611", Population = 1386932 },
            new() { Region = "North America", Country = "Mexico", StateOrProvince = "Jalisco", City = "Guadalajara", PostalCode = "44100", TimeZone = "America/Mexico_City", Latitude = "20.6597", Longitude = "-103.3496", Population = 1495182 },
            new() { Region = "South America", Country = "Brazil", StateOrProvince = "Sao Paulo", City = "Campinas", PostalCode = "13010-000", TimeZone = "America/Sao_Paulo", Latitude = "-22.9099", Longitude = "-47.0626", Population = 1213792 },
            new() { Region = "Europe", Country = "United Kingdom", StateOrProvince = "England", City = "Manchester", PostalCode = "M1 1AE", TimeZone = "Europe/London", Latitude = "53.4808", Longitude = "-2.2426", Population = 552858 },
            new() { Region = "Europe", Country = "Germany", StateOrProvince = "Hesse", City = "Frankfurt", PostalCode = "60311", TimeZone = "Europe/Berlin", Latitude = "50.1109", Longitude = "8.6821", Population = 773068 },
            new() { Region = "Africa", Country = "Nigeria", StateOrProvince = "Lagos", City = "Lagos", PostalCode = "100001", TimeZone = "Africa/Lagos", Latitude = "6.5244", Longitude = "3.3792", Population = 15388000 },
            new() { Region = "Africa", Country = "Kenya", StateOrProvince = "Nairobi County", City = "Nairobi", PostalCode = "00100", TimeZone = "Africa/Nairobi", Latitude = "-1.2864", Longitude = "36.8172", Population = 4397073 },
            new() { Region = "Asia", Country = "India", StateOrProvince = "Karnataka", City = "Bengaluru", PostalCode = "560001", TimeZone = "Asia/Kolkata", Latitude = "12.9716", Longitude = "77.5946", Population = 8443675 },
            new() { Region = "Asia", Country = "Japan", StateOrProvince = "Tokyo", City = "Tokyo", PostalCode = "100-0001", TimeZone = "Asia/Tokyo", Latitude = "35.6762", Longitude = "139.6503", Population = 13960000 }
        };

        return all.Where(c => countries.Count == 0 || countries.Contains(c.Country)).ToList();
    }

    private static CityRow RepairCityRow(CityRow row)
    {
        if (!NorthAmericaCityRepairs.TryGetValue($"{row.Country}|{NormalizeCityKey(row.City)}", out var repair))
        {
            return row with
            {
                HasPostalDetails = !string.IsNullOrWhiteSpace(row.PostalCode)
            };
        }

        var applyCanonicalRepair = ShouldApplyCanonicalCityRepair(row, repair);
        var repaired = row with
        {
            StateOrProvince = applyCanonicalRepair ? repair.StateOrProvince : FirstNonEmpty(row.StateOrProvince, repair.StateOrProvince),
            PostalCode = applyCanonicalRepair ? repair.PostalCode : FirstNonEmpty(row.PostalCode, repair.PostalCode),
            TimeZone = applyCanonicalRepair ? repair.TimeZone : FirstNonEmpty(row.TimeZone, repair.TimeZone),
            Latitude = applyCanonicalRepair ? repair.Latitude : FirstNonEmpty(row.Latitude, repair.Latitude),
            Longitude = applyCanonicalRepair ? repair.Longitude : FirstNonEmpty(row.Longitude, repair.Longitude)
        };

        return repaired with
        {
            HasPostalDetails = !string.IsNullOrWhiteSpace(repaired.PostalCode)
        };
    }

    private static bool ShouldApplyCanonicalCityRepair(CityRow row, CityRepairRow repair)
    {
        if (string.IsNullOrWhiteSpace(row.StateOrProvince)
            || string.IsNullOrWhiteSpace(row.PostalCode)
            || string.IsNullOrWhiteSpace(row.TimeZone))
        {
            return true;
        }

        if (!string.Equals(NormalizeCityKey(row.StateOrProvince), NormalizeCityKey(repair.StateOrProvince), StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(row.TimeZone.Trim(), repair.TimeZone, StringComparison.OrdinalIgnoreCase)
            && GetPreferredOfficeCityRank(row) > 0)
        {
            return true;
        }

        return false;
    }

    private static Dictionary<string, CityRow> BuildPostalLookup(CatalogSet catalogs)
    {
        if (catalogs.CsvCatalogs.TryGetValue("locality_reference", out var localityRows))
        {
            var countryLookup = ReadCountryReferenceRows(catalogs);
            return localityRows
                .Select(r =>
                {
                    var countryCode = Read(r, "CountryCode");
                    countryLookup.TryGetValue(countryCode, out var countryReference);
                    return new CityRow
                    {
                        Region = countryReference?.Region ?? string.Empty,
                        Country = countryReference?.Country ?? countryCode,
                        StateOrProvince = FirstNonEmpty(Read(r, "StateOrProvince"), Read(r, "State")),
                        City = Read(r, "City"),
                        PostalCode = Read(r, "PostalCode"),
                        TimeZone = FirstNonEmpty(Read(r, "TimeZone"), InferTimeZone(countryReference?.Country ?? string.Empty)),
                        Latitude = Read(r, "Latitude"),
                        Longitude = Read(r, "Longitude"),
                        Accuracy = ParseInteger(Read(r, "Accuracy")),
                        HasPostalDetails = !string.IsNullOrWhiteSpace(Read(r, "PostalCode"))
                    };
                })
                .Where(r => !string.IsNullOrWhiteSpace(r.Country) && !string.IsNullOrWhiteSpace(r.City))
                .Where(r => !IsLikelyLowFidelityLocality(r))
                .GroupBy(r => $"{r.Country}|{NormalizeCityKey(r.City)}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(row => row.HasPostalDetails)
                        .ThenByDescending(row => row.Accuracy)
                        .ThenBy(row => row.PostalCode.Length)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);
        }

        if (catalogs.CsvCatalogs.TryGetValue("city_postal_reference", out var postalRows))
        {
            return postalRows
                .Select(r => new CityRow
                {
                    Region = Read(r, "Region"),
                    Country = Read(r, "Country"),
                    StateOrProvince = FirstNonEmpty(Read(r, "StateOrProvince"), Read(r, "State")),
                    City = Read(r, "City"),
                    PostalCode = Read(r, "PostalCode"),
                    TimeZone = FirstNonEmpty(Read(r, "TimeZone"), InferTimeZone(Read(r, "Country"))),
                    Latitude = Read(r, "Latitude"),
                    Longitude = Read(r, "Longitude"),
                    Accuracy = ParseInteger(Read(r, "Accuracy")),
                    HasPostalDetails = !string.IsNullOrWhiteSpace(Read(r, "PostalCode"))
                })
                .Where(r => !string.IsNullOrWhiteSpace(r.Country) && !string.IsNullOrWhiteSpace(r.City))
                .GroupBy(r => $"{r.Country}|{NormalizeCityKey(r.City)}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(row => row.HasPostalDetails)
                        .ThenByDescending(row => row.Accuracy)
                        .ThenBy(row => row.PostalCode.Length)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);
        }

        if (!catalogs.CsvCatalogs.TryGetValue("cities", out var rows))
        {
            return new Dictionary<string, CityRow>(StringComparer.OrdinalIgnoreCase);
        }

        return rows
            .Select(r => new CityRow
            {
                Region = Read(r, "Region"),
                Country = Read(r, "Country"),
                StateOrProvince = Read(r, "State"),
                City = Read(r, "City"),
                PostalCode = Read(r, "PostalCode"),
                TimeZone = Read(r, "TimeZone"),
                Latitude = Read(r, "Latitude"),
                Longitude = Read(r, "Longitude")
            })
            .Where(r => !string.IsNullOrWhiteSpace(r.Country) && !string.IsNullOrWhiteSpace(r.City))
            .GroupBy(r => $"{r.Country}|{NormalizeCityKey(r.City)}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.FirstOrDefault(row => !string.IsNullOrWhiteSpace(row.PostalCode)) ?? group.First(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, CountryReferenceRow> ReadCountryReferenceRows(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("countries_reference", out var rows))
        {
            return new Dictionary<string, CountryReferenceRow>(StringComparer.OrdinalIgnoreCase);
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "Code")))
            .GroupBy(row => Read(row, "Code"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var row = group.First();
                    return new CountryReferenceRow(Read(row, "Name"), Read(row, "Continent"));
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool CountryMatches(IReadOnlyCollection<string> countries, string countryName, string countryCode)
        => countries.Contains(countryName)
           || countries.Contains(countryCode);

    private static long ParsePopulation(string value)
        => long.TryParse(value, out var parsed) ? parsed : 0L;

    private static int ParseInteger(string value)
        => int.TryParse(value, out var parsed) ? parsed : 0;

    private static bool IsLikelyAdministrativeArea(string city)
    {
        var normalized = city.Trim();
        return normalized.EndsWith(" County", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("County Of ", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" District", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Province", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Municipality", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Region", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Area", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("City Of ", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("City And Borough Of ", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Governorate", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Prefecture", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Oblast", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Rayon", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Qu", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Shi", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Xian", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Republic", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains(" Kingdom", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "England", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "Scotland", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "Wales", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "Northern Ireland", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyLowFidelityLocality(CityRow row)
    {
        if (IsLikelyAdministrativeArea(row.City))
        {
            return true;
        }

        var normalizedCity = row.City.Trim();
        if (normalizedCity.Length > 0 && char.IsDigit(normalizedCity[0]))
        {
            return true;
        }

        if (string.Equals(row.Country, "United Kingdom", StringComparison.OrdinalIgnoreCase)
            && row.Accuracy <= 0
            && string.IsNullOrWhiteSpace(row.StateOrProvince)
            && string.IsNullOrWhiteSpace(row.PostalCode))
        {
            return true;
        }

        if (IsLikelySubCityLocality(row) && GetPreferredOfficeCityRank(row) == 0 && row.Population <= 0)
        {
            return true;
        }

        if ((string.Equals(row.Country, "Canada", StringComparison.OrdinalIgnoreCase)
             || string.Equals(row.Country, "Mexico", StringComparison.OrdinalIgnoreCase))
            && GetPreferredOfficeCityRank(row) == 0
            && row.Population <= 0)
        {
            return true;
        }

        return false;
    }

    private static bool IsLikelySubCityLocality(CityRow row)
    {
        var city = row.City.Trim();
        if (string.IsNullOrWhiteSpace(city))
        {
            return false;
        }

        if (city.Contains('(') || city.Contains(')'))
        {
            return true;
        }

        if (SubCityDirectionalPattern.IsMatch(city))
        {
            return true;
        }

        if ((string.Equals(row.Country, "Canada", StringComparison.OrdinalIgnoreCase)
             || string.Equals(row.Country, "Mexico", StringComparison.OrdinalIgnoreCase))
            && city.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3
            && GetPreferredOfficeCityRank(row) == 0
            && row.Population <= 0)
        {
            return true;
        }

        return false;
    }

    private static string NormalizeCityKey(string value)
    {
        var normalized = RemoveDiacritics(value.Trim()).ToLowerInvariant();
        if (normalized.EndsWith(" city", StringComparison.Ordinal))
        {
            normalized = normalized[..^5];
        }

        return normalized.Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal);
    }

    private static string RemoveDiacritics(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var chars = normalized
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(chars).Normalize(NormalizationForm.FormC);
    }

    private static List<string> ExtractUsStatesFromScenarioText(string? text)
    {
        var matches = new List<(string State, int Index)>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new();
        }

        foreach (var state in UsStateNames)
        {
            var match = Regex.Match(text, $@"\b{Regex.Escape(state)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                matches.Add((state, match.Index));
            }
        }

        return matches
            .OrderBy(item => item.Index)
            .Select(item => item.State)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeStateName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return UsStateAliases.TryGetValue(trimmed, out var canonical)
            ? canonical
            : trimmed;
    }

    private static List<string> ReadCatalogValues(CatalogSet catalogs, string catalogName, string field, IEnumerable<string> fallback)
    {
        if (catalogs.CsvCatalogs.TryGetValue(catalogName, out var rows))
        {
            var values = rows
                .Where(row => row.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value))
                .Select(row => row[field]!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (values.Count > 0)
            {
                return values;
            }
        }

        return fallback.ToList();
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private string ApplyPhonePattern(string pattern, string dialCode)
    {
        var effectivePattern = string.IsNullOrWhiteSpace(pattern)
            ? $"+{dialCode} XX XXXX XXXX"
            : pattern.Replace("{DialCode}", dialCode, StringComparison.OrdinalIgnoreCase);

        if (!effectivePattern.Contains($"+{dialCode}", StringComparison.Ordinal))
        {
            effectivePattern = effectivePattern.Replace("+1", $"+{dialCode}", StringComparison.Ordinal);
        }

        var digits = new Queue<char>(Enumerable.Range(0, 32).Select(_ => (char)('0' + _randomSource.Next(10))));
        effectivePattern = ReplacePhoneToken(effectivePattern, "NPA", digits, firstDigit: '2', lastDigit: '9');
        effectivePattern = ReplacePhoneToken(effectivePattern, "NXX", digits, firstDigit: '2', lastDigit: '9');
        var result = new char[effectivePattern.Length];
        for (var i = 0; i < effectivePattern.Length; i++)
        {
            var character = effectivePattern[i];
            result[i] = character is 'X' or 'N'
                ? digits.Dequeue()
                : character;
        }

        return new string(result);
    }

    private string ReplacePhoneToken(string pattern, string token, Queue<char> digits, char firstDigit, char lastDigit)
    {
        while (pattern.Contains(token, StringComparison.Ordinal))
        {
            var replacement = string.Create(3, (Generator: this, Digits: digits, FirstDigit: firstDigit, LastDigit: lastDigit), static (buffer, state) =>
            {
                buffer[0] = (char)('0' + state.Generator._randomSource.Next(state.FirstDigit - '0', state.LastDigit - '0' + 1));
                buffer[1] = state.Digits.Dequeue();
                buffer[2] = state.Digits.Dequeue();
            });
            pattern = pattern.Replace(token, replacement, StringComparison.Ordinal);
        }

        return pattern;
    }

    private string BuildUnitedKingdomBusinessPhone(string city)
    {
        var citySpecificAreaCode = city.Trim() switch
        {
            var value when value.Equals("London", StringComparison.OrdinalIgnoreCase) => "20",
            var value when value.Equals("Birmingham", StringComparison.OrdinalIgnoreCase) => "121",
            var value when value.Equals("Liverpool", StringComparison.OrdinalIgnoreCase) => "151",
            var value when value.Equals("Manchester", StringComparison.OrdinalIgnoreCase) => "161",
            var value when value.Equals("Leeds", StringComparison.OrdinalIgnoreCase) => "113",
            var value when value.Equals("Sheffield", StringComparison.OrdinalIgnoreCase) => "114",
            var value when value.Equals("Nottingham", StringComparison.OrdinalIgnoreCase) => "115",
            var value when value.Equals("Leicester", StringComparison.OrdinalIgnoreCase) => "116",
            var value when value.Equals("Bristol", StringComparison.OrdinalIgnoreCase) => "117",
            var value when value.Equals("Reading", StringComparison.OrdinalIgnoreCase) => "118",
            var value when value.Equals("Edinburgh", StringComparison.OrdinalIgnoreCase) => "131",
            var value when value.Equals("Glasgow", StringComparison.OrdinalIgnoreCase) => "141",
            var value when value.Equals("Newcastle", StringComparison.OrdinalIgnoreCase) => "191",
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(citySpecificAreaCode))
        {
            return citySpecificAreaCode.Length == 2
                ? $"+44 {citySpecificAreaCode} {_randomSource.Next(1000, 9999):0000} {_randomSource.Next(1000, 9999):0000}"
                : $"+44 {citySpecificAreaCode} {_randomSource.Next(100, 999):000} {_randomSource.Next(1000, 9999):0000}";
        }

        var businessAreaCodes = new[]
        {
            "20", "121", "131", "141", "151", "161", "191", "113", "114", "115", "116", "117", "118"
        };

        var areaCode = businessAreaCodes[_randomSource.Next(businessAreaCodes.Length)];
        return areaCode.Length switch
        {
            2 => $"+44 {areaCode} {_randomSource.Next(1000, 9999):0000} {_randomSource.Next(1000, 9999):0000}",
            _ => $"+44 {areaCode} {_randomSource.Next(100, 999):000} {_randomSource.Next(1000, 9999):0000}"
        };
    }

    private static string InferTimeZone(string country)
        => country switch
        {
            "United States" => "America/Chicago",
            "Canada" => "America/Toronto",
            "Mexico" => "America/Mexico_City",
            "United Kingdom" => "Europe/London",
            "Germany" => "Europe/Berlin",
            "France" => "Europe/Paris",
            "India" => "Asia/Kolkata",
            "Japan" => "Asia/Tokyo",
            "Brazil" => "America/Sao_Paulo",
            _ => string.Empty
        };

    private static string Read(Dictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) ? value ?? "" : "";

    private sealed record CityRow
    {
        public string Region { get; init; } = "";
        public string Country { get; init; } = "";
        public string StateOrProvince { get; init; } = "";
        public string City { get; init; } = "";
        public string PostalCode { get; init; } = "";
        public string TimeZone { get; init; } = "";
        public string Latitude { get; init; } = "";
        public string Longitude { get; init; } = "";
        public long Population { get; init; }
        public int Accuracy { get; init; }
        public bool HasPostalDetails { get; init; }
    }

    private sealed record CityRepairRow(
        string StateOrProvince,
        string PostalCode,
        string TimeZone,
        string Latitude,
        string Longitude);

    private sealed record CountryIdentityRule(
        string DialCode,
        string PrimaryDomainSuffix,
        string AlternateDomainSuffix,
        string PhonePattern);

    private sealed record CountryReferenceRow(string Country, string Region);

    private static readonly string[] UsStateNames =
    {
        "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut", "Delaware",
        "Florida", "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa", "Kansas", "Kentucky",
        "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan", "Minnesota", "Mississippi",
        "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", "New Jersey", "New Mexico",
        "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania",
        "Rhode Island", "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah", "Vermont",
        "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming"
    };

    private static readonly IReadOnlyDictionary<string, CityRepairRow> NorthAmericaCityRepairs =
        new Dictionary<string, CityRepairRow>(StringComparer.OrdinalIgnoreCase)
        {
            ["Canada|quebec"] = new("Quebec", "G1A", "America/Toronto", "46.8139", "-71.2080"),
            ["Canada|montreal"] = new("Quebec", "H2Y", "America/Toronto", "45.5019", "-73.5674"),
            ["Canada|toronto"] = new("Ontario", "M5H", "America/Toronto", "43.6532", "-79.3832"),
            ["Mexico|mexico"] = new("Ciudad de Mexico", "06000", "America/Mexico_City", "19.4326", "-99.1332"),
            ["Mexico|iztapalapa"] = new("Ciudad de Mexico", "09000", "America/Mexico_City", "19.3574", "-99.0926"),
            ["Mexico|ecatepec"] = new("Estado de Mexico", "55000", "America/Mexico_City", "19.6018", "-99.0507"),
            ["Mexico|guadalajara"] = new("Jalisco", "44100", "America/Mexico_City", "20.6597", "-103.3496"),
            ["Mexico|naucalpan"] = new("Estado de Mexico", "53370", "America/Mexico_City", "19.4785", "-99.2396"),
            ["Mexico|naucalpan de juarez"] = new("Estado de Mexico", "53370", "America/Mexico_City", "19.4785", "-99.2396"),
            ["Mexico|santa maria chimalhuacan"] = new("Estado de Mexico", "56330", "America/Mexico_City", "19.4216", "-98.9504"),
            ["Mexico|tijuana"] = new("Baja California", "22000", "America/Tijuana", "32.5149", "-117.0382"),
            ["Mexico|mexicali"] = new("Baja California", "21000", "America/Tijuana", "32.6245", "-115.4523")
        };

    private static readonly IReadOnlyDictionary<string, int> PreferredOfficeCityRanks =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Canada|toronto"] = 100,
            ["Canada|montreal"] = 95,
            ["Canada|vancouver"] = 90,
            ["Canada|calgary"] = 85,
            ["Canada|ottawa"] = 80,
            ["Canada|edmonton"] = 75,
            ["Canada|quebec"] = 70,
            ["Canada|mississauga"] = 65,
            ["Mexico|mexico"] = 100,
            ["Mexico|guadalajara"] = 95,
            ["Mexico|monterrey"] = 90,
            ["Mexico|tijuana"] = 85,
            ["Mexico|mexicali"] = 80,
            ["Mexico|puebla"] = 75,
            ["Mexico|queretaro"] = 70,
            ["Mexico|leon"] = 65,
            ["Mexico|merida"] = 60,
            ["Mexico|chihuahua"] = 55,
            ["Mexico|toluca"] = 50,
            ["Mexico|naucalpan"] = 45,
            ["Mexico|naucalpan de juarez"] = 45,
            ["Mexico|ecatepec"] = 40
        };

    private static readonly Regex SubCityDirectionalPattern =
        new(@"\b(Central|East|West|North|South|Northeast|Northwest|Southeast|Southwest)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, string> UsStateAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AL"] = "Alabama",
            ["AK"] = "Alaska",
            ["AZ"] = "Arizona",
            ["AR"] = "Arkansas",
            ["CA"] = "California",
            ["CO"] = "Colorado",
            ["CT"] = "Connecticut",
            ["DE"] = "Delaware",
            ["FL"] = "Florida",
            ["GA"] = "Georgia",
            ["HI"] = "Hawaii",
            ["ID"] = "Idaho",
            ["IL"] = "Illinois",
            ["IN"] = "Indiana",
            ["IA"] = "Iowa",
            ["KS"] = "Kansas",
            ["KY"] = "Kentucky",
            ["LA"] = "Louisiana",
            ["ME"] = "Maine",
            ["MD"] = "Maryland",
            ["MA"] = "Massachusetts",
            ["MI"] = "Michigan",
            ["MN"] = "Minnesota",
            ["MS"] = "Mississippi",
            ["MO"] = "Missouri",
            ["MT"] = "Montana",
            ["NE"] = "Nebraska",
            ["NV"] = "Nevada",
            ["NH"] = "New Hampshire",
            ["NJ"] = "New Jersey",
            ["NM"] = "New Mexico",
            ["NY"] = "New York",
            ["NC"] = "North Carolina",
            ["ND"] = "North Dakota",
            ["OH"] = "Ohio",
            ["OK"] = "Oklahoma",
            ["OR"] = "Oregon",
            ["PA"] = "Pennsylvania",
            ["RI"] = "Rhode Island",
            ["SC"] = "South Carolina",
            ["SD"] = "South Dakota",
            ["TN"] = "Tennessee",
            ["TX"] = "Texas",
            ["UT"] = "Utah",
            ["VT"] = "Vermont",
            ["VA"] = "Virginia",
            ["WA"] = "Washington",
            ["WV"] = "West Virginia",
            ["WI"] = "Wisconsin",
            ["WY"] = "Wyoming"
        };
}
