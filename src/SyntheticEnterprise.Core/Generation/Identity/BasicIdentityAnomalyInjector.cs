namespace SyntheticEnterprise.Core.Generation.Identity;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class BasicIdentityAnomalyInjector : IAnomalyInjector
{
    private readonly IIdFactory _idFactory;
    private readonly IRandomSource _randomSource;

    public BasicIdentityAnomalyInjector(IIdFactory idFactory, IRandomSource randomSource)
    {
        _idFactory = idFactory;
        _randomSource = randomSource;
    }

    public void Apply(SyntheticEnterpriseWorld world, GenerationContext context, CatalogSet catalogs, AnomalyProfile profile)
    {
        if (!string.Equals(profile.Category, "Identity", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var candidateAccounts = world.Accounts
            .Where(a => a.AccountType == "User" || a.AccountType == "Privileged" || a.AccountType == "Service")
            .ToList();

        if (candidateAccounts.Count == 0)
        {
            return;
        }

        var targetCount = Math.Max(1, (int)Math.Round(candidateAccounts.Count * Math.Min(profile.Intensity, 1.0) * 0.05));

        for (var i = 0; i < targetCount && i < candidateAccounts.Count; i++)
        {
            var account = candidateAccounts[i];

            switch (profile.Name)
            {
                case "PrivilegedNoMfa":
                    world.Accounts[world.Accounts.FindIndex(a => a.Id == account.Id)] = account with
                    {
                        Privileged = true,
                        MfaEnabled = false
                    };

                    world.IdentityAnomalies.Add(new IdentityAnomaly
                    {
                        Id = _idFactory.Next("ANOM"),
                        CompanyId = account.CompanyId,
                        Category = "Identity",
                        Severity = "High",
                        AffectedObjectId = account.Id,
                        Description = "Privileged account without MFA."
                    });
                    break;

                case "DisabledUsersInGroups":
                    world.Accounts[world.Accounts.FindIndex(a => a.Id == account.Id)] = account with
                    {
                        Enabled = false
                    };

                    world.IdentityAnomalies.Add(new IdentityAnomaly
                    {
                        Id = _idFactory.Next("ANOM"),
                        CompanyId = account.CompanyId,
                        Category = "Identity",
                        Severity = "Medium",
                        AffectedObjectId = account.Id,
                        Description = "Disabled account still present in security groups."
                    });
                    break;

                case "StaleServiceAccounts":
                    if (account.AccountType != "Service") continue;
                    world.IdentityAnomalies.Add(new IdentityAnomaly
                    {
                        Id = _idFactory.Next("ANOM"),
                        CompanyId = account.CompanyId,
                        Category = "Identity",
                        Severity = "Medium",
                        AffectedObjectId = account.Id,
                        Description = "Service account appears stale and lacks ownership metadata."
                    });
                    break;

                default:
                    if (_randomSource.NextDouble() < 0.2)
                    {
                        world.IdentityAnomalies.Add(new IdentityAnomaly
                        {
                            Id = _idFactory.Next("ANOM"),
                            CompanyId = account.CompanyId,
                            Category = "Identity",
                            Severity = "Low",
                            AffectedObjectId = account.Id,
                            Description = $"Generic identity anomaly injected by profile {profile.Name}."
                        });
                    }
                    break;
            }
        }
    }
}
