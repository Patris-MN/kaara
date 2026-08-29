using PTS.Host.Authentication;
using PTS.Host.Http;
using PTS.Host.Persistence;
using PTS.Modules.Audit;
using PTS.Modules.Billing;
using PTS.Modules.Entitlements;
using PTS.Modules.Identity;
using PTS.Modules.PlatformAdministration;
using PTS.Modules.Storage;
using PTS.Modules.Tenancy;
using PTS.Modules.WorkManagement;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddIdentityModule()
    .AddTenancyModule()
    .AddWorkManagementModule()
    .AddEntitlementsModule()
    .AddBillingModule()
    .AddStorageModule()
    .AddPlatformAdministrationModule()
    .AddAuditModule();

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddPtsAuthentication(builder.Configuration);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Frontend", policy =>
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod());
    });
}

var app = builder.Build();

if (allowedOrigins.Length > 0)
{
    app.UseCors("Frontend");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", phase = "4.5-frontend-vertical-slice" }));
app.MapAuthEndpoints();
app.MapTenantIsolationEndpoints();
app.MapTenantLifecycleEndpoints();
app.MapWorkManagementEndpoints();
app.MapTaskEndpoints();
app.MapTaskCollaborationEndpoints();

app.Run();

public partial class Program;
