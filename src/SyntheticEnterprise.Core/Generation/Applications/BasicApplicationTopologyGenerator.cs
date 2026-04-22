namespace SyntheticEnterprise.Core.Generation.Applications;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicApplicationTopologyGenerator : IApplicationTopologyGenerator
{
    private readonly IIdFactory _idFactory;

    public BasicApplicationTopologyGenerator(IIdFactory idFactory)
    {
        _idFactory = idFactory;
    }

    public void GenerateTopology(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs)
    {
        var topologyPatterns = ReadApplicationTopologyPatterns(catalogs);
        var serviceDependencyPatterns = ReadApplicationServiceDependencyPatterns(catalogs);
        var serviceHostingPatterns = ReadApplicationServiceHostingPatterns(catalogs);

        foreach (var company in world.Companies)
        {
            var applications = world.Applications.Where(app => app.CompanyId == company.Id).ToList();
            var teams = world.Teams.Where(team => team.CompanyId == company.Id).ToList();
            var servers = world.Servers.Where(server => server.CompanyId == company.Id).ToList();
            var peopleCount = world.People.Count(person => person.CompanyId == company.Id);
            var industry = company.Industry;

            var primaryServiceByApplication = new Dictionary<string, ApplicationService>(StringComparer.OrdinalIgnoreCase);
            var servicesByApplication = new Dictionary<string, List<ApplicationService>>(StringComparer.OrdinalIgnoreCase);

            foreach (var application in applications)
            {
                var ownerTeam = SelectOwnerTeam(teams, application.OwnerDepartmentId);
                var topologyPattern = SelectApplicationTopologyPattern(topologyPatterns, company.Industry, peopleCount, application);
                var createdServices = CreateServicesForApplication(company, application, ownerTeam, topologyPattern);
                servicesByApplication[application.Id] = createdServices;

                foreach (var service in createdServices)
                {
                    world.ApplicationServices.Add(service);
                    world.ApplicationServiceHostings.Add(CreateHosting(
                        company,
                        application,
                        service,
                        servers,
                        topologyPattern,
                        serviceHostingPatterns,
                        peopleCount,
                        industry));
                }

                var frontend = createdServices.FirstOrDefault(service => service.ServiceType == "Frontend");
                var api = createdServices.FirstOrDefault(service => service.ServiceType == "API");
                var worker = createdServices.FirstOrDefault(service => service.ServiceType == "Worker");
                var integration = createdServices.FirstOrDefault(service => service.ServiceType == "Integration");
                var data = createdServices.FirstOrDefault(service => service.ServiceType == "Data");

                if (frontend is not null && api is not null)
                {
                    world.ApplicationServiceDependencies.Add(CreateDependency(company, frontend, api, "Internal", "HTTPS", "High"));
                }

                if (worker is not null && api is not null)
                {
                    world.ApplicationServiceDependencies.Add(CreateDependency(company, worker, api, "Internal", "Queue", "Medium"));
                }

                if (integration is not null && api is not null)
                {
                    world.ApplicationServiceDependencies.Add(CreateDependency(company, integration, api, "Internal", "API", "Medium"));
                }

                if (api is not null && data is not null)
                {
                    world.ApplicationServiceDependencies.Add(CreateDependency(company, api, data, "Internal", "TCP", application.Criticality));
                }

                primaryServiceByApplication[application.Id] = api
                    ?? frontend
                    ?? integration
                    ?? worker
                    ?? createdServices.First();
            }

            foreach (var dependency in world.ApplicationDependencies.Where(item => item.CompanyId == company.Id).ToList())
            {
                var sourceApplication = applications.FirstOrDefault(application => application.Id == dependency.SourceApplicationId);
                var targetApplication = applications.FirstOrDefault(application => application.Id == dependency.TargetApplicationId);
                if (sourceApplication is null || targetApplication is null)
                {
                    continue;
                }

                var serviceDependencyPattern = SelectApplicationServiceDependencyPattern(
                    serviceDependencyPatterns,
                    industry,
                    peopleCount,
                    sourceApplication,
                    targetApplication,
                    dependency);

                var sourceService = ResolveServiceForDependency(
                    dependency.SourceApplicationId,
                    serviceDependencyPattern?.SourceServiceType,
                    servicesByApplication,
                    primaryServiceByApplication);
                var targetService = ResolveServiceForDependency(
                    dependency.TargetApplicationId,
                    serviceDependencyPattern?.TargetServiceType,
                    servicesByApplication,
                    primaryServiceByApplication);
                if (sourceService is null || targetService is null)
                {
                    continue;
                }

                world.ApplicationServiceDependencies.Add(CreateDependency(
                    company,
                    sourceService,
                    targetService,
                    FirstNonEmpty(serviceDependencyPattern?.DependencyType, dependency.DependencyType),
                    FirstNonEmpty(serviceDependencyPattern?.InterfaceType, dependency.InterfaceType),
                    FirstNonEmpty(serviceDependencyPattern?.Criticality, dependency.Criticality)));
            }
        }
    }

    private List<ApplicationService> CreateServicesForApplication(
        Company company,
        ApplicationRecord application,
        Team? ownerTeam,
        ApplicationTopologyPattern? topologyPattern)
    {
        var deploymentModel = topologyPattern?.DeploymentModel ?? ResolveDeploymentModel(application.HostingModel, application.Category);
        var runtime = ResolveRuntime(application, topologyPattern);
        var services = new List<ApplicationService>();

        var addFrontend = topologyPattern?.AddFrontend ?? ShouldAddFrontend(application);
        var addApi = topologyPattern?.AddApi ?? ShouldAddApi(application);
        var addWorker = topologyPattern?.AddWorker ?? ShouldAddWorker(application);
        var addIntegration = topologyPattern?.AddIntegration ?? ShouldAddIntegration(application);
        var addData = topologyPattern?.AddData ?? ShouldAddData(application);

        if (addFrontend)
        {
            services.Add(CreateService(
                company,
                application,
                ownerTeam,
                BuildServiceName(application, "Frontend", topologyPattern?.FrontendRuntime ?? runtime),
                "Frontend",
                topologyPattern?.FrontendRuntime ?? runtime,
                deploymentModel));
        }

        if (addApi)
        {
            services.Add(CreateService(
                company,
                application,
                ownerTeam,
                BuildServiceName(application, "API", topologyPattern?.ApiRuntime ?? runtime),
                "API",
                topologyPattern?.ApiRuntime ?? runtime,
                deploymentModel));
        }

        if (addWorker)
        {
            services.Add(CreateService(
                company,
                application,
                ownerTeam,
                BuildServiceName(application, "Worker", topologyPattern?.WorkerRuntime ?? runtime),
                "Worker",
                topologyPattern?.WorkerRuntime ?? runtime,
                deploymentModel));
        }

        if (addIntegration)
        {
            services.Add(CreateService(
                company,
                application,
                ownerTeam,
                BuildServiceName(application, "Integration", topologyPattern?.IntegrationRuntime ?? runtime),
                "Integration",
                topologyPattern?.IntegrationRuntime ?? runtime,
                deploymentModel));
        }

        if (addData)
        {
            services.Add(CreateService(
                company,
                application,
                ownerTeam,
                BuildServiceName(application, "Data", topologyPattern?.DataRuntime ?? ResolveDataRuntime(application)),
                "Data",
                topologyPattern?.DataRuntime ?? ResolveDataRuntime(application),
                deploymentModel));
        }

        return services;
    }

    private ApplicationService CreateService(
        Company company,
        ApplicationRecord application,
        Team? ownerTeam,
        string serviceName,
        string serviceType,
        string runtime,
        string deploymentModel)
    {
        return new ApplicationService
        {
            Id = _idFactory.Next("APPSVC"),
            CompanyId = company.Id,
            ApplicationId = application.Id,
            Name = serviceName,
            ServiceType = serviceType,
            Runtime = runtime,
            DeploymentModel = deploymentModel,
            Environment = application.Environment,
            OwnerTeamId = ownerTeam?.Id ?? string.Empty,
            Criticality = application.Criticality
        };
    }

    private ApplicationServiceHosting CreateHosting(
        Company company,
        ApplicationRecord application,
        ApplicationService service,
        IReadOnlyList<ServerAsset> servers,
        ApplicationTopologyPattern? topologyPattern,
        IReadOnlyList<ApplicationServiceHostingPattern> hostingPatterns,
        int peopleCount,
        string industry)
    {
        var hostingPattern = SelectApplicationServiceHostingPattern(
            hostingPatterns,
            industry,
            peopleCount,
            application,
            service);

        if (string.Equals(hostingPattern?.HostType, "SaaSPlatform", StringComparison.OrdinalIgnoreCase)
            || (hostingPattern is null && application.HostingModel == "SaaS"))
        {
            return new ApplicationServiceHosting
            {
                Id = _idFactory.Next("APPHST"),
                CompanyId = company.Id,
                ApplicationServiceId = service.Id,
                HostType = "SaaSPlatform",
                HostName = FirstNonEmpty(
                    hostingPattern?.HostName,
                    string.IsNullOrWhiteSpace(application.Vendor) ? application.Name : $"{application.Vendor} Cloud"),
                HostingRole = FirstNonEmpty(hostingPattern?.HostingRole, service.ServiceType),
                DeploymentModel = service.DeploymentModel
            };
        }

        if (string.Equals(hostingPattern?.HostType, "ManagedPlatform", StringComparison.OrdinalIgnoreCase))
        {
            return new ApplicationServiceHosting
            {
                Id = _idFactory.Next("APPHST"),
                CompanyId = company.Id,
                ApplicationServiceId = service.Id,
                HostType = "ManagedPlatform",
                HostName = FirstNonEmpty(
                    hostingPattern!.HostName,
                    service.DeploymentModel is "Containerized" ? "Kubernetes Cluster" : "Virtual Machine Platform"),
                HostingRole = FirstNonEmpty(hostingPattern.HostingRole, service.ServiceType),
                DeploymentModel = service.DeploymentModel
            };
        }

        var preferredRole = FirstNonEmpty(hostingPattern?.PreferredHostRole, GetPreferredHostRole(service.ServiceType, topologyPattern));

        var hostServer = servers.FirstOrDefault(server =>
                             string.Equals(server.ServerRole, preferredRole, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(server.Environment, service.Environment, StringComparison.OrdinalIgnoreCase))
                         ?? servers.FirstOrDefault(server =>
                             string.Equals(server.ServerRole, preferredRole, StringComparison.OrdinalIgnoreCase))
                         ?? servers.FirstOrDefault(server =>
                             string.Equals(server.Environment, service.Environment, StringComparison.OrdinalIgnoreCase))
                         ?? servers.FirstOrDefault();

        if (hostServer is not null)
        {
            return new ApplicationServiceHosting
            {
                Id = _idFactory.Next("APPHST"),
                CompanyId = company.Id,
                ApplicationServiceId = service.Id,
                HostType = "Server",
                HostId = hostServer.Id,
                HostName = hostServer.Hostname,
                HostingRole = FirstNonEmpty(hostingPattern?.HostingRole, hostServer.ServerRole),
                DeploymentModel = service.DeploymentModel
            };
        }

        return new ApplicationServiceHosting
        {
            Id = _idFactory.Next("APPHST"),
            CompanyId = company.Id,
            ApplicationServiceId = service.Id,
            HostType = "ManagedPlatform",
            HostName = FirstNonEmpty(
                hostingPattern?.HostName,
                service.DeploymentModel is "Containerized" ? "Kubernetes Cluster" : "Virtual Machine Platform"),
            HostingRole = FirstNonEmpty(hostingPattern?.HostingRole, service.ServiceType),
            DeploymentModel = service.DeploymentModel
        };
    }

    private ApplicationServiceDependency CreateDependency(
        Company company,
        ApplicationService source,
        ApplicationService target,
        string dependencyType,
        string interfaceType,
        string criticality)
    {
        return new ApplicationServiceDependency
        {
            Id = _idFactory.Next("APPSD"),
            CompanyId = company.Id,
            SourceServiceId = source.Id,
            TargetServiceId = target.Id,
            DependencyType = dependencyType,
            InterfaceType = interfaceType,
            Criticality = criticality
        };
    }

    private static Team? SelectOwnerTeam(IReadOnlyList<Team> teams, string departmentId)
        => teams.FirstOrDefault(team => team.DepartmentId == departmentId)
           ?? teams.FirstOrDefault();

    private static bool ShouldAddFrontend(ApplicationRecord application)
    {
        if (string.Equals(application.HostingModel, "SaaS", StringComparison.OrdinalIgnoreCase))
        {
            return application.Name.Contains("Admin Center", StringComparison.OrdinalIgnoreCase)
                || application.Name.Contains("Portal", StringComparison.OrdinalIgnoreCase)
                || application.Name.Contains("Console", StringComparison.OrdinalIgnoreCase);
        }

        return application.Name.Contains("Portal", StringComparison.OrdinalIgnoreCase)
               || application.Name.Contains("Workspace", StringComparison.OrdinalIgnoreCase)
               || application.Name.Contains("Studio", StringComparison.OrdinalIgnoreCase)
               || application.Name.Contains("Console", StringComparison.OrdinalIgnoreCase)
               || application.Name.Contains("Hub", StringComparison.OrdinalIgnoreCase)
               || application.Category is "Collaboration" or "Productivity" or "Web" or "Sales" or "Marketing";
    }

    private static bool ShouldAddApi(ApplicationRecord application)
        => !string.Equals(application.HostingModel, "SaaS", StringComparison.OrdinalIgnoreCase)
           || application.Name.Contains("Admin Center", StringComparison.OrdinalIgnoreCase)
           || application.Category is "Security" or "Analytics";

    private static bool ShouldAddWorker(ApplicationRecord application)
    {
        if (string.Equals(application.HostingModel, "SaaS", StringComparison.OrdinalIgnoreCase))
        {
            return application.Category == "Analytics";
        }

        return application.Category is "Operations" or "Analytics" or "Security" or "Finance" or "Procurement";
    }

    private static bool ShouldAddIntegration(ApplicationRecord application)
        => application.HostingModel is "Hybrid" or "SaaS"
           || application.Name.Contains("Integration", StringComparison.OrdinalIgnoreCase)
           || application.Name.Contains("Exchange", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldAddData(ApplicationRecord application)
    {
        if (string.Equals(application.HostingModel, "SaaS", StringComparison.OrdinalIgnoreCase))
        {
            return application.Category is "Analytics" or "Database";
        }

        return application.Category is "Analytics" or "Finance" or "Operations" or "Database" or "Security"
               || application.Criticality == "High";
    }

    private static string BuildServiceName(ApplicationRecord application, string serviceType, string runtime)
        => serviceType switch
        {
            "Frontend" => $"{application.Name} Web Portal",
            "API" => $"{application.Name} API Service",
            "Worker" when string.Equals(runtime, "spark", StringComparison.OrdinalIgnoreCase) => $"{application.Name} Spark Jobs",
            "Worker" => $"{application.Name} Windows Service",
            "Integration" => $"{application.Name} Integration Service",
            "Data" when string.Equals(runtime, "spark", StringComparison.OrdinalIgnoreCase) => $"{application.Name} SQL Warehouse",
            "Data" => $"{application.Name} Database Jobs",
            _ => $"{application.Name} {serviceType}"
        };

    private static string ResolveDeploymentModel(string hostingModel, string category)
        => hostingModel switch
        {
            "SaaS" => "VendorManaged",
            "Hybrid" when category is "Developer" or "Analytics" or "Web" => "Containerized",
            "Hybrid" => "VirtualMachine",
            "OnPrem" when category is "Web" or "Developer" => "Containerized",
            _ => "VirtualMachine"
        };

    private static string ResolveRuntime(ApplicationRecord application)
    {
        if (application.Name.Contains("SQL", StringComparison.OrdinalIgnoreCase))
        {
            return "sql";
        }

        return application.Category switch
        {
            "Analytics" => "dotnet",
            "Developer" => "dotnet",
            "Web" => "dotnet",
            "Security" => "dotnet",
            "Finance" => "java",
            "Operations" => "dotnet",
            "Sales" or "Marketing" => "node",
            "Collaboration" or "Productivity" when application.HostingModel == "SaaS" => "saas",
            _ => application.HostingModel == "SaaS" ? "saas" : "dotnet"
        };
    }

    private static string ResolveRuntime(ApplicationRecord application, ApplicationTopologyPattern? topologyPattern)
        => topologyPattern?.ApiRuntime
           ?? ResolveRuntime(application);

    private static string ResolveDataRuntime(ApplicationRecord application)
        => application.Category switch
        {
            "Analytics" => "spark",
            "Database" => "sql",
            _ => "sql"
        };

    private static string GetPreferredHostRole(string serviceType, ApplicationTopologyPattern? topologyPattern)
        => serviceType switch
        {
            "Frontend" => FirstNonEmpty(topologyPattern?.PreferredFrontendHostRole, "Web Server"),
            "API" => FirstNonEmpty(topologyPattern?.PreferredApiHostRole, "Application Server"),
            "Worker" => FirstNonEmpty(topologyPattern?.PreferredWorkerHostRole, "Application Server"),
            "Integration" => FirstNonEmpty(topologyPattern?.PreferredIntegrationHostRole, "Application Server"),
            "Data" => FirstNonEmpty(topologyPattern?.PreferredDataHostRole, "SQL Server"),
            _ => "Application Server"
        };

    private static ApplicationTopologyPattern? SelectApplicationTopologyPattern(
        IReadOnlyList<ApplicationTopologyPattern> patterns,
        string industry,
        int peopleCount,
        ApplicationRecord application)
    {
        var industryTokens = BuildIndustryTokens(industry);

        return patterns
            .Where(pattern => pattern.MinimumEmployees <= Math.Max(1, peopleCount))
            .Where(pattern => pattern.IndustryTags.Count == 0
                              || pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                              || pattern.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .Where(pattern => MatchesPattern(application, pattern))
            .OrderByDescending(GetPatternSpecificity)
            .FirstOrDefault();
    }

    private static bool MatchesPattern(ApplicationRecord application, ApplicationTopologyPattern pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern.MatchNameContains)
            && !application.Name.Contains(pattern.MatchNameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchVendor)
            && !string.Equals(application.Vendor, pattern.MatchVendor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchCategory)
            && !string.Equals(application.Category, pattern.MatchCategory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchHostingModel)
            && !string.Equals(application.HostingModel, pattern.MatchHostingModel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static int GetPatternSpecificity(ApplicationTopologyPattern pattern)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(pattern.MatchNameContains))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchVendor))
        {
            score += 3;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchCategory))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchHostingModel))
        {
            score += 1;
        }

        if (pattern.IndustryTags.Count > 0 && !pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase))
        {
            score += 1;
        }

        return score;
    }

    private static ApplicationService? ResolveServiceForDependency(
        string applicationId,
        string? preferredServiceType,
        IReadOnlyDictionary<string, List<ApplicationService>> servicesByApplication,
        IReadOnlyDictionary<string, ApplicationService> primaryServiceByApplication)
    {
        if (servicesByApplication.TryGetValue(applicationId, out var services)
            && !string.IsNullOrWhiteSpace(preferredServiceType))
        {
            var matchedService = services.FirstOrDefault(service =>
                string.Equals(service.ServiceType, preferredServiceType, StringComparison.OrdinalIgnoreCase));
            if (matchedService is not null)
            {
                return matchedService;
            }
        }

        return primaryServiceByApplication.TryGetValue(applicationId, out var primaryService)
            ? primaryService
            : null;
    }

    private static ApplicationServiceDependencyPattern? SelectApplicationServiceDependencyPattern(
        IReadOnlyList<ApplicationServiceDependencyPattern> patterns,
        string industry,
        int peopleCount,
        ApplicationRecord sourceApplication,
        ApplicationRecord targetApplication,
        ApplicationDependency dependency)
    {
        var industryTokens = BuildIndustryTokens(industry);

        return patterns
            .Where(pattern => pattern.MinimumEmployees <= Math.Max(1, peopleCount))
            .Where(pattern => pattern.IndustryTags.Count == 0
                              || pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                              || pattern.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .Where(pattern => MatchesServiceDependencyPattern(sourceApplication, targetApplication, dependency, pattern))
            .OrderByDescending(GetPatternSpecificity)
            .FirstOrDefault();
    }

    private static bool MatchesServiceDependencyPattern(
        ApplicationRecord sourceApplication,
        ApplicationRecord targetApplication,
        ApplicationDependency dependency,
        ApplicationServiceDependencyPattern pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern.MatchSourceNameContains)
            && !sourceApplication.Name.Contains(pattern.MatchSourceNameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchTargetNameContains)
            && !targetApplication.Name.Contains(pattern.MatchTargetNameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchDependencyType)
            && !string.Equals(dependency.DependencyType, pattern.MatchDependencyType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchSourceVendor)
            && !string.Equals(sourceApplication.Vendor, pattern.MatchSourceVendor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchTargetVendor)
            && !string.Equals(targetApplication.Vendor, pattern.MatchTargetVendor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchSourceCategory)
            && !string.Equals(sourceApplication.Category, pattern.MatchSourceCategory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchTargetCategory)
            && !string.Equals(targetApplication.Category, pattern.MatchTargetCategory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static int GetPatternSpecificity(ApplicationServiceDependencyPattern pattern)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(pattern.MatchSourceNameContains))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchTargetNameContains))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchDependencyType))
        {
            score += 3;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchSourceVendor))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchTargetVendor))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchSourceCategory))
        {
            score += 1;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchTargetCategory))
        {
            score += 1;
        }

        if (pattern.IndustryTags.Count > 0 && !pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase))
        {
            score += 1;
        }

        return score;
    }

    private static ApplicationServiceHostingPattern? SelectApplicationServiceHostingPattern(
        IReadOnlyList<ApplicationServiceHostingPattern> patterns,
        string industry,
        int peopleCount,
        ApplicationRecord application,
        ApplicationService service)
    {
        var industryTokens = BuildIndustryTokens(industry);

        return patterns
            .Where(pattern => pattern.MinimumEmployees <= Math.Max(1, peopleCount))
            .Where(pattern => pattern.IndustryTags.Count == 0
                              || pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase)
                              || pattern.IndustryTags.Any(tag => industryTokens.Contains(tag)))
            .Where(pattern => MatchesServiceHostingPattern(application, service, pattern))
            .OrderByDescending(GetPatternSpecificity)
            .FirstOrDefault();
    }

    private static bool MatchesServiceHostingPattern(
        ApplicationRecord application,
        ApplicationService service,
        ApplicationServiceHostingPattern pattern)
    {
        if (!string.IsNullOrWhiteSpace(pattern.MatchNameContains)
            && !application.Name.Contains(pattern.MatchNameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchVendor)
            && !string.Equals(application.Vendor, pattern.MatchVendor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchServiceType)
            && !string.Equals(service.ServiceType, pattern.MatchServiceType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchCategory)
            && !string.Equals(application.Category, pattern.MatchCategory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchDeploymentModel)
            && !string.Equals(service.DeploymentModel, pattern.MatchDeploymentModel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static int GetPatternSpecificity(ApplicationServiceHostingPattern pattern)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(pattern.MatchNameContains))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchVendor))
        {
            score += 3;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchServiceType))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchCategory))
        {
            score += 1;
        }

        if (!string.IsNullOrWhiteSpace(pattern.MatchDeploymentModel))
        {
            score += 1;
        }

        if (pattern.IndustryTags.Count > 0 && !pattern.IndustryTags.Contains("All", StringComparer.OrdinalIgnoreCase))
        {
            score += 1;
        }

        return score;
    }

    private static IReadOnlyList<ApplicationTopologyPattern> ReadApplicationTopologyPatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("application_topology_patterns", out var rows))
        {
            return Array.Empty<ApplicationTopologyPattern>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "MatchNameContains"))
                          || !string.IsNullOrWhiteSpace(Read(row, "MatchVendor"))
                          || !string.IsNullOrWhiteSpace(Read(row, "MatchCategory")))
            .Select(row => new ApplicationTopologyPattern(
                Read(row, "MatchNameContains"),
                Read(row, "MatchVendor"),
                Read(row, "MatchCategory"),
                Read(row, "MatchHostingModel"),
                SplitPipeSeparated(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                ReadNullableBool(row, "AddFrontend"),
                ReadNullableBool(row, "AddApi"),
                ReadNullableBool(row, "AddWorker"),
                ReadNullableBool(row, "AddIntegration"),
                ReadNullableBool(row, "AddData"),
                Read(row, "FrontendRuntime"),
                Read(row, "ApiRuntime"),
                Read(row, "WorkerRuntime"),
                Read(row, "IntegrationRuntime"),
                Read(row, "DataRuntime"),
                Read(row, "PreferredFrontendHostRole"),
                Read(row, "PreferredApiHostRole"),
                Read(row, "PreferredWorkerHostRole"),
                Read(row, "PreferredIntegrationHostRole"),
                Read(row, "PreferredDataHostRole"),
                Read(row, "DeploymentModel")))
            .ToList();
    }

    private static IReadOnlyList<ApplicationServiceDependencyPattern> ReadApplicationServiceDependencyPatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("application_service_dependency_patterns", out var rows))
        {
            return Array.Empty<ApplicationServiceDependencyPattern>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "MatchSourceNameContains"))
                          || !string.IsNullOrWhiteSpace(Read(row, "MatchTargetNameContains"))
                          || !string.IsNullOrWhiteSpace(Read(row, "MatchDependencyType")))
            .Select(row => new ApplicationServiceDependencyPattern(
                Read(row, "MatchSourceNameContains"),
                Read(row, "MatchTargetNameContains"),
                Read(row, "MatchDependencyType"),
                Read(row, "MatchSourceVendor"),
                Read(row, "MatchTargetVendor"),
                Read(row, "MatchSourceCategory"),
                Read(row, "MatchTargetCategory"),
                SplitPipeSeparated(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "SourceServiceType"),
                Read(row, "TargetServiceType"),
                Read(row, "InterfaceType"),
                Read(row, "DependencyType"),
                Read(row, "Criticality")))
            .ToList();
    }

    private static IReadOnlyList<ApplicationServiceHostingPattern> ReadApplicationServiceHostingPatterns(CatalogSet catalogs)
    {
        if (!catalogs.CsvCatalogs.TryGetValue("application_service_hosting_patterns", out var rows))
        {
            return Array.Empty<ApplicationServiceHostingPattern>();
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(Read(row, "MatchNameContains"))
                          || !string.IsNullOrWhiteSpace(Read(row, "MatchVendor"))
                          || !string.IsNullOrWhiteSpace(Read(row, "MatchServiceType")))
            .Select(row => new ApplicationServiceHostingPattern(
                Read(row, "MatchNameContains"),
                Read(row, "MatchVendor"),
                Read(row, "MatchServiceType"),
                Read(row, "MatchCategory"),
                Read(row, "MatchDeploymentModel"),
                SplitPipeSeparated(Read(row, "IndustryTags")),
                int.TryParse(Read(row, "MinimumEmployees"), out var minimumEmployees) ? minimumEmployees : 0,
                Read(row, "HostType"),
                Read(row, "PreferredHostRole"),
                Read(row, "HostName"),
                Read(row, "HostingRole")))
            .ToList();
    }

    private static bool? ReadNullableBool(IReadOnlyDictionary<string, string?> row, string key)
    {
        var value = Read(row, key);
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<string> SplitPipeSeparated(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static string Read(IReadOnlyDictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

    private static HashSet<string> BuildIndustryTokens(string industry)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(industry))
        {
            return tokens;
        }

        foreach (var token in industry.Split(['|', ',', '/', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            tokens.Add(token);
        }

        return tokens;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private sealed record ApplicationTopologyPattern(
        string MatchNameContains,
        string MatchVendor,
        string MatchCategory,
        string MatchHostingModel,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        bool? AddFrontend,
        bool? AddApi,
        bool? AddWorker,
        bool? AddIntegration,
        bool? AddData,
        string FrontendRuntime,
        string ApiRuntime,
        string WorkerRuntime,
        string IntegrationRuntime,
        string DataRuntime,
        string PreferredFrontendHostRole,
        string PreferredApiHostRole,
        string PreferredWorkerHostRole,
        string PreferredIntegrationHostRole,
        string PreferredDataHostRole,
        string DeploymentModel);

    private sealed record ApplicationServiceDependencyPattern(
        string MatchSourceNameContains,
        string MatchTargetNameContains,
        string MatchDependencyType,
        string MatchSourceVendor,
        string MatchTargetVendor,
        string MatchSourceCategory,
        string MatchTargetCategory,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        string SourceServiceType,
        string TargetServiceType,
        string InterfaceType,
        string DependencyType,
        string Criticality);

    private sealed record ApplicationServiceHostingPattern(
        string MatchNameContains,
        string MatchVendor,
        string MatchServiceType,
        string MatchCategory,
        string MatchDeploymentModel,
        IReadOnlyList<string> IndustryTags,
        int MinimumEmployees,
        string HostType,
        string PreferredHostRole,
        string HostName,
        string HostingRole);
}
