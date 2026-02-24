using GostEditor.Core.Models;

namespace GostEditor.Core.Interfaces;

public interface IValidationService
{
    List<string> Validate(GostDocument document);
}
