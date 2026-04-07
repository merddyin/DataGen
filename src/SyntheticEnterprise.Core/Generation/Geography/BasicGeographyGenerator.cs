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
                    StreetName = BuildStreetName(city.City),
                    FloorOrSuite = $"Suite {100 + _randomSource.Next(1, 30)}",
                    Latitude = companyDefinition.IncludeGeocodes ? city.Latitude : null,
                    Longitude = companyDefinition.IncludeGeocodes ? city.Longitude : null,
                    Geocoded = companyDefinition.IncludeGeocodes
                };

                createdOffices.Add(office);
                world.Offices.Add(office);
            }

            AssignPeopleToOffices(world, company.Id, createdOffices);
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

    private static string BuildStreetName(string city)
    {
        var safeCity = new string(city.Where(char.IsLetter).ToArray());
        return string.IsNullOrWhiteSpace(safeCity) ? "Enterprise Way" : $"{safeCity} Center Blvd";
    }

    private static List<CityRow> SelectCities(IReadOnlyList<CityRow> rows, int officeCount)
    {
        var selected = new List<CityRow>();
        for (var i = 0; i < officeCount; i++)
        {
            selected.Add(rows[i % rows.Count]);
        }
        return selected;
    }

    private static List<CityRow> GetCityRows(CatalogSet catalogs, IReadOnlyCollection<string> countries)
    {
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

    private static List<CityRow> GetFallbackCities(IReadOnlyCollection<string> countries)
    {
        var all = new List<CityRow>
        {
            new() { Region = "North America", Country = "United States", StateOrProvince = "Texas", City = "Dallas", PostalCode = "75201", TimeZone = "America/Chicago", Latitude = "32.7767", Longitude = "-96.7970" },
            new() { Region = "North America", Country = "United States", StateOrProvince = "California", City = "San Diego", PostalCode = "92101", TimeZone = "America/Los_Angeles", Latitude = "32.7157", Longitude = "-117.1611" },
            new() { Region = "North America", Country = "Mexico", StateOrProvince = "Jalisco", City = "Guadalajara", PostalCode = "44100", TimeZone = "America/Mexico_City", Latitude = "20.6597", Longitude = "-103.3496" },
            new() { Region = "South America", Country = "Brazil", StateOrProvince = "Sao Paulo", City = "Campinas", PostalCode = "13010-000", TimeZone = "America/Sao_Paulo", Latitude = "-22.9099", Longitude = "-47.0626" },
            new() { Region = "Europe", Country = "United Kingdom", StateOrProvince = "England", City = "Manchester", PostalCode = "M1 1AE", TimeZone = "Europe/London", Latitude = "53.4808", Longitude = "-2.2426" },
            new() { Region = "Europe", Country = "Germany", StateOrProvince = "Hesse", City = "Frankfurt", PostalCode = "60311", TimeZone = "Europe/Berlin", Latitude = "50.1109", Longitude = "8.6821" },
            new() { Region = "Africa", Country = "Nigeria", StateOrProvince = "Lagos", City = "Lagos", PostalCode = "100001", TimeZone = "Africa/Lagos", Latitude = "6.5244", Longitude = "3.3792" },
            new() { Region = "Africa", Country = "Kenya", StateOrProvince = "Nairobi County", City = "Nairobi", PostalCode = "00100", TimeZone = "Africa/Nairobi", Latitude = "-1.2864", Longitude = "36.8172" },
            new() { Region = "Asia", Country = "India", StateOrProvince = "Karnataka", City = "Bengaluru", PostalCode = "560001", TimeZone = "Asia/Kolkata", Latitude = "12.9716", Longitude = "77.5946" },
            new() { Region = "Asia", Country = "Japan", StateOrProvince = "Tokyo", City = "Tokyo", PostalCode = "100-0001", TimeZone = "Asia/Tokyo", Latitude = "35.6762", Longitude = "139.6503" }
        };

        return all.Where(c => countries.Count == 0 || countries.Contains(c.Country)).ToList();
    }

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
    }
}
