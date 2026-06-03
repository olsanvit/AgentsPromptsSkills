using Anthropic.SDK;
using FluentValidation;
using AgentsPromptsSkills.Web.Services;
using AgentsPromptsSkills.Web.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Exceptions;
using Serilog.Sinks.PostgreSQL.ColumnWriters;
using SharedServices;
using Npgsql;
using System.Net;
using System.Net.Sockets;

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
    options.UseNpgsql(apsDataSource,
        o => o.CommandTimeout(120)));

builder.Services.AddDbContext<AppDbContextAps>(options =>
    options.UseNpgsql(apsDataSource,
        o => o.CommandTimeout(120)),
    ServiceLifetime.Scoped);

// ── ASP.NET Core Identity ───────────────────────────────────────────────────
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
    })
    .AddEntityFrameworkStores<AppDbContextAps>()
    .AddDefaultTokenProviders()
    .AddDefaultUI();

builder.Services.AddAuthentication()
    .AddGoogle(googleOptions =>
    {
        googleOptions.ClientId     = builder.Configuration["Authentication:Google:ClientId"]     ?? "";
        googleOptions.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "";
    });

builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, NoOpEmailSender>();

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ── Startup: migrate + seed ─────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var services    = scope.ServiceProvider;
    var db          = services.GetRequiredService<AppDbContextAps>();
    var userManager = services.GetRequiredService<UserManager<AppUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    await db.Database.MigrateAsync();

    // Ensure Admin role exists
    if (!await roleManager.RoleExistsAsync("Admin"))
        await roleManager.CreateAsync(new IdentityRole("Admin"));

    // Seed admin users from config
    var admins = builder.Configuration.GetSection("SeedAdmins").Get<SeedAdmin[]>() ?? [];
    foreach (var a in admins)
    {
        if (string.IsNullOrWhiteSpace(a.Email)) continue;
        var existing = await userManager.FindByEmailAsync(a.Email);
        if (existing is null)
        {
            var user = new AppUser { UserName = a.Username ?? a.Email, Email = a.Email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, a.Password ?? "Admin1234!");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(user, "Admin");
        }
        else if (!await userManager.IsInRoleAsync(existing, "Admin"))
        {
            await userManager.AddToRoleAsync(existing, "Admin");
        }
    }
}

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
        if (v4 != null)
        {
            Log.Information("Host '{Host}' -> IPv4 {IPv4}", b.Host, v4);
            b.Host = v4.ToString();
        }
        else
        {
            Log.Warning("Host '{Host}' -> no IPv4 found", b.Host);
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "DNS resolve failed for '{Host}'", b.Host);
    }
    return b.ToString();
}

// ── Local records ────────────────────────────────────────────────────────────
file sealed class NoOpEmailSender : Microsoft.AspNetCore.Identity.UI.Services.IEmailSender
{
    public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
}

file sealed record SeedAdmin(string? Email, string? Username, string? Password);

public partial class Program { }
