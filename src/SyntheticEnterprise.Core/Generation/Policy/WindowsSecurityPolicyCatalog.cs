namespace SyntheticEnterprise.Core.Generation.Policy;

/// <summary>
/// Windows security-policy token vocabulary shared by every generator that emits
/// canonical <c>UserRight:</c>, <c>Audit:</c>, <c>FileACL:</c> and <c>RegistryACL:</c>
/// policy settings, so the baseline side and the per-endpoint side cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// The principal display names below are a deliberate <b>mirror</b> of the collector's
/// <c>SidResolver</c> behaviour, not an independent vocabulary. That resolver answers in
/// two tiers, and both tiers are reproduced here exactly:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Tier 1</b> — SIDs present in the resolver's curated well-known table. The table
/// short-circuits before any host lookup, so these strings are host-independent. Six of
/// them are curated forms that deliberately differ from what Windows itself returns
/// (<c>Local System</c> rather than <c>NT AUTHORITY\SYSTEM</c>, and so on). The curated
/// form is what a real collection produces, so the curated form is what is emitted.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Tier 2</b> — SIDs absent from that table. The resolver falls through to the live
/// Windows translation, so the Windows form is emitted, inconsistent casing included.
/// The <c>SeDeny*</c> rights of real security templates use these SIDs.
/// </description>
/// </item>
/// </list>
/// <para>
/// Bare SIDs are never emitted: a bare SID is what the resolver falls through to on a
/// non-Windows host, an artefact of offline parsing rather than a collection scenario
/// this generator models.
/// </para>
/// <para>
/// Because this is a mirror, it is coupled to the collector. If the collector's curated
/// table is rewritten upstream, the values emitted here must change in lockstep, as a
/// documented breaking change. Every string in this class was verified against the
/// collector's source or against shipped baseline content; do not re-case, reformat,
/// abbreviate or reorder them.
/// </para>
/// </remarks>
internal static class WindowsSecurityPolicyCatalog
{
    /// <summary>Canonical key prefix for a privilege-rights assignment.</summary>
    internal const string UserRightKeyPrefix = "UserRight:";

    /// <summary>Canonical key prefix for an advanced audit subcategory.</summary>
    internal const string AuditKeyPrefix = "Audit:";

    /// <summary>Canonical key prefix for a file-system security descriptor.</summary>
    internal const string FileAclKeyPrefix = "FileACL:";

    /// <summary>Canonical key prefix for a registry-key security descriptor.</summary>
    internal const string RegistryAclKeyPrefix = "RegistryACL:";

    /// <summary>
    /// Name of the directory policy object carrying the security-template vocabulary.
    /// Both the generator that emits it and every generator that reads it back use this
    /// constant, so the two can never disagree about which object is the baseline.
    /// </summary>
    internal const string SecurityTemplateBaselinePolicyName = "Windows Security Template Baseline";

    /// <summary>
    /// Name of the directory policy object carrying the advanced audit vocabulary.
    /// </summary>
    internal const string AdvancedAuditPolicyTemplatePolicyName = "Windows Advanced Audit Policy Template";

    /// <summary>Policy type of a directory-authored Group Policy object.</summary>
    internal const string GroupPolicyObjectPolicyType = "GroupPolicyObject";

    /// <summary>
    /// Policy type of the effective configuration a single machine actually carries, as
    /// <c>secedit</c> and <c>auditpol</c> report it on that machine.
    /// </summary>
    internal const string LocalSecurityPolicyPolicyType = "LocalSecurityPolicy";

    /// <summary>Platform of a local security policy object.</summary>
    internal const string WindowsPlatform = "Windows";

    /// <summary>Category of the directory-authored security-template policy object.</summary>
    internal const string SecurityTemplatePolicyCategory = "SecurityTemplate";

    /// <summary>Category of a per-machine effective configuration policy object.</summary>
    internal const string EffectiveConfigurationPolicyCategory = "EffectiveConfiguration";

    /// <summary>Setting category that resolves a row to the <c>SecTemplate</c> source.</summary>
    internal const string UserRightsAssignmentCategory = "UserRightsAssignment";

    /// <summary>Setting category that resolves a row to the <c>AuditCsv</c> source.</summary>
    internal const string AuditPolicyCategory = "AuditPolicy";

    /// <summary>
    /// Setting category for an audit row whose evidence came from a Group Policy report
    /// export rather than from a backup's <c>audit.csv</c>. Rows in this category carry the
    /// combined value as a report reader renders the numeric code, and resolve to the
    /// <c>GPO</c> source, so the value spelling and the recorded provenance agree.
    /// </summary>
    internal const string AuditPolicyReportCategory = "AuditPolicyReport";

    /// <summary>Setting category for a <c>[File Security]</c> security-template row.</summary>
    internal const string FileSecurityCategory = "FileSecurity";

    /// <summary>Setting category for a <c>[Registry Keys]</c> security-template row.</summary>
    internal const string RegistryKeysCategory = "RegistryKeys";

    /// <summary>
    /// Separator used between principals in a multi-principal rights assignment. The
    /// consumer splits on both <c>,</c> and <c>;</c> and trims, but a single form is used
    /// throughout so the emitted text is stable.
    /// </summary>
    internal const string PrincipalDelimiter = ",";

    /// <summary>
    /// Maximum length of a NetBIOS domain name, applied when qualifying a generated
    /// domain principal.
    /// </summary>
    private const int NetBiosNameMaximumLength = 15;

    /// <summary>
    /// Principal display names the collector's curated well-known table resolves
    /// directly, paired with the SID each one mirrors.
    /// </summary>
    internal static class Tier1Principals
    {
        internal const string Nobody = "Nobody";
        internal const string Everyone = "Everyone";
        internal const string Local = "Local";
        internal const string CreatorOwner = "Creator Owner";
        internal const string CreatorGroup = "Creator Group";
        internal const string Anonymous = "Anonymous";
        internal const string AuthenticatedUsers = "Authenticated Users";
        internal const string LocalSystem = "Local System";
        internal const string LocalService = "Local Service";
        internal const string NetworkService = "Network Service";
        internal const string Administrators = @"BUILTIN\Administrators";
        internal const string Users = @"BUILTIN\Users";
        internal const string Guests = @"BUILTIN\Guests";
        internal const string PowerUsers = @"BUILTIN\Power Users";
        internal const string AccountOperators = @"BUILTIN\Account Operators";
        internal const string ServerOperators = @"BUILTIN\Server Operators";
        internal const string PrintOperators = @"BUILTIN\Print Operators";
        internal const string BackupOperators = @"BUILTIN\Backup Operators";
        internal const string Replicators = @"BUILTIN\Replicators";
        internal const string PreWindows2000CompatibleAccess = @"BUILTIN\Pre-Windows 2000 Compatible Access";
        internal const string RemoteDesktopUsers = @"BUILTIN\Remote Desktop Users";
        internal const string NetworkConfigurationOperators = @"BUILTIN\Network Configuration Operators";
        internal const string IncomingForestTrustBuilders = @"BUILTIN\Incoming Forest Trust Builders";
        internal const string PerformanceMonitorUsers = @"BUILTIN\Performance Monitor Users";
        internal const string PerformanceLogUsers = @"BUILTIN\Performance Log Users";
        internal const string WindowsAuthorizationAccessGroup = @"BUILTIN\Windows Authorization Access Group";
        internal const string TerminalServerLicenseServers = @"BUILTIN\Terminal Server License Servers";
        internal const string DistributedComUsers = @"BUILTIN\Distributed COM Users";
    }

    /// <summary>
    /// Principal display names the collector obtains from the live Windows translation
    /// because the SID is absent from the curated table. The casing is inconsistent in
    /// Windows itself and is preserved as-is.
    /// </summary>
    internal static class Tier2Principals
    {
        internal const string Service = @"NT AUTHORITY\SERVICE";
        internal const string EnterpriseDomainControllers = @"NT AUTHORITY\ENTERPRISE DOMAIN CONTROLLERS";
        internal const string LocalAccount = @"NT AUTHORITY\Local account";
        internal const string LocalAccountAndMemberOfAdministratorsGroup = @"NT AUTHORITY\Local account and member of Administrators group";
    }

    /// <summary>
    /// The curated well-known table, each display name paired with the SID it mirrors.
    /// </summary>
    internal static readonly IReadOnlyList<(string Sid, string DisplayName)> Tier1PrincipalTable =
    [
        ("S-1-0-0", Tier1Principals.Nobody),
        ("S-1-1-0", Tier1Principals.Everyone),
        ("S-1-2-0", Tier1Principals.Local),
        ("S-1-3-0", Tier1Principals.CreatorOwner),
        ("S-1-3-1", Tier1Principals.CreatorGroup),
        ("S-1-5-7", Tier1Principals.Anonymous),
        ("S-1-5-11", Tier1Principals.AuthenticatedUsers),
        ("S-1-5-18", Tier1Principals.LocalSystem),
        ("S-1-5-19", Tier1Principals.LocalService),
        ("S-1-5-20", Tier1Principals.NetworkService),
        ("S-1-5-32-544", Tier1Principals.Administrators),
        ("S-1-5-32-545", Tier1Principals.Users),
        ("S-1-5-32-546", Tier1Principals.Guests),
        ("S-1-5-32-547", Tier1Principals.PowerUsers),
        ("S-1-5-32-548", Tier1Principals.AccountOperators),
        ("S-1-5-32-549", Tier1Principals.ServerOperators),
        ("S-1-5-32-550", Tier1Principals.PrintOperators),
        ("S-1-5-32-551", Tier1Principals.BackupOperators),
        ("S-1-5-32-552", Tier1Principals.Replicators),
        ("S-1-5-32-554", Tier1Principals.PreWindows2000CompatibleAccess),
        ("S-1-5-32-555", Tier1Principals.RemoteDesktopUsers),
        ("S-1-5-32-556", Tier1Principals.NetworkConfigurationOperators),
        ("S-1-5-32-557", Tier1Principals.IncomingForestTrustBuilders),
        ("S-1-5-32-558", Tier1Principals.PerformanceMonitorUsers),
        ("S-1-5-32-559", Tier1Principals.PerformanceLogUsers),
        ("S-1-5-32-560", Tier1Principals.WindowsAuthorizationAccessGroup),
        ("S-1-5-32-561", Tier1Principals.TerminalServerLicenseServers),
        ("S-1-5-32-562", Tier1Principals.DistributedComUsers)
    ];

    /// <summary>
    /// SIDs outside the curated table together with the live Windows translation the
    /// collector falls through to for them.
    /// </summary>
    internal static readonly IReadOnlyList<(string Sid, string DisplayName)> Tier2PrincipalTable =
    [
        ("S-1-5-6", Tier2Principals.Service),
        ("S-1-5-9", Tier2Principals.EnterpriseDomainControllers),
        ("S-1-5-113", Tier2Principals.LocalAccount),
        ("S-1-5-114", Tier2Principals.LocalAccountAndMemberOfAdministratorsGroup)
    ];

    /// <summary>
    /// Privilege constants whose direction is inverted: a principal listed here is
    /// denied the logon type rather than granted it.
    /// </summary>
    internal static class DenyRights
    {
        internal const string NetworkLogon = "SeDenyNetworkLogonRight";
        internal const string InteractiveLogon = "SeDenyInteractiveLogonRight";
        internal const string BatchLogon = "SeDenyBatchLogonRight";
        internal const string ServiceLogon = "SeDenyServiceLogonRight";
        internal const string RemoteInteractiveLogon = "SeDenyRemoteInteractiveLogonRight";
    }

    /// <summary>Privilege constants that grant a privilege or logon right.</summary>
    internal static class GrantRights
    {
        internal const string Debug = "SeDebugPrivilege";
        internal const string Backup = "SeBackupPrivilege";
        internal const string Restore = "SeRestorePrivilege";
        internal const string TakeOwnership = "SeTakeOwnershipPrivilege";
        internal const string LoadDriver = "SeLoadDriverPrivilege";
        internal const string Security = "SeSecurityPrivilege";
        internal const string SystemTime = "SeSystemtimePrivilege";
        internal const string RemoteShutdown = "SeRemoteShutdownPrivilege";
        internal const string Shutdown = "SeShutdownPrivilege";
        internal const string Impersonate = "SeImpersonatePrivilege";
        internal const string CreateGlobal = "SeCreateGlobalPrivilege";
        internal const string AssignPrimaryToken = "SeAssignPrimaryTokenPrivilege";
        internal const string IncreaseQuota = "SeIncreaseQuotaPrivilege";
        internal const string SystemEnvironment = "SeSystemEnvironmentPrivilege";
        internal const string ManageVolume = "SeManageVolumePrivilege";
        internal const string ProfileSingleProcess = "SeProfileSingleProcessPrivilege";
        internal const string SystemProfile = "SeSystemProfilePrivilege";
        internal const string CreatePagefile = "SeCreatePagefilePrivilege";
        internal const string CreateSymbolicLink = "SeCreateSymbolicLinkPrivilege";
        internal const string IncreaseBasePriority = "SeIncreaseBasePriorityPrivilege";
        internal const string EnableDelegation = "SeEnableDelegationPrivilege";
        internal const string Audit = "SeAuditPrivilege";
        internal const string NetworkLogon = "SeNetworkLogonRight";
        internal const string InteractiveLogon = "SeInteractiveLogonRight";
        internal const string RemoteInteractiveLogon = "SeRemoteInteractiveLogonRight";
        internal const string ServiceLogon = "SeServiceLogonRight";
        internal const string BatchLogon = "SeBatchLogonRight";
        internal const string CreateToken = "SeCreateTokenPrivilege";
        internal const string Tcb = "SeTcbPrivilege";
        internal const string LockMemory = "SeLockMemoryPrivilege";
        internal const string CreatePermanent = "SeCreatePermanentPrivilege";
        internal const string Relabel = "SeRelabelPrivilege";
        internal const string TrustedCredManAccess = "SeTrustedCredManAccessPrivilege";
    }

    /// <summary>Every deny-family privilege constant, in template order.</summary>
    internal static readonly IReadOnlyList<string> DenyRightConstants =
    [
        DenyRights.NetworkLogon,
        DenyRights.InteractiveLogon,
        DenyRights.BatchLogon,
        DenyRights.ServiceLogon,
        DenyRights.RemoteInteractiveLogon
    ];

    /// <summary>Every grant-family privilege constant, in template order.</summary>
    internal static readonly IReadOnlyList<string> GrantRightConstants =
    [
        GrantRights.Debug,
        GrantRights.Backup,
        GrantRights.Restore,
        GrantRights.TakeOwnership,
        GrantRights.LoadDriver,
        GrantRights.Security,
        GrantRights.SystemTime,
        GrantRights.RemoteShutdown,
        GrantRights.Shutdown,
        GrantRights.Impersonate,
        GrantRights.CreateGlobal,
        GrantRights.AssignPrimaryToken,
        GrantRights.IncreaseQuota,
        GrantRights.SystemEnvironment,
        GrantRights.ManageVolume,
        GrantRights.ProfileSingleProcess,
        GrantRights.SystemProfile,
        GrantRights.CreatePagefile,
        GrantRights.CreateSymbolicLink,
        GrantRights.IncreaseBasePriority,
        GrantRights.EnableDelegation,
        GrantRights.Audit,
        GrantRights.NetworkLogon,
        GrantRights.InteractiveLogon,
        GrantRights.RemoteInteractiveLogon,
        GrantRights.ServiceLogon,
        GrantRights.BatchLogon,
        GrantRights.CreateToken,
        GrantRights.Tcb,
        GrantRights.LockMemory,
        GrantRights.CreatePermanent,
        GrantRights.Relabel,
        GrantRights.TrustedCredManAccess
    ];

    /// <summary>
    /// Grant rights every generated company must carry, because they are the rights an
    /// over-privileged machine most commonly differs on.
    /// </summary>
    internal static readonly IReadOnlyList<string> RequiredGrantRightConstants =
    [
        GrantRights.Debug,
        GrantRights.Backup,
        GrantRights.Restore,
        GrantRights.TakeOwnership,
        GrantRights.LoadDriver
    ];

    /// <summary>
    /// Advanced audit subcategory display names. The leading word <c>Audit</c> is part of
    /// the subcategory name, so a canonical key doubles it — <c>Audit:Audit Logon</c> is
    /// correct. Verified against the <c>Subcategory</c> column of shipped
    /// <c>audit.csv</c> content.
    /// </summary>
    internal static class AuditSubcategories
    {
        internal const string CredentialValidation = "Audit Credential Validation";
        internal const string KerberosAuthenticationService = "Audit Kerberos Authentication Service";
        internal const string KerberosServiceTicketOperations = "Audit Kerberos Service Ticket Operations";
        internal const string ComputerAccountManagement = "Audit Computer Account Management";
        internal const string OtherAccountManagementEvents = "Audit Other Account Management Events";
        internal const string SecurityGroupManagement = "Audit Security Group Management";
        internal const string UserAccountManagement = "Audit User Account Management";
        internal const string DirectoryServiceAccess = "Audit Directory Service Access";
        internal const string DirectoryServiceChanges = "Audit Directory Service Changes";
        internal const string AccountLockout = "Audit Account Lockout";
        internal const string GroupMembership = "Audit Group Membership";
        internal const string Logon = "Audit Logon";
        internal const string Logoff = "Audit Logoff";
        internal const string SpecialLogon = "Audit Special Logon";
        internal const string OtherLogonLogoffEvents = "Audit Other Logon/Logoff Events";
        internal const string DetailedFileShare = "Audit Detailed File Share";
        internal const string FileShare = "Audit File Share";
        internal const string FileSystem = "Audit File System";
        internal const string Registry = "Audit Registry";
        internal const string RemovableStorage = "Audit Removable Storage";
        internal const string OtherObjectAccessEvents = "Audit Other Object Access Events";
        internal const string PnpActivity = "Audit PNP Activity";
        internal const string ProcessCreation = "Audit Process Creation";
        internal const string ProcessTermination = "Audit Process Termination";
        internal const string AuditPolicyChange = "Audit Audit Policy Change";
        internal const string AuthenticationPolicyChange = "Audit Authentication Policy Change";
        internal const string AuthorizationPolicyChange = "Audit Authorization Policy Change";
        internal const string MpsSvcRuleLevelPolicyChange = "Audit MPSSVC Rule-Level Policy Change";
        internal const string OtherPolicyChangeEvents = "Audit Other Policy Change Events";
        internal const string SensitivePrivilegeUse = "Audit Sensitive Privilege Use";
        internal const string OtherSystemEvents = "Audit Other System Events";
        internal const string SecurityStateChange = "Audit Security State Change";
        internal const string SecuritySystemExtension = "Audit Security System Extension";
        internal const string SystemIntegrity = "Audit System Integrity";
    }

    /// <summary>Every advanced audit subcategory display name, in <c>audit.csv</c> order.</summary>
    internal static readonly IReadOnlyList<string> AuditSubcategoryNames =
    [
        AuditSubcategories.CredentialValidation,
        AuditSubcategories.KerberosAuthenticationService,
        AuditSubcategories.KerberosServiceTicketOperations,
        AuditSubcategories.ComputerAccountManagement,
        AuditSubcategories.OtherAccountManagementEvents,
        AuditSubcategories.SecurityGroupManagement,
        AuditSubcategories.UserAccountManagement,
        AuditSubcategories.DirectoryServiceAccess,
        AuditSubcategories.DirectoryServiceChanges,
        AuditSubcategories.AccountLockout,
        AuditSubcategories.GroupMembership,
        AuditSubcategories.Logon,
        AuditSubcategories.Logoff,
        AuditSubcategories.SpecialLogon,
        AuditSubcategories.OtherLogonLogoffEvents,
        AuditSubcategories.DetailedFileShare,
        AuditSubcategories.FileShare,
        AuditSubcategories.FileSystem,
        AuditSubcategories.Registry,
        AuditSubcategories.RemovableStorage,
        AuditSubcategories.OtherObjectAccessEvents,
        AuditSubcategories.PnpActivity,
        AuditSubcategories.ProcessCreation,
        AuditSubcategories.ProcessTermination,
        AuditSubcategories.AuditPolicyChange,
        AuditSubcategories.AuthenticationPolicyChange,
        AuditSubcategories.AuthorizationPolicyChange,
        AuditSubcategories.MpsSvcRuleLevelPolicyChange,
        AuditSubcategories.OtherPolicyChangeEvents,
        AuditSubcategories.SensitivePrivilegeUse,
        AuditSubcategories.OtherSystemEvents,
        AuditSubcategories.SecurityStateChange,
        AuditSubcategories.SecuritySystemExtension,
        AuditSubcategories.SystemIntegrity
    ];

    /// <summary>
    /// Inclusion-setting values an audit subcategory can carry. The spelling of the
    /// combined value follows the collection method rather than any single convention:
    /// <c>auditpol</c> and a GPO backup's <c>audit.csv</c> both emit
    /// <see cref="SuccessAndFailure"/>, while a <c>gpreport.xml</c> export carries the
    /// numeric code <c>3</c>, which a reader renders as
    /// <see cref="SuccessCommaFailure"/>. Both shapes are real and neither is normalised
    /// into the other.
    /// </summary>
    internal static class AuditValues
    {
        internal const string NoAuditing = "No Auditing";
        internal const string Success = "Success";
        internal const string Failure = "Failure";

        /// <summary>Combined value as <c>auditpol</c> and <c>audit.csv</c> spell it.</summary>
        internal const string SuccessAndFailure = "Success and Failure";

        /// <summary>Combined value as a <c>gpreport.xml</c> reader renders code <c>3</c>.</summary>
        internal const string SuccessCommaFailure = "Success, Failure";
    }

    /// <summary>
    /// Security descriptors in SDDL, each a normal <c>O:</c>/<c>G:</c>/<c>D:</c> container
    /// whose ACEs carry the full
    /// <c>ace_type;ace_flags;rights;object_guid;inherit_object_guid;sid</c> structure,
    /// including inherited (<c>ID</c>) ACEs, explicit ACEs and a deny (<c>D;</c>) ACE.
    /// The SID tokens inside are SDDL short forms within an opaque string; they are not
    /// principals and are unaffected by the resolver mirror above.
    /// </summary>
    internal static class SecurityDescriptors
    {
        /// <summary>Protected, explicitly inherited descriptor with a deny ACE for Guests.</summary>
        internal const string ProtectedSystemDirectory =
            "O:BAG:SYD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICIID;0x1200a9;;;BU)(D;OICI;FA;;;BG)";

        /// <summary>Wholly inherited descriptor with a deny ACE for anonymous logons.</summary>
        internal const string InheritedSystemDirectory =
            "O:SYG:SYD:AI(A;ID;FA;;;BA)(A;ID;FA;;;SY)(A;CIID;0x1200a9;;;BU)(D;CIIO;FA;;;AN)";

        /// <summary>Administrator-owned descriptor with a deny ACE for Everyone.</summary>
        internal const string AdministratorOwnedDirectory =
            "O:BAG:BAD:PAI(A;;FA;;;BA)(A;;0x1301bf;;;SY)(A;OICIIO;GA;;;CO)(D;;FA;;;WD)";
    }

    /// <summary>File-system paths carrying a <c>[File Security]</c> descriptor.</summary>
    internal static class FileAclPaths
    {
        internal const string SystemConfig = @"%SystemRoot%\System32\config";
        internal const string SystemDrivers = @"%SystemRoot%\System32\drivers";
        internal const string ProgramFiles = @"%SystemDrive%\Program Files";
    }

    /// <summary>Registry keys carrying a <c>[Registry Keys]</c> descriptor.</summary>
    internal static class RegistryAclKeys
    {
        internal const string Services = @"MACHINE\SYSTEM\CurrentControlSet\Services";
        internal const string Policies = @"MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies";
    }

    /// <summary>Every file-system path carrying a security descriptor, in template order.</summary>
    internal static readonly IReadOnlyList<string> FileAclPathNames =
    [
        FileAclPaths.SystemConfig,
        FileAclPaths.SystemDrivers,
        FileAclPaths.ProgramFiles
    ];

    /// <summary>Every registry key carrying a security descriptor, in template order.</summary>
    internal static readonly IReadOnlyList<string> RegistryAclKeyNames =
    [
        RegistryAclKeys.Services,
        RegistryAclKeys.Policies
    ];

    /// <summary>
    /// Names describing how a single machine's effective configuration relates to the
    /// security template assigned to it. Each name states an observable property of the
    /// emitted data — which token sets are supersets, subsets or merely overlapping, and
    /// which audit values cover more or less than the template — and nothing about how any
    /// consumer might later rate that machine.
    /// </summary>
    internal static class ConfigurationProfiles
    {
        /// <summary>Every token set and audit value identical to the template.</summary>
        internal const string Aligned = "Aligned";

        /// <summary>A grant right holds a strict superset of the template's principals.</summary>
        internal const string ExpandedPrincipals = "ExpandedPrincipals";

        /// <summary>A grant right holds a strict, non-empty subset of the template's principals.</summary>
        internal const string ReducedPrincipals = "ReducedPrincipals";

        /// <summary>A grant right overlaps the template's principals without containing or being contained by them.</summary>
        internal const string DivergentPrincipals = "DivergentPrincipals";

        /// <summary>A <c>SeDeny*</c> right holds a strict superset of the template's principals.</summary>
        internal const string ExpandedDenyPrincipals = "ExpandedDenyPrincipals";

        /// <summary>An audit subcategory covers one outcome where the template covers both.</summary>
        internal const string ReducedAuditCoverage = "ReducedAuditCoverage";

        /// <summary>An audit subcategory covers both outcomes where the template covers one.</summary>
        internal const string ExpandedAuditCoverage = "ExpandedAuditCoverage";

        /// <summary>A security descriptor differs from the template's descriptor for the same object.</summary>
        internal const string DivergentAcl = "DivergentAcl";
    }

    /// <summary>
    /// Every configuration profile, in the order endpoints are assigned to them. A world
    /// generated with at least this many endpoints carries all of them.
    /// </summary>
    internal static readonly IReadOnlyList<string> ConfigurationProfileNames =
    [
        ConfigurationProfiles.Aligned,
        ConfigurationProfiles.ExpandedPrincipals,
        ConfigurationProfiles.ReducedPrincipals,
        ConfigurationProfiles.DivergentPrincipals,
        ConfigurationProfiles.ExpandedDenyPrincipals,
        ConfigurationProfiles.ReducedAuditCoverage,
        ConfigurationProfiles.ExpandedAuditCoverage,
        ConfigurationProfiles.DivergentAcl
    ];

    /// <summary>Builds the canonical key for a privilege-rights assignment.</summary>
    internal static string BuildUserRightKey(string privilegeConstant)
        => UserRightKeyPrefix + privilegeConstant;

    /// <summary>Builds the canonical key for an advanced audit subcategory.</summary>
    internal static string BuildAuditKey(string subcategoryDisplayName)
        => AuditKeyPrefix + subcategoryDisplayName;

    /// <summary>Builds the canonical key for a file-system security descriptor.</summary>
    internal static string BuildFileAclKey(string path)
        => FileAclKeyPrefix + path;

    /// <summary>Builds the canonical key for a registry-key security descriptor.</summary>
    internal static string BuildRegistryAclKey(string registryKey)
        => RegistryAclKeyPrefix + registryKey;

    /// <summary>Joins principal display names into a single assignment value.</summary>
    internal static string JoinPrincipals(params string[] principals)
        => string.Join(PrincipalDelimiter, principals.Where(principal => !string.IsNullOrWhiteSpace(principal)));

    /// <summary>
    /// Derives the NetBIOS form of a DNS domain name, matching how a domain-qualified
    /// principal is spelled in collected output.
    /// </summary>
    internal static string BuildNetBiosName(string dnsDomainName)
    {
        var firstLabel = dnsDomainName.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var normalized = new string(firstLabel.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return normalized.Length > NetBiosNameMaximumLength
            ? normalized[..NetBiosNameMaximumLength]
            : normalized;
    }

    /// <summary>
    /// Qualifies a generated group or account with its NetBIOS domain, producing the
    /// <c>NETBIOS\logon name</c> form a collection reports for a domain principal.
    /// </summary>
    internal static string QualifyDomainPrincipal(string netBiosName, string logonName)
        => string.IsNullOrWhiteSpace(netBiosName) || string.IsNullOrWhiteSpace(logonName)
            ? ""
            : netBiosName + "\\" + logonName;
}
