# Document model foundation

Этот документ сохранён как краткое описание исходного подэтапа. Полное
состояние этапа 1, включая интеграцию истории и layout, описано в
`stage-1-editor-core.md`.

## Назначение

Структурированная модель отделяет семантику документа от `.gost v2`, UI и
Avalonia layout. Она вводит секции, блоки, inline-узлы, ресурсы и стабильные
идентификаторы, не меняя существующий формат хранения.

## Совместимость

`LegacyDocumentAdapter` является явной границей между `GostDocument` и
`DocumentRoot`. Неподдерживаемые v2-конструкции не проецируются молча.

## Интеграция

- `LegacyDocumentEditingBridge` создаёт `DocumentEditingSession`;
- `DocumentEditor` предоставляет создание структурированной сессии и применение
  её результата;
- активная история использует локальные `DocumentMutationCommand`;
- `RenderController` подключает `DocumentChanged` к инкрементальному layout.
