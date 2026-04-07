namespace SyntheticEnterprise.Core.Generation.Infrastructure;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicInfrastructureAnomalyInjector : IAnomalyInjector
{
    private readonly IIdFactory _idFactory;

    public BasicInfrastructureAnomalyInjector(IIdFactory idFactory)
    {
        _idFactory = idFactory;
    }

    public void Apply(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs, AnomalyProfile profile)
    {
        if (!string.Equals(profile.Category, "Infrastructure", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        switch (profile.Name)
        {
            case "NonCompliantEndpoints":
                InjectNonCompliantEndpoints(world, profile);
                break;
            case "UnownedServers":
                InjectUnownedServers(world, profile);
                break;
            case "InactiveDevices":
                InjectInactiveDevices(world, profile);
                break;
        }
    }

    private void InjectNonCompliantEndpoints(SyntheticEnterpriseWorld world, AnomalyProfile profile)
    {
        var targets = world.Devices.Take(Math.Max(1, (int)(world.Devices.Count * Math.Min(1.0, profile.Intensity) * 0.05))).ToList();
        foreach (var device in targets)
        {
            var idx = world.Devices.FindIndex(d => d.Id == device.Id);
            if (idx >= 0)
            {
                world.Devices[idx] = device with { ComplianceState = "NonCompliant" };
                world.InfrastructureAnomalies.Add(new InfrastructureAnomaly
                {
                    Id = _idFactory.Next("ANOM"),
                    CompanyId = device.CompanyId,
                    Severity = "Medium",
                    AffectedObjectId = device.Id,
                    Description = "Endpoint is marked non-compliant."
                });
            }
        }
    }

    private void InjectUnownedServers(SyntheticEnterpriseWorld world, AnomalyProfile profile)
    {
        var targets = world.Servers.Take(Math.Max(1, (int)(world.Servers.Count * Math.Min(1.0, profile.Intensity) * 0.08))).ToList();
        foreach (var server in targets)
        {
            var idx = world.Servers.FindIndex(s => s.Id == server.Id);
            if (idx >= 0)
            {
                world.Servers[idx] = server with { OwnerTeamId = "" };
                world.InfrastructureAnomalies.Add(new InfrastructureAnomaly
                {
                    Id = _idFactory.Next("ANOM"),
                    CompanyId = server.CompanyId,
                    Severity = "High",
                    AffectedObjectId = server.Id,
                    Description = "Server has no owning team."
                });
            }
        }
    }

    private void InjectInactiveDevices(SyntheticEnterpriseWorld world, AnomalyProfile profile)
    {
        var targets = world.Devices.Take(Math.Max(1, (int)(world.Devices.Count * Math.Min(1.0, profile.Intensity) * 0.06))).ToList();
        foreach (var device in targets)
        {
            var idx = world.Devices.FindIndex(d => d.Id == device.Id);
            if (idx >= 0)
            {
                world.Devices[idx] = device with { LastSeen = DateTimeOffset.UtcNow.AddDays(-120) };
                world.InfrastructureAnomalies.Add(new InfrastructureAnomaly
                {
                    Id = _idFactory.Next("ANOM"),
                    CompanyId = device.CompanyId,
                    Severity = "Low",
                    AffectedObjectId = device.Id,
                    Description = "Device has not checked in recently."
                });
            }
        }
    }
}
