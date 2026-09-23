namespace SyntheticEnterprise.Core.Generation.Organization;

using SyntheticEnterprise.Contracts.Abstractions;

/// <summary>
/// Derives the primary DNS domain a scenario company resolves to. Every directory naming context,
/// and therefore every distinguished name, hangs off this value, so scenario validation and
/// generation must agree on it exactly: both call this type rather than deriving it again.
/// </summary>
public static class CompanyPrimaryDomainResolver
{
    public static string ResolvePrimaryCountry(IReadOnlyCollection<string> countries)
        => (countries.Count == 0
            ? new[] { "United States" }
            : countries.Distinct(StringComparer.OrdinalIgnoreCase)).FirstOrDefault() ?? "United States";

    public static string Resolve(string companyName, string primaryCountry, CatalogSet catalogs)
    {
        var slug = new string(companyName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "example";
        }

        var availableSuffixes = ReadCatalogValues(catalogs, "domain_suffixes", "Value", new[] { "com", "net", "org" })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().TrimStart('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (availableSuffixes.Count == 0)
        {
            availableSuffixes.Add("com");
        }

        var preferredSuffix = ResolvePreferredDomainSuffix(primaryCountry, catalogs);
        if (string.IsNullOrWhiteSpace(preferredSuffix))
        {
            preferredSuffix = "com";
        }
        else if (!CountryHasIdentityRule(primaryCountry, catalogs)
            && !availableSuffixes.Contains(preferredSuffix, StringComparer.OrdinalIgnoreCase)
            && preferredSuffix.Contains('.'))
        {
            var lastLabel = preferredSuffix.Split('.').Last();
            if (availableSuffixes.Contains(lastLabel, StringComparer.OrdinalIgnoreCase))
            {
                preferredSuffix = lastLabel;
            }
        }

        if (!CountryHasIdentityRule(primaryCountry, catalogs)
            && !availableSuffixes.Contains(preferredSuffix, StringComparer.OrdinalIgnoreCase)
            && !preferredSuffix.Contains('.'))
        {
            preferredSuffix = availableSuffixes.Contains("com", StringComparer.OrdinalIgnoreCase)
                ? "com"
                : availableSuffixes[GetDeterministicIndex(primaryCountry, availableSuffixes.Count)];
        }

        return $"{slug}.{preferredSuffix}";
    }

    private static string ResolvePreferredDomainSuffix(string primaryCountry, CatalogSet catalogs)
    {
        if (catalogs.CsvCatalogs.TryGetValue("country_identity_rules", out var rows))
        {
            var match = rows.FirstOrDefault(row =>
                string.Equals(Read(row, "Country"), primaryCountry, StringComparison.OrdinalIgnoreCase));
            var configured = match is null ? string.Empty : Read(match, "PrimaryDomainSuffix");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim().TrimStart('.');
            }
        }

        return primaryCountry switch
        {
            "United Kingdom" => "co.uk",
            "Australia" => "com.au",
            "New Zealand" => "co.nz",
            "Japan" => "co.jp",
            "Germany" => "de",
            "France" => "fr",
            "Canada" => "ca",
            "India" => "in",
            "Mexico" => "mx",
            "Brazil" => "com.br",
            _ => "com"
        };
    }

    private static bool CountryHasIdentityRule(string primaryCountry, CatalogSet catalogs)
        => catalogs.CsvCatalogs.TryGetValue("country_identity_rules", out var rows)
           && rows.Any(row => string.Equals(Read(row, "Country"), primaryCountry, StringComparison.OrdinalIgnoreCase));

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

    private static string Read(IReadOnlyDictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

    private static int GetDeterministicIndex(string seed, int count)
    {
        if (count <= 1)
        {
            return 0;
        }

        unchecked
        {
            var hash = 17;
            foreach (var character in seed)
            {
                hash = (hash * 31) + char.ToUpperInvariant(character);
            }

            return Math.Abs(hash % count);
        }
    }
}
