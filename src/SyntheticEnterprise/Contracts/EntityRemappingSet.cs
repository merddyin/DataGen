using System.Collections.Generic;

namespace SyntheticEnterprise.Contracts;

public sealed class EntityRemappingSet
{
    public string? LayerName { get; set; }
    public List<EntityRemappingRecord> Records { get; } = new();
}
