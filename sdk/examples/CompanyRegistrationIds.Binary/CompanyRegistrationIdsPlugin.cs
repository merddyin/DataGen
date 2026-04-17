using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Contracts.Plugins;

namespace CompanyRegistrationIds.Binary;

public sealed class CompanyRegistrationIdsPlugin : IExternalGenerationAssemblyPlugin
{
    public string Capability => "CompanyRegistrationIds";

    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
    {
        var records = new List<PluginGeneratedRecord>();

        foreach (var company in request.InputWorld.Companies)
        {
            var digits = new string(company.Id.Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(digits))
            {
                digits = "0";
            }

            var registrationId = $"REG-{digits.PadLeft(6, '0')}";
            records.Add(new PluginGeneratedRecord
            {
                Id = $"SDK-{company.Id}",
                PluginCapability = Capability,
                RecordType = "CompanyRegistrationId",
                AssociatedEntityType = "Company",
                AssociatedEntityId = company.Id,
                Properties = new Dictionary<string, string?>
                {
                    ["RegistrationId"] = registrationId,
                    ["Scheme"] = "ExampleRegistrationScheme",
                    ["Source"] = "sdk-example-binary"
                }
            });
        }

        return new ExternalPluginExecutionResponse
        {
            Executed = true,
            Records = records,
            Warnings = new()
            {
                "sdk-binary-plugin-ok"
            }
        };
    }
}
