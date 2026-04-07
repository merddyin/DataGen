namespace SyntheticEnterprise.Core.Generation.Repository;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicRepositoryAnomalyInjector : IAnomalyInjector
{
    private readonly IIdFactory _idFactory;

    public BasicRepositoryAnomalyInjector(IIdFactory idFactory)
    {
        _idFactory = idFactory;
    }

    public void Apply(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs, AnomalyProfile profile)
    {
        if (!string.Equals(profile.Category, "Repository", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        switch (profile.Name)
        {
            case "OpenShares":
                InjectOpenShares(world, profile);
                break;
            case "OrphanedSites":
                InjectOrphanedSites(world, profile);
                break;
            case "SensitiveDatabaseBroadAccess":
                InjectSensitiveDatabaseBroadAccess(world, profile);
                break;
        }
    }

    private void InjectOpenShares(SyntheticEnterpriseWorld world, AnomalyProfile profile)
    {
        var targets = world.FileShares.Take(Math.Max(1, (int)(world.FileShares.Count * Math.Min(1.0, profile.Intensity) * 0.1))).ToList();
        foreach (var share in targets)
        {
            var idx = world.FileShares.FindIndex(s => s.Id == share.Id);
            if (idx >= 0)
            {
                world.FileShares[idx] = share with { AccessModel = "Open" };
                world.RepositoryAnomalies.Add(new RepositoryAnomaly
                {
                    Id = _idFactory.Next("ANOM"),
                    CompanyId = share.CompanyId,
                    Severity = "High",
                    AffectedObjectId = share.Id,
                    Description = "File share is overly broad or effectively open."
                });
            }
        }
    }

    private void InjectOrphanedSites(SyntheticEnterpriseWorld world, AnomalyProfile profile)
    {
        var targets = world.CollaborationSites.Take(Math.Max(1, (int)(world.CollaborationSites.Count * Math.Min(1.0, profile.Intensity) * 0.08))).ToList();
        foreach (var site in targets)
        {
            var idx = world.CollaborationSites.FindIndex(s => s.Id == site.Id);
            if (idx >= 0)
            {
                world.CollaborationSites[idx] = site with { OwnerPersonId = "" };
                world.RepositoryAnomalies.Add(new RepositoryAnomaly
                {
                    Id = _idFactory.Next("ANOM"),
                    CompanyId = site.CompanyId,
                    Severity = "Medium",
                    AffectedObjectId = site.Id,
                    Description = "Collaboration site has no clear owner."
                });
            }
        }
    }

    private void InjectSensitiveDatabaseBroadAccess(SyntheticEnterpriseWorld world, AnomalyProfile profile)
    {
        var targets = world.Databases.Where(d => d.Sensitivity == "Restricted").Take(Math.Max(1, (int)(world.Databases.Count * Math.Min(1.0, profile.Intensity) * 0.06))).ToList();
        foreach (var db in targets)
        {
            world.RepositoryAnomalies.Add(new RepositoryAnomaly
            {
                Id = _idFactory.Next("ANOM"),
                CompanyId = db.CompanyId,
                Severity = "High",
                AffectedObjectId = db.Id,
                Description = "Restricted database appears to have broader-than-expected access."
            });
        }
    }
}
