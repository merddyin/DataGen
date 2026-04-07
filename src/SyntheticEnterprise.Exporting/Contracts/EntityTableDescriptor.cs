using System;
using System.Collections.Generic;

namespace SyntheticEnterprise.Exporting.Contracts;

public sealed class EntityTableDescriptor<TRecord>
{
    public required string LogicalName { get; init; }
    public required string RelativePathStem { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required Func<object, IEnumerable<TRecord>> RecordAccessor { get; init; }
    public required Func<TRecord, IReadOnlyDictionary<string, object?>> RowProjector { get; init; }
    public required Func<TRecord, string> SortKeySelector { get; init; }
}
