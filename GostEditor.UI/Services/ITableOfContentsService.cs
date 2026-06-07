using System.Collections.Generic;
using GostEditor.Core.Models;

namespace GostEditor.UI.Services;

/// <summary>
/// Сервис для генерации оглавления (таблицы содержания) документа
/// </summary>
public interface ITableOfContentsService
{
    /// <summary>
    /// Сканирует документ и создаёт список записей оглавления с реальными номерами страниц
    /// </summary>
    List<TocEntry> GenerateTableOfContents(
        GostDocument document,
        dynamic renderedPages,
        int contentStartPage);
}

/// <summary>
/// Запись в оглавлении (таблице содержания)
/// </summary>
public class TocEntry
{
    /// <summary>
    /// Заголовок раздела (текст)
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Уровень заголовка (1 = Heading1, 2 = Heading2)
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Номер страницы, на которой находится этот раздел
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Индекс параграфа в документе
    /// </summary>
    public int ParagraphIndex { get; set; }
}
