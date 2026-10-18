using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Grid.Reconciliation.Permissions;

public class ReconciliationPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.GetGroupOrNull(ReconciliationPermissions.GroupName)
                    ?? context.AddGroup(ReconciliationPermissions.GroupName, new FixedLocalizableString("Reconciliation"));

        var analysis = group.AddPermission(ReconciliationPermissions.Analysis.Default, new FixedLocalizableString("Analysis"));
        analysis.AddChild(ReconciliationPermissions.Analysis.Read, new FixedLocalizableString("Read"));
        analysis.AddChild(ReconciliationPermissions.Analysis.Start, new FixedLocalizableString("Start"));
        analysis.AddChild(ReconciliationPermissions.Analysis.Decide, new FixedLocalizableString("Decide"));
        analysis.AddChild(ReconciliationPermissions.Analysis.Execute, new FixedLocalizableString("Execute"));
    }
}
