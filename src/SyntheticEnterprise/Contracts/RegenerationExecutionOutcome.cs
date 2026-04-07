namespace SyntheticEnterprise.Contracts;

public enum RegenerationExecutionOutcome
{
    Applied = 0,
    Skipped = 1,
    Replaced = 2,
    Merged = 3,
    AppliedWithWarnings = 4,
    Failed = 5,
}
