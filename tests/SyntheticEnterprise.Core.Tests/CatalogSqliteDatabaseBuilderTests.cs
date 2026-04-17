using SyntheticEnterprise.Core.Catalogs;

namespace SyntheticEnterprise.Core.Tests;

public sealed class CatalogSqliteDatabaseBuilderTests
{
    [Fact]
    public void Build_Creates_Curated_Runtime_Tables_With_Metadata()
    {
        var tempRoot = CreateTempDirectory();
        var outputPath = Path.Combine(tempRoot, "catalogs.sqlite");

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "catalog-import-manifest.json"), """
                {
                  "version": "test-1",
                  "tables": [
                    { "tableName": "first_names_country", "strategy": "copy_csv", "sourceFiles": [ "first_names_country.csv" ] },
                    { "tableName": "last_names_country", "strategy": "copy_csv", "sourceFiles": [ "last_names_country.csv" ] },
                    { "tableName": "departments", "strategy": "line_list_name", "sourceFiles": [ "departments.txt" ] },
                    { "tableName": "titles", "strategy": "titles_merge", "sourceFiles": [ "deptjobtitles.csv", "generaltitles.csv" ] },
                    { "tableName": "industries", "strategy": "industry_taxonomy", "sourceFiles": [ "industries.csv" ] },
                    { "tableName": "company_size_bands", "strategy": "company_size_bands", "sourceFiles": [ "companysize.csv" ] },
                    { "tableName": "continents", "strategy": "copy_csv", "sourceFiles": [ "continents.csv" ] },
                    { "tableName": "company_name_elements", "strategy": "company_terms", "sourceFiles": [ "companynameelements.csv" ] },
                    { "tableName": "countries_reference", "strategy": "country_reference", "sourceFiles": [ "countries.csv" ] },
                    { "tableName": "country_identity_rules", "strategy": "country_identity_rules", "sourceFiles": [ "countries.csv" ] },
                    { "tableName": "locality_reference", "strategy": "locality_reference", "sourceFiles": [ "PostalCodes", "countries.csv", "CitiesByCountry-Filt.csv" ] },
                    { "tableName": "first_names_gendered", "strategy": "first_names_gendered", "sourceFiles": [ "firstnames.csv" ] },
                    { "tableName": "given_names_male", "strategy": "line_list_name", "sourceFiles": [ "given-male.txt" ] },
                    { "tableName": "given_names_female", "strategy": "line_list_name", "sourceFiles": [ "given-female.txt" ] },
                    { "tableName": "surnames_reference", "strategy": "line_list_value", "sourceFiles": [ "surnames.txt" ] },
                    { "tableName": "counterparty_name_prefixes", "strategy": "line_list_value", "sourceFiles": [ "counterparty-prefixes.txt" ] },
                    { "tableName": "counterparty_name_terms", "strategy": "line_list_value", "sourceFiles": [ "counterparty-terms.txt" ] },
                    { "tableName": "software_catalog", "strategy": "copy_csv", "sourceFiles": [ "software_catalog.csv" ] },
                    { "tableName": "essential_software_patterns", "strategy": "copy_csv", "sourceFiles": [ "essential_software_patterns.csv" ] },
                    { "tableName": "enterprise_platform_patterns", "strategy": "copy_csv", "sourceFiles": [ "enterprise_platform_patterns.csv" ] },
                    { "tableName": "application_templates", "strategy": "copy_csv", "sourceFiles": [ "application_templates.csv" ] },
                    { "tableName": "business_process_templates", "strategy": "copy_csv", "sourceFiles": [ "business_process_templates.csv" ] },
                    { "tableName": "industry_process_templates", "strategy": "copy_csv", "sourceFiles": [ "industry_process_templates.csv" ] },
                    { "tableName": "contextual_business_process_patterns", "strategy": "copy_csv", "sourceFiles": [ "contextual_business_process_patterns.csv" ] },
                    { "tableName": "industry_application_patterns", "strategy": "copy_csv", "sourceFiles": [ "industry_application_patterns.csv" ] },
                    { "tableName": "organization_templates", "strategy": "copy_csv", "sourceFiles": [ "organization_templates.csv" ] },
                    { "tableName": "department_application_patterns", "strategy": "copy_csv", "sourceFiles": [ "department_application_patterns.csv" ] },
                    { "tableName": "office_application_patterns", "strategy": "copy_csv", "sourceFiles": [ "office_application_patterns.csv" ] },
                    { "tableName": "vendor_reference", "strategy": "copy_csv", "sourceFiles": [ "vendor_reference.csv" ] },
                    { "tableName": "fake_companies_reference", "strategy": "fake_company_reference", "sourceFiles": [ "faux_id_fake_companies.csv" ] }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "countries.csv"), """
                Name,Code,Phone,Capital,Continent,PostalCode
                United States,US,1,Washington,North America,1
                """);
            File.WriteAllText(Path.Combine(tempRoot, "first_names_country.csv"), """
                Name,Gender,Country,Region
                James,Male,United States,North America
                """);
            File.WriteAllText(Path.Combine(tempRoot, "last_names_country.csv"), """
                Name,Country,Region
                Smith,United States,North America
                """);
            File.WriteAllText(Path.Combine(tempRoot, "departments.txt"), "Finance\nEngineering\n");
            File.WriteAllText(Path.Combine(tempRoot, "deptjobtitles.csv"), """
                Department,Title,Level
                Engineering,Platform Engineer,Experienced
                """);
            File.WriteAllText(Path.Combine(tempRoot, "generaltitles.csv"), """
                Title,Type
                Manager,Leadership
                """);
            File.WriteAllText(Path.Combine(tempRoot, "industries.csv"), """
                Sector,IndustryGroup,Industry,Sub-Industry
                Industrials,Capital Goods,Industrial Machinery,Industrial Components
                """);
            File.WriteAllText(Path.Combine(tempRoot, "companysize.csv"), """
                Size,EmployeeMin,EmployeeMax,LocationMax,Distribution,RevenueMin,RevenueMax
                Medium,1500,2500,50,"City,State",500000,2000000
                """);
            File.WriteAllText(Path.Combine(tempRoot, "continents.csv"), """
                name,code
                North America,NA
                """);
            File.WriteAllText(Path.Combine(tempRoot, "companynameelements.csv"), """
                Sector,Terms
                Manufacturing,Forge
                """);
            File.WriteAllText(Path.Combine(tempRoot, "CitiesByCountry-Filt.csv"), """
                Name,CountryCode,Latitude,Longitude,Population
                Chicago,US,41.85003,-87.65005,2746388
                United States,US,38.0000,-97.0000,331449281
                """);
            var postalRoot = Path.Combine(tempRoot, "PostalCodes");
            Directory.CreateDirectory(postalRoot);
            File.WriteAllText(Path.Combine(postalRoot, "US.txt"), """
                US	60601	CHICAGO	Illinois	IL	Cook	031			41.8864	-87.6186	6
                US	10001	NEW YORK	New York	NY	New York	061			40.7506	-73.9972	6
                """);
            File.WriteAllText(Path.Combine(tempRoot, "firstnames.csv"), """
                Name,Gender
                Alex,male
                Jamie,female
                """);
            File.WriteAllText(Path.Combine(tempRoot, "given-male.txt"), "Noah\nLiam\n");
            File.WriteAllText(Path.Combine(tempRoot, "given-female.txt"), "Emma\nAva\n");
            File.WriteAllText(Path.Combine(tempRoot, "surnames.txt"), "Carter\nPatel\n");
            File.WriteAllText(Path.Combine(tempRoot, "counterparty-prefixes.txt"), "Northwind\nHarbor\n");
            File.WriteAllText(Path.Combine(tempRoot, "counterparty-terms.txt"), "Distribution\nLogistics\n");
            File.WriteAllText(Path.Combine(tempRoot, "software_catalog.csv"), """
                Name,Category,Vendor,Version
                Microsoft 365 Apps,Productivity,Microsoft,2408
                Cisco AnyConnect,VPN,Cisco,5.1
                """);
            File.WriteAllText(Path.Combine(tempRoot, "essential_software_patterns.csv"), """
                IndustryTags,MinimumEmployees,Priority,Name
                All,0,10,Microsoft 365 Apps
                Manufacturing,500,20,Cisco AnyConnect
                """);
            File.WriteAllText(Path.Combine(tempRoot, "enterprise_platform_patterns.csv"), """
                IndustryTags,MinimumEmployees,MinimumOfficeCount,Name,Category,HostingModel
                All,0,0,{Company} ERP Core,Operations,Hybrid
                Manufacturing,500,0,{Company} Warehouse Management,Operations,Hybrid
                """);
            File.WriteAllText(Path.Combine(tempRoot, "application_templates.csv"), """
                TemplateType,Name,Category,Vendor,BusinessCapability,HostingModel,IndustryTags,MinimumEmployees,UserScope,Criticality,DataSensitivity
                Enterprise,Workday HCM,HR,Workday,Human Capital Management,SaaS,All,250,Enterprise,High,Confidential
                """);
            File.WriteAllText(Path.Combine(tempRoot, "business_process_templates.csv"), """
                IndustryTags,Name,Domain,BusinessCapability,OperatingModel,ProcessScope,Criticality,CustomerFacing,OwnerHints,MinimumEmployees
                All,Govern to Comply,Security,Governance and Compliance,Centralized,Enterprise,High,false,Security|Finance,250
                """);
            File.WriteAllText(Path.Combine(tempRoot, "industry_process_templates.csv"), """
                IndustryTags,Name,Domain,BusinessCapability,OperatingModel,ProcessScope,Criticality,CustomerFacing,OwnerHints,MinimumEmployees
                Manufacturing,Plan to Produce,Manufacturing,Manufacturing Planning,SiteBased,Enterprise,High,false,Operations|Engineering,250
                """);
            File.WriteAllText(Path.Combine(tempRoot, "contextual_business_process_patterns.csv"), """
                IndustryTags,RequiredDepartmentHints,MinimumEmployees,MinimumOfficeCount,Name,Domain,BusinessCapability,OperatingModel,ProcessScope,Criticality,CustomerFacing,OwnerHints
                Manufacturing,,250,2,Plan to Produce,Manufacturing,Manufacturing Planning,SiteBased,Enterprise,High,false,Operations|Engineering
                All,Support,50,1,Case to Resolution,Customer Service,Customer Support,Regional,BusinessUnit,Medium,true,Support|Operations
                """);
            File.WriteAllText(Path.Combine(tempRoot, "industry_application_patterns.csv"), """
                IndustryTags,MinimumEmployees,Name,Category,HostingModel
                Manufacturing,0,{Company} Production Planning,Operations,Hybrid
                All,0,{Company} Operations Portal,Operations,Hybrid
                """);
            File.WriteAllText(Path.Combine(tempRoot, "organization_templates.csv"), """
                Layer,IndustryTags,Name,ParentHints,MinimumEmployees
                BusinessUnit,Manufacturing,Supply Chain and Manufacturing,,100
                Department,Manufacturing,Operations,Supply Chain and Manufacturing,100
                Team,Manufacturing,Production Scheduling,Operations,100
                """);
            File.WriteAllText(Path.Combine(tempRoot, "department_application_patterns.csv"), """
                PatternType,DepartmentMatch,IndustryTags,MinimumEmployees,Name,Category,HostingModel
                Specialty,Finance,All,0,{Company} Revenue Recognition Desk,Finance,SaaS
                """);
            File.WriteAllText(Path.Combine(tempRoot, "office_application_patterns.csv"), """
                PatternType,IndustryTags,MinimumEmployees,Name,Category,HostingModel
                Base,All,0,{SitePrefix} Workplace Coordination,Operations,SaaS
                Industry,Manufacturing,250,{SitePrefix} Plant Operations Console,Operations,Hybrid
                """);
            File.WriteAllText(Path.Combine(tempRoot, "vendor_reference.csv"), """
                Name,Industry,Segment,Criticality,IndustryTags,OwnerHints,MinimumEmployees
                Fastenal,Industrial Supply,OperationalSupplier,Medium,Manufacturing,Procurement|Operations,50
                """);
            File.WriteAllText(Path.Combine(tempRoot, "faux_id_fake_companies.csv"), """
                id,fake-company-name,description,tagline,company-email,ein
                1,Acme North,Regional Supplier,Deliver Daily,ops@acme.test,12-3456789
                """);
            File.WriteAllText(Path.Combine(tempRoot, "identity_anomaly_profiles.json"), """{ "profiles": [ "baseline" ] }""");

            CatalogSqliteDatabaseBuilder.Build(outputPath, new[] { tempRoot });

            var loader = new FileSystemCatalogLoader();
            var catalogs = loader.LoadFromPath(outputPath);

            Assert.Equal("test-1", catalogs.BuildMetadata?.ManifestVersion);
            Assert.Contains(catalogs.CsvCatalogs["departments"], row => row["Name"] == "Finance");
            Assert.Contains(catalogs.CsvCatalogs["titles"], row => row["Title"] == "Platform Engineer");
            Assert.Contains(catalogs.CsvCatalogs["industries"], row => row["SubIndustry"] == "Industrial Components");
            Assert.Contains(catalogs.CsvCatalogs["company_size_bands"], row => row["Size"] == "Medium");
            Assert.Contains(catalogs.CsvCatalogs["continents"], row => row["name"] == "North America");
            Assert.Contains(catalogs.CsvCatalogs["countries_reference"], row => row["Code"] == "US" && row["PostalCodeSupported"] == "true");
            Assert.Contains(catalogs.CsvCatalogs["country_identity_rules"], row => row["Country"] == "United States" && row["PrimaryDomainSuffix"] == "com" && row["PhonePattern"] == "+1 NPA-NXX-XXXX");
            Assert.Contains(catalogs.CsvCatalogs["locality_reference"], row => row["CountryCode"] == "US" && row["City"] == "Chicago" && row["StateOrProvince"] == "Illinois" && row["TimeZone"] == "America/Chicago" && row["Population"] == "2746388");
            Assert.Contains(catalogs.CsvCatalogs["locality_reference"], row => row["CountryCode"] == "US" && row["City"] == "New York" && row["TimeZone"] == "America/New_York");
            Assert.DoesNotContain(catalogs.CsvCatalogs["locality_reference"], row => row["CountryCode"] == "US" && row["City"] == "United States");
            Assert.Contains(catalogs.CsvCatalogs["company_name_elements"], row => row["Term"] == "Forge");
            Assert.Contains(catalogs.CsvCatalogs["first_names_gendered"], row => row["Name"] == "Alex" && row["Gender"] == "Male");
            Assert.Contains(catalogs.CsvCatalogs["given_names_male"], row => row["Name"] == "Noah");
            Assert.Contains(catalogs.CsvCatalogs["given_names_female"], row => row["Name"] == "Emma");
            Assert.Contains(catalogs.CsvCatalogs["surnames_reference"], row => row["Value"] == "Patel");
            Assert.Contains(catalogs.CsvCatalogs["counterparty_name_prefixes"], row => row["Value"] == "Northwind");
            Assert.Contains(catalogs.CsvCatalogs["counterparty_name_terms"], row => row["Value"] == "Logistics");
            Assert.Contains(catalogs.CsvCatalogs["software_catalog"], row => row["Name"] == "Cisco AnyConnect");
            Assert.Contains(catalogs.CsvCatalogs["essential_software_patterns"], row => row["Name"] == "Cisco AnyConnect");
            Assert.Contains(catalogs.CsvCatalogs["enterprise_platform_patterns"], row => row["Name"] == "{Company} Warehouse Management");
            Assert.Contains(catalogs.CsvCatalogs["application_templates"], row => row["Name"] == "Workday HCM");
            Assert.Contains(catalogs.CsvCatalogs["business_process_templates"], row => row["Name"] == "Govern to Comply");
            Assert.Contains(catalogs.CsvCatalogs["industry_process_templates"], row => row["Name"] == "Plan to Produce");
            Assert.Contains(catalogs.CsvCatalogs["contextual_business_process_patterns"], row => row["Name"] == "Case to Resolution");
            Assert.Contains(catalogs.CsvCatalogs["industry_application_patterns"], row => row["Name"] == "{Company} Production Planning");
            Assert.Contains(catalogs.CsvCatalogs["organization_templates"], row => row["Name"] == "Supply Chain and Manufacturing");
            Assert.Contains(catalogs.CsvCatalogs["department_application_patterns"], row => row["Name"] == "{Company} Revenue Recognition Desk");
            Assert.Contains(catalogs.CsvCatalogs["office_application_patterns"], row => row["Name"] == "{SitePrefix} Plant Operations Console");
            Assert.Contains(catalogs.CsvCatalogs["vendor_reference"], row => row["Name"] == "Fastenal");
            Assert.Contains(catalogs.CsvCatalogs["fake_companies_reference"], row => row["CompanyName"] == "Acme North");
            Assert.DoesNotContain(catalogs.JsonCatalogs.Keys, key => key == "identity_anomaly_profiles");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "country_identity_rules" && source.Strategy == "country_identity_rules");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "locality_reference" && source.Strategy == "locality_reference");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "fake_companies_reference" && source.Strategy == "fake_company_reference");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "counterparty_name_prefixes" && source.Strategy == "line_list_value");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "counterparty_name_terms" && source.Strategy == "line_list_value");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "organization_templates" && source.Strategy == "copy_csv");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "contextual_business_process_patterns" && source.Strategy == "copy_csv");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "industry_application_patterns" && source.Strategy == "copy_csv");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "department_application_patterns" && source.Strategy == "copy_csv");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "office_application_patterns" && source.Strategy == "copy_csv");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "enterprise_platform_patterns" && source.Strategy == "copy_csv");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "vendor_reference" && source.Strategy == "copy_csv");
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (File.Exists(outputPath))
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        File.Delete(outputPath);
                        break;
                    }
                    catch (IOException)
                    {
                        if (attempt == 4)
                        {
                            break;
                        }

                        Thread.Sleep(200);
                    }
                }
            }

            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public void Build_LocalityReference_Filters_Administrative_Population_Rows()
    {
        var tempRoot = CreateTempDirectory();
        var outputPath = Path.Combine(tempRoot, "catalogs.sqlite");

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "catalog-import-manifest.json"), """
                {
                  "version": "test-locality-filter",
                  "tables": [
                    { "tableName": "countries_reference", "strategy": "country_reference", "sourceFiles": [ "countries.csv" ] },
                    { "tableName": "locality_reference", "strategy": "locality_reference", "sourceFiles": [ "PostalCodes", "countries.csv", "CitiesByCountry-Filt.csv" ] }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "countries.csv"), """
                Name,Code,Phone,Capital,Continent,PostalCode
                United Kingdom,GB,44,London,Europe,1
                China,CN,86,Beijing,Asia,1
                """);
            File.WriteAllText(Path.Combine(tempRoot, "CitiesByCountry-Filt.csv"), """
                Name,CountryCode,Latitude,Longitude,Population
                England,GB,52.3555,-1.1743,55268067
                London,GB,51.5072,-0.1276,8908081
                Banan Qu,CN,29.5000,106.5667,29914000
                Shanghai,CN,31.2222,121.4581,22315474
                """);

            Directory.CreateDirectory(Path.Combine(tempRoot, "PostalCodes"));

            CatalogSqliteDatabaseBuilder.Build(outputPath, new[] { tempRoot });

            var loader = new FileSystemCatalogLoader();
            var catalogs = loader.LoadFromPath(outputPath);

            Assert.DoesNotContain(catalogs.CsvCatalogs["locality_reference"], row => row["City"] == "England");
            Assert.DoesNotContain(catalogs.CsvCatalogs["locality_reference"], row => row["City"] == "Banan Qu");
            Assert.Contains(catalogs.CsvCatalogs["locality_reference"], row => row["City"] == "London");
            Assert.Contains(catalogs.CsvCatalogs["locality_reference"], row => row["City"] == "Shanghai");
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public void LoadDefault_Finds_Catalogs_By_Walking_Current_Directory_Parents()
    {
        var tempRoot = CreateTempDirectory();
        var repoRoot = Path.Combine(tempRoot, "workspace");
        var nestedRoot = Path.Combine(repoRoot, "src", "SyntheticEnterprise.PowerShell", "bin", "Debug", "net8.0");
        var catalogRoot = Path.Combine(repoRoot, "catalogs");
        var originalCurrentDirectory = Environment.CurrentDirectory;

        Directory.CreateDirectory(nestedRoot);
        Directory.CreateDirectory(catalogRoot);
        File.WriteAllText(Path.Combine(catalogRoot, "software_catalog.csv"), """
            Name,Category,Vendor,Version
            Test App,Productivity,Contoso,1.0
            """);

        try
        {
            Environment.CurrentDirectory = nestedRoot;

            var loader = new FileSystemCatalogLoader();
            var catalogs = loader.LoadDefault();

            Assert.Contains(catalogs.CsvCatalogs.Keys, key => key == "software_catalog");
            Assert.Contains(catalogs.CsvCatalogs["software_catalog"], row => row["Name"] == "Test App");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "software_catalog");
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;

            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public void LoadFromPath_Prefers_Seeded_Sqlite_Over_Raw_Runtime_Files()
    {
        var tempRoot = CreateTempDirectory();
        var catalogRoot = Path.Combine(tempRoot, "catalogs");
        var outputPath = Path.Combine(catalogRoot, "catalogs.sqlite");

        Directory.CreateDirectory(catalogRoot);

        try
        {
            File.WriteAllText(Path.Combine(catalogRoot, "catalog-import-manifest.json"), """
                {
                  "version": "test-sqlite-precedence",
                  "tables": [
                    { "tableName": "software_catalog", "strategy": "copy_csv", "sourceFiles": [ "software_catalog.csv" ] }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(catalogRoot, "software_catalog.csv"), """
                Name,Category,Vendor,Version
                Sqlite App,Security,Contoso,1.0
                """);

            CatalogSqliteDatabaseBuilder.Build(outputPath, new[] { catalogRoot });

            File.WriteAllText(Path.Combine(catalogRoot, "software_catalog.csv"), """
                Name,Category,Vendor,Version
                Filesystem App,Security,Contoso,2.0
                """);

            var loader = new FileSystemCatalogLoader();
            var catalogs = loader.LoadFromPath(catalogRoot);

            Assert.Contains(catalogs.CsvCatalogs.Keys, key => key == "software_catalog");
            Assert.Contains(catalogs.CsvCatalogs["software_catalog"], row => row["Name"] == "Sqlite App");
            Assert.DoesNotContain(catalogs.CsvCatalogs["software_catalog"], row => row["Name"] == "Filesystem App");
            Assert.Equal("test-sqlite-precedence", catalogs.BuildMetadata?.ManifestVersion);
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public void Build_Defaults_To_Curated_Runtime_Tables_Only()
    {
        var tempRoot = CreateTempDirectory();
        var outputPath = Path.Combine(tempRoot, "catalogs.sqlite");

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "catalog-import-manifest.json"), """
                {
                  "version": "test-curated-only",
                  "tables": [
                    { "tableName": "software_catalog", "strategy": "copy_csv", "sourceFiles": [ "software_catalog.csv" ] }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "software_catalog.csv"), """
                Name,Category,Vendor,Version
                Curated App,Security,Contoso,1.0
                """);
            File.WriteAllText(Path.Combine(tempRoot, "allCountries.csv"), """
                Name,CountryCode
                Spare Row,XX
                """);
            File.WriteAllText(Path.Combine(tempRoot, "identity_anomaly_profiles.json"), """{ "profiles": [ "baseline" ] }""");

            CatalogSqliteDatabaseBuilder.Build(outputPath, new[] { tempRoot });

            var loader = new FileSystemCatalogLoader();
            var catalogs = loader.LoadFromPath(outputPath);

            Assert.Contains(catalogs.CsvCatalogs.Keys, key => key == "software_catalog");
            Assert.DoesNotContain(catalogs.CsvCatalogs.Keys, key => key == "allCountries");
            Assert.DoesNotContain(catalogs.JsonCatalogs.Keys, key => key == "identity_anomaly_profiles");
            Assert.DoesNotContain(catalogs.Sources, source => source.CatalogName == "allCountries");
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    [Fact]
    public void Build_Can_Opt_In_To_Uncurated_Source_Copying()
    {
        var tempRoot = CreateTempDirectory();
        var outputPath = Path.Combine(tempRoot, "catalogs.sqlite");

        try
        {
            File.WriteAllText(Path.Combine(tempRoot, "catalog-import-manifest.json"), """
                {
                  "version": "test-uncurated-opt-in",
                  "tables": [
                    { "tableName": "software_catalog", "strategy": "copy_csv", "sourceFiles": [ "software_catalog.csv" ] }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(tempRoot, "software_catalog.csv"), """
                Name,Category,Vendor,Version
                Curated App,Security,Contoso,1.0
                """);
            File.WriteAllText(Path.Combine(tempRoot, "allCountries.csv"), """
                Name,CountryCode
                Spare Row,XX
                """);
            File.WriteAllText(Path.Combine(tempRoot, "identity_anomaly_profiles.json"), """{ "profiles": [ "baseline" ] }""");

            CatalogSqliteDatabaseBuilder.Build(outputPath, new[] { tempRoot }, includeUncuratedSources: true);

            var loader = new FileSystemCatalogLoader();
            var catalogs = loader.LoadFromPath(outputPath);

            Assert.Contains(catalogs.CsvCatalogs.Keys, key => key == "software_catalog");
            Assert.Contains(catalogs.CsvCatalogs.Keys, key => key == "allCountries");
            Assert.Contains(catalogs.JsonCatalogs.Keys, key => key == "identity_anomaly_profiles");
            Assert.Contains(catalogs.Sources, source => source.CatalogName == "allCountries" && source.Strategy == "copy_csv");
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"datagen-core-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
