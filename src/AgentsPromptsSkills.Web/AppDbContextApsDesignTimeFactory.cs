using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;
using SharedServices;

namespace AgentsPromptsSkills.Web;

/// <summary>
/// Design-time factory so that EF Core CLI tools (dotnet ef migrations add / update) can
/// instantiate <see cref="AppDbContextAps"/> without going through the full DI container.
/// </summary>
public sealed class AppDbContextApsDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContextAps>
{
    public AppDbContextAps CreateDbContext(string[] args)
    {
        // Use the dev connection string (localhost) for design-time tooling.
        var cs = "Host=localhost;Port=5432;Database=AgentsPromptsSkills;Username=postgres;Password=postgres";

        var dsb = new NpgsqlDataSourceBuilder(cs);
        dsb.EnableDynamicJson();
        var dataSource = dsb.Build();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContextAps>();
        optionsBuilder.UseNpgsql(dataSource, o => o.CommandTimeout(30));

        return new AppDbContextAps(optionsBuilder.Options);
    }
}
