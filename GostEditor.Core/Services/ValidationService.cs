using System.Collections.Generic;
using GostEditor.Core.Interfaces;
using GostEditor.Core.Models;

namespace GostEditor.Core.Services;

public class ValidationService : IValidationService
{
    public List<string> Validate(GostDocument document)
    {
        List<string> errors = [];

        // Проверка титульного листа.
        if (string.IsNullOrWhiteSpace(document.TitlePage.University))
        {
            errors.Add("Не указан университет.");
        }

        if (string.IsNullOrWhiteSpace(document.TitlePage.WorkTitle))
        {
            errors.Add("Не указано название работы.");
        }

        if (string.IsNullOrWhiteSpace(document.TitlePage.StudentName))
        {
            errors.Add("Не указано ФИО студента.");
        }

        if (string.IsNullOrWhiteSpace(document.TitlePage.GroupNumber))
        {
            errors.Add("Не указан номер группы.");
        }

        if (string.IsNullOrWhiteSpace(document.TitlePage.TeacherName))
        {
            errors.Add("Не указано ФИО преподавателя.");
        }

        // Проверка содержимого.
        if (document.Paragraphs.Count == 0)
        {
            errors.Add("Документ пуст.");
        }
        else
        {
            bool hasText = false;
            foreach (var p in document.Paragraphs)
            {
                if (!string.IsNullOrWhiteSpace(p.GetPlainText()) || p.ImageData != null)
                {
                    hasText = true;
                    break;
                }
            }
            if (!hasText)
            {
                errors.Add("Документ не содержит текста или изображений.");
            }
        }

        return errors;
    }
}
