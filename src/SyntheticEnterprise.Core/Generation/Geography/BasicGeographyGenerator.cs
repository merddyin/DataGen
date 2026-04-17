namespace SyntheticEnterprise.Core.Generation.Geography;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

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
                    StreetName = BuildStreetName(city.City, streetSuffixes),
                    FloorOrSuite = $"Suite {100 + _randomSource.Next(1, 30)}",
                    BusinessPhone = BuildBusinessPhone(city.Country, countryIdentityRules, countryDialCodes),
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

    private string BuildStreetName(string city, IReadOnlyList<string> suffixes)
    {
        var safeCity = new string(city.Where(char.IsLetter).ToArray());
        var stem = string.IsNullOrWhiteSpace(safeCity) ? "Enterprise" : $"{safeCity} Center";
        var suffix = suffixes.Count == 0 ? "Way" : suffixes[_randomSource.Next(suffixes.Count)];
        return $"{stem} {suffix}";
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
        IReadOnlyDictionary<string, CountryIdentityRule> countryRules,
        IReadOnlyDictionary<string, string> countryDialCodes)
    {
        if (countryRules.TryGetValue(country, out var rule))
        {
            var dialCode = string.IsNullOrWhiteSpace(rule.DialCode)
                ? (countryDialCodes.TryGetValue(country, out var fallbackDialCode) ? fallbackDialCode : "1")
                : rule.DialCode;
            return ApplyPhonePattern(rule.PhonePattern, dialCode);
        }

        return BuildBusinessPhone(country, countryDialCodes);
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

        var grouped = rows
            .GroupBy(row => row.Country, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .GroupBy(row => $"{row.Country}|{row.StateOrProvince}|{NormalizeCityKey(row.City)}", StringComparer.OrdinalIgnoreCase)
                .Select(candidate => candidate.First())
                .OrderByDescending(row => row.Population)
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
            .Where(r => !string.IsNullOrWhiteSpace(r.Country) && !string.IsNullOrWhiteSpace(r.City))
            .Where(r => !IsLikelyAdministrativeArea(r.City))
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
               || normalized.EndsWith(" District", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Province", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Municipality", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Region", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(" Area", StringComparison.OrdinalIgnoreCase)
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

    private static string NormalizeCityKey(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.EndsWith(" city", StringComparison.Ordinal))
        {
            normalized = normalized[..^5];
        }

        return normalized.Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal);
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

    private sealed record CountryIdentityRule(
        string DialCode,
        string PrimaryDomainSuffix,
        string AlternateDomainSuffix,
        string PhonePattern);

    private sealed record CountryReferenceRow(string Country, string Region);
}
