using System.Collections.Generic;
using GostEditor.Core.TextEngine.DOM;

namespace GostEditor.Core.TextEngine.Layout;

/// <summary>
/// Класс, отвечающий за расчет координат текста и разбиение на строки/страницы.
/// </summary>
public class LayoutEngine
{
    private readonly GostDocument _document;

    public LayoutEngine(GostDocument document)
    {
        _document = document;
    }

    // Этот метод будет вызываться из UI, так как только UI знает,
    // как физически выглядит шрифт на конкретном экране.
    public void UpdateLayout(double availableWidth)
    {
        // 1. Берем список параграфов из _document
        // 2. Для каждого параграфа считаем, сколько строк он займет
        // 3. Учитываем FirstLineIndent (1.25 см) для первой строки каждого параграфа
        // 4. Формируем список визуальных строк с координатами X, Y
    }
}
