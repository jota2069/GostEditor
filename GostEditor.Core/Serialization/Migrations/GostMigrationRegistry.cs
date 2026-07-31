namespace GostEditor.Core.Serialization.Migrations;

internal sealed class GostMigrationRegistry
{
    private readonly IReadOnlyDictionary<int, IGostFormatMigration> _migrations;

    public GostMigrationRegistry(IEnumerable<IGostFormatMigration> migrations)
    {
        Dictionary<int, IGostFormatMigration> bySourceVersion = new();

        foreach (IGostFormatMigration migration in migrations)
        {
            if (migration.ToVersion != migration.FromVersion + 1)
            {
                throw new InvalidOperationException(
                    $"Миграция {migration.FromVersion}→{migration.ToVersion} должна вести в следующую версию.");
            }

            if (!bySourceVersion.TryAdd(migration.FromVersion, migration))
            {
                throw new InvalidOperationException(
                    $"Для версии {migration.FromVersion} зарегистрировано несколько миграций.");
            }
        }

        _migrations = bySourceVersion;
    }

    public async Task MigrateToAsync(
        GostMigrationContext context,
        int targetVersion,
        CancellationToken cancellationToken = default)
    {
        if (context.WorkingVersion > targetVersion)
        {
            throw new InvalidOperationException(
                $"Нельзя понизить версию {context.WorkingVersion} до {targetVersion}.");
        }

        while (context.WorkingVersion < targetVersion)
        {
            if (!_migrations.TryGetValue(
                    context.WorkingVersion,
                    out IGostFormatMigration? migration))
            {
                throw new InvalidOperationException(
                    $"Не зарегистрирована миграция из версии {context.WorkingVersion}.");
            }

            int sourceVersion = context.WorkingVersion;
            await migration.ApplyAsync(context, cancellationToken);

            if (context.WorkingVersion != migration.ToVersion)
            {
                throw new InvalidOperationException(
                    $"Миграция {sourceVersion}→{migration.ToVersion} не обновила рабочую версию.");
            }
        }
    }
}
