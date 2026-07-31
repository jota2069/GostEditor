using Microsoft.Extensions.DependencyInjection;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Services;
using GostEditor.Core.Serialization;

namespace GostEditor.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddGostEditorCore(this IServiceCollection services)
    {
        services.AddSingleton<IArchiveService, ArchiveService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IValidationService, ValidationService>();
        services.AddSingleton<ITextNormalizerService, TextNormalizerService>();
        services.AddSingleton<ICodeParserService, CodeParserService>(); // ← УБЕДИСЬ ЧТО ЕСТЬ
        services.AddSingleton<IImageService, ImageService>();

        return services;
    }
}
