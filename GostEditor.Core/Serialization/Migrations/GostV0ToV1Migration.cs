using GostEditor.Core.Serialization.Format;

namespace GostEditor.Core.Serialization.Migrations;

internal sealed class GostV0ToV1Migration : IGostFormatMigration
{
    public int FromVersion => GostFormatVersions.LegacyWithoutVersion;
    public int ToVersion => GostFormatVersions.Legacy;

    public Task ApplyAsync(
        GostMigrationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (context.LegacyDocument == null)
        {
            throw new InvalidOperationException(
                "Для миграции v0→v1 отсутствует legacy-модель документа.");
        }

        // v0 means that FormatVersion was absent. Its DTO shape is identical to v1.
        context.WorkingVersion = ToVersion;
        return Task.CompletedTask;
    }
}
