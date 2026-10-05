# Лицензии, NOTICE и SBOM

## Что проверяется

`eng/release/generate_compliance.py` работает с уже собранным Release publish и:

1. читает фактический runtime graph из `GostEditor.UI.deps.json`;
2. требует точного совпадения package/version с одобренным RID-графом;
3. проверяет NuGet license metadata и хэши license/NOTICE evidence;
4. запрещает Xceed/DocX и любые неучтённые runtime-пакеты;
5. сопоставляет каждый распространяемый файл с приложением или NuGet-пакетом;
6. требует точного совпадения состава publish с committed baseline;
7. помещает `THIRD-PARTY-NOTICES.txt` в publish;
8. создаёт детерминированные `publish-inventory.json` и CycloneDX 1.6
   `sbom.cdx.json` с SHA-256 файлов и SHA-256/SHA-512 NuGet-архивов.

Политика допускает только явно перечисленные компоненты и лицензии. Изменение
версии, лицензии, NOTICE, runtime graph или publish content требует осознанного
review и обновления baseline; неизвестное изменение автоматически блокирует CI.

## Воспроизведение локально

После locked restore выполните для каждого поддерживаемого RID:

```bash
dotnet publish GostEditor.UI/GostEditor.UI.csproj \
  --configuration Release --runtime linux-x64 --no-restore \
  --output /tmp/gosteditor-release/linux-x64

python3 eng/release/generate_compliance.py \
  --rid linux-x64 \
  --publish-dir /tmp/gosteditor-release/linux-x64 \
  --output-dir /tmp/gosteditor-compliance/linux-x64
```

Повторите с `win-x64`. CI выполняет обе публикации и сохраняет полученные
NOTICE, inventory и SBOM как artifact `release-compliance`.

## Источники лицензий

- MIT-текст и Avalonia NOTICE закреплены в `eng/release/notices` с хэшами;
- Inter font сопровождается фактической OFL 1.1;
- CommunityToolkit, SkiaSharp/HarfBuzzSharp и .NET notices читаются напрямую
  из восстановленных NuGet-пакетов и проверяются по SHA-256;
- ANGLE license читается из Windows native package;
- происхождение и copyright каждого runtime-компонента зафиксированы в policy.

NuGet metadata и самостоятельные bundled assets используют MIT, BSD-3-Clause
и SIL Open Font License 1.1. Кроме того, закреплённые Avalonia и
SkiaSharp/HarfBuzzSharp notices содержат полный набор условий для включённого
ими third-party кода (в том числе BSD-family, Apache-2.0, MS-PL, Old MIT и
иные тексты). Эти notice сохраняются дословно: инструмент намеренно не заменяет
их потенциально неточной классификацией только по короткому SPDX-имени. Это
технический inventory, а не юридическое заключение.

## Границы

Проект сейчас не содержит отдельной лицензии на собственный исходный код
GostEditor. Эта ветка не выбирает её от имени владельца; перед публичным
распространением исходников или приёмом внешних вкладов владельцу следует явно
определить лицензию проекта.

Проверка относится к framework-dependent publish `linux-x64` и `win-x64`.
Инсталляторы, self-contained bundles, code signing, auto-update и store packages
создадут другой состав распространяемых файлов и требуют отдельного inventory в
соответствующей packaging-ветке.
