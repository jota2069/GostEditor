# Зависимости распространяемой сборки

Этот документ описывает граф зависимостей framework-dependent публикации
`GostEditor.UI` для поддерживаемых RID `linux-x64` и `win-x64`. Источник истины
для версий — `Directory.Packages.props` и lock-файлы проектов. Точный граф
каждого publish фиксируется в `eng/release/compliance-policy.json`, а состав
файлов — в `eng/release/publish-baseline.json`.

## Прямые production-зависимости

`GostEditor.Core` напрямую использует:

- CommunityToolkit.Mvvm 8.4.0;
- DocumentFormat.OpenXml 3.5.1;
- Microsoft.Extensions.DependencyInjection 10.0.3;
- Newtonsoft.Json 13.0.4;
- SkiaSharp.NativeAssets.Linux.NoDependencies 2.88.8.

`GostEditor.UI` напрямую использует Avalonia 11.3.22 (`Avalonia`, `Desktop`,
`Themes.Fluent`, `Fonts.Inter`) и CommunityToolkit.Mvvm 8.4.2.
`Avalonia.Diagnostics` разрешается для разработки, но исключён из Release
publish через `IncludeAssets=None` и `PrivateAssets=All`.

В итоговом UI publish более новые версии из общего графа побеждают прямые
версии Core: CommunityToolkit.Mvvm разрешается как 8.4.2, SkiaSharp и нативные
assets — как 2.88.9. Это существующее состояние графа, а не скрытое обновление
в рамках compliance-ветки.

## Транзитивный runtime-граф

Обе публикации включают Avalonia runtime (включая Desktop, FreeDesktop,
Native, Remote.Protocol, Skia, Win32 и X11), Open XML Framework,
HarfBuzzSharp, MicroCom.Runtime, DI abstractions, SkiaSharp,
System.IO.Packaging и Tmds.DBus.Protocol. Linux дополнительно содержит
`HarfBuzzSharp.NativeAssets.Linux` и `SkiaSharp.NativeAssets.Linux`; Windows —
соответствующие Win32-пакеты и `Avalonia.Angle.Windows.Natives`.

Полные точные списки package/version находятся в секции `rids` файла
`eng/release/compliance-policy.json`. Генератор сверяет их с фактическим
`GostEditor.UI.deps.json`, а затем связывает каждый распространяемый managed,
native и resource-файл с владельцем из `.deps.json`. Сгенерированный
`publish-inventory.json` для каждого файла хранит категорию (`managed`,
`native`, `resource`, `app-host`, `runtime-metadata`, `symbols` или `notice`),
происхождение, размер и SHA-256. Пакеты помечаются как `direct` или
`transitive` по project nodes фактического `.deps.json`; CycloneDX dependency
graph сохраняет эти связи и связывает пакеты с принадлежащими им файлами.

## Test/tooling-зависимости

`GostEditor.Tests` использует Microsoft.NET.Test.Sdk 17.6.0, xUnit 2.4.2,
xunit.runner.visualstudio 2.4.5 и coverlet.collector 6.0.0. Их транзитивные
компоненты (testhost, Microsoft.CodeCoverage, xUnit core/assert/abstractions и
другие test SDK assets) не входят в Release publish и намеренно не считаются
распространяемыми runtime-компонентами.

## Xceed/DocX

Xceed, DocX и пакеты с такими префиксами запрещены release policy. Они
отсутствуют в production package graph, `.deps.json` и publish inventory.
Возврат такого пакета приводит к ошибке compliance-проверки CI.
