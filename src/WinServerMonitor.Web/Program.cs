using System.Text;
using Microsoft.AspNetCore.Authentication.Negotiate;
using WinServerMonitor.Infrastructure;
using WinServerMonitor.Infrastructure.Persistence;
using WinServerMonitor.Infrastructure.Services;
using WinServerMonitor.Web.Components;
using WinServerMonitor.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Allows installing the app as a Windows service (sc.exe create ...); also sets the content root
// to the executable folder when running as a service. No-op when run from console.
builder.Host.UseWindowsService(o => o.ServiceName = "WinServerMonitor");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var useWindowsAuth = builder.Configuration.GetValue<bool>("Authentication:WindowsAuthentication");
if (useWindowsAuth)
{
    var requiredRole = builder.Configuration["Authentication:RequiredRole"];
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
    builder.Services.AddAuthorization(o =>
    {
        o.FallbackPolicy = string.IsNullOrWhiteSpace(requiredRole)
            ? o.DefaultPolicy
            : new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireRole(requiredRole).Build();
    });
}

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddWinServerMonitor(builder.Configuration);

var app = builder.Build();

await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

if (useWindowsAuth)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseAntiforgery();

app.MapStaticAssets();

app.MapGet("/api/runs/{id:long}/log.txt", async (long id, TaskRunService runs, CancellationToken ct) =>
{
    var run = await runs.GetAsync(id, ct);
    if (run is null)
    {
        return Results.NotFound();
    }

    var logs = await runs.GetLogsAsync(id, take: int.MaxValue, cancellationToken: ct);
    var text = new StringBuilder()
        .AppendLine($"# {run.TaskDefinition?.Name} - běh #{run.Id} ({run.Status})");
    foreach (var entry in logs)
    {
        text.AppendLine($"{entry.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Message}");
    }

    return Results.File(Encoding.UTF8.GetBytes(text.ToString()), "text/plain; charset=utf-8", $"run-{id}.log");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
