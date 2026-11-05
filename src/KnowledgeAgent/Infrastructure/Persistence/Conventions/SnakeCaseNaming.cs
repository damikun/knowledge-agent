using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace KnowledgeAgent.Infrastructure.Persistence.Conventions;

internal static class SnakeCaseNaming
{
    public static void Apply(IMutableModel model)
    {
        var entityTypes = model.GetEntityTypes().ToList();

        foreach (var entityType in entityTypes)
        {
            if (entityType.GetTableName() is { } tableName)
            {
                entityType.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }

        // Constraint and index default names derive from table and column names, so they are renamed afterwards.
        foreach (var entityType in entityTypes)
        {
            foreach (var key in entityType.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetDefaultName()!));
            }

            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetDefaultName()!));
            }

            foreach (var index in entityType.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDefaultDatabaseName()!));
            }
        }
    }

    // "HTMLParser" becomes "html_parser" and "PK_sources" becomes "pk_sources".
    public static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            if (!char.IsUpper(current))
            {
                builder.Append(current);
                continue;
            }

            if (i > 0 && name[i - 1] != '_')
            {
                var previous = name[i - 1];
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                {
                    builder.Append('_');
                }
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
