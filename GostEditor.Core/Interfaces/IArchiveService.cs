using System.IO;
using System.Threading.Tasks;
using GostEditor.Core.Models;

namespace GostEditor.Core.Interfaces;

/// <summary>
/// Сервис для сохранения и загрузки документов в собственном формате (.gost)
/// </summary>
public interface IArchiveService
{
    /// <summary>
    /// Создает новый пустой документ со стандартными настройками ГОСТ
    /// </summary>
    /// <returns>Новый экземпляр GostDocument</returns>
    GostDocument CreateNew();

    /// <summary>
    /// Загружает документ из файла по указанному физическому пути
    /// </summary>
    /// <param name="filePath">Путь к файлу .gost</param>
    /// <returns>Десериализованный документ</returns>
    Task<GostDocument> LoadAsync(string filePath);

    /// <summary>
    /// Загружает документ из потока данных (например, при открытии через диалог Avalonia)
    /// </summary>
    /// <param name="stream">Поток с данными файла</param>
    /// <returns>Десериализованный документ</returns>
    Task<GostDocument> LoadAsync(Stream stream);

    /// <summary>
    /// Сохраняет документ в файл по указанному физическому пути
    /// </summary>
    /// <param name="document">Документ для сохранения</param>
    /// <param name="filePath">Путь назначения</param>
    Task SaveAsync(GostDocument document, string filePath);

    /// <summary>
    /// Сохраняет документ напрямую в поток данных
    /// </summary>
    /// <param name="document">Документ для сохранения</param>
    /// <param name="stream">Целевой поток</param>
    Task SaveAsync(GostDocument document, Stream stream);
}
