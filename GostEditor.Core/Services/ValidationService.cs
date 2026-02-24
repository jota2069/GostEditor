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
        if (document.Sections.Count == 0)
        {
            errors.Add("Документ не содержит ни одного раздела.");
        }

        foreach (DocumentSection section in document.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Title))
            {
                errors.Add($"Раздел {section.Order + 1} не имеет заголовка.");
            }

            if (string.IsNullOrWhiteSpace(section.Content))
            {
                errors.Add($"Раздел \"{section.Title}\" не содержит текста.");
            }
        }

        return errors;
    }
}
