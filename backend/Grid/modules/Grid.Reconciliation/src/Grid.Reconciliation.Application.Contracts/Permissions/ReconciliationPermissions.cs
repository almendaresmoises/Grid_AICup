namespace Grid.Reconciliation.Permissions;

public static class ReconciliationPermissions
{
    public const string GroupName = "Reconciliation";

    public static class Analysis
    {
        public const string Default = GroupName + ".Analysis";
        public const string Read = Default + ".Read";
        public const string Start = Default + ".Start";
        public const string Decide = Default + ".Decide";
        public const string Execute = Default + ".Execute";
    }
}
