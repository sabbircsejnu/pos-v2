using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using RetailPOS.API.Configuration;
using RetailPOS.API.Authorization;
using RetailPOS.API.Documents.Pdf;
using RetailPOS.API.Models;
using RetailPOS.API.Middleware;
using RetailPOS.API.Services;
using RetailPOS.Application;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Core.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Authentication;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Grns;
using RetailPOS.Infrastructure.Pos;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

// Configure Npgsql to handle DateTime without requiring UTC
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// Audit context (scoped per request) — register before DbContext so the interceptor can resolve it
builder.Services.AddApplication();
builder.Services.AddPosPricingCacheInvalidation();
builder.Services.AddScoped<IAuditContext, AuditContext>();
builder.Services.AddScoped<AuditChangeTrackerInterceptor>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IClientIpResolver, ClientIpResolver>();

// ForwardedHeaders — allows ASP.NET Core to see the real client IP when behind a reverse proxy.
// By default only loopback proxies are trusted (safe for local dev).
// PRODUCTION: set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true OR call Configure<ForwardedHeadersOptions>
// and add your proxy IPs to KnownProxies / KnownNetworks, e.g.:
//   options.KnownProxies.Add(IPAddress.Parse("10.0.0.5"));         // Nginx node
//   options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("173.245.48.0"), 20)); // Cloudflare range
// Without explicit configuration, X-Forwarded-For from untrusted sources is ignored by this middleware
// but ClientIpResolver still reads it directly — keep KnownProxies tight in production.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Trust only loopback by default. Add production proxy IPs here or via environment variables.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    // Allow loopback proxies (safe default for Docker-compose where Nginx → API on same host)
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Loopback, 8));
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.IPv6Loopback, 128));
});

// Add services to the container.
var dbConnection = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<RetailPOSDbContext>((sp, options) =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           .AddInterceptors(sp.GetRequiredService<AuditChangeTrackerInterceptor>())
           .AddPosPricingCacheInvalidation(sp));

// Configure JWT Settings
builder.Services.AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection("JwtSettings"))
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Secret),
        "Configure JwtSettings:Secret through development user-secrets or JwtSettings__Secret in the deployment secret configuration.")
    .ValidateOnStart();
builder.Services.Configure<JwtTokenSettings>(builder.Configuration.GetSection("JwtSettings"));

// Add Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()
        ?? throw new InvalidOperationException("JwtSettings configuration is required.");
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in PermissionCatalog.All)
    {
        options.AddPolicy(permission, policy =>
        {
            policy.RequireAssertion(context => context.User.HasPermission(permission));
            var capability = HrPayrollPermissionCatalog.RequiredCapability(permission);
            if (capability is not null)
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new RetailPOS.API.Authorization.HrPayrollCapabilityRequirement(capability));
            }
        });
    }

    RetailPOS.API.Authorization.LookupAuthorizationPolicies.Register(options);
});

// Register Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    RetailPOS.API.Authorization.HrPayrollCapabilityAuthorizationHandler>();
builder.Services.AddScoped<RetailPOS.Application.Authentication.Abstractions.IAuthenticationTokenService, JwtAuthenticationTokenService>();
builder.Services.AddScoped<RetailPOS.Application.Authentication.Abstractions.IRefreshTokenStore, RefreshTokenStore>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<RetailPOS.Application.Authentication.Abstractions.IAuthenticationStore, AuthenticationStore>();
builder.Services.AddScoped<RetailPOS.Application.Authentication.Abstractions.IAuthenticationProfileStore>(sp =>
    sp.GetRequiredService<RetailPOS.Application.Authentication.Abstractions.IAuthenticationStore>());
builder.Services.AddScoped<RetailPOS.Application.Authentication.Abstractions.IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<RetailPOS.Application.Authentication.Abstractions.IAuthenticationCredentialsIssuer, AuthenticationCredentialsIssuer>();
builder.Services.AddScoped<IRoleSwitchContext, RoleSwitchContext>();
builder.Services.AddScoped<IRoleGovernanceContext>(sp => sp.GetRequiredService<IRoleSwitchContext>());
builder.Services.AddScoped<IUserGovernanceContext>(sp => sp.GetRequiredService<IRoleSwitchContext>());
builder.Services.AddScoped<ITenantAccessService, TenantAccessService>();
builder.Services.AddScoped<RetailPOS.Application.Abstractions.IBusinessScope, BusinessScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.BusinessAdministration.Abstractions.IBusinessTargetAccess,
    BusinessTargetAccessAdapter>();
builder.Services.AddScoped<RetailPOS.Application.HR.Capabilities.IHrPayrollCapabilityScope,
    HrPayrollCapabilityScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.HR.Capabilities.IHrPayrollCapabilityReadStore,
    RetailPOS.Infrastructure.HR.Capabilities.HrPayrollCapabilityReadStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Organisation.IHrOrganisationAccess,
    RetailPOS.API.Services.HR.HrOrganisationAccessAdapter>();
builder.Services.AddScoped<RetailPOS.Application.HR.Organisation.IHrOrganisationReadStore,
    RetailPOS.Infrastructure.HR.Organisation.HrOrganisationStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Organisation.IHrOrgUnitWriteStore,
    RetailPOS.Infrastructure.HR.Organisation.HrOrganisationStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Organisation.IHrPositionStore,
    RetailPOS.Infrastructure.HR.Organisation.HrPositionStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Calendars.IHrCalendarStore,
    RetailPOS.Infrastructure.HR.Calendars.HrCalendarStore>();
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Payroll.PayrollSalarySetupStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollSalarySetupStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.HR.Payroll.PayrollSalarySetupStore>());
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Payroll.PayrollPeriodStore>();
builder.Services.AddSingleton<RetailPOS.Application.HR.Payroll.IPayrollPeriodCloseExceptionPolicy,
    RetailPOS.Infrastructure.HR.Payroll.DenyPayrollPeriodCloseExceptionPolicy>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollPeriodStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.HR.Payroll.PayrollPeriodStore>());
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollPeriodAccess,
    RetailPOS.API.Services.HR.PayrollPeriodAccessAdapter>();
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Payroll.PayrollRunStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollRunStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.HR.Payroll.PayrollRunStore>());
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollRunApprovalWorkflow>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.HR.Payroll.PayrollRunStore>());
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollRunCalculationStore,
    RetailPOS.Infrastructure.HR.Payroll.PayrollRunCalculationStore>();
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Payroll.PayrollRunValidationStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollRunValidationStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.HR.Payroll.PayrollRunValidationStore>());
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollDashboardStore,
    RetailPOS.Infrastructure.HR.Payroll.PayrollDashboardStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollEmployeePaymentDestinationStore,
    RetailPOS.Infrastructure.HR.Payroll.PayrollEmployeePaymentDestinationStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollPaymentStore,
    RetailPOS.Infrastructure.HR.Payroll.PayrollPaymentStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollPaymentBatchStore,
    RetailPOS.Infrastructure.HR.Payroll.PayrollPaymentBatchStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollPaymentReconciliationStore,
    RetailPOS.Infrastructure.HR.Payroll.PayrollPaymentReconciliationStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollPayslipStore,
    RetailPOS.Infrastructure.HR.Payroll.PayrollPayslipStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollPayslipRenderer,
    RetailPOS.Infrastructure.HR.Payroll.PayrollPayslipPdfRenderer>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.PayrollPayslipService>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollExportStore,
    RetailPOS.Infrastructure.HR.Payroll.PayrollExportStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollExportRenderer,
    RetailPOS.API.Services.HR.PayrollExportRenderer>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.PayrollExportService>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollAccountingPostingStore,
    RetailPOS.Infrastructure.HR.Payroll.PayrollAccountingPostingStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollAccountingCorrectionPolicy,
    RetailPOS.Application.HR.Payroll.UnconfiguredPayrollAccountingCorrectionPolicy>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.PayrollAccountingPostingService>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.PayrollAccountingSettlementPostingService>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollPayslipEmailTransport,
    RetailPOS.Infrastructure.ConfidentialFiles.UnconfiguredPayrollPayslipEmailTransport>();
builder.Services.AddScoped<RetailPOS.Application.ConfidentialFiles.IPrivateBlobStore,
    RetailPOS.Infrastructure.ConfidentialFiles.UnconfiguredPrivateBlobStore>();
builder.Services.AddScoped<RetailPOS.Application.ConfidentialFiles.IConfidentialFileEncryption,
    RetailPOS.Infrastructure.ConfidentialFiles.UnconfiguredConfidentialFileEncryption>();
builder.Services.AddScoped<RetailPOS.Application.ConfidentialFiles.IConfidentialContentInspector,
    RetailPOS.Infrastructure.ConfidentialFiles.UnconfiguredConfidentialContentInspector>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollPaymentAccess,
    RetailPOS.API.Services.HR.PayrollPaymentAccessAdapter>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollCalculationPolicyStore,
    RetailPOS.Infrastructure.HR.Payroll.PayrollCalculationPolicyStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollMonetaryConfigurationStore,
    RetailPOS.Infrastructure.HR.Payroll.PayrollMonetaryConfigurationStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollRunAccess,
    RetailPOS.API.Services.HR.PayrollRunAccessAdapter>();
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Payroll.PayrollSalaryRevisionStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollSalaryRevisionStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.HR.Payroll.PayrollSalaryRevisionStore>());
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Payroll.PayrollEmployeeAdjustmentStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollEmployeeAdjustmentStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.HR.Payroll.PayrollEmployeeAdjustmentStore>());
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Payroll.PayrollEmployeeLoanStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollEmployeeLoanStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.HR.Payroll.PayrollEmployeeLoanStore>());
builder.Services.AddScoped<RetailPOS.API.Services.HR.PayrollSalarySetupAccessAdapter>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollSalarySetupAccess>(sp =>
    sp.GetRequiredService<RetailPOS.API.Services.HR.PayrollSalarySetupAccessAdapter>());
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollEmployeePaymentDestinationAccess>(sp =>
    sp.GetRequiredService<RetailPOS.API.Services.HR.PayrollSalarySetupAccessAdapter>());
builder.Services.AddScoped<RetailPOS.Application.HR.Leave.IHrLeaveAccess,
    RetailPOS.API.Services.HR.HrLeaveAccessAdapter>();
builder.Services.AddScoped<RetailPOS.Application.HR.Leave.IHrLeaveStore,
    RetailPOS.Infrastructure.HR.Leave.HrLeaveStore>();
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Leave.HrLeaveRequestStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Leave.IHrLeaveRequestStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.HR.Leave.HrLeaveRequestStore>());
builder.Services.AddScoped<RetailPOS.Application.HR.Leave.IHrLeaveRequestAccess>(sp =>
    (RetailPOS.Application.HR.Leave.IHrLeaveRequestAccess)
    sp.GetRequiredService<RetailPOS.Application.HR.Leave.IHrLeaveAccess>());
builder.Services.AddScoped<RetailPOS.Application.HR.Attendance.IHrRosterStore,
    RetailPOS.Infrastructure.HR.Attendance.HrRosterStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Attendance.IHrDeviceStore,
    RetailPOS.Infrastructure.HR.Attendance.HrDeviceStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Attendance.IHrRawAttendancePunchStore,
    RetailPOS.Infrastructure.HR.Attendance.HrRawAttendancePunchStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Attendance.IHrAttendanceProcessingStore,
    RetailPOS.Infrastructure.HR.Attendance.HrAttendanceProcessingStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Attendance.IHrAttendanceReportStore,
    RetailPOS.Infrastructure.HR.Attendance.HrAttendanceReportStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Reports.IHrCoreReportStore,
    RetailPOS.Infrastructure.HR.Reports.HrCoreReportStore>();
builder.Services.AddSingleton<RetailPOS.API.Services.HR.HrCoreReportExportRenderer>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollAdvancedReportStore,
    RetailPOS.Infrastructure.HR.Payroll.PayrollAdvancedReportStore>();
builder.Services.AddSingleton<RetailPOS.API.Services.Payroll.PayrollAdvancedReportExportRenderer>();
builder.Services.AddScoped<RetailPOS.Application.HR.Dashboard.IHrDashboardStore,
    RetailPOS.Infrastructure.HR.Dashboard.HrDashboardStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Attendance.IHrAttendanceCorrectionStore,
    RetailPOS.Infrastructure.HR.Attendance.HrAttendanceCorrectionStore>();
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Attendance.HrAttendanceCorrectionApprovalWorkflow>();
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Overtime.HrOvertimeStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Overtime.IHrOvertimeStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.HR.Overtime.HrOvertimeStore>());
builder.Services.AddScoped<RetailPOS.Application.HR.Overtime.IHrOvertimeAccess,
    RetailPOS.API.Services.HR.HrOvertimeAccessAdapter>();
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Exit.HrExitStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Exit.IHrExitStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.HR.Exit.HrExitStore>());
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Payroll.PayrollFinalSettlementStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollFinalSettlementStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.HR.Payroll.PayrollFinalSettlementStore>());
builder.Services.AddScoped<RetailPOS.Application.HR.Payroll.IPayrollFinalSettlementExecutionStore,
    RetailPOS.Infrastructure.HR.Payroll.PayrollFinalSettlementExecutionStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Exit.IHrExitAccess,
    RetailPOS.API.Services.HR.HrExitAccessAdapter>();
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Employees.HrEmployeeLocationApprovalWorkflow>();
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Employees.HrEmployeeLocationApprovalTargetSnapshotReader>();
builder.Services.AddScoped<RetailPOS.Application.HR.Approvals.IApprovalTargetSnapshotReader,
    RetailPOS.Infrastructure.HR.Approvals.HrApprovalTargetSnapshotReader>();
builder.Services.AddScoped<RetailPOS.Application.HR.Approvals.IApprovalKernelWorkflow,
    RetailPOS.Infrastructure.HR.Approvals.HrApprovalKernelWorkflow>();
builder.Services.AddScoped<RetailPOS.Application.HR.Attendance.ITrustedNormalizedAttendancePunchIngestor,
    RetailPOS.Infrastructure.HR.Attendance.HrRawAttendancePunchStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Employees.IHrEmployeeAccess,
    RetailPOS.API.Services.HR.HrEmployeeAccessAdapter>();
builder.Services.AddScoped<RetailPOS.Application.HR.Employees.IHrEmployeeStore,
    RetailPOS.Infrastructure.HR.Employees.HrEmployeeStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Employees.IHrOpeningImportStore,
    RetailPOS.Infrastructure.HR.Employees.HrOpeningImportStore>();
builder.Services.AddSingleton<RetailPOS.Application.HR.Employees.IHrOpeningImportParser,
    RetailPOS.Application.HR.Employees.HrOpeningImportCsvParser>();
builder.Services.AddScoped<RetailPOS.Application.HR.Employees.IHrOpeningImportService,
    RetailPOS.Application.HR.Employees.HrOpeningImportService>();
builder.Services.AddScoped<RetailPOS.Application.HR.Employees.IIdentityUserDirectory,
    RetailPOS.Infrastructure.HR.Employees.IdentityUserDirectory>();
builder.Services.AddScoped<RetailPOS.Application.HR.Organisation.IOperationalLocationProjectionWriteStore,
    RetailPOS.Infrastructure.HR.Organisation.HrOrganisationStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Organisation.IOperationalLocationSourceReadStore,
    RetailPOS.Infrastructure.HR.Organisation.HrOrganisationStore>();
builder.Services.AddScoped<RetailPOS.Application.Integration.IOperationalLocationSourceEventWriter,
    RetailPOS.Infrastructure.Integration.OperationalLocationSourceEventWriter>();
builder.Services.AddScoped<RetailPOS.Application.Integration.IIntegrationConsumer,
    RetailPOS.Infrastructure.Integration.OperationalLocationProjectionConsumer>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Integration.RabbitMqInboxDelivery>();
builder.Services.AddScoped<RetailPOS.Application.HR.Approvals.IApprovalKernelIdentity,
    RetailPOS.API.Services.HR.ApprovalKernelIdentityAdapter>();
builder.Services.AddScoped<RetailPOS.Application.HR.Approvals.IApprovalKernelAccess,
    RetailPOS.API.Services.HR.ApprovalKernelAccessAdapter>();
builder.Services.AddScoped<RetailPOS.Application.HR.Approvals.IApprovalCandidateResolver,
    RetailPOS.Infrastructure.HR.Approvals.SalarySetupApprovalCandidateResolver>();
builder.Services.AddScoped<RetailPOS.Application.HR.Employees.IHrEmployeeLocationAssignmentStore,
    RetailPOS.Infrastructure.HR.Employees.HrEmployeeLocationAssignmentStore>();
builder.Services.AddScoped<RetailPOS.Infrastructure.HR.Employees.HrEmployeeDocumentStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Employees.IHrEmployeeDocumentStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.HR.Employees.HrEmployeeDocumentStore>());
builder.Services.AddScoped<RetailPOS.Application.ConfidentialFiles.IConfidentialFileVersionStore,
    RetailPOS.Infrastructure.HR.Leave.HrConfidentialFileVersionStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Employees.IHrEmployeeDocumentReadStore>(sp =>
    (RetailPOS.Application.HR.Employees.IHrEmployeeDocumentReadStore)
    sp.GetRequiredService<RetailPOS.Application.HR.Employees.IHrEmployeeDocumentStore>());
builder.Services.AddScoped<RetailPOS.Application.HR.Employees.IHrEmployeeDocumentAccess>(sp =>
    (RetailPOS.Application.HR.Employees.IHrEmployeeDocumentAccess)
    sp.GetRequiredService<RetailPOS.Application.HR.Organisation.IHrOrganisationAccess>());
builder.Services.AddScoped<RetailPOS.Application.ConfidentialFiles.IConfidentialFileAccess>(sp =>
    (RetailPOS.Application.ConfidentialFiles.IConfidentialFileAccess)
    sp.GetRequiredService<RetailPOS.Application.HR.Organisation.IHrOrganisationAccess>());
builder.Services.AddScoped<RetailPOS.Application.HR.Employees.IHrEmployeeLocationTransferActivationStore,
    RetailPOS.Infrastructure.HR.Employees.HrEmployeeLocationAssignmentStore>();
builder.Services.AddScoped<RetailPOS.Application.HR.Approvals.IApprovalKernelStore,
    RetailPOS.Infrastructure.HR.Approvals.ApprovalKernelStore>();
builder.Services.AddScoped<IFeatureEntitlementService, FeatureEntitlementService>();
builder.Services.AddScoped<RetailPOS.Application.FeatureEntitlements.Abstractions.IFeatureEntitlementScope,
    FeatureEntitlementScopeAdapter>();

// Register Repositories
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IUserRepository, RetailPOS.Infrastructure.Repositories.UserRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IRoleRepository, RetailPOS.Infrastructure.Repositories.RoleRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Identity.IdentityReadStore>();
builder.Services.AddScoped<IRoleGovernanceStore, RetailPOS.Infrastructure.Identity.RoleGovernanceStore>();
builder.Services.AddScoped<IUserGovernanceStore, RetailPOS.Infrastructure.Identity.UserGovernanceStore>();
builder.Services.AddScoped<RetailPOS.Application.Identity.Abstractions.IRoleReadStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.Identity.IdentityReadStore>());
builder.Services.AddScoped<RetailPOS.Application.Identity.Abstractions.IUserReadStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.Identity.IdentityReadStore>());
builder.Services.AddScoped<RetailPOS.Application.Categories.Abstractions.ICategoryCatalog, RetailPOS.Infrastructure.Categories.CategoryCatalog>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductReadStore, RetailPOS.Infrastructure.Products.ProductReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductStockReadStore, RetailPOS.Infrastructure.Products.ProductStockReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IMobileProductCatalogueReadStore,
    RetailPOS.Infrastructure.Products.MobileProductCatalogueReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IMobileInventoryReadStore,
    RetailPOS.Infrastructure.ReadStores.MobileInventoryReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IVariantDisplayMetadataReadStore,
    RetailPOS.Infrastructure.Products.VariantDisplayMetadataReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.IPosProductLookupStore,
    RetailPOS.Infrastructure.Pos.PosProductLookupStore>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.IPosCatalogueReadStore,
    RetailPOS.Infrastructure.Pos.PosCatalogueReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Pricing.Abstractions.IPriceRuleReadStore,
    RetailPOS.Infrastructure.Pricing.PriceRuleReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Pricing.Abstractions.IPriceRuleCommandStore,
    RetailPOS.Infrastructure.Pricing.PriceRuleCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.Pricing.Abstractions.IOutletPriceOverrideCommandStore,
    RetailPOS.Infrastructure.Pricing.OutletPriceOverrideCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.Pricing.Abstractions.IOutletPriceOverrideWriteAuthorizer,
    OutletPriceOverrideWriteAuthorizerAdapter>();
builder.Services.AddScoped<RetailPOS.Application.Pricing.Abstractions.IOutletPriceOverrideReadStore,
    RetailPOS.Infrastructure.Pricing.OutletPriceOverrideReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.IPosTerminalReadStore,
    RetailPOS.Infrastructure.Pos.PosTerminalReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionStore,
    RetailPOS.Infrastructure.Pos.CashSessionStore>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionQueryStore,
    RetailPOS.Infrastructure.Pos.CashSessionQueryStore>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashDifferenceStore,
    RetailPOS.Infrastructure.Pos.CashDifferenceStore>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionEvidenceStore,
    RetailPOS.Infrastructure.Pos.CashSessionEvidenceStore>();
builder.Services.AddSingleton(new RetailPOS.Infrastructure.Pos.CashSessionAttachmentStorageOptions(
    Path.Combine(builder.Configuration["CashSessions:AttachmentRoot"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data"), "cash-session-attachments")));
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionAttachmentFiles,
    RetailPOS.Infrastructure.Pos.CashSessionAttachmentFiles>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionMovementRecorder,
    RetailPOS.Infrastructure.Pos.CashSessionMovementRecorder>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleCashRefundRecorder,
    RetailPOS.Infrastructure.Pos.SaleCashRefundRecorder>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionEventWriter,
    RetailPOS.Infrastructure.Pos.CashSessionEventWriter>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionAuditWriter,
    RetailPOS.Infrastructure.Pos.CashSessionAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionOutletAuthorizer,
    CashSessionOutletAuthorizerAdapter>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionWriteOutletAuthorizer,
    CashSessionOutletAuthorizerAdapter>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionOpenAuditWriter,
    RetailPOS.Infrastructure.Pos.CashSessionOpenAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionCloseAuditWriter,
    RetailPOS.Infrastructure.Pos.CashSessionCloseAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionOpenedPublisher,
    CashSessionOpenedPublisherAdapter>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionClosedPublisher,
    CashSessionClosedPublisherAdapter>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionVarianceJournalPoster,
    CashSessionVarianceJournalPoster>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.IPosProductLookupCache,
    RetailPOS.Infrastructure.Pos.PosProductLookupCache>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.IPosCartPricingStore,
    RetailPOS.Infrastructure.Pos.PosCartPricingStore>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.IPosCartPriceCache,
    RetailPOS.Infrastructure.Pos.PosCartPriceCache>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleReadScopeProvider,
    SaleReadScopeProvider>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleTodaySummaryReadStore,
    RetailPOS.Infrastructure.Sales.SaleTodaySummaryReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleSalespersonDirectory,
    RetailPOS.Infrastructure.Sales.SaleSalespersonDirectory>();
builder.Services.AddSingleton<RetailPOS.Application.Pos.Abstractions.ICashSessionCloseSummaryPdfRenderer,
    RetailPOS.Infrastructure.Pos.CashSessionCloseSummaryPdfRenderer>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleReadStore,
    RetailPOS.Infrastructure.Sales.SaleReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleCreationStore,
    RetailPOS.Infrastructure.Sales.SaleCreationStore>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleVoidStore,
    RetailPOS.Infrastructure.Sales.SaleVoidStore>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleRefundStore,
    RetailPOS.Infrastructure.Sales.SaleVoidStore>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleMutationOutletAuthorizer,
    SaleMutationOutletAuthorizerAdapter>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleReceiptHistoryWriter,
    RetailPOS.Infrastructure.Sales.SaleReceiptHistoryWriter>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleReceiptAttemptAuditWriter,
    RetailPOS.Infrastructure.Sales.SaleReceiptAttemptAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleReceiptSettingsProvider,
    RetailPOS.Infrastructure.Sales.SaleReceiptSettingsProvider>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleReceiptActionAuditWriter,
    RetailPOS.Infrastructure.Sales.SaleReceiptActionAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleReceiptPdfRenderer,
    RetailPOS.Infrastructure.Sales.SaleReceiptPdfRenderer>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IInventoryReadStore,
    RetailPOS.Infrastructure.ReadStores.InventoryReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockLedgerReadStore,
    RetailPOS.Infrastructure.ReadStores.StockLedgerReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockLedgerReadScopeProvider,
    StockLedgerReadScopeProvider>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockAdjustmentReadStore,
    RetailPOS.Infrastructure.ReadStores.StockAdjustmentReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockAdjustmentReadScopeProvider,
    StockAdjustmentReadScopeProvider>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferReadStore,
    RetailPOS.Infrastructure.ReadStores.StockTransferReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferReadScopeProvider,
    StockTransferReadScopeProvider>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferCreationStore,
    RetailPOS.Infrastructure.StockTransfers.StockTransferCreationStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferDraftStore,
    RetailPOS.Infrastructure.StockTransfers.StockTransferDraftStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferSubmitStore,
    RetailPOS.Infrastructure.StockTransfers.StockTransferSubmitStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferApprovalStore,
    RetailPOS.Infrastructure.StockTransfers.StockTransferApprovalStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferRejectionStore,
    RetailPOS.Infrastructure.StockTransfers.StockTransferRejectionStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferDispatchStore,
    RetailPOS.Infrastructure.StockTransfers.StockTransferDispatchStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferDispatchCacheInvalidator,
    StockTransferDispatchCacheInvalidator>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferReceiveStore,
    RetailPOS.Infrastructure.StockTransfers.StockTransferReceiveStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferReceiptAuditWriter,
    RetailPOS.Infrastructure.StockTransfers.StockTransferReceiptAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferReceiveCacheInvalidator,
    StockTransferReceiveCacheInvalidator>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferCancellationStore,
    RetailPOS.Infrastructure.StockTransfers.StockTransferCancellationStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockRequisitionTransferStore,
    RetailPOS.Infrastructure.StockRequisitions.StockRequisitionTransferStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockRequisitionReadStore,
    RetailPOS.Infrastructure.ReadStores.StockRequisitionReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockRequisitionWriteStore,
    RetailPOS.Infrastructure.StockRequisitions.StockRequisitionWriteStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockRequisitionLifecycleStore,
    RetailPOS.Infrastructure.StockRequisitions.StockRequisitionLifecycleStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockRequisitionBusinessScopeProvider,
    StockRequisitionBusinessScopeProvider>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockAdjustmentCreateContextProvider,
    StockAdjustmentCreateContextProvider>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockAdjustmentCreationStore,
    RetailPOS.Infrastructure.ReadStores.StockAdjustmentCreationStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockAdjustmentApprovalStore,
    RetailPOS.Infrastructure.ReadStores.StockAdjustmentApprovalStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockAdjustmentLifecycleStore,
    RetailPOS.Infrastructure.ReadStores.StockAdjustmentLifecycleStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockAdjustmentReversalStore,
    RetailPOS.Infrastructure.ReadStores.StockAdjustmentReversalStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockAdjustmentPostingAccountResolver,
    StockAdjustmentPostingAccountResolver>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockAdjustmentApprovalCacheInvalidator,
    StockAdjustmentApprovalCacheInvalidator>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IInventoryWriteStore,
    RetailPOS.Infrastructure.InventoryStores.InventoryWriteStore>();
builder.Services.AddScoped<RetailPOS.Application.Authentication.Context.UserContextPolicy>();
builder.Services.AddScoped<RetailPOS.Application.Identity.LocationAccess.IUserOutletAccess,
    RetailPOS.Application.Identity.LocationAccess.UserOutletAccess>();
builder.Services.AddScoped<RetailPOS.Application.Identity.LocationAccess.IUserOutletAccessStore,
    RetailPOS.Infrastructure.Identity.UserOutletAccessStore>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IProductRepository, RetailPOS.Infrastructure.Repositories.ProductRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IProductVariantRepository, RetailPOS.Infrastructure.Repositories.ProductVariantRepository>();
builder.Services.AddScoped<RetailPOS.Application.Variations.Abstractions.IVariationReadStore,
    RetailPOS.Infrastructure.Variations.VariationReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Variations.Abstractions.IVariationWriteStore,
    RetailPOS.Infrastructure.Variations.VariationWriteStore>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IInventoryRepository, RetailPOS.Infrastructure.Repositories.InventoryRepository>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IPurchaseOrderRepository, RetailPOS.Infrastructure.Repositories.PurchaseOrderRepository>();
builder.Services.AddScoped<RetailPOS.Application.PurchaseOrders.Abstractions.IPurchaseOrderCreationStore,
    RetailPOS.Infrastructure.PurchaseOrders.PurchaseOrderCreationStore>();
builder.Services.AddScoped<RetailPOS.Application.PurchaseOrders.Abstractions.IPurchaseOrderUpdateStore,
    RetailPOS.Infrastructure.PurchaseOrders.PurchaseOrderUpdateStore>();
builder.Services.AddScoped<RetailPOS.Application.PurchaseOrders.Abstractions.IPurchaseOrderDeletionStore,
    RetailPOS.Infrastructure.PurchaseOrders.PurchaseOrderDeletionStore>();
builder.Services.AddScoped<RetailPOS.Application.PurchaseOrders.Abstractions.IPurchaseAndReceiveStore,
    RetailPOS.Infrastructure.PurchaseOrders.PurchaseAndReceiveStore>();
builder.Services.AddScoped<RetailPOS.Application.PurchaseOrders.Abstractions.IPurchaseAndReceiveStockLedgerWriter,
    PurchaseAndReceiveStockLedgerWriterAdapter>();
builder.Services.AddScoped<RetailPOS.Application.PurchaseOrders.Abstractions.IPurchaseReceiptJournalPoster,
    PurchaseReceiptJournalPoster>();
builder.Services.AddScoped<RetailPOS.Application.PurchaseOrders.Abstractions.IPurchaseOrderReadStore,
    RetailPOS.Infrastructure.PurchaseOrders.PurchaseOrderReadStore>();
builder.Services.AddScoped<RetailPOS.Application.PurchaseOrders.Abstractions.IPurchaseReportReadStore,
    RetailPOS.Infrastructure.PurchaseOrders.PurchaseReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.PurchaseOrders.Abstractions.IPurchaseOrderTenantScope,
    PurchaseOrderTenantScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.PurchaseOrders.Abstractions.IPurchaseOrderLocationAuthorizer,
    PurchaseOrderLocationAuthorizerAdapter>();
builder.Services.AddScoped<RetailPOS.Application.AccountingSettlements.Abstractions.ISettlementTransferReadStore,
    RetailPOS.Infrastructure.AccountingSettlements.SettlementTransferReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingSettlements.Abstractions.ISettlementTransferCommandStore,
    RetailPOS.Infrastructure.AccountingSettlements.SettlementTransferCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingSettlements.Abstractions.ISettlementPostingAccountResolver,
    SettlementPostingAccountResolver>();
builder.Services.AddScoped<RetailPOS.Application.AccountingSettlements.Abstractions.ISettlementJournalPoster,
    SettlementJournalPoster>();
builder.Services.AddScoped<RetailPOS.Application.AccountingSettlements.Abstractions.ISettlementJournalReverser,
    SettlementJournalReverser>();
builder.Services.AddScoped<RetailPOS.Application.AccountingSettlements.Abstractions.ISettlementTransferTenantScope,
    SettlementTransferTenantScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.BankStatements.Abstractions.IBankStatementImportCommandStore,
    RetailPOS.Infrastructure.BankStatements.BankStatementImportCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.BankStatements.Abstractions.IBankStatementImportTenantScope,
    BankStatementImportTenantScope>();
builder.Services.AddScoped<RetailPOS.Application.BankStatements.Abstractions.IBankStatementImportLocationAuthorizer,
    BankStatementImportLocationAuthorizer>();
builder.Services.AddScoped<RetailPOS.Application.BankStatements.Abstractions.IBankStatementImportReadStore,
    RetailPOS.Infrastructure.BankStatements.BankStatementImportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.BankStatements.Parsing.BankStatementCsvParser>();
builder.Services.AddScoped<RetailPOS.Application.Barcodes.Abstractions.IBarcodeCompanyNameReadStore,
    RetailPOS.Infrastructure.Barcodes.BarcodeCompanyNameReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Barcodes.Abstractions.IBarcodePrintHistoryReadStore,
    RetailPOS.Infrastructure.Barcodes.BarcodePrintHistoryReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Barcodes.Abstractions.IBarcodePrintHistoryWriteStore,
    RetailPOS.Infrastructure.Barcodes.BarcodePrintHistoryWriteStore>();
builder.Services.AddScoped<RetailPOS.Application.Barcodes.Abstractions.IBarcodePrintHistoryReadScope,
    BarcodePrintHistoryReadScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.Barcodes.Abstractions.IBarcodePrintHistoryWriteScope,
    BarcodePrintHistoryReadScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.Barcodes.Abstractions.IBarcodeTemplateReadStore,
    RetailPOS.Infrastructure.Barcodes.BarcodeTemplateReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Barcodes.Abstractions.IBarcodeTemplateDefaultsProvisioner,
    RetailPOS.Infrastructure.Barcodes.BarcodeTemplateDefaultsProvisioner>();
builder.Services.AddScoped<RetailPOS.Application.Barcodes.Abstractions.IBarcodeTemplateReadScope,
    BarcodeTemplateReadScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.Barcodes.Abstractions.IBarcodeTemplateWriteStore,
    RetailPOS.Infrastructure.Barcodes.BarcodeTemplateWriteStore>();
builder.Services.AddScoped<RetailPOS.Application.Barcodes.Abstractions.IBarcodeTemplateWriteScope,
    BarcodeTemplateReadScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.FeatureEntitlements.Abstractions.IFeatureEntitlementReadStore,
    RetailPOS.Infrastructure.FeatureEntitlements.FeatureEntitlementReadStore>();
builder.Services.AddScoped<RetailPOS.Application.BusinessAdministration.Abstractions.IBusinessAdministrationReadStore,
    RetailPOS.Infrastructure.BusinessAdministration.BusinessAdministrationReadStore>();
builder.Services.AddScoped<RetailPOS.Application.BusinessAdministration.Abstractions.IBusinessAdministrationWriteStore,
    RetailPOS.Infrastructure.BusinessAdministration.BusinessAdministrationWriteStore>();
builder.Services.AddScoped<RetailPOS.Application.BusinessAdministration.Abstractions.IBusinessOnboardingWriteStore,
    RetailPOS.Infrastructure.BusinessAdministration.BusinessOnboardingWriteStore>();
builder.Services.AddScoped<RetailPOS.Infrastructure.AccountingConfigurations.AccountingConfigurationProvisioner>();
builder.Services.AddScoped<RetailPOS.Application.AccountingConfiguration.Abstractions.IAccountingConfigurationInitializationStore,
    RetailPOS.Infrastructure.AccountingConfigurations.AccountingConfigurationInitializationStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingConfiguration.Abstractions.IAccountingConfigurationTenantScope,
    AccountingConfigurationTenantScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.AccountingConfiguration.Abstractions.IAccountingOutletConfigurationReadStore,
    RetailPOS.Infrastructure.AccountingConfigurations.AccountingOutletConfigurationReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingConfiguration.Abstractions.IAccountingPostingAccountReadStore,
    RetailPOS.Infrastructure.AccountingConfigurations.AccountingPostingAccountReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingConfiguration.Abstractions.IAccountingConfigurationWriteStore,
    RetailPOS.Infrastructure.AccountingConfigurations.AccountingConfigurationWriteStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingConfiguration.Abstractions.IAccountingOutletConfigurationWriteStore,
    RetailPOS.Infrastructure.AccountingConfigurations.AccountingOutletConfigurationWriteStore>();
builder.Services.AddScoped<RetailPOS.Application.FinancialPeriods.Abstractions.IFinancialPeriodTenantScope,
    FinancialPeriodTenantScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.FinancialPeriods.Abstractions.IFinancialPeriodReadStore,
    RetailPOS.Infrastructure.FinancialPeriods.FinancialPeriodReadStore>();
builder.Services.AddScoped<RetailPOS.Application.FinancialPeriods.Abstractions.IFinancialYearCommandStore,
    RetailPOS.Infrastructure.FinancialPeriods.FinancialYearCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.FinancialPeriods.Abstractions.IAccountingPeriodCommandStore,
    RetailPOS.Infrastructure.FinancialPeriods.AccountingPeriodCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.Barcodes.Abstractions.IBarcodeTenantScope,
    BarcodeTenantScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.BankReconciliation.Abstractions.IBankReconciliationReadStore,
    RetailPOS.Infrastructure.BankReconciliation.BankReconciliationReadStore>();
builder.Services.AddScoped<RetailPOS.Application.BankReconciliation.Abstractions.IBankReconciliationSessionCommandStore,
    RetailPOS.Infrastructure.BankReconciliation.BankReconciliationSessionCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.BankReconciliation.Abstractions.IBankReconciliationMatchCommandStore,
    RetailPOS.Infrastructure.BankReconciliation.BankReconciliationMatchCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.BankReconciliation.Abstractions.IBankReconciliationCloseCommandStore,
    RetailPOS.Infrastructure.BankReconciliation.BankReconciliationCloseCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.BankReconciliation.Abstractions.IBankStatementAdjustmentCommandStore,
    RetailPOS.Infrastructure.BankReconciliation.BankStatementAdjustmentCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.BankReconciliation.Abstractions.IBankStatementAdjustmentApprovalStore,
    RetailPOS.Infrastructure.BankReconciliation.BankStatementAdjustmentApprovalStore>();
builder.Services.AddScoped<RetailPOS.Application.BankReconciliation.Abstractions.IBankStatementAdjustmentReversalStore,
    RetailPOS.Infrastructure.BankReconciliation.BankStatementAdjustmentReversalStore>();
builder.Services.AddScoped<RetailPOS.Application.BankReconciliation.Abstractions.IBankAccountCommandStore,
    RetailPOS.Infrastructure.BankReconciliation.BankAccountCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.Subscriptions.Abstractions.ISubscriptionRenewalReminderStore,
    RetailPOS.Infrastructure.Subscriptions.SubscriptionRenewalReminderStore>();
builder.Services.AddScoped<RetailPOS.Application.BankReconciliation.Abstractions.IBankStatementAdjustmentJournalPoster,
    BankStatementAdjustmentJournalPoster>();
builder.Services.AddScoped<RetailPOS.Application.BankReconciliation.Abstractions.IBankReconciliationTenantScope,
    BankReconciliationTenantScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.BankReconciliation.Abstractions.IBankStatementExceptionWriteStore,
    RetailPOS.Infrastructure.BankReconciliation.BankStatementExceptionWriteStore>();
builder.Services.AddScoped<RetailPOS.Application.AuditEvents.Abstractions.IAuditEventReadStore,
    RetailPOS.Infrastructure.AuditEvents.AuditEventReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AuditEvents.Abstractions.IAuditEventCommandStore,
    RetailPOS.Infrastructure.AuditEvents.AuditEventCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.AuditEvents.Abstractions.IRequiredAuditTransaction,
    RetailPOS.Infrastructure.AuditEvents.RequiredAuditTransaction>();
builder.Services.AddScoped<RetailPOS.Application.AuditEvents.Abstractions.IAuditReadAccessScope,
    AuditReadAccessScopeAdapter>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<RetailPOS.Application.Integration.IIntegrationReplayAuthorization,
    RetailPOS.Infrastructure.Integration.DisabledIntegrationReplayAuthorization>();
builder.Services.AddScoped<RetailPOS.Application.Integration.IIntegrationOutbox,
    RetailPOS.Infrastructure.Integration.IntegrationOutbox>();
builder.Services.AddScoped<AuditContext>(sp => (AuditContext)sp.GetRequiredService<IAuditContext>());
builder.Services.AddScoped<RetailPOS.Infrastructure.Integration.IntegrationInbox>();
builder.Services.AddScoped<RetailPOS.Application.AccountingJournals.Abstractions.IFrozenSystemJournalStore,
    RetailPOS.Infrastructure.AccountingJournals.FrozenSystemJournalStore>();
var deliveryOptions = builder.Configuration.GetSection("DurableDelivery")
    .Get<RetailPOS.Infrastructure.Integration.RabbitMqDeliveryOptions>()
    ?? new RetailPOS.Infrastructure.Integration.RabbitMqDeliveryOptions();
if (deliveryOptions.Enabled)
{
    deliveryOptions.Validate();
    var retryOptions = builder.Configuration.GetSection("DurableDelivery:Retry")
        .Get<RetailPOS.Application.Integration.DurableDeliveryOptions>()
        ?? new RetailPOS.Application.Integration.DurableDeliveryOptions();
    retryOptions.Validate();
    if (retryOptions.LeaseSeconds <= retryOptions.BatchSize * deliveryOptions.PublishTimeoutSeconds)
        throw new InvalidOperationException("Outbox lease must exceed the configured bounded batch confirm duration.");
    builder.Services.AddSingleton(deliveryOptions);
    builder.Services.AddSingleton(retryOptions);
    builder.Services.AddScoped<RetailPOS.Application.Integration.IConfirmedIntegrationPublisher,
        RetailPOS.Infrastructure.Integration.RabbitMqConfirmedPublisher>();
    builder.Services.AddScoped<RetailPOS.Infrastructure.Integration.OutboxDispatcher>();
    builder.Services.AddHostedService<IntegrationOutboxHostedService>();
}
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IGrnRepository, RetailPOS.Infrastructure.Repositories.GrnRepository>();

// Register Master Data Services
builder.Services.AddScoped<RetailPOS.Application.Outlets.Abstractions.IOutletScopeProvider,
    OutletScopeProvider>();
builder.Services.AddScoped<RetailPOS.Application.Outlets.Abstractions.IOutletReadStore,
    RetailPOS.Infrastructure.Outlets.OutletReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Warehouses.Abstractions.IWarehouseReadScopeProvider,
    WarehouseReadScopeProvider>();
builder.Services.AddScoped<RetailPOS.Application.Warehouses.Abstractions.IWarehouseReadStore,
    RetailPOS.Infrastructure.Warehouses.WarehouseReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Warehouses.Abstractions.IWarehouseWriteStore,
    RetailPOS.Infrastructure.Warehouses.WarehouseWriteStore>();
builder.Services.AddScoped<RetailPOS.Application.Suppliers.Abstractions.ISupplierReadScopeProvider,
    SupplierReadScopeProvider>();
builder.Services.AddScoped<RetailPOS.Application.Suppliers.Abstractions.ISupplierReadStore,
    RetailPOS.Infrastructure.Suppliers.SupplierReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Suppliers.Abstractions.ISupplierWriteStore,
    RetailPOS.Infrastructure.Suppliers.SupplierWriteStore>();
builder.Services.AddScoped<RetailPOS.Application.Bills.Abstractions.IBillReadScopeProvider,
    BillReadScopeProvider>();
builder.Services.AddScoped<RetailPOS.Application.Bills.Abstractions.IBillReadStore,
    RetailPOS.Infrastructure.Bills.BillReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Outlets.Abstractions.IOutletWriteStore,
    RetailPOS.Infrastructure.Outlets.OutletWriteStore>();
builder.Services.AddScoped<RetailPOS.Application.Outlets.Abstractions.IBusinessSetupOutletWriteStore,
    RetailPOS.Infrastructure.Outlets.OutletWriteStore>();
builder.Services.AddScoped<IVariantDisplayFormatterService, VariantDisplayFormatterService>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductIdentifierResolver,
    RetailPOS.Application.Products.Services.ProductIdentifierResolver>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductIdentifierLookupStore,
    RetailPOS.Infrastructure.Products.ProductIdentifierLookupStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductCreateStore,
    RetailPOS.Infrastructure.Products.ProductCreateStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductUpdateStore,
    RetailPOS.Infrastructure.Products.ProductUpdateStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductDeleteStore,
    RetailPOS.Infrastructure.Products.ProductDeleteStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductSkuStore,
    RetailPOS.Infrastructure.Products.ProductSkuStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductVariationsReadStore,
    RetailPOS.Infrastructure.Products.ProductVariationsReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductVariationCommandStore,
    RetailPOS.Infrastructure.Products.ProductVariationCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductCombinationGenerationStore,
    RetailPOS.Infrastructure.Products.ProductCombinationGenerationStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductCombinationsReadStore,
    RetailPOS.Infrastructure.Products.ProductCombinationsReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductManualCombinationStore,
    RetailPOS.Infrastructure.Products.ProductManualCombinationStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductVariantMaintenanceStore,
    RetailPOS.Infrastructure.Products.ProductVariantMaintenanceStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IInventoryReportReadStore,
    RetailPOS.Infrastructure.InventoryReports.InventoryReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IProductLedgerReportReadStore,
    RetailPOS.Infrastructure.InventoryReports.ProductLedgerReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransactionReportReadStore,
    RetailPOS.Infrastructure.InventoryReports.StockTransactionReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockMovementReportReadStore,
    RetailPOS.Infrastructure.InventoryReports.StockMovementReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockValuationReportReadStore,
    RetailPOS.Infrastructure.InventoryReports.StockValuationReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IOutletWiseStockReportReadStore,
    RetailPOS.Infrastructure.InventoryReports.OutletWiseStockReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.ILowStockReportReadStore,
    RetailPOS.Infrastructure.InventoryReports.LowStockReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IOutOfStockReportReadStore,
    RetailPOS.Infrastructure.InventoryReports.OutOfStockReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.INegativeStockReportReadStore,
    RetailPOS.Infrastructure.InventoryReports.NegativeStockReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockAdjustmentReportReadStore,
    RetailPOS.Infrastructure.InventoryReports.StockAdjustmentReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockTransferReportReadStore,
    RetailPOS.Infrastructure.InventoryReports.StockTransferReportReadStore>();
builder.Services.AddSingleton(
    builder.Configuration.GetSection("OwnerDashboard").Get<RetailPOS.Application.OwnerDashboards.OwnerDashboardPolicy>()
    ?? new RetailPOS.Application.OwnerDashboards.OwnerDashboardPolicy());
builder.Services.AddScoped<RetailPOS.Application.OwnerDashboards.IOwnerDashboardScopeProvider,
    OwnerDashboardScopeProvider>();
builder.Services.AddScoped<RetailPOS.Application.Outlets.SalesTargets.IOutletSalesTargetReadStore,
    RetailPOS.Infrastructure.Outlets.OutletSalesTargetReadStore>();
builder.Services.AddScoped<RetailPOS.Application.OwnerDashboards.ISalesFinanceReadStore,
    RetailPOS.Infrastructure.OwnerDashboards.SalesFinanceReadStore>();
builder.Services.AddScoped<RetailPOS.Application.OwnerDashboards.IInventoryDashboardReadStore,
    RetailPOS.Infrastructure.OwnerDashboards.InventoryDashboardReadStore>();
builder.Services.AddScoped<RetailPOS.Application.SalesReports.Abstractions.ISalesReportReadStore,
    RetailPOS.Infrastructure.SalesReports.SalesReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.SalesReports.Abstractions.ISalesReportReadScopeProvider,
    SalesReportReadScopeProvider>();
builder.Services.AddScoped<RetailPOS.Application.Mobile.IMobileSingleBusinessReadinessGuard,
    RetailPOS.Infrastructure.Mobile.MobileSingleBusinessReadinessGuard>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IStockCountSearchReadStore,
    RetailPOS.Infrastructure.StockCounts.StockCountSearchReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IMobileStockTakeReadStore,
    RetailPOS.Infrastructure.StockCounts.MobileStockTakeReadStore>();
builder.Services.AddScoped<RetailPOS.Infrastructure.StockCounts.StockCountMovementEvidenceService>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IMobileStockTakeReviewStore,
    RetailPOS.Infrastructure.StockCounts.MobileStockTakeReviewStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IMobileStockCountOperationStore,
    RetailPOS.Infrastructure.StockCounts.MobileStockCountOperationStore>();
var mobileSubmissionOptions = builder.Configuration.GetSection("MobileStockTake:Submission")
    .Get<RetailPOS.Application.Inventory.StockCounts.Abstractions.MobileStockTakeSubmissionOptions>()
    ?? new RetailPOS.Application.Inventory.StockCounts.Abstractions.MobileStockTakeSubmissionOptions();
mobileSubmissionOptions.Validate();
builder.Services.AddSingleton(mobileSubmissionOptions);
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IMobileStockTakeSubmissionStore,
    RetailPOS.Infrastructure.StockCounts.MobileStockTakeSubmissionStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IMobileStockTakeSubmissionActorPolicy,
    RetailPOS.Application.Inventory.StockCounts.MobileStockTakeSubmissionActorPolicy>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IMobileStockTakeSubmissionProcessor,
    RetailPOS.Infrastructure.StockCounts.MobileStockTakeSubmissionProcessor>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Integration.MobileStockTakeSubmissionDelivery>();
if (builder.Configuration.GetValue("MobileStockTake:Submission:WorkerEnabled", true))
{
    builder.Services.AddHostedService<MobileStockTakeSubmissionSweeperHostedService>();
    if (deliveryOptions.Enabled)
        builder.Services.AddHostedService<MobileStockTakeSubmissionConsumerHostedService>();
}
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IStockCountCreationStore,
    RetailPOS.Infrastructure.StockCounts.StockCountCreationStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IStockCountUploadStore,
    RetailPOS.Infrastructure.StockCounts.StockCountUploadStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IStockCountLifecycleStore,
    RetailPOS.Infrastructure.StockCounts.StockCountLifecycleStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IStockCountApprovalStore,
    RetailPOS.Infrastructure.StockCounts.StockCountApprovalStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IStockCountAdjustmentDraftStore,
    RetailPOS.Infrastructure.StockCounts.StockCountAdjustmentDraftStore>();
var stockCountAdjustmentOptions = builder.Configuration.GetSection("StockCount:Adjustment")
    .Get<RetailPOS.Application.Inventory.StockCounts.StockCountAdjustmentOptions>()
    ?? new RetailPOS.Application.Inventory.StockCounts.StockCountAdjustmentOptions();
stockCountAdjustmentOptions.Validate();
builder.Services.AddSingleton(stockCountAdjustmentOptions);
builder.Services.AddSingleton<RetailPOS.Application.Inventory.StockCounts.IStockCountAsyncAdjustmentSwitch,
    StockCountAsyncAdjustmentConfigurationSwitch>();
builder.Services.AddScoped<RetailPOS.Infrastructure.StockCounts.StockCountAdjustmentDraftBuilder>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IStockCountAdjustmentJobStore,
    RetailPOS.Infrastructure.StockCounts.StockCountAdjustmentJobStore>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IStockCountAdjustmentApproverPolicy,
    RetailPOS.Application.Inventory.StockCounts.StockCountAdjustmentApproverPolicy>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IStockCountAdjustmentProcessor,
    RetailPOS.Infrastructure.StockCounts.StockCountAdjustmentProcessor>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Integration.StockCountAdjustmentDelivery>();
if (builder.Configuration.GetValue("StockCount:Adjustment:WorkerEnabled", true))
{
    builder.Services.AddHostedService<StockCountAdjustmentSweeperHostedService>();
    if (deliveryOptions.Enabled)
        builder.Services.AddHostedService<StockCountAdjustmentConsumerHostedService>();
}
builder.Services.AddScoped<RetailPOS.Application.Inventory.StockCounts.Abstractions.IStockCountSearchReadScopeProvider,
    StockCountSearchReadScopeProvider>();
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IInventoryReportReadScopeProvider,
    InventoryReportReadScopeProvider>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductMediaStore,
    RetailPOS.Infrastructure.Products.ProductMediaStore>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductImageProcessor,
    RetailPOS.Infrastructure.Products.ProductImageProcessor>();
builder.Services.AddScoped<RetailPOS.Application.Products.Abstractions.IProductImageFileStore,
    RetailPOS.Infrastructure.Products.ProductImageFileStore>();
builder.Services.AddSingleton(new RetailPOS.Infrastructure.Products.ProductImageStorageOptions(
    builder.Environment.WebRootPath,
    builder.Environment.ContentRootPath));
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierPaymentCreationStore,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPaymentCreationStore>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierAdvanceCreationStore,
    RetailPOS.Infrastructure.SupplierPayments.SupplierAdvanceCreationStore>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierAdvanceSubmissionStore,
    RetailPOS.Infrastructure.SupplierPayments.SupplierAdvanceSubmissionStore>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierAdvanceCreationAuditWriter,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPaymentCreationAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierAdvanceSubmissionAuditWriter,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPaymentCreationAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierAdvanceApprovalStore,
    RetailPOS.Infrastructure.SupplierPayments.SupplierAdvanceApprovalStore>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierAdvanceApprovalAuditWriter,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPaymentCreationAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierAdvanceApprovalJournalPoster,
    SupplierAdvanceApprovalJournalPoster>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierAdvanceUtilizationStore,
    RetailPOS.Infrastructure.SupplierPayments.SupplierAdvanceUtilizationStore>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierAdvanceUtilizationAuditWriter,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPaymentCreationAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierAdvanceUtilizationJournalPoster,
    SupplierAdvanceUtilizationJournalPoster>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierPaymentCreationAuditWriter,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPaymentCreationAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierPaymentSubmissionStore,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPaymentCreationStore>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierPaymentSubmissionAuditWriter,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPaymentCreationAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierPaymentApprovalStore,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPaymentApprovalStore>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierPaymentApprovalAuditWriter,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPaymentCreationAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierPaymentApprovalJournalPoster,
    SupplierPaymentApprovalJournalPoster>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierPaymentReversalStore,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPaymentReversalStore>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierPaymentReversalAuditWriter,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPaymentCreationAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierPaymentReversalJournalPoster,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPaymentReversalJournalPoster>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierPaymentCreationAuthorizer,
    SupplierPaymentCreationAuthorizer>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierPaymentWorkflowAuthorizer,
    SupplierPaymentCreationAuthorizer>();
builder.Services.AddScoped<IGrnCompletionStore, GrnCompletionStore>();
builder.Services.AddScoped<RetailPOS.Application.Grns.Abstractions.IGrnCreationStore,
    RetailPOS.Infrastructure.Grns.GrnCreationStore>();
builder.Services.AddScoped<RetailPOS.Application.Grns.Abstractions.IGrnCreationAuditWriter,
    RetailPOS.Infrastructure.Grns.GrnCreationAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.Grns.Abstractions.IGrnReadStore,
    RetailPOS.Infrastructure.Grns.GrnReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Grns.Abstractions.IPendingGrnPurchaseOrderReadStore,
    RetailPOS.Infrastructure.Grns.GrnReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Grns.Abstractions.IGrnPoVarianceReadStore,
    RetailPOS.Infrastructure.Grns.GrnPoVarianceReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Grns.Abstractions.IGrnTenantScope,
    GrnTenantScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.Grns.Abstractions.IGrnReadScopeProvider,
    GrnReadScopeProviderAdapter>();
builder.Services.AddScoped<RetailPOS.Application.Grns.Abstractions.IGrnReceiptLocationAuthorizer,
    GrnReceiptLocationAuthorizerAdapter>();
builder.Services.AddScoped<IGrnCompletionAuthorizer, GrnCompletionAuthorizerAdapter>();
builder.Services.AddScoped<IGrnCompletionStockLedgerWriter, GrnCompletionStockLedgerWriterAdapter>();
builder.Services.AddScoped<IGrnCompletionAccountingPoster, GrnCompletionAccountingPosterAdapter>();
builder.Services.AddScoped<IGrnCompletionAuditWriter, GrnCompletionAuditWriterAdapter>();

// Customer Management
builder.Services.AddScoped<RetailPOS.Application.Customers.Abstractions.ICustomerCreateStore,
    RetailPOS.Infrastructure.Customers.CustomerCreateStore>();
builder.Services.AddScoped<RetailPOS.Application.Customers.Abstractions.ICustomerUpdateStore,
    RetailPOS.Infrastructure.Customers.CustomerUpdateStore>();
builder.Services.AddScoped<RetailPOS.Application.Customers.Abstractions.ICustomerDeleteStore,
    RetailPOS.Infrastructure.Customers.CustomerDeleteStore>();
builder.Services.AddScoped<RetailPOS.Application.Customers.Abstractions.ICustomerLoyaltyStore,
    RetailPOS.Infrastructure.Customers.CustomerLoyaltyStore>();
builder.Services.AddScoped<RetailPOS.Application.Customers.Abstractions.ICustomerReadStore,
    RetailPOS.Infrastructure.Customers.CustomerReadStore>();
// Sales
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleCashSessionValidator,
    RetailPOS.Infrastructure.Pos.SaleCashSessionValidator>();
builder.Services.AddScoped<RetailPOS.Application.Pos.Abstractions.ICashSessionExpiryPublisher,
    CashSessionExpiryPublisherAdapter>();
builder.Services.AddSingleton<RetailPOS.Infrastructure.Settings.JsonSystemSettingsStore>();
builder.Services.AddScoped<RetailPOS.Application.Settings.Abstractions.ICompanyTimeZoneProvider,
    RetailPOS.Application.Settings.Abstractions.CompanyTimeZoneProvider>();
builder.Services.AddScoped<RetailPOS.Application.Settings.Abstractions.ICurrentCompanyTimeZone,
    RetailPOS.Application.Settings.Abstractions.CurrentCompanyTimeZone>();
builder.Services.AddSingleton<RetailPOS.Application.Settings.Abstractions.ISystemSettingsStore>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.Settings.JsonSystemSettingsStore>());
builder.Services.AddSingleton<RetailPOS.Application.Settings.Abstractions.ICompanyTimeZoneSnapshot>(sp =>
    sp.GetRequiredService<RetailPOS.Infrastructure.Settings.JsonSystemSettingsStore>());
builder.Services.AddScoped<RetailPOS.Application.Settings.Abstractions.IBusinessPosSettingsStore,
    RetailPOS.Infrastructure.Settings.BusinessPosSettingsStore>();
builder.Services.AddScoped<RetailPOS.Application.Settings.Commands.IBusinessPosSettingsAuditWriter,
    BusinessPosSettingsAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleInvoiceNumberAllocator,
    RetailPOS.Infrastructure.Sales.SaleInvoiceNumberAllocator>();
// D10 (Phase 3.1): sales, voids and refunds commit a durable posting intent; the GL journal is posted afterwards by
// the background worker (or an operator retry), exactly once per intent.
var saleFinancialPostingOptions = builder.Configuration.GetSection("FinancialPosting:Sales")
    .Get<RetailPOS.Application.Sales.FinancialPostings.SaleFinancialPostingOptions>()
    ?? new RetailPOS.Application.Sales.FinancialPostings.SaleFinancialPostingOptions();
saleFinancialPostingOptions.Validate();
builder.Services.AddSingleton(saleFinancialPostingOptions);
var cashDifferencePostingOptions = builder.Configuration.GetSection("FinancialPosting:CashDifferences")
    .Get<CashDifferencePostingWorkerOptions>() ?? new CashDifferencePostingWorkerOptions();
cashDifferencePostingOptions.Validate();
builder.Services.AddSingleton(cashDifferencePostingOptions);
builder.Services.AddSingleton<RetailPOS.Infrastructure.Sales.SaleFinancialPostingSignal>();
builder.Services.AddSingleton<FinancialPostingWorkerHealth>();
builder.Services.AddScoped<RetailPOS.Application.Sales.FinancialPostings.ISaleFinancialPostingStore,
    RetailPOS.Infrastructure.Sales.SaleFinancialPostingStore>();
builder.Services.AddScoped<RetailPOS.Application.Sales.FinancialPostings.ISaleFinancialPostingWriter,
    RetailPOS.Infrastructure.Sales.SaleFinancialPostingWriter>();
builder.Services.AddScoped<RetailPOS.Application.Sales.FinancialPostings.ISaleFinancialJournalPoster,
    SaleFinancialJournalPoster>();
builder.Services.AddScoped<RetailPOS.Application.Sales.FinancialPostings.ISaleFinancialPostingAuditWriter,
    RetailPOS.Infrastructure.Sales.SaleFinancialPostingAuditWriter>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Sales.FinancialPostingHeartbeatStore>();
builder.Services.AddSingleton<SaleFinancialPostingBatchRunner>();
builder.Services.AddSingleton<CashDifferencePostingBatchRunner>();
// P3.2-D05: the same build runs as the API (same-process workers) or as a dedicated financial worker process.
var financialPostingPlan = FinancialPostingWorkerPlan.Resolve(
    builder.Configuration["FinancialPosting:HostRole"],
    saleFinancialPostingOptions.WorkerEnabled,
    cashDifferencePostingOptions.WorkerEnabled,
    builder.Environment.IsEnvironment("Testing"));
builder.Services.AddSingleton(financialPostingPlan);
builder.Services.AddSingleton(new FinancialPostingInstance(financialPostingPlan.Role));
builder.Services.AddSingleton<FinancialPostingHeartbeatPublisher>();
if (financialPostingPlan.RunSales)
    builder.Services.AddHostedService<SaleFinancialPostingHostedService>();
if (financialPostingPlan.RunCashDifferences)
    builder.Services.AddHostedService<CashDifferencePostingHostedService>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleCreationAuditWriter,
    SaleCreationAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleStockLedgerWriter,
    SaleStockLedgerWriter>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleStockCacheInvalidator,
    SaleStockCacheInvalidator>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleCompletedPublisher,
    SaleCompletedPublisher>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleCreationLogger,
    SaleCreationLogger>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleVoidStockLedgerWriter,
    SaleVoidStockLedgerWriter>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleVoidAuditWriter,
    SaleVoidAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleVoidCacheInvalidator,
    SaleVoidCacheInvalidator>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleVoidLogger,
    SaleVoidLogger>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleRefundStockLedgerWriter,
    SaleRefundStockLedgerWriter>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleRefundAuditWriter,
    SaleRefundAuditWriter>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleRefundCacheInvalidator,
    SaleRefundCacheInvalidator>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.ISaleRefundLogger,
    SaleRefundLogger>();

// NEW — Hold/Park Sale
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.IHeldSaleStore,
    RetailPOS.Infrastructure.Sales.HeldSaleStore>();
builder.Services.AddScoped<RetailPOS.Application.Sales.Abstractions.IHeldSaleOutletAuthorizer,
    HeldSaleOutletAuthorizerAdapter>();

// NEW — Sale event hook (default in-process publisher; extend by registering ISaleCompletedHandler)
builder.Services.AddScoped<ISaleEventPublisher, LoggingSaleEventPublisher>();

// Stock Movement
builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockAdjustmentAuditWriter,
    RetailPOS.Infrastructure.ReadStores.StockAdjustmentAuditWriter>();

builder.Services.AddScoped<RetailPOS.Application.Inventory.Abstractions.IStockLedgerWriter,
    RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>();
builder.Services.AddScoped<RetailPOS.Application.SupplierPayments.Abstractions.ISupplierPayableRecognitionWriter,
    RetailPOS.Infrastructure.SupplierPayments.SupplierPayableRecognitionWriter>();

// Accounting
builder.Services.AddScoped<RetailPOS.Application.Accounts.Abstractions.IAccountReadStore, RetailPOS.Infrastructure.Accounts.AccountReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Accounts.Abstractions.IAccountReadScope, AccountReadScope>();
builder.Services.AddScoped<RetailPOS.Application.Transactions.Abstractions.ITransactionReadStore, RetailPOS.Infrastructure.Transactions.TransactionReadStore>();
builder.Services.AddScoped<RetailPOS.Application.Transactions.Abstractions.ITransactionReadScope, TransactionReadScope>();
builder.Services.AddScoped<RetailPOS.Infrastructure.Repositories.IExpenseRepository, RetailPOS.Infrastructure.Repositories.ExpenseRepository>();
builder.Services.AddScoped<RetailPOS.Application.Expenses.Abstractions.IExpenseCommandStore,
    RetailPOS.Infrastructure.Expenses.ExpenseCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.Expenses.Abstractions.IExpenseReadStore,
    RetailPOS.Infrastructure.Expenses.ExpenseReadStore>();
builder.Services.AddScoped<RetailPOS.Application.OtherIncome.Abstractions.IOtherIncomeCommandStore,
    RetailPOS.Infrastructure.OtherIncome.OtherIncomeCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.OtherIncome.Abstractions.IOtherIncomeReadStore,
    RetailPOS.Infrastructure.OtherIncome.OtherIncomeReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingJournals.Abstractions.IJournalEntryReadStore,
    RetailPOS.Infrastructure.AccountingJournals.JournalEntryReadStore>();
builder.Services.AddScoped<RetailPOS.Application.OtherIncome.Abstractions.IOtherIncomePostingAccountResolver,
    RetailPOS.API.Services.OtherIncomePostingAccountResolver>();
builder.Services.AddScoped<RetailPOS.Application.OtherIncome.Abstractions.IOtherIncomeApprovalJournalPoster,
    RetailPOS.API.Services.OtherIncomeApprovalJournalPoster>();
builder.Services.AddScoped<RetailPOS.Application.OpeningBalances.Abstractions.IOpeningBalanceCommandStore,
    RetailPOS.Infrastructure.OpeningBalances.OpeningBalanceCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.OpeningBalances.Abstractions.IOpeningBalanceQueryStore,
    RetailPOS.Infrastructure.OpeningBalances.OpeningBalanceQueryStore>();
builder.Services.AddScoped<RetailPOS.Application.OpeningBalances.Abstractions.IOpeningBalanceMappingValidator,
    RetailPOS.API.Services.OpeningBalanceMappingValidator>();
builder.Services.AddScoped<RetailPOS.Application.OpeningBalances.Abstractions.IOpeningBalanceJournalPoster,
    RetailPOS.API.Services.OpeningBalanceJournalPoster>();
builder.Services.AddScoped<RetailPOS.Application.Expenses.Abstractions.IExpenseApprovalJournalPoster,
    ExpenseApprovalJournalPoster>();
builder.Services.AddScoped<RetailPOS.Application.Expenses.Abstractions.IExpenseMakerCheckerPolicy,
    ExpenseMakerCheckerPolicy>();
builder.Services.AddScoped<RetailPOS.Application.Expenses.Abstractions.IExpenseDrawerStore,
    RetailPOS.Infrastructure.Expenses.ExpenseDrawerStore>();
builder.Services.AddScoped<RetailPOS.Application.Expenses.Abstractions.IExpenseLocationAuthorizer,
    ExpenseLocationAuthorizer>();
builder.Services.AddScoped<RetailPOS.Application.Expenses.Abstractions.IExpenseNumberAllocator,
    RetailPOS.Infrastructure.Expenses.ExpenseNumberAllocator>();
builder.Services.AddScoped<RetailPOS.Application.Expenses.Abstractions.IExpenseAttachmentStore,
    RetailPOS.Infrastructure.Expenses.ExpenseAttachmentStore>();
builder.Services.AddSingleton<RetailPOS.Application.Expenses.Abstractions.IExpenseAttachmentFileStore,
    RetailPOS.Infrastructure.Expenses.ExpenseAttachmentFileStore>();
builder.Services.AddOptions<RetailPOS.Application.Expenses.Abstractions.ExpenseAttachmentOptions>()
    .Bind(builder.Configuration.GetSection(RetailPOS.Application.Expenses.Abstractions.ExpenseAttachmentOptions.SectionName))
    .PostConfigure(options => options.StorageRoot = Path.GetFullPath(options.StorageRoot, builder.Environment.ContentRootPath))
    .Validate(options => options.MaxFileSizeBytes > 0 && options.MaxFilesPerExpense > 0, "Expense attachment limits must be positive.")
    .Validate(
        options => !options.StorageRoot.StartsWith(
            Path.GetFullPath("wwwroot", builder.Environment.ContentRootPath),
            StringComparison.OrdinalIgnoreCase),
        "Expense attachments must not be stored under wwwroot, which is served without authorization.")
    .ValidateOnStart();
builder.Services.AddScoped<RetailPOS.Application.Expenses.Abstractions.IExpensePostingAccountResolver,
    ExpenseApprovalPostingAccountResolver>();
builder.Services.AddScoped<RetailPOS.Application.ChartAccounts.Abstractions.IChartAccountReadStore,
    RetailPOS.Infrastructure.ChartAccounts.ChartAccountReadStore>();
builder.Services.AddScoped<RetailPOS.Application.ChartAccounts.Abstractions.IChartAccountCommandStore,
    RetailPOS.Infrastructure.ChartAccounts.ChartAccountCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.ChartAccounts.Abstractions.IChartAccountReadScope,
    ChartAccountReadScope>();
builder.Services.AddScoped<IAccountingConfigurationService, AccountingConfigurationService>();
builder.Services.AddJournalNumberAllocator();
builder.Services.AddScoped<RetailPOS.Application.AccountingJournals.Abstractions.IManualJournalDraftStore,
    RetailPOS.Infrastructure.AccountingJournals.ManualJournalDraftStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingJournals.Abstractions.IJournalAttachmentTenantScope,
    JournalAttachmentTenantScope>();
builder.Services.AddScoped<RetailPOS.Application.AccountingJournals.Abstractions.IJournalAttachmentStore,
    RetailPOS.Infrastructure.AccountingJournals.JournalAttachmentStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingJournals.Abstractions.IJournalAttachmentFileStore,
    RetailPOS.Infrastructure.AccountingJournals.JournalAttachmentFileStore>();
builder.Services.AddSingleton(new RetailPOS.Infrastructure.AccountingJournals.JournalAttachmentStorageOptions(
    builder.Environment.ContentRootPath));

// Reports
builder.Services.AddScoped<RetailPOS.Application.ApReports.Abstractions.IApPayablesReportReadStore,
    RetailPOS.Infrastructure.ApReports.ApPayablesReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.ApReports.Abstractions.IApReportTenantScope,
    ApReportTenantScope>();
builder.Services.AddScoped<RetailPOS.Application.AccountingReports.Abstractions.IGeneralLedgerReadStore,
    RetailPOS.Infrastructure.AccountingReports.GeneralLedgerReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingReports.Abstractions.IBalanceSheetReadStore,
    RetailPOS.Infrastructure.AccountingReports.BalanceSheetReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingReports.Abstractions.IEnhancedGeneralLedgerReadStore,
    RetailPOS.Infrastructure.AccountingReports.EnhancedGeneralLedgerReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingReports.Abstractions.ICashBookReadStore,
    RetailPOS.Infrastructure.AccountingReports.CashBookReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingReports.Abstractions.ICogsReportReadStore,
    RetailPOS.Infrastructure.AccountingReports.CogsReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingReports.Abstractions.IInventoryValuationReadStore,
    RetailPOS.Infrastructure.AccountingReports.InventoryValuationReadStore>();
builder.Services.AddScoped<RetailPOS.Application.VatReports.Abstractions.IVatReportReadStore,
    RetailPOS.Infrastructure.AccountingReports.VatReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingReports.Abstractions.IProfitabilityReportReadStore,
    RetailPOS.Infrastructure.AccountingReports.ProfitabilityReportReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingReports.Abstractions.IProductContributionReadStore,
    RetailPOS.Infrastructure.AccountingReports.ProductContributionReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingReports.Abstractions.ICashBookPostingAccountResolver,
    CashBookPostingAccountResolver>();
builder.Services.AddScoped<RetailPOS.Application.AccountingReports.Abstractions.ICashFlowOwnerAccountsProvider,
    CashFlowOwnerAccountsProvider>();
builder.Services.AddScoped<RetailPOS.Application.AccountingReports.Abstractions.IAccountingReportTenantScope,
    AccountingReportTenantScope>();
builder.Services.AddScoped<RetailPOS.Application.AccountingCloseRuns.Abstractions.IAccountingPeriodCloseRunReadStore,
    RetailPOS.Infrastructure.AccountingCloseRuns.AccountingPeriodCloseRunReadStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingCloseRuns.Abstractions.IAccountingPeriodCloseRunCommandStore,
    RetailPOS.Infrastructure.AccountingCloseRuns.AccountingPeriodCloseRunCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.AccountingCloseRuns.Abstractions.IAccountingPeriodCloseTenantScope,
    AccountingPeriodCloseTenantScopeAdapter>();
builder.Services.AddScoped<RetailPOS.Application.AccountingCloseRuns.Abstractions.IAccountingPeriodCloseFinancialGateEvaluator,
    RetailPOS.Application.AccountingCloseRuns.Queries.AccountingPeriodCloseFinancialGateEvaluator>();
builder.Services.AddScoped<RetailPOS.Application.FinancialYearCloseRuns.Abstractions.IAccountingFinancialYearCloseRunReadStore,
    RetailPOS.Infrastructure.FinancialYearCloseRuns.AccountingFinancialYearCloseRunReadStore>();
builder.Services.AddScoped<RetailPOS.Application.FinancialYearCloseRuns.Abstractions.IAccountingFinancialYearCloseRunCommandStore,
    RetailPOS.Infrastructure.FinancialYearCloseRuns.AccountingFinancialYearCloseRunCommandStore>();
builder.Services.AddScoped<RetailPOS.Application.FinancialYearCloseRuns.Abstractions.IAccountingFinancialYearCloseJournalPosting,
    AccountingFinancialYearCloseJournalPostingAdapter>();
builder.Services.AddScoped<RetailPOS.Application.FinancialYearCloseRuns.Abstractions.IAccountingFinancialYearCloseTenantScope,
    AccountingFinancialYearCloseTenantScopeAdapter>();
builder.Services.AddScoped<IUserOutletAccessService, UserOutletAccessService>();

// Pricing Engine

// NEW — POS Performance & Concurrency Layer
// IDistributedCache: Redis when ConnectionStrings:Redis is configured, in-memory fallback otherwise
// (Development/CI only — Staging/Production are rejected by InfrastructureConfigurationValidator).
builder.Services.AddPosDistributedCache(builder.Configuration);

builder.Services.AddSingleton<IPosCacheService, PosCacheService>();

// PDF generation
builder.Services.AddScoped<IDocumentPdfRenderer, QuestDocumentPdfRenderer>();
builder.Services.AddScoped<IPurchaseOrderPdfMapper, PurchaseOrderPdfMapper>();
builder.Services.AddScoped<IPurchaseOrderPdfService, PurchaseOrderPdfService>();
builder.Services.AddScoped<IGrnPdfMapper, GrnPdfMapper>();
builder.Services.AddScoped<IGrnPdfService, GrnPdfService>();
builder.Services.AddScoped<IBarcodeLabelPdfService, BarcodeLabelPdfService>();
builder.Services.AddScoped<IDocumentPdfDispatcher, DocumentPdfDispatcher>();

// Add Controllers
builder.Services.AddControllers();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth-login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("auth-register", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("auth-refresh", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<SubscriptionRenewalReminderHostedService>();
    builder.Services.AddHostedService<HrEmployeeLocationTransferHostedService>();
}

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "http://127.0.0.1:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Retail POS API",
        Version = "v1",
        Description = "Comprehensive Retail Point of Sale System API"
    });

    // Prevent schema ID clashes when different namespaces contain same DTO class names.
    options.CustomSchemaIds(GenerateSwaggerSchemaId);

    // Add JWT Authentication to Swagger
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

static string GenerateSwaggerSchemaId(Type type)
{
    if (type.IsGenericType)
    {
        var genericTypeName = type.GetGenericTypeDefinition().Name;
        var tickIndex = genericTypeName.IndexOf('`');
        if (tickIndex > 0)
        {
            genericTypeName = genericTypeName[..tickIndex];
        }

        var genericArguments = string.Join("And", type.GetGenericArguments().Select(GenerateSwaggerSchemaId));
        return $"{genericTypeName}Of{genericArguments}";
    }

    var fullName = type.FullName ?? type.Name;
    return fullName
        .Replace('.', '_')
        .Replace('+', '_')
        .Replace("[]", "Array");
}

if (financialPostingPlan.WorkerOnly)
{
    // A dedicated financial worker runs nothing but the financial posting workers: every other background service
    // of this application stays with the API instances, so nothing runs twice because a worker process was added.
    var financialWorkers = new[] { typeof(SaleFinancialPostingHostedService), typeof(CashDifferencePostingHostedService) };
    foreach (var descriptor in builder.Services
                 .Where(service => service.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                     && service.ImplementationType?.Assembly == typeof(Program).Assembly
                     && !financialWorkers.Contains(service.ImplementationType))
                 .ToList())
        builder.Services.Remove(descriptor);
}

var app = builder.Build();

// Fail fast (sanitized) before any migration, seeding or background work when the environment's
// infrastructure settings are missing or point at the wrong environment.
InfrastructureConfigurationValidator.ValidateOrThrow(app.Configuration, app.Environment.EnvironmentName);

var databaseStartup = app.Configuration.GetSection("Database").Get<DatabaseStartupOptions>()
    ?? new DatabaseStartupOptions();

// Migration and seeding are opt-in (Database:MigrateOnStartup / Database:SeedOnStartup) and rejected
// outside Development by the validator; production schema changes go through the release pipeline.
if (!app.Environment.IsEnvironment("Testing")
    && (databaseStartup.MigrateOnStartup || databaseStartup.SeedOnStartup))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<RetailPOSDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();

        if (databaseStartup.MigrateOnStartup)
        {
            // Ensure database is created and migrations are applied
            await context.Database.MigrateAsync();

            // Idempotent DDL for the product_variation_selected_options table.
            // Executed every startup so new deployments automatically get the table
            // without requiring a full EF Core migration workflow.
            await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS product_variation_selected_options (
                id          BIGSERIAL    PRIMARY KEY,
                product_id  BIGINT       NOT NULL REFERENCES products(id)          ON DELETE CASCADE,
                variation_id BIGINT      NOT NULL REFERENCES variations(id)        ON DELETE CASCADE,
                option_id   BIGINT       NOT NULL REFERENCES variation_options(id) ON DELETE CASCADE,
                created_at  TIMESTAMP    NOT NULL DEFAULT NOW(),
                UNIQUE (product_id, variation_id, option_id)
            )");
        }

        if (databaseStartup.SeedOnStartup)
        {
            logger.LogInformation("Checking if database needs seeding...");

            // Seed default roles, users, and all module sample data
            await RetailPOS.Infrastructure.Data.DbSeeder.SeedAsync(context);

            logger.LogInformation("Database seeding completed successfully");
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Retail POS API v1");
        options.RoutePrefix = "swagger";
    });
}

// Disable HTTPS redirection in development
// app.UseHttpsRedirection()

// Must be first so RemoteIpAddress is rewritten before any middleware reads it.
app.UseForwardedHeaders();

// Add global exception handler middleware
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseCors("AllowAngularApp");
app.UseRateLimiter();

// Serve uploaded product images from wwwroot/uploads with long-lived cache headers.
// Filenames embed the image id so URLs are immutable; safe to cache aggressively.
{
    var uploadsRoot = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads");
    Directory.CreateDirectory(uploadsRoot);
    app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsRoot),
        RequestPath = "/uploads",
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers["Cache-Control"] = "public,max-age=2592000,immutable";
        },
    });
}

app.UseAuthentication();
app.UseMiddleware<BusinessContextMiddleware>();
app.UseAuthorization();

// Audit context — must be after authentication so user claims are available
app.UseMiddleware<AuditContextMiddleware>();

if (financialPostingPlan.WorkerOnly)
{
    // Worker process: liveness for the orchestrator only; no API surface, no Business data.
    app.MapGet("/health/financial-posting", (FinancialPostingWorkerHealth health, FinancialPostingInstance instance) =>
    {
        var workers = health.Snapshot();
        var healthy = workers.All(worker => worker.ConsecutiveRunFailures < 3);
        return Results.Json(
            new { role = instance.Role, instanceId = instance.InstanceId, healthy, workers },
            statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }).AllowAnonymous();
}
else
{
    app.MapControllers();
}

app.Run();

public partial class Program { }
