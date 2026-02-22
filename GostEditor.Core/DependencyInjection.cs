using GostEditor.Core.Interfaces;
using GostEditor.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GostEditor.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddGostEditorCore(
        this IServiceCollection services)
    {
        services.AddSingleton<ITextNormalizerService, TextNormalizerService>();
        services.AddSingleton<ICodeParserService, CodeParserService>();
        services.AddSingleton<IDocumentService, DocumentService>();
        services.AddSingleton<IExportService, ExportService>();
        return services;
    }
}