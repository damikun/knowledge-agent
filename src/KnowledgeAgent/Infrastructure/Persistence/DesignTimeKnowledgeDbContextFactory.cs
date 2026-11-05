using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KnowledgeAgent.Infrastructure.Persistence;

internal sealed class DesignTimeKnowledgeDbContextFactory : IDesignTimeDbContextFactory<KnowledgeDbContext>
{
    // "dotnet ef migrations add" never opens a connection, so the placeholder only matters for "database update".
    private const string PlaceholderConnectionString = "Host=localhost;Port=5432;Database=knowledge;Username=postgres;Password=postgres";

    public KnowledgeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__knowledgedb");
        var options = new DbContextOptionsBuilder<KnowledgeDbContext>()
            .UseNpgsql(string.IsNullOrWhiteSpace(connectionString) ? PlaceholderConnectionString : connectionString)
            .Options;

        return new KnowledgeDbContext(options);
    }
}
