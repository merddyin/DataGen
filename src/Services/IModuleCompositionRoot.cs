namespace SyntheticEnterprise.Module.Services;

public interface IModuleCompositionRoot
{
    T Resolve<T>() where T : class;
    CmdletServiceRegistry BuildRegistry();
}
