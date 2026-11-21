namespace Nexus.SharedKernel.Security;

public enum PermissionScope
{
    Platform = 1,
    Tenant = 2,
    Both = 3
}

public static class Permissions
{
    // Tenants (Platform-scoped)
    public const string TenantsRead = "platform.tenants.read";
    public const string TenantsCreate = "platform.tenants.create";
    public const string TenantsUpdate = "platform.tenants.update";
    public const string TenantsSuspend = "platform.tenants.suspend";

    // Organizations (Tenant-scoped)
    public const string OrganizationsRead = "platform.organizations.read";
    public const string OrganizationsManage = "platform.organizations.manage";

    // Users & Memberships
    public const string UsersRead = "platform.users.read";
    public const string UsersCreate = "platform.users.create";
    public const string UsersManage = "platform.users.manage";

    // Roles & Access Control
    public const string RolesRead = "platform.roles.read";
    public const string RolesCreate = "platform.roles.create";
    public const string RolesAssign = "platform.roles.assign";

    // Audit (Read-only — never update/delete!)
    public const string AuditRead = "platform.audit.read";
    public const string AuditReadAllTenants = "platform.audit.read.all";

    // Workflows
    public const string WorkflowsRead = "platform.workflows.read";
    public const string WorkflowsCreate = "platform.workflows.create";
    public const string WorkflowsApprove = "platform.workflows.approve";
    public const string WorkflowsManage = "platform.workflows.manage";

    // Subscriptions
    public const string SubscriptionsRead = "platform.subscriptions.read";
    public const string SubscriptionsManage = "platform.subscriptions.manage";

    // Configuration & Feature Flags
    public const string ConfigurationRead = "platform.configuration.read";
    public const string ConfigurationManage = "platform.configuration.manage";
    public const string PlatformConfigurationManage = "platform.configuration.platform.manage";

    // Integrations & AI Gateway
    public const string IntegrationsRead = "platform.integrations.read";
    public const string IntegrationsManage = "platform.integrations.manage";
    public const string AiGatewayInvoke = "platform.ai.invoke";
    public const string AiGatewayManage = "platform.ai.manage";

    // Notifications
    public const string NotificationsRead = "platform.notifications.read";
    public const string NotificationsManage = "platform.notifications.manage";

    // File Storage
    public const string FilesRead = "platform.files.read";
    public const string FilesUpload = "platform.files.upload";
    public const string FilesDelete = "platform.files.delete";

    public static readonly IReadOnlyList<(string Code, string Name, PermissionScope Scope, string Category)> Catalog =
    [
        (TenantsRead, "Read Tenants", PermissionScope.Platform, "Tenancy"),
        (TenantsCreate, "Create Tenants", PermissionScope.Platform, "Tenancy"),
        (TenantsUpdate, "Update Tenants", PermissionScope.Platform, "Tenancy"),
        (TenantsSuspend, "Suspend/Activate Tenants", PermissionScope.Platform, "Tenancy"),

        (OrganizationsRead, "Read Organization Structure", PermissionScope.Tenant, "Organization"),
        (OrganizationsManage, "Manage Legal Entities, Branches & Units", PermissionScope.Tenant, "Organization"),

        (UsersRead, "Read Users", PermissionScope.Both, "Identity"),
        (UsersCreate, "Create/Invite Users", PermissionScope.Both, "Identity"),
        (UsersManage, "Manage User Status, Sessions & Memberships", PermissionScope.Both, "Identity"),

        (RolesRead, "Read Roles & Permissions", PermissionScope.Both, "AccessControl"),
        (RolesCreate, "Create Custom Roles", PermissionScope.Both, "AccessControl"),
        (RolesAssign, "Assign Roles to Users", PermissionScope.Both, "AccessControl"),

        (AuditRead, "Read Tenant Audit Logs", PermissionScope.Both, "Audit"),
        (AuditReadAllTenants, "Read Cross-Tenant Audit Logs", PermissionScope.Platform, "Audit"),

        (WorkflowsRead, "Read Workflows & Tasks", PermissionScope.Tenant, "Workflow"),
        (WorkflowsCreate, "Create Workflow Instances", PermissionScope.Tenant, "Workflow"),
        (WorkflowsApprove, "Approve/Reject Workflow Tasks", PermissionScope.Tenant, "Workflow"),
        (WorkflowsManage, "Manage Workflow Definitions", PermissionScope.Tenant, "Workflow"),

        (SubscriptionsRead, "Read Subscriptions & Entitlements", PermissionScope.Both, "Subscription"),
        (SubscriptionsManage, "Manage Plans & Tenant Subscriptions", PermissionScope.Platform, "Subscription"),

        (ConfigurationRead, "Read Tenant Configuration & Flags", PermissionScope.Both, "Configuration"),
        (ConfigurationManage, "Manage Tenant Configuration & Flags", PermissionScope.Tenant, "Configuration"),
        (PlatformConfigurationManage, "Manage Global Platform Configuration", PermissionScope.Platform, "Configuration"),

        (IntegrationsRead, "Read Integrations", PermissionScope.Tenant, "Integration"),
        (IntegrationsManage, "Manage Integrations & Webhooks", PermissionScope.Tenant, "Integration"),
        (AiGatewayInvoke, "Invoke AI Gateway", PermissionScope.Tenant, "AI"),
        (AiGatewayManage, "Manage AI Gateway Models", PermissionScope.Platform, "AI"),

        (NotificationsRead, "Read Notifications", PermissionScope.Tenant, "Notification"),
        (NotificationsManage, "Manage Templates & Send Notifications", PermissionScope.Both, "Notification"),

        (FilesRead, "Read & Download Tenant Files", PermissionScope.Tenant, "FileStorage"),
        (FilesUpload, "Upload Tenant Files", PermissionScope.Tenant, "FileStorage"),
        (FilesDelete, "Delete Tenant Files", PermissionScope.Tenant, "FileStorage")
    ];
}

public static class SystemRoles
{
    public const string PlatformSuperAdmin = "PlatformSuperAdmin";
    public const string PlatformSupport = "PlatformSupport";
    public const string TenantOwner = "TenantOwner";
    public const string TenantAdmin = "TenantAdmin";
    public const string CFO = "CFO";
    public const string Accountant = "Accountant";
    public const string LogisticsDirector = "LogisticsDirector";
    public const string WarehouseManager = "WarehouseManager";
    public const string Employee = "Employee";
    public const string Auditor = "Auditor";
}
