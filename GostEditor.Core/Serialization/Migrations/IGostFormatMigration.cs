namespace GostEditor.Core.Serialization.Migrations;

internal interface IGostFormatMigration
{
    int FromVersion { get; }
    int ToVersion { get; }

    Task ApplyAsync(
        GostMigrationContext context,
        CancellationToken cancellationToken = default);
}
