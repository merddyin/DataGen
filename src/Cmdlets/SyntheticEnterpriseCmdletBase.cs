using System.Management.Automation;
using SyntheticEnterprise.Module.Contracts;
using SyntheticEnterprise.Module.Services;

namespace SyntheticEnterprise.Module.Cmdlets;

public abstract class SyntheticEnterpriseCmdletBase : PSCmdlet
{
    protected IModuleCompositionRoot CompositionRoot { get; private set; } = new DefaultModuleCompositionRoot();

    protected override void BeginProcessing()
    {
        base.BeginProcessing();
        CompositionRoot = CreateCompositionRoot();
    }

    protected virtual IModuleCompositionRoot CreateCompositionRoot()
    {
        return new DefaultModuleCompositionRoot();
    }

    protected void WriteValidationIssues(IEnumerable<ValidationIssue> issues)
    {
        foreach (var issue in issues)
        {
            switch (issue.Severity)
            {
                case ValidationIssueSeverity.Error:
                    WriteError(new ErrorRecord(new InvalidOperationException(issue.Message), issue.Code, ErrorCategory.InvalidData, issue.Target));
                    break;
                case ValidationIssueSeverity.Warning:
                    WriteWarning(issue.Message);
                    break;
                default:
                    WriteVerbose(issue.Message);
                    break;
            }
        }
    }
}
