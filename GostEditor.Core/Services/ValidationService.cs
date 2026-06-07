using System.Collections.Generic;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.Services;

/// <summary>
/// Сервис для проверки документа на соответствие минимальным требованиям ГОСТ перед экспортом.
/// </summary>
public class ValidationService : IValidationService
{
    /// <summary>
    /// Выполняет полную проверку документа и возвращает список найденных ошибок.
    /// </summary>
    /// <param name="document">Документ для проверки</param>
    /// <returns>Список строк с описанием ошибок. Если список пуст — документ валиден.</returns>
    public List<string> Validate(GostDocument document)
    {
        List<string> errors = new List<string>();

        if (document is null)
        {
            errors.Add("Документ не инициализирован.");
            return errors;
        }

        // 1. Проверка на абсолютную пустоту
        if (document.Paragraphs.Count == 0)
        {
            errors.Add("Документ пуст. Добавьте хотя бы один абзац текста.");
            return errors; // Дальше проверять нет смысла, прерываем проверку
        }

        // 2. Проверка структуры (наличие заголовков)
        bool hasHeadings = false;
        foreach (Paragraph p in document.Paragraphs)
        {
            if (p.Style == ParagraphStyle.Heading1 || p.Style == ParagraphStyle.Heading2)
            {
                hasHeadings = true;
                break;
            }
        }

        if (!hasHeadings)
        {
            errors.Add("Документ не содержит структуры. Рекомендуется добавить хотя бы одну Главу (Заголовок 1).");
        }

        // 3. Проверка минимального объема текста (считаем только обычные абзацы, без заголовков)
        int totalTextLength = 0;
        foreach (Paragraph p in document.Paragraphs)
        {
            if (p.Style == ParagraphStyle.Normal)
            {
                totalTextLength += p.GetPlainText().Length;
            }
        }

        if (totalTextLength < 100)
        {
            errors.Add("Основной текст документа слишком короткий (менее 100 символов).");
        }

        return errors;
    }
}
