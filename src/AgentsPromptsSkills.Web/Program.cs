using Anthropic.SDK;
using FluentValidation;
using AgentsPromptsSkills.Web;
using AgentsPromptsSkills.Web.Services;
using AgentsPromptsSkills.Web.Components;
using MercenariesAndBeasts.Infrastructure;
using MercenariesAndBeasts.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Exceptions;
using Serilog.Sinks.PostgreSQL.ColumnWriters;
using SharedServices;
using Npgsql;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ────────────────────────────────────────────────────────────────
Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "Logs"));
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.WithMachineName()
    .Enrich.WithProcessId()
    .Enrich.WithThreadId()
    .Enrich.FromLogContext()
    .Enrich.WithExceptionDetails()
    .WriteTo.Console(
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        "Logs/log-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .WriteTo.PostgreSQL(
        connectionString: builder.Configuration.GetConnectionString("ApsDatabase") ?? "",
        tableName: "Logs",
        columnOptions: (IDictionary<string, ColumnWriterBase>?)null,
        needAutoCreateTable: true,
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning)
    .CreateLogger();
builder.Host.UseSerilog();

// ── Razor / Blazor ──────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRazorPages();

// ── UI ──────────────────────────────────────────────────────────────────────
builder.Services.AddSharedUI(builder.Configuration);
builder.Services.AddGlobalErrorNotifications();

// ── Database ────────────────────────────────────────────────────────────────
var csRaw = builder.Configuration.GetConnectionString("ApsDatabase");
if (string.IsNullOrWhiteSpace(csRaw))
    throw new InvalidOperationException("Connection string 'ApsDatabase' is missing.");

var cs = PreferIPv4Host(csRaw);
Log.Information("DB connection string (masked): {Masked}", Mask(cs));

var apsDsb = new NpgsqlDataSourceBuilder(cs);
apsDsb.EnableDynamicJson();
var apsDataSource = apsDsb.Build();

builder.Services.AddDbContextFactory<AppDbContextAps>(options =>
    options.UseNpgsql(apsDataSource, o => o.CommandTimeout(120)));

builder.Services.AddDbContext<AppDbContextAps>(options =>
    options.UseNpgsql(apsDataSource, o => o.CommandTimeout(120)),
    ServiceLifetime.Scoped);

// ── Auth (Identity + optional Google OAuth) ─────────────────────────────────
builder.Services.AddMabAuth<AppDbContextAps>(builder.Configuration);
builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender,
    NoOpEmailSender>();

// ── Anthropic SDK ───────────────────────────────────────────────────────────
var anthropicApiKey = builder.Configuration["Anthropic:ApiKey"] ?? "";
builder.Services.AddSingleton(_ => new AnthropicClient(anthropicApiKey));

// ── App services ────────────────────────────────────────────────────────────
builder.Services.AddScoped<ApsPlaygroundService>();
builder.Services.AddScoped<ApsItemService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHealthChecks();

// ── FluentValidation ────────────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ── Global exception handlers ───────────────────────────────────────────────
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    Log.Fatal(e.ExceptionObject as Exception, "UNHANDLED AppDomain exception");

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Fatal(e.Exception, "UNOBSERVED task exception");
    e.SetObserved();
};

var app = builder.Build();

// ── Path base ───────────────────────────────────────────────────────────────
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
var pathBase = builder.Configuration["PathBase"];
if (!string.IsNullOrWhiteSpace(pathBase))
    app.UsePathBase(pathBase);

app.MapHealthChecks("/health");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();
app.MapStaticAssets();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorPages();

// ── Google OAuth external login endpoints ────────────────────────────────────
app.MapPost("/Identity/Account/ExternalLogin", async (
    HttpContext http,
    SignInManager<AppUser> signInManager) =>
{
    var provider  = http.Request.Form["provider"].ToString();
    var returnUrl = http.Request.Form["returnUrl"].ToString() ?? "/";
    var callback  = $"/Identity/Account/ExternalLogin/Callback?returnUrl={Uri.EscapeDataString(returnUrl)}";
    var props     = signInManager.ConfigureExternalAuthenticationProperties(provider, callback);
    return Results.Challenge(props, new[] { provider });
}).DisableAntiforgery();

app.MapGet("/Identity/Account/ExternalLogin/Callback", async (
    HttpContext http,
    string? returnUrl,
    SignInManager<AppUser> signInManager,
    UserManager<AppUser> userManager,
    IWebHostEnvironment env,
    IConfiguration config) =>
{
    returnUrl ??= "/";
    var info = await signInManager.GetExternalLoginInfoAsync();
    if (info is null)
        return Results.Redirect("/login?error=external");

    var signIn = await signInManager.ExternalLoginSignInAsync(
        info.LoginProvider, info.ProviderKey, isPersistent: true);

    if (signIn.Succeeded)
    {
        var signedInUser = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (signedInUser is not null)
        {
            var denied = await AccessGate.CheckAsync(signedInUser, signInManager, env, config);
            if (denied is not null) return Results.Redirect(denied);
        }
        return Results.Redirect(returnUrl);
    }

    var email = info.Principal.FindFirstValue(ClaimTypes.Email) ?? "";
    if (string.IsNullOrWhiteSpace(email))
        return Results.Redirect("/login?error=noemail");

    var user = new AppUser { UserName = email, Email = email };
    var created = await userManager.CreateAsync(user);
    if (created.Succeeded)
    {
        await userManager.AddLoginAsync(user, info);
        await signInManager.SignInAsync(user, isPersistent: true);
        var deniedNew = await AccessGate.CheckAsync(user, signInManager, env, config);
        if (deniedNew is not null) return Results.Redirect(deniedNew);
        return Results.Redirect(returnUrl);
    }

    var existing = await userManager.FindByEmailAsync(email);
    if (existing is not null)
    {
        await userManager.AddLoginAsync(existing, info);
        await signInManager.SignInAsync(existing, isPersistent: true);
        var deniedExisting = await AccessGate.CheckAsync(existing, signInManager, env, config);
        if (deniedExisting is not null) return Results.Redirect(deniedExisting);
        return Results.Redirect(returnUrl);
    }

    return Results.Redirect("/login?error=external");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(MercenariesAndBeasts.Infrastructure.Components.Account.Login).Assembly);

// ── Startup: migrate + seed ─────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var services    = scope.ServiceProvider;
    var db          = services.GetRequiredService<AppDbContextAps>();
    var userManager = services.GetRequiredService<UserManager<AppUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    await db.Database.MigrateAsync();

    const string adminRole = "Admin";
    if (!await roleManager.RoleExistsAsync(adminRole))
        await roleManager.CreateAsync(new IdentityRole(adminRole));

    await EnsureAdminAsync(userManager, adminRole, "olsanskyvitek@gmail.com", "vitek", "Vitek575");
}

// Seed role a admin účet
await AdminUserSeeder.SeedAsync(app.Services, app.Configuration);

app.Lifetime.ApplicationStopping.Register(() =>
    Log.Warning("Application stopping — flushing logs..."));

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// ── Helpers ─────────────────────────────────────────────────────────────────
static async Task EnsureAdminAsync(UserManager<AppUser> um, string role, string email, string username, string password)
{
    var user = await um.FindByEmailAsync(email);
    if (user is null)
    {
        user = new AppUser { UserName = username, Email = email, EmailConfirmed = true, IsAdmin = true };
        var r = await um.CreateAsync(user, password);
        if (!r.Succeeded) return;
    }
    else if (!user.IsAdmin) { user.IsAdmin = true; await um.UpdateAsync(user); }

    if (!await um.IsInRoleAsync(user, role))
        await um.AddToRoleAsync(user, role);
}

static string Mask(string? s)
{
    if (string.IsNullOrWhiteSpace(s)) return "(null)";
    return System.Text.RegularExpressions.Regex.Replace(s, "(?i)Password=([^;]+)", "Password=***");
}

static string PreferIPv4Host(string cs)
{
    var b = new NpgsqlConnectionStringBuilder(cs);
    if (IPAddress.TryParse(b.Host, out _))
        return b.ToString();
    try
    {
        var addrs = Dns.GetHostAddresses(b.Host!);
        var v4 = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        if (v4 != null) { Log.Information("Host '{Host}' -> IPv4 {IPv4}", b.Host, v4); b.Host = v4.ToString(); }
        else Log.Warning("Host '{Host}' -> no IPv4 found", b.Host);
    }
    catch (Exception ex) { Log.Warning(ex, "DNS resolve failed for '{Host}'", b.Host); }
    return b.ToString();
}

file sealed class NoOpEmailSender : Microsoft.AspNetCore.Identity.UI.Services.IEmailSender
{
    public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
}

public partial class Program { }
