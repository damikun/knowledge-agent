using KnowledgeAgent.Features.Sources.Folders;
using KnowledgeAgent.Tests.Infrastructure.Persistence.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeAgent.Tests.Features.Sources.Folders;

public sealed class PostgresSourceFolderRepositoryTests(PostgresPersistenceFixture fixture)
    : SourceFolderRepositoryContractTests<PostgresPersistenceFixture>(fixture), IClassFixture<PostgresPersistenceFixture>
{
    [Fact]
    public async Task The_folder_filter_and_the_subfolder_lookup_use_indexes()
    {
        var folderId = Guid.NewGuid();

        var sourcesPlan = await ExplainAsync($"SELECT id FROM sources WHERE folder_id = '{folderId}'");
        var foldersPlan = await ExplainAsync($"SELECT id FROM source_folders WHERE parent_id = '{folderId}'");

        sourcesPlan.ShouldContain("ix_sources_folder_id");
        foldersPlan.ShouldContain("ix_source_folders_parent_id");
    }

    [Fact]
    public async Task The_database_rejects_a_folder_reference_to_a_missing_folder()
    {
        var orphan = new SourceFolder { Name = "Orphan", ParentId = Guid.NewGuid() };

        var exception = await Should.ThrowAsync<DbUpdateException>(() =>
            InScopeAsync((ISourceFolderRepository folders) => folders.AddAsync(orphan, Ct)));

        exception.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    private async Task<string> ExplainAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(Fixture.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync(Ct);

        // Sequential scans are disabled so the plan does not depend on the table's size.
        await using (var disable = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await disable.ExecuteNonQueryAsync(Ct);
        }

        await using var command = new NpgsqlCommand("EXPLAIN " + sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var lines = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join('\n', lines);
    }
}
