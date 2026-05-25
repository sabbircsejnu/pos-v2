namespace RetailPOS.Core.Entities.Audit;

public static class AuditActionType
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string Approve = "Approve";
    public const string Cancel = "Cancel";
    public const string Login = "Login";
    public const string Logout = "Logout";
    public const string RoleSwitch = "RoleSwitch";
    public const string Export = "Export";
    public const string Import = "Import";
    public const string Payment = "Payment";
    public const string Adjustment = "Adjustment";
    public const string PermissionChange = "PermissionChange";
    public const string Reverse = "Reverse";
    public const string Hold = "Hold";
    public const string Resume = "Resume";
    public const string Print = "Print";
    public const string Submit = "Submit";
    public const string Reject = "Reject";
}

public static class AuditModule
{
    public const string Sales = "Sales";
    public const string Purchase = "Purchase";
    public const string Inventory = "Inventory";
    public const string Accounts = "Accounts";
    public const string Admin = "Admin";
    public const string Settings = "Settings";
    public const string Reports = "Reports";
    public const string Auth = "Auth";
    public const string System = "System";
}

public static class AuditSource
{
    public const string UI = "UI";
    public const string API = "API";
    public const string SystemJob = "SystemJob";
    public const string Import = "Import";
    public const string Integration = "Integration";
}

public static class AuditStatus
{
    public const string Success = "Success";
    public const string Failed = "Failed";
}

public static class AuditOperationType
{
    public const string Insert = "Insert";
    public const string Update = "Update";
    public const string Delete = "Delete";
}
