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

Generic metadata содержит только общие timestamps. Academic title-page,
modules, listings, bibliography и legacy counters изолированы в immutable
JSON payload с namespace `org.gosteditor.compatibility.gost-v2` внутри
bounded `DocumentExtensionCatalog`. Core владеет bytes и limits, но не
интерпретирует Study semantics. Неизвестные extensions нельзя экспортировать
в `.gost v2` без явного loss diagnostic.

## Identity и graph invariants

`DocumentRoot` поддерживает полный runtime index всех sections, blocks,
table rows/cells и inlines. `FindNode`/`FindBlock` используют index; editing
boundary перестраивает его после mutations. Rebuild сначала валидирует новый
graph и заменяет index только целиком, поэтому невалидный rebuild не публикует
частичный index.

`DocumentGraphValidator` детерминированно проверяет unique non-empty IDs,
single-parent ownership, cycles, required node state, table nesting depth,
figure dimensions и resource references. Dangling/empty resources являются
errors; orphan resource является явным warning и может быть сохранён для
последующего placement. Full-tree validation предназначена для import,
debug/test boundaries и structural mutation boundaries, а не для каждого
keystroke.

## Интеграция

- `LegacyDocumentEditingBridge` создаёт `DocumentEditingSession`;
- `DocumentEditor` предоставляет создание структурированной сессии и применение
  её результата;
- активная история использует локальные `DocumentMutationCommand`;
- `RenderController` подключает `DocumentChanged` к инкрементальному layout.
