namespace SyntheticEnterprise.Core.Catalogs;

using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

public static class CatalogSqliteDatabaseBuilder
{
    public static void Build(string outputPath, IEnumerable<string> sourceRoots, bool includeUncuratedSources = false)
    {
        SQLitePCL.Batteries_V2.Init();

        var normalizedRoots = sourceRoots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var manifest = LoadManifest(normalizedRoots);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using var connection = new SqliteConnection($"Data Source={outputPath}");
        connection.Open();

        CreateMetadataTables(connection, manifest.Version);

        var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processedCatalogNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in manifest.Tables)
        {
            ImportCuratedTable(connection, normalizedRoots, definition, processedFiles, processedCatalogNames);
        }

        if (!includeUncuratedSources)
        {
            return;
        }

        foreach (var sourceRoot in normalizedRoots)
        {
            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.csv", SearchOption.AllDirectories))
            {
                if (processedFiles.Contains(Path.GetFullPath(file)))
                {
                    continue;
                }

                var tableName = Path.GetFileNameWithoutExtension(file);
                if (!processedCatalogNames.Add(tableName))
                {
                    continue;
                }

                ImportCsv(connection, sourceRoot, file, strategy: "copy_csv");
            }

            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.json", SearchOption.AllDirectories))
            {
                if (processedFiles.Contains(Path.GetFullPath(file))
                    || string.Equals(Path.GetFileName(file), FileSystemCatalogLoader.ManifestFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var catalogName = Path.GetFileNameWithoutExtension(file);
                if (!processedCatalogNames.Add(catalogName))
                {
                    continue;
                }

                ImportJson(connection, sourceRoot, file, strategy: "copy_json");
            }
        }
    }

    private static CatalogImportManifest LoadManifest(IReadOnlyList<string> sourceRoots)
    {
        foreach (var sourceRoot in sourceRoots)
        {
            var manifestPath = Path.Combine(sourceRoot, FileSystemCatalogLoader.ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var manifest = JsonSerializer.Deserialize<CatalogImportManifest>(FileSystemCatalogLoader.ReadAllTextShared(manifestPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (manifest is not null)
            {
                return manifest;
            }
        }

        return new CatalogImportManifest
        {
            Version = "2026.04",
            Tables = new()
            {
                new() { TableName = "first_names_country", Strategy = "copy_csv", SourceFiles = new() { "first_names_country.csv" } },
                new() { TableName = "last_names_country", Strategy = "copy_csv", SourceFiles = new() { "last_names_country.csv" } },
                new() { TableName = "departments", Strategy = "line_list_name", SourceFiles = new() { "departments.txt" } },
                new() { TableName = "titles", Strategy = "titles_merge", SourceFiles = new() { "deptjobtitles.csv", "generaltitles.csv" } },
                new() { TableName = "industries", Strategy = "industry_taxonomy", SourceFiles = new() { "industries.csv" } },
                new() { TableName = "company_size_bands", Strategy = "company_size_bands", SourceFiles = new() { "companysize.csv" } },
                new() { TableName = "continents", Strategy = "copy_csv", SourceFiles = new() { "continents.csv" } },
                new() { TableName = "countries_reference", Strategy = "country_reference", SourceFiles = new() { "countries.csv" } },
                new() { TableName = "country_identity_rules", Strategy = "country_identity_rules", SourceFiles = new() { "countries.csv" } },
                new() { TableName = "locality_reference", Strategy = "locality_reference", SourceFiles = new() { "PostalCodes", "countries.csv", "CitiesByCountry-Filt.csv" } },
                new() { TableName = "company_name_elements", Strategy = "company_terms", SourceFiles = new() { "companynameelements.csv" } },
                new() { TableName = "first_names_gendered", Strategy = "first_names_gendered", SourceFiles = new() { "firstnames.csv" } },
                new() { TableName = "given_names_male", Strategy = "line_list_name", SourceFiles = new() { "given-male.txt" } },
                new() { TableName = "given_names_female", Strategy = "line_list_name", SourceFiles = new() { "given-female.txt" } },
                new() { TableName = "surnames_reference", Strategy = "line_list_value", SourceFiles = new() { "surnames.txt" } },
                new() { TableName = "company_suffixes", Strategy = "line_list_value", SourceFiles = new() { "companysuffix.txt" } },
                new() { TableName = "domain_suffixes", Strategy = "line_list_value", SourceFiles = new() { "domainsuffix.txt" } },
                new() { TableName = "street_suffixes", Strategy = "line_list_value", SourceFiles = new() { "streetsuffix.txt" } },
                new() { TableName = "taglines", Strategy = "line_list_value", SourceFiles = new() { "tagline.txt" } },
                new() { TableName = "application_templates", Strategy = "copy_csv", SourceFiles = new() { "application_templates.csv" } },
                new() { TableName = "application_suite_templates", Strategy = "copy_csv", SourceFiles = new() { "application_suite_templates.csv" } },
                new() { TableName = "industry_process_templates", Strategy = "copy_csv", SourceFiles = new() { "industry_process_templates.csv" } },
                new() { TableName = "organization_templates", Strategy = "copy_csv", SourceFiles = new() { "organization_templates.csv" } },
                new() { TableName = "vendor_reference", Strategy = "copy_csv", SourceFiles = new() { "vendor_reference.csv" } },
                new() { TableName = "fake_companies_reference", Strategy = "fake_company_reference", SourceFiles = new() { "fake_companies_reference.csv", "faux_id_fake_companies.csv" } }
            }
        };
    }

    private static void CreateMetadataTables(SqliteConnection connection, string manifestVersion)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE "__catalog_build"
            (
                built_at_utc TEXT NOT NULL,
                version TEXT NOT NULL,
                manifest_version TEXT NULL
            );

            CREATE TABLE "__catalog_source"
            (
                catalog_name TEXT NOT NULL,
                source_file TEXT NOT NULL,
                source_root TEXT NOT NULL,
                source_kind TEXT NOT NULL,
                strategy TEXT NULL
            );

            CREATE TABLE "json_catalogs"
            (
                catalog_name TEXT NOT NULL PRIMARY KEY,
                source_file TEXT NOT NULL,
                json_payload TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        using var insertBuild = connection.CreateCommand();
        insertBuild.CommandText = """
            INSERT INTO "__catalog_build" (built_at_utc, version, manifest_version)
            VALUES ($builtAt, $version, $manifestVersion)
            """;
        insertBuild.Parameters.AddWithValue("$builtAt", DateTimeOffset.UtcNow.ToString("O"));
        insertBuild.Parameters.AddWithValue("$version", "2");
        insertBuild.Parameters.AddWithValue("$manifestVersion", manifestVersion);
        insertBuild.ExecuteNonQuery();
    }

    private static void ImportCuratedTable(SqliteConnection connection, IReadOnlyList<string> sourceRoots, CatalogImportDefinition definition, HashSet<string> processedFiles, HashSet<string> processedCatalogNames)
    {
        var sources = ResolveSources(sourceRoots, definition.SourceFiles);
        if (sources.Count == 0 || !processedCatalogNames.Add(definition.TableName))
        {
            return;
        }

        switch (definition.Strategy.ToLowerInvariant())
        {
            case "postal_codes_to_cities":
                ImportCities(connection, definition.TableName, sourceRoots, sources[0], definition.Strategy);
                processedFiles.Add(sources[0].FullPath);
                break;
            case "copy_csv":
                ImportCsv(connection, sources[0].SourceRoot, sources[0].FullPath, definition.TableName, definition.Strategy);
                processedFiles.Add(sources[0].FullPath);
                break;
            case "line_list_name":
                ImportLineList(connection, definition.TableName, "Name", sources[0], definition.Strategy);
                processedFiles.Add(sources[0].FullPath);
                break;
            case "line_list_value":
                ImportLineList(connection, definition.TableName, "Value", sources[0], definition.Strategy);
                processedFiles.Add(sources[0].FullPath);
                break;
            case "titles_merge":
                ImportTitles(connection, definition.TableName, sources);
                foreach (var source in sources) processedFiles.Add(source.FullPath);
                break;
            case "industry_taxonomy":
                ImportIndustryTaxonomy(connection, definition.TableName, sources[0], definition.Strategy);
                processedFiles.Add(sources[0].FullPath);
                break;
            case "company_size_bands":
                ImportCompanySizeBands(connection, definition.TableName, sources[0], definition.Strategy);
                processedFiles.Add(sources[0].FullPath);
                break;
            case "company_terms":
                ImportCompanyTerms(connection, definition.TableName, sources[0], definition.Strategy);
                processedFiles.Add(sources[0].FullPath);
                break;
            case "country_reference":
                ImportCountryReference(connection, definition.TableName, sources[0], definition.Strategy);
                processedFiles.Add(sources[0].FullPath);
                break;
            case "country_identity_rules":
                ImportCountryIdentityRules(connection, definition.TableName, sources[0], definition.Strategy);
                processedFiles.Add(sources[0].FullPath);
                break;
            case "city_population_reference":
                ImportCityPopulationReference(connection, definition.TableName, sourceRoots, sources[0], definition.Strategy);
                processedFiles.Add(sources[0].FullPath);
                break;
            case "city_postal_reference":
                ImportCityPostalReference(connection, definition.TableName, sourceRoots, sources, definition.Strategy);
                foreach (var source in sources)
                {
                    processedFiles.Add(source.FullPath);
                }

                break;
            case "locality_reference":
                ImportLocalityReference(connection, definition.TableName, sourceRoots, sources, definition.Strategy);
                foreach (var source in sources)
                {
                    processedFiles.Add(source.FullPath);
                }

                break;
            case "first_names_gendered":
                ImportFirstNamesGendered(connection, definition.TableName, sources[0], definition.Strategy);
                processedFiles.Add(sources[0].FullPath);
                break;
            case "fake_company_reference":
                ImportFakeCompanies(connection, definition.TableName, sources[0], definition.Strategy);
                processedFiles.Add(sources[0].FullPath);
                break;
        }
    }

    private static List<ResolvedCatalogSource> ResolveSources(IReadOnlyList<string> sourceRoots, IReadOnlyList<string> sourceFiles)
    {
        var results = new List<ResolvedCatalogSource>();
        foreach (var sourceFile in sourceFiles)
        {
            foreach (var sourceRoot in sourceRoots)
            {
                var directPath = Path.Combine(sourceRoot, sourceFile);
                if (File.Exists(directPath) || Directory.Exists(directPath))
                {
                    results.Add(new ResolvedCatalogSource(sourceRoot, Path.GetFullPath(directPath)));
                    break;
                }

                var match = Directory.EnumerateFiles(sourceRoot, sourceFile, SearchOption.AllDirectories).FirstOrDefault();
                if (match is null)
                {
                    match = Directory.EnumerateDirectories(sourceRoot, sourceFile, SearchOption.AllDirectories).FirstOrDefault();
                }

                if (match is not null)
                {
                    results.Add(new ResolvedCatalogSource(sourceRoot, Path.GetFullPath(match)));
                    break;
                }
            }
        }

        return results;
    }

    private static void ImportCityPostalReference(SqliteConnection connection, string tableName, IReadOnlyList<string> sourceRoots, IReadOnlyList<ResolvedCatalogSource> sources, string strategy)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"""
                CREATE TABLE "{tableName}"
                (
                    "Region" TEXT NULL,
                    "Country" TEXT NOT NULL,
                    "CountryCode" TEXT NOT NULL,
                    "StateOrProvince" TEXT NULL,
                    "StateCode" TEXT NULL,
                    "City" TEXT NOT NULL,
                    "PostalCode" TEXT NOT NULL,
                    "Latitude" TEXT NULL,
                    "Longitude" TEXT NULL,
                    "Accuracy" TEXT NULL,
                    "TimeZone" TEXT NULL
                );
                CREATE UNIQUE INDEX "IX_{tableName}_identity" ON "{tableName}" ("CountryCode", "StateCode", "City");
                CREATE INDEX "IX_{tableName}_country_city" ON "{tableName}" ("Country", "City");
                """;
            create.ExecuteNonQuery();
        }

        var countryLookup = LoadCountryLookup(sourceRoots);
        var postalDirectory = sources.FirstOrDefault(source => Directory.Exists(source.FullPath));
        if (postalDirectory is null)
        {
            return;
        }

        var representativeRows = new Dictionary<string, PostalReferenceRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(postalDirectory.FullPath, "*.txt", SearchOption.TopDirectoryOnly))
        {
            foreach (var row in EnumeratePostalReferenceRows(file, countryLookup))
            {
                var key = $"{row.CountryCode}|{row.StateCode}|{NormalizeKey(row.City)}";
                if (!representativeRows.TryGetValue(key, out var existing) || IsBetterPostalReference(row, existing))
                {
                    representativeRows[key] = row;
                }
            }
        }

        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""INSERT INTO "{tableName}" ("Region","Country","CountryCode","StateOrProvince","StateCode","City","PostalCode","Latitude","Longitude","Accuracy","TimeZone") VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7,$p8,$p9,$p10)""";

        foreach (var row in representativeRows.Values
                     .OrderBy(value => value.Country, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(value => value.StateOrProvince, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(value => value.City, StringComparer.OrdinalIgnoreCase))
        {
            insert.Parameters.Clear();
            insert.Parameters.AddWithValue("$p0", row.Region);
            insert.Parameters.AddWithValue("$p1", row.Country);
            insert.Parameters.AddWithValue("$p2", row.CountryCode);
            insert.Parameters.AddWithValue("$p3", row.StateOrProvince);
            insert.Parameters.AddWithValue("$p4", row.StateCode);
            insert.Parameters.AddWithValue("$p5", row.City);
            insert.Parameters.AddWithValue("$p6", row.PostalCode);
            insert.Parameters.AddWithValue("$p7", row.Latitude);
            insert.Parameters.AddWithValue("$p8", row.Longitude);
            insert.Parameters.AddWithValue("$p9", row.Accuracy);
            insert.Parameters.AddWithValue("$p10", row.TimeZone);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();

        InsertSourceRecord(connection, tableName, postalDirectory.SourceRoot, postalDirectory.FullPath, "directory", strategy);
        foreach (var source in sources.Where(source => File.Exists(source.FullPath)))
        {
            InsertSourceRecord(connection, tableName, source.SourceRoot, source.FullPath, "csv", strategy);
        }
    }

    private static void ImportLocalityReference(SqliteConnection connection, string tableName, IReadOnlyList<string> sourceRoots, IReadOnlyList<ResolvedCatalogSource> sources, string strategy)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"""
                CREATE TABLE "{tableName}"
                (
                    "CountryCode" TEXT NOT NULL,
                    "StateCode" TEXT NOT NULL,
                    "StateOrProvince" TEXT NULL,
                    "City" TEXT NOT NULL,
                    "PostalCode" TEXT NULL,
                    "TimeZone" TEXT NULL,
                    "Latitude" REAL NULL,
                    "Longitude" REAL NULL,
                    "Population" INTEGER NULL,
                    "Accuracy" INTEGER NULL,
                    PRIMARY KEY ("CountryCode", "StateCode", "City")
                ) WITHOUT ROWID;
                CREATE INDEX "IX_{tableName}_country_population" ON "{tableName}" ("CountryCode", "Population");
                CREATE INDEX "IX_{tableName}_city" ON "{tableName}" ("City");
                """;
            create.ExecuteNonQuery();
        }

        var countryLookup = LoadCountryLookup(sourceRoots);
        var knownStateNames = LoadKnownStateNames(sources);
        var populationLookup = LoadPopulationReference(sources, sourceRoots, countryLookup, knownStateNames);
        var insertedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var postalDirectory = sources.FirstOrDefault(source => Directory.Exists(source.FullPath));

        if (postalDirectory is not null)
        {
            var representativeRows = new Dictionary<string, LocalityReferenceRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(postalDirectory.FullPath, "*.txt", SearchOption.TopDirectoryOnly))
            {
                foreach (var postalRow in EnumeratePostalReferenceRows(file, countryLookup))
                {
                    var populationLookupKey = $"{postalRow.CountryCode}|{NormalizeLookupKey(postalRow.City)}";
                    populationLookup.TryGetValue(populationLookupKey, out var populationReference);
                    var row = new LocalityReferenceRow(
                        postalRow.CountryCode,
                        postalRow.StateCode,
                        postalRow.StateOrProvince,
                        postalRow.City,
                        postalRow.PostalCode,
                        postalRow.TimeZone,
                        postalRow.Latitude,
                        postalRow.Longitude,
                        populationReference?.Population ?? 0L,
                        ParseAccuracy(postalRow.Accuracy),
                        populationLookupKey);

                    var key = $"{row.CountryCode}|{row.StateCode}|{NormalizeLookupKey(row.City)}";
                    if (!representativeRows.TryGetValue(key, out var existing)
                        || IsBetterLocalityReference(row, existing))
                    {
                        representativeRows[key] = row;
                    }
                }
            }

            using var transaction = connection.BeginTransaction();
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT OR IGNORE INTO "{tableName}"
                ("CountryCode","StateCode","StateOrProvince","City","PostalCode","TimeZone","Latitude","Longitude","Population","Accuracy")
                VALUES ($countryCode,$stateCode,$stateOrProvince,$city,$postalCode,$timeZone,$latitude,$longitude,$population,$accuracy)
                """;
            insert.Parameters.Add("$countryCode", SqliteType.Text);
            insert.Parameters.Add("$stateCode", SqliteType.Text);
            insert.Parameters.Add("$stateOrProvince", SqliteType.Text);
            insert.Parameters.Add("$city", SqliteType.Text);
            insert.Parameters.Add("$postalCode", SqliteType.Text);
            insert.Parameters.Add("$timeZone", SqliteType.Text);
            insert.Parameters.Add("$latitude", SqliteType.Real);
            insert.Parameters.Add("$longitude", SqliteType.Real);
            insert.Parameters.Add("$population", SqliteType.Integer);
            insert.Parameters.Add("$accuracy", SqliteType.Integer);

            foreach (var row in representativeRows.Values
                         .OrderBy(value => value.CountryCode, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(value => value.StateCode, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(value => value.City, StringComparer.OrdinalIgnoreCase))
            {
                insertedKeys.Add($"{row.CountryCode}|{row.StateCode}|{NormalizeLookupKey(row.City)}");
                populationLookup.Remove(row.PopulationLookupKey);

                insert.Parameters["$countryCode"].Value = row.CountryCode;
                insert.Parameters["$stateCode"].Value = row.StateCode;
                insert.Parameters["$stateOrProvince"].Value = row.StateOrProvince;
                insert.Parameters["$city"].Value = row.City;
                insert.Parameters["$postalCode"].Value = row.PostalCode;
                insert.Parameters["$timeZone"].Value = row.TimeZone;
                insert.Parameters["$latitude"].Value = TryParseDouble(row.Latitude);
                insert.Parameters["$longitude"].Value = TryParseDouble(row.Longitude);
                insert.Parameters["$population"].Value = row.Population;
                insert.Parameters["$accuracy"].Value = row.Accuracy;
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        var remainingPopulationRows = populationLookup.Values
            .GroupBy(row => $"{row.CountryCode}|{NormalizeLookupKey(row.City)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(row => row.Population).First())
            .ToList();

        using (var transaction = connection.BeginTransaction())
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT OR IGNORE INTO "{tableName}"
                ("CountryCode","StateCode","StateOrProvince","City","PostalCode","TimeZone","Latitude","Longitude","Population","Accuracy")
                VALUES ($countryCode,$stateCode,$stateOrProvince,$city,$postalCode,$timeZone,$latitude,$longitude,$population,$accuracy)
                """;
            insert.Parameters.Add("$countryCode", SqliteType.Text);
            insert.Parameters.Add("$stateCode", SqliteType.Text);
            insert.Parameters.Add("$stateOrProvince", SqliteType.Text);
            insert.Parameters.Add("$city", SqliteType.Text);
            insert.Parameters.Add("$postalCode", SqliteType.Text);
            insert.Parameters.Add("$timeZone", SqliteType.Text);
            insert.Parameters.Add("$latitude", SqliteType.Real);
            insert.Parameters.Add("$longitude", SqliteType.Real);
            insert.Parameters.Add("$population", SqliteType.Integer);
            insert.Parameters.Add("$accuracy", SqliteType.Integer);

            foreach (var row in remainingPopulationRows)
            {
                var key = $"{row.CountryCode}||{NormalizeLookupKey(row.City)}";
                if (!insertedKeys.Add(key))
                {
                    continue;
                }

                var inferredCountryName = countryLookup.TryGetValue(row.CountryCode, out var countryInfo) ? countryInfo.Name : string.Empty;
                insert.Parameters["$countryCode"].Value = row.CountryCode;
                insert.Parameters["$stateCode"].Value = string.Empty;
                insert.Parameters["$stateOrProvince"].Value = string.Empty;
                insert.Parameters["$city"].Value = row.City;
                insert.Parameters["$postalCode"].Value = string.Empty;
                insert.Parameters["$timeZone"].Value = InferTimeZone(inferredCountryName, row.CountryCode, row.Longitude, row.Latitude);
                insert.Parameters["$latitude"].Value = TryParseDouble(row.Latitude);
                insert.Parameters["$longitude"].Value = TryParseDouble(row.Longitude);
                insert.Parameters["$population"].Value = row.Population;
                insert.Parameters["$accuracy"].Value = 0;
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        foreach (var source in sources)
        {
            InsertSourceRecord(connection, tableName, source.SourceRoot, source.FullPath, Directory.Exists(source.FullPath) ? "directory" : "csv", strategy);
        }
    }

    private static void ImportCities(SqliteConnection connection, string tableName, IReadOnlyList<string> sourceRoots, ResolvedCatalogSource source, string strategy)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"""
                CREATE TABLE "{tableName}"
                (
                    "Region" TEXT NOT NULL,
                    "Country" TEXT NOT NULL,
                    "State" TEXT NOT NULL,
                    "City" TEXT NOT NULL,
                    "PostalCode" TEXT NOT NULL,
                    "TimeZone" TEXT NULL,
                    "Latitude" TEXT NULL,
                    "Longitude" TEXT NULL
                );
                CREATE UNIQUE INDEX "IX_{tableName}_identity" ON "{tableName}" ("Country", "State", "City", "PostalCode");
                """;
            create.ExecuteNonQuery();
        }

        var countryLookup = LoadCountryLookup(sourceRoots);
        CopyRows(connection, source.FullPath, row =>
        {
            var rawPostal = row.ContainsKey("countryCode");
            var countryCode = rawPostal ? Read(row, "countryCode") : string.Empty;
            var countryInfo = rawPostal && countryLookup.TryGetValue(countryCode, out var resolvedCountryInfo)
                ? resolvedCountryInfo
                : null;
            var country = rawPostal ? (countryInfo?.Name ?? countryCode) : Read(row, "Country");
            var city = rawPostal ? ToTitleCase(Read(row, "city")) : Read(row, "City");
            var state = rawPostal ? ToTitleCase(Read(row, "state")) : Read(row, "State");
            var region = rawPostal ? (countryInfo?.Region ?? string.Empty) : Read(row, "Region");
            var postalCode = rawPostal ? Read(row, "postalCode") : Read(row, "PostalCode");
            if (string.IsNullOrWhiteSpace(country) || string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(postalCode))
            {
                return null;
            }

            return new[]
            {
                region, country, state, city, postalCode,
                rawPostal ? InferTimeZone(country, countryCode, Read(row, "longitude"), Read(row, "latitude"), Read(row, "stateShort")) : Read(row, "TimeZone"),
                rawPostal ? Read(row, "latitude") : Read(row, "Latitude"),
                rawPostal ? Read(row, "longitude") : Read(row, "Longitude")
            };
        }, $"""INSERT OR IGNORE INTO "{tableName}" ("Region","Country","State","City","PostalCode","TimeZone","Latitude","Longitude") VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6,$p7)""");

        InsertSourceRecord(connection, tableName, source.SourceRoot, source.FullPath, "csv", strategy);
    }

    private static void ImportLineList(SqliteConnection connection, string tableName, string columnName, ResolvedCatalogSource source, string strategy)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE \"{tableName}\" (\"{columnName}\" TEXT NOT NULL)";
            create.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO \"{tableName}\" (\"{columnName}\") VALUES ($value)";
        insert.Parameters.Add("$value", SqliteType.Text);

        foreach (var line in FileSystemCatalogLoader.EnumerateLinesShared(source.FullPath))
        {
            var value = line.Trim();
            if (string.IsNullOrWhiteSpace(value)) continue;
            insert.Parameters["$value"].Value = value;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        InsertSourceRecord(connection, tableName, source.SourceRoot, source.FullPath, "text", strategy);
    }

    private static void ImportTitles(SqliteConnection connection, string tableName, IReadOnlyList<ResolvedCatalogSource> sources)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"""CREATE TABLE "{tableName}" ("Title" TEXT NOT NULL, "Type" TEXT NULL, "Department" TEXT NULL, "Level" TEXT NULL, "Source" TEXT NOT NULL)""";
            create.ExecuteNonQuery();
        }

        foreach (var source in sources)
        {
            CopyRows(connection, source.FullPath, row =>
            {
                var title = Read(row, "Title");
                if (string.IsNullOrWhiteSpace(title)) return null;
                return new[] { title, Read(row, "Type"), Read(row, "Department"), FirstNonEmpty(Read(row, "Level"), Read(row, "Type")), Path.GetFileName(source.FullPath) };
            }, $"""INSERT INTO "{tableName}" ("Title","Type","Department","Level","Source") VALUES ($p0,$p1,$p2,$p3,$p4)""");

            InsertSourceRecord(connection, tableName, source.SourceRoot, source.FullPath, "csv", "titles_merge");
        }
    }

    private static void ImportCompanyTerms(SqliteConnection connection, string tableName, ResolvedCatalogSource source, string strategy)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"""CREATE TABLE "{tableName}" ("Sector" TEXT NULL, "Term" TEXT NOT NULL)""";
            create.ExecuteNonQuery();
        }

        CopyRows(connection, source.FullPath, row =>
        {
            var term = Read(row, "Terms");
            return string.IsNullOrWhiteSpace(term) ? null : new[] { Read(row, "Sector"), term };
        }, $"""INSERT INTO "{tableName}" ("Sector","Term") VALUES ($p0,$p1)""");

        InsertSourceRecord(connection, tableName, source.SourceRoot, source.FullPath, "csv", strategy);
    }

    private static void ImportCountryReference(SqliteConnection connection, string tableName, ResolvedCatalogSource source, string strategy)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"""
                CREATE TABLE "{tableName}"
                (
                    "Name" TEXT NOT NULL,
                    "Code" TEXT NOT NULL,
                    "Phone" TEXT NULL,
                    "Capital" TEXT NULL,
                    "Continent" TEXT NULL,
                    "PostalCodeSupported" TEXT NULL
                );
                CREATE UNIQUE INDEX "IX_{tableName}_code" ON "{tableName}" ("Code");
                """;
            create.ExecuteNonQuery();
        }

        CopyRows(connection, source.FullPath, row =>
        {
            var name = Read(row, "Name");
            var code = Read(row, "Code");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            return new[]
            {
                name,
                code,
                Read(row, "Phone"),
                Read(row, "Capital"),
                Read(row, "Continent"),
                NormalizeBooleanFlag(Read(row, "PostalCode"))
            };
        }, $"""INSERT OR IGNORE INTO "{tableName}" ("Name","Code","Phone","Capital","Continent","PostalCodeSupported") VALUES ($p0,$p1,$p2,$p3,$p4,$p5)""");

        InsertSourceRecord(connection, tableName, source.SourceRoot, source.FullPath, "csv", strategy);
    }

    private static void ImportFirstNamesGendered(SqliteConnection connection, string tableName, ResolvedCatalogSource source, string strategy)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"""
                CREATE TABLE "{tableName}"
                (
                    "Name" TEXT NOT NULL,
                    "Gender" TEXT NULL
                );
                CREATE INDEX "IX_{tableName}_name" ON "{tableName}" ("Name");
                """;
            create.ExecuteNonQuery();
        }

        CopyRows(connection, source.FullPath, row =>
        {
            var name = Read(row, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return new[]
            {
                name,
                ToTitleCase(Read(row, "Gender"))
            };
        }, $"""INSERT INTO "{tableName}" ("Name","Gender") VALUES ($p0,$p1)""");

        InsertSourceRecord(connection, tableName, source.SourceRoot, source.FullPath, "csv", strategy);
    }

    private static void ImportCityPopulationReference(SqliteConnection connection, string tableName, IReadOnlyList<string> sourceRoots, ResolvedCatalogSource source, string strategy)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"""
                CREATE TABLE "{tableName}"
                (
                    "Region" TEXT NULL,
                    "Country" TEXT NOT NULL,
                    "CountryCode" TEXT NOT NULL,
                    "City" TEXT NOT NULL,
                    "Latitude" TEXT NULL,
                    "Longitude" TEXT NULL,
                    "Population" TEXT NULL
                );
                CREATE UNIQUE INDEX "IX_{tableName}_identity" ON "{tableName}" ("CountryCode", "City", "Latitude", "Longitude");
                CREATE INDEX "IX_{tableName}_country_population" ON "{tableName}" ("Country", "Population");
                """;
            create.ExecuteNonQuery();
        }

        var countryLookup = LoadCountryLookup(sourceRoots);
        CopyRows(connection, source.FullPath, row =>
        {
            var countryCode = Read(row, "CountryCode");
            var city = ToTitleCase(FirstNonEmpty(Read(row, "Name"), Read(row, "City")));
            if (string.IsNullOrWhiteSpace(countryCode) || string.IsNullOrWhiteSpace(city))
            {
                return null;
            }

            var countryInfo = countryLookup.TryGetValue(countryCode, out var resolvedCountry) ? resolvedCountry : null;
            return new[]
            {
                countryInfo?.Region ?? string.Empty,
                countryInfo?.Name ?? countryCode,
                countryCode,
                city,
                Read(row, "Latitude"),
                Read(row, "Longitude"),
                Read(row, "Population")
            };
        }, $"""INSERT OR IGNORE INTO "{tableName}" ("Region","Country","CountryCode","City","Latitude","Longitude","Population") VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6)""");

        InsertSourceRecord(connection, tableName, source.SourceRoot, source.FullPath, "csv", strategy);
    }

    private static void ImportFakeCompanies(SqliteConnection connection, string tableName, ResolvedCatalogSource source, string strategy)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"""
                CREATE TABLE "{tableName}"
                (
                    "CompanyName" TEXT NOT NULL,
                    "Description" TEXT NULL,
                    "Tagline" TEXT NULL,
                    "CompanyEmail" TEXT NULL,
                    "TaxIdentifier" TEXT NULL
                );
                CREATE INDEX "IX_{tableName}_company" ON "{tableName}" ("CompanyName");
                """;
            create.ExecuteNonQuery();
        }

        CopyRows(connection, source.FullPath, row =>
        {
            var companyName = FirstNonEmpty(Read(row, "fake-company-name"), Read(row, "CompanyName"));
            if (string.IsNullOrWhiteSpace(companyName))
            {
                return null;
            }

            return new[]
            {
                companyName,
                Read(row, "description"),
                Read(row, "tagline"),
                Read(row, "company-email"),
                FirstNonEmpty(Read(row, "ein"), Read(row, "tax-identifier"))
            };
        }, $"""INSERT INTO "{tableName}" ("CompanyName","Description","Tagline","CompanyEmail","TaxIdentifier") VALUES ($p0,$p1,$p2,$p3,$p4)""");

        InsertSourceRecord(connection, tableName, source.SourceRoot, source.FullPath, "csv", strategy);
    }

    private static void ImportIndustryTaxonomy(SqliteConnection connection, string tableName, ResolvedCatalogSource source, string strategy)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"""
                CREATE TABLE "{tableName}"
                (
                    "Sector" TEXT NOT NULL,
                    "IndustryGroup" TEXT NOT NULL,
                    "Industry" TEXT NOT NULL,
                    "SubIndustry" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX "IX_{tableName}_taxonomy" ON "{tableName}" ("Sector", "IndustryGroup", "Industry", "SubIndustry");
                """;
            create.ExecuteNonQuery();
        }

        CopyRows(connection, source.FullPath, row =>
        {
            var sector = Read(row, "Sector");
            var industryGroup = FirstNonEmpty(Read(row, "IndustryGroup"), Read(row, "Industry Group"));
            var industry = Read(row, "Industry");
            var subIndustry = FirstNonEmpty(Read(row, "SubIndustry"), Read(row, "Sub-Industry"));

            if (string.IsNullOrWhiteSpace(sector)
                || string.IsNullOrWhiteSpace(industryGroup)
                || string.IsNullOrWhiteSpace(industry)
                || string.IsNullOrWhiteSpace(subIndustry))
            {
                return null;
            }

            return new[] { sector, industryGroup, industry, subIndustry };
        }, $"""INSERT OR IGNORE INTO "{tableName}" ("Sector","IndustryGroup","Industry","SubIndustry") VALUES ($p0,$p1,$p2,$p3)""");

        InsertSourceRecord(connection, tableName, source.SourceRoot, source.FullPath, "csv", strategy);
    }

    private static void ImportCompanySizeBands(SqliteConnection connection, string tableName, ResolvedCatalogSource source, string strategy)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"""
                CREATE TABLE "{tableName}"
                (
                    "Size" TEXT NOT NULL,
                    "EmployeeMin" TEXT NOT NULL,
                    "EmployeeMax" TEXT NOT NULL,
                    "LocationMax" TEXT NOT NULL,
                    "Distribution" TEXT NOT NULL,
                    "RevenueMin" TEXT NOT NULL,
                    "RevenueMax" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX "IX_{tableName}_size" ON "{tableName}" ("Size");
                """;
            create.ExecuteNonQuery();
        }

        CopyRows(connection, source.FullPath, row =>
        {
            var size = Read(row, "Size");
            if (string.IsNullOrWhiteSpace(size))
            {
                return null;
            }

            return new[]
            {
                size,
                Read(row, "EmployeeMin"),
                Read(row, "EmployeeMax"),
                Read(row, "LocationMax"),
                Read(row, "Distribution"),
                Read(row, "RevenueMin"),
                Read(row, "RevenueMax")
            };
        }, $"""INSERT OR IGNORE INTO "{tableName}" ("Size","EmployeeMin","EmployeeMax","LocationMax","Distribution","RevenueMin","RevenueMax") VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6)""");

        InsertSourceRecord(connection, tableName, source.SourceRoot, source.FullPath, "csv", strategy);
    }

    private static void ImportCountryIdentityRules(SqliteConnection connection, string tableName, ResolvedCatalogSource source, string strategy)
    {
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"""
                CREATE TABLE "{tableName}"
                (
                    "Country" TEXT NOT NULL,
                    "CountryCode" TEXT NOT NULL,
                    "DialCode" TEXT NULL,
                    "PostalCodeSupported" TEXT NOT NULL,
                    "PrimaryDomainSuffix" TEXT NOT NULL,
                    "AlternateDomainSuffix" TEXT NULL,
                    "PhonePattern" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX "IX_{tableName}_country" ON "{tableName}" ("Country");
                CREATE UNIQUE INDEX "IX_{tableName}_country_code" ON "{tableName}" ("CountryCode");
                """;
            create.ExecuteNonQuery();
        }

        CopyRows(connection, source.FullPath, row =>
        {
            var country = Read(row, "Name");
            var countryCode = Read(row, "Code").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(country) || string.IsNullOrWhiteSpace(countryCode))
            {
                return null;
            }

            var dialCode = new string(Read(row, "Phone").Where(char.IsDigit).ToArray());
            var postalCodeSupported = NormalizeBooleanFlag(Read(row, "PostalCode"));
            var primaryDomainSuffix = ResolvePrimaryDomainSuffix(country, countryCode);
            var alternateDomainSuffix = ResolveAlternateDomainSuffix(countryCode, primaryDomainSuffix);
            var phonePattern = ResolvePhonePattern(countryCode, dialCode);

            return new[]
            {
                country,
                countryCode,
                dialCode,
                postalCodeSupported,
                primaryDomainSuffix,
                alternateDomainSuffix,
                phonePattern
            };
        }, $"""INSERT OR IGNORE INTO "{tableName}" ("Country","CountryCode","DialCode","PostalCodeSupported","PrimaryDomainSuffix","AlternateDomainSuffix","PhonePattern") VALUES ($p0,$p1,$p2,$p3,$p4,$p5,$p6)""");

        InsertSourceRecord(connection, tableName, source.SourceRoot, source.FullPath, "csv", strategy);
    }

    private static void ImportCsv(SqliteConnection connection, string sourceRoot, string file, string? tableName = null, string strategy = "copy_csv")
    {
        using var enumerator = FileSystemCatalogLoader.EnumerateLinesShared(file).GetEnumerator();
        if (!enumerator.MoveNext()) return;
        tableName ??= Path.GetFileNameWithoutExtension(file);
        var headers = FileSystemCatalogLoader.SplitCsvLine(enumerator.Current);
        if (headers.Count == 0) return;

        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE TABLE \"{tableName}\" ({string.Join(", ", headers.Select(header => $"\"{header}\" TEXT"))})";
            create.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO \"{tableName}\" ({string.Join(", ", headers.Select(header => $"\"{header}\""))}) VALUES ({string.Join(", ", headers.Select((_, index) => $"$p{index}"))})";
        for (var i = 0; i < headers.Count; i++) insert.Parameters.Add($"$p{i}", SqliteType.Text);

        while (enumerator.MoveNext())
        {
            var values = FileSystemCatalogLoader.SplitCsvLine(enumerator.Current);
            if (values.Count == 0 || values.All(string.IsNullOrWhiteSpace)) continue;
            for (var i = 0; i < headers.Count; i++) insert.Parameters[i].Value = i < values.Count ? values[i] : string.Empty;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        InsertSourceRecord(connection, tableName, sourceRoot, file, "csv", strategy);
    }

    private static void ImportJson(SqliteConnection connection, string sourceRoot, string file, string strategy)
    {
        var catalogName = Path.GetFileNameWithoutExtension(file);
        using var insert = connection.CreateCommand();
        insert.CommandText = """INSERT OR REPLACE INTO json_catalogs (catalog_name, source_file, json_payload) VALUES ($name, $sourceFile, $json)""";
        insert.Parameters.AddWithValue("$name", catalogName);
        insert.Parameters.AddWithValue("$sourceFile", Path.GetRelativePath(sourceRoot, file));
        insert.Parameters.AddWithValue("$json", FileSystemCatalogLoader.ReadAllTextShared(file));
        insert.ExecuteNonQuery();
        InsertSourceRecord(connection, catalogName, sourceRoot, file, "json", strategy);
    }

    private static void CopyRows(SqliteConnection connection, string file, Func<Dictionary<string, string>, string[]?> projector, string commandText)
    {
        using var enumerator = FileSystemCatalogLoader.EnumerateLinesShared(file).GetEnumerator();
        if (!enumerator.MoveNext()) return;
        var headers = FileSystemCatalogLoader.SplitCsvLine(enumerator.Current);

        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = commandText;

        while (enumerator.MoveNext())
        {
            var values = FileSystemCatalogLoader.SplitCsvLine(enumerator.Current);
            if (values.Count == 0 || values.All(string.IsNullOrWhiteSpace)) continue;
            var row = headers.Select((header, index) => new { header, value = index < values.Count ? values[index] : string.Empty })
                .ToDictionary(item => item.header, item => item.value, StringComparer.OrdinalIgnoreCase);
            var projected = projector(row);
            if (projected is null) continue;

            insert.Parameters.Clear();
            for (var i = 0; i < projected.Length; i++) insert.Parameters.AddWithValue($"$p{i}", projected[i]);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void InsertSourceRecord(SqliteConnection connection, string catalogName, string sourceRoot, string file, string sourceKind, string? strategy)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText = """INSERT INTO "__catalog_source" (catalog_name, source_file, source_root, source_kind, strategy) VALUES ($name, $sourceFile, $sourceRoot, $kind, $strategy)""";
        insert.Parameters.AddWithValue("$name", catalogName);
        insert.Parameters.AddWithValue("$sourceFile", Path.GetRelativePath(sourceRoot, file));
        insert.Parameters.AddWithValue("$sourceRoot", sourceRoot);
        insert.Parameters.AddWithValue("$kind", sourceKind);
        insert.Parameters.AddWithValue("$strategy", strategy ?? string.Empty);
        insert.ExecuteNonQuery();
    }

    private static Dictionary<string, CountryInfo> LoadCountryLookup(IReadOnlyList<string> sourceRoots)
    {
        var source = ResolveSources(sourceRoots, new[] { "countries.csv" }).FirstOrDefault();
        var results = new Dictionary<string, CountryInfo>(StringComparer.OrdinalIgnoreCase);
        if (source is null) return results;

        using var enumerator = FileSystemCatalogLoader.EnumerateLinesShared(source.FullPath).GetEnumerator();
        if (!enumerator.MoveNext()) return results;
        var headers = FileSystemCatalogLoader.SplitCsvLine(enumerator.Current);
        while (enumerator.MoveNext())
        {
            var values = FileSystemCatalogLoader.SplitCsvLine(enumerator.Current);
            if (values.Count == 0 || values.All(string.IsNullOrWhiteSpace)) continue;
            var row = headers.Select((header, index) => new { header, value = index < values.Count ? values[index] : string.Empty })
                .ToDictionary(item => item.header, item => item.value, StringComparer.OrdinalIgnoreCase);
            var code = Read(row, "Code");
            if (!string.IsNullOrWhiteSpace(code)) results[code] = new CountryInfo(Read(row, "Name"), Read(row, "Continent"));
        }

        return results;
    }

    private static Dictionary<string, LocalityPopulationReference> LoadPopulationReference(
        IReadOnlyList<ResolvedCatalogSource> sources,
        IReadOnlyList<string> sourceRoots,
        IReadOnlyDictionary<string, CountryInfo> countryLookup,
        IReadOnlyDictionary<string, HashSet<string>> knownStateNames)
    {
        var source = sources
            .FirstOrDefault(candidate => string.Equals(Path.GetFileName(candidate.FullPath), "CitiesByCountry-Filt.csv", StringComparison.OrdinalIgnoreCase))
            ?? ResolveSources(sourceRoots, new[] { "CitiesByCountry-Filt.csv" }).FirstOrDefault();
        var results = new Dictionary<string, LocalityPopulationReference>(StringComparer.OrdinalIgnoreCase);
        if (source is null || !File.Exists(source.FullPath))
        {
            return results;
        }

        using var enumerator = FileSystemCatalogLoader.EnumerateLinesShared(source.FullPath).GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return results;
        }

        var headers = FileSystemCatalogLoader.SplitCsvLine(enumerator.Current);
        while (enumerator.MoveNext())
        {
            var values = FileSystemCatalogLoader.SplitCsvLine(enumerator.Current);
            if (values.Count == 0 || values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var row = headers.Select((header, index) => new { header, value = index < values.Count ? values[index] : string.Empty })
                .ToDictionary(item => item.header, item => item.value, StringComparer.OrdinalIgnoreCase);

            var countryCode = Read(row, "CountryCode").Trim().ToUpperInvariant();
            var city = ToTitleCase(FirstNonEmpty(Read(row, "Name"), Read(row, "City")));
            if (string.IsNullOrWhiteSpace(countryCode) || string.IsNullOrWhiteSpace(city))
            {
                continue;
            }

            countryLookup.TryGetValue(countryCode, out var countryInfo);
            var countryStates = knownStateNames.TryGetValue(countryCode, out var stateNames)
                ? stateNames
                : null;
            if (!ShouldIncludePopulationLocality(city, countryInfo?.Name, countryStates))
            {
                continue;
            }

            var key = $"{countryCode}|{NormalizeLookupKey(city)}";
            var population = ParseLong(Read(row, "Population"));
            if (results.TryGetValue(key, out var existing) && existing.Population >= population)
            {
                continue;
            }

            results[key] = new LocalityPopulationReference(
                countryCode,
                countryInfo?.Name ?? countryCode,
                countryInfo?.Region ?? string.Empty,
                city,
                Read(row, "Latitude"),
                Read(row, "Longitude"),
                population);
        }

        return results;
    }

    private static bool IsBetterLocalityReference(LocalityReferenceRow candidate, LocalityReferenceRow existing)
    {
        if (candidate.Accuracy != existing.Accuracy)
        {
            return candidate.Accuracy > existing.Accuracy;
        }

        if (candidate.Population != existing.Population)
        {
            return candidate.Population > existing.Population;
        }

        if (!string.IsNullOrWhiteSpace(candidate.StateOrProvince) && string.IsNullOrWhiteSpace(existing.StateOrProvince))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(candidate.Latitude) && string.IsNullOrWhiteSpace(existing.Latitude))
        {
            return true;
        }

        return candidate.PostalCode.Length < existing.PostalCode.Length;
    }

    private static string NormalizeLookupKey(string value)
        => NormalizeKey(value).Replace(" CITY", string.Empty, StringComparison.Ordinal);

    private static long ParseLong(string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L;

    private static object TryParseDouble(string value)
        => double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : DBNull.Value;

    private static bool ShouldIncludePopulationLocality(string city, string? countryName, IReadOnlySet<string>? knownStateNames)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            return false;
        }

        var normalizedCity = city.Trim();
        if (!string.IsNullOrWhiteSpace(countryName)
            && string.Equals(normalizedCity, countryName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (knownStateNames is not null && knownStateNames.Contains(normalizedCity))
        {
            return false;
        }

        return !normalizedCity.EndsWith(" County", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.EndsWith(" District", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.EndsWith(" Province", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.EndsWith(" Municipality", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.EndsWith(" Region", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.EndsWith(" Area", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.EndsWith(" Governorate", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.EndsWith(" Prefecture", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.EndsWith(" Oblast", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.EndsWith(" Rayon", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.EndsWith(" Qu", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.EndsWith(" Shi", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.EndsWith(" Xian", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.Contains("Republic", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.Contains(" Kingdom", StringComparison.OrdinalIgnoreCase)
               && !normalizedCity.StartsWith("State Of ", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(normalizedCity, "England", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(normalizedCity, "Scotland", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(normalizedCity, "Wales", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(normalizedCity, "Northern Ireland", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, HashSet<string>> LoadKnownStateNames(IReadOnlyList<ResolvedCatalogSource> sources)
    {
        var results = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var postalDirectory = sources.FirstOrDefault(source => Directory.Exists(source.FullPath));
        if (postalDirectory is null)
        {
            return results;
        }

        foreach (var file in Directory.EnumerateFiles(postalDirectory.FullPath, "*.txt", SearchOption.TopDirectoryOnly))
        {
            var countryCode = Path.GetFileNameWithoutExtension(file).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(countryCode))
            {
                continue;
            }

            if (!results.TryGetValue(countryCode, out var states))
            {
                states = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                results[countryCode] = states;
            }

            foreach (var line in FileSystemCatalogLoader.EnumerateLinesShared(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split('\t');
                if (parts.Length < 5)
                {
                    continue;
                }

                var stateName = NormalizePlaceName(parts[3]);
                if (!string.IsNullOrWhiteSpace(stateName))
                {
                    states.Add(stateName);
                }
            }
        }

        return results;
    }

    private static string InferTimeZone(string country, string countryCode, string longitude = "", string latitude = "", string stateCode = "")
        => (countryCode, country) switch
        {
            ("US", _) or (_, "United States") => InferNorthAmericaTimeZone(countryCode, longitude, latitude, stateCode, "America/New_York"),
            ("CA", _) or (_, "Canada") => InferNorthAmericaTimeZone(countryCode, longitude, latitude, stateCode, "America/Toronto"),
            ("MX", _) or (_, "Mexico") => "America/Mexico_City",
            ("GB", _) or (_, "United Kingdom") => "Europe/London",
            ("DE", _) or (_, "Germany") => "Europe/Berlin",
            ("FR", _) or (_, "France") => "Europe/Paris",
            ("IN", _) or (_, "India") => "Asia/Kolkata",
            ("JP", _) or (_, "Japan") => "Asia/Tokyo",
            ("AU", _) or (_, "Australia") => InferAustraliaTimeZone(stateCode, longitude),
            ("BR", _) or (_, "Brazil") => InferBrazilTimeZone(longitude),
            _ => string.Empty
        };

    private static string InferNorthAmericaTimeZone(string countryCode, string longitude, string latitude, string stateCode, string easternFallback)
    {
        var normalizedStateCode = stateCode.Trim().ToUpperInvariant();
        if (countryCode == "US")
        {
            if (normalizedStateCode is "AK") return "America/Anchorage";
            if (normalizedStateCode is "HI") return "Pacific/Honolulu";
            if (new[] { "CA", "NV", "OR", "WA" }.Contains(normalizedStateCode, StringComparer.OrdinalIgnoreCase)) return "America/Los_Angeles";
            if (new[] { "AZ", "CO", "ID", "MT", "NM", "UT", "WY" }.Contains(normalizedStateCode, StringComparer.OrdinalIgnoreCase)) return "America/Denver";
            if (new[] { "AL", "AR", "IA", "IL", "KS", "LA", "MN", "MO", "MS", "ND", "NE", "OK", "SD", "TX", "WI" }.Contains(normalizedStateCode, StringComparer.OrdinalIgnoreCase)) return "America/Chicago";
            if (!string.IsNullOrWhiteSpace(normalizedStateCode)) return "America/New_York";
        }
        else if (countryCode == "CA")
        {
            if (new[] { "BC", "YT" }.Contains(normalizedStateCode, StringComparer.OrdinalIgnoreCase)) return "America/Vancouver";
            if (new[] { "AB", "NT" }.Contains(normalizedStateCode, StringComparer.OrdinalIgnoreCase)) return "America/Edmonton";
            if (normalizedStateCode is "SK") return "America/Regina";
            if (new[] { "MB", "NU" }.Contains(normalizedStateCode, StringComparer.OrdinalIgnoreCase)) return "America/Winnipeg";
            if (new[] { "NB", "NS", "PE" }.Contains(normalizedStateCode, StringComparer.OrdinalIgnoreCase)) return "America/Halifax";
            if (normalizedStateCode is "NL") return "America/St_Johns";
            if (!string.IsNullOrWhiteSpace(normalizedStateCode)) return "America/Toronto";
        }

        if (!double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLongitude))
        {
            return easternFallback;
        }

        var parsedLatitude = double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitudeValue)
            ? latitudeValue
            : 0d;

        if (countryCode == "US" && parsedLatitude < 24d && parsedLongitude < -154d)
        {
            return "Pacific/Honolulu";
        }

        if (parsedLongitude <= -136d)
        {
            return "America/Anchorage";
        }

        if (parsedLongitude <= -114d)
        {
            return "America/Los_Angeles";
        }

        if (parsedLongitude <= -100d)
        {
            return countryCode == "CA" ? "America/Winnipeg" : "America/Denver";
        }

        if (parsedLongitude <= -84d)
        {
            return "America/Chicago";
        }

        if (countryCode == "CA" && parsedLongitude <= -52d)
        {
            return "America/Halifax";
        }

        if (countryCode == "CA" && parsedLongitude > -52d)
        {
            return "America/St_Johns";
        }

        return easternFallback;
    }

    private static string InferAustraliaTimeZone(string stateCode, string longitude)
    {
        var normalizedStateCode = stateCode.Trim().ToUpperInvariant();
        if (normalizedStateCode is "WA") return "Australia/Perth";
        if (normalizedStateCode is "NT") return "Australia/Darwin";
        if (normalizedStateCode is "SA") return "Australia/Adelaide";
        if (normalizedStateCode is "QLD") return "Australia/Brisbane";
        if (new[] { "NSW", "ACT", "VIC", "TAS" }.Contains(normalizedStateCode, StringComparer.OrdinalIgnoreCase)) return "Australia/Sydney";

        return double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLongitude) switch
        {
            true when parsedLongitude < 129d => "Australia/Perth",
            true when parsedLongitude < 138d => "Australia/Darwin",
            true when parsedLongitude < 141d => "Australia/Adelaide",
            true => "Australia/Sydney",
            _ => "Australia/Sydney"
        };
    }

    private static string InferBrazilTimeZone(string longitude)
    {
        if (!double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLongitude))
        {
            return "America/Sao_Paulo";
        }

        if (parsedLongitude <= -67d)
        {
            return "America/Rio_Branco";
        }

        if (parsedLongitude <= -55d)
        {
            return "America/Cuiaba";
        }

        if (parsedLongitude <= -40d)
        {
            return "America/Sao_Paulo";
        }

        return "America/Fortaleza";
    }

    private static string Read(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out var value) ? value : string.Empty;

    private static string ToTitleCase(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim().ToLowerInvariant());

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string NormalizeBooleanFlag(string value)
        => value.Trim() switch
        {
            "1" => "true",
            "0" => "false",
            _ => value
        };

    private static string ResolvePrimaryDomainSuffix(string country, string countryCode)
        => (countryCode, country) switch
        {
            ("GB", _) or (_, "United Kingdom") => "co.uk",
            ("AU", _) or (_, "Australia") => "com.au",
            ("NZ", _) or (_, "New Zealand") => "co.nz",
            ("JP", _) or (_, "Japan") => "co.jp",
            ("BR", _) or (_, "Brazil") => "com.br",
            ("US", _) or (_, "United States") => "com",
            _ => countryCode.ToLowerInvariant()
        };

    private static string ResolveAlternateDomainSuffix(string countryCode, string primaryDomainSuffix)
    {
        var normalizedCode = countryCode.ToLowerInvariant();
        if (string.Equals(primaryDomainSuffix, "com", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedCode;
        }

        if (primaryDomainSuffix.Contains('.'))
        {
            return normalizedCode;
        }

        return "com";
    }

    private static string ResolvePhonePattern(string countryCode, string dialCode)
    {
        if (string.IsNullOrWhiteSpace(dialCode))
        {
            dialCode = "1";
        }

        return countryCode.ToUpperInvariant() switch
        {
            "US" or "CA" => $"+{dialCode} NPA-NXX-XXXX",
            "GB" => $"+{dialCode} XXXX XXXXXX",
            "AU" => $"+{dialCode} X XXXX XXXX",
            "DE" => $"+{dialCode} XXX XXXXXXX",
            "FR" => $"+{dialCode} X XX XX XX XX",
            "JP" => $"+{dialCode} XX XXXX XXXX",
            "IN" => $"+{dialCode} XXXXX XXXXX",
            _ => $"+{dialCode} XX XXXX XXXX"
        };
    }

    private static IEnumerable<PostalReferenceRow> EnumeratePostalReferenceRows(string filePath, IReadOnlyDictionary<string, CountryInfo> countryLookup)
    {
        foreach (var line in FileSystemCatalogLoader.EnumerateLinesShared(filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 12)
            {
                continue;
            }

            var countryCode = parts[0].Trim();
            var postalCode = parts[1].Trim();
            var city = NormalizePlaceName(parts[2]);
            if (string.IsNullOrWhiteSpace(countryCode) || string.IsNullOrWhiteSpace(postalCode) || string.IsNullOrWhiteSpace(city))
            {
                continue;
            }

            countryLookup.TryGetValue(countryCode, out var countryInfo);
            yield return new PostalReferenceRow(
                countryInfo?.Region ?? string.Empty,
                countryInfo?.Name ?? countryCode,
                countryCode,
                NormalizePlaceName(parts[3]),
                parts[4].Trim(),
                city,
                postalCode,
                parts[9].Trim(),
                parts[10].Trim(),
                parts[11].Trim(),
                InferTimeZone(countryInfo?.Name ?? countryCode, countryCode, parts[10].Trim(), parts[9].Trim(), parts[4].Trim()));
        }
    }

    private static bool IsBetterPostalReference(PostalReferenceRow candidate, PostalReferenceRow existing)
    {
        var candidateAccuracy = ParseAccuracy(candidate.Accuracy);
        var existingAccuracy = ParseAccuracy(existing.Accuracy);
        if (candidateAccuracy != existingAccuracy)
        {
            return candidateAccuracy > existingAccuracy;
        }

        if (!string.IsNullOrWhiteSpace(candidate.StateOrProvince) && string.IsNullOrWhiteSpace(existing.StateOrProvince))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(candidate.Latitude) && string.IsNullOrWhiteSpace(existing.Latitude))
        {
            return true;
        }

        return candidate.PostalCode.Length < existing.PostalCode.Length;
    }

    private static int ParseAccuracy(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static string NormalizePlaceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Any(char.IsLower)
            ? trimmed
            : ToTitleCase(trimmed);
    }

    private static string NormalizeKey(string value)
        => string.Concat(value.Trim().ToUpperInvariant().Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)));

    private sealed record ResolvedCatalogSource(string SourceRoot, string FullPath);

    private sealed record CountryInfo(string Name, string Region);

    private sealed record LocalityPopulationReference(
        string CountryCode,
        string Country,
        string Region,
        string City,
        string Latitude,
        string Longitude,
        long Population);

    private sealed record LocalityReferenceRow(
        string CountryCode,
        string StateCode,
        string StateOrProvince,
        string City,
        string PostalCode,
        string TimeZone,
        string Latitude,
        string Longitude,
        long Population,
        int Accuracy,
        string PopulationLookupKey);

    private sealed record PostalReferenceRow(
        string Region,
        string Country,
        string CountryCode,
        string StateOrProvince,
        string StateCode,
        string City,
        string PostalCode,
        string Latitude,
        string Longitude,
        string Accuracy,
        string TimeZone);
}
