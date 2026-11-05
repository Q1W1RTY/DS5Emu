# Сборка

Нужны Windows 11 x64, Git, Visual Studio 2022 Build Tools (C++ x64, CMake, Ninja, Windows SDK) и .NET SDK из global.json. Использованные версии: MSVC 14.44, SDK 10.0.26100, .NET SDK 10.0.403.

```powershell
git clone --recurse-submodules https://github.com/Q1W1RTY/DS5Link.git
cd DS5Link
.\scripts\prepare-engine.ps1
.\scripts\build.ps1
.\scripts\test.ps1
```

Движок закреплён на upstream-коммите через submodule. Изменения DS5Link применяются из patches/vds/0001-ds5link-bootstrap.patch. Экспериментальное уменьшение аудиобуфера в этот патч не входит.

Native-сборка: artifacts/native-Release. Интерфейс: src/DS5Link.App/bin/Release. Сборка и тесты не устанавливают службу или драйверы.

## Установочный комплект

```powershell
.\scripts\fetch-dependencies.ps1
.\scripts\package.ps1
```

Скрипт проверяет хеши и подписи пакетов драйверов. В ZIP включены приложение, .NET runtime, app-local Visual C++ CRT, лицензии и установочные скрипты. Пути и версия MSVC CRT в package.ps1 закреплены под сборочную среду; на другой машине их нужно адаптировать.

EXE-мастер собирается через scripts/build-setup.ps1. У текущего релизного профиля строгая проверка хеша принятого native-бинарника: он рассчитан на сохранённый проверенный пакет 064430, а не на произвольный свежий билд. Для обычной установки использовать готовый EXE из Releases. Не снимать эту проверку для публикации непроверенного движка.

Иконки созданы через image_gen; исходные PNG и запросы находятся в assets/brand. scripts/generate-icons.ps1 упаковывает отдельные изображения приложения и трея в ICO разных размеров, сохраняя прозрачность. Bootstrap компилируется встроенным в Windows Framework64 csc. Проверка EXE без установки: scripts/qa-setup.ps1 -ReleaseDirectory <каталог релиза>.

## Проверки

scripts/test.ps1 запускает CTest и managed-сценарии протокола и диагностики. Аппаратные QA-скрипты находятся в scripts/qa-*.ps1; они могут переподключать контроллер или переустанавливать приложение, поэтому их следует запускать отдельно на тестовой машине.

Локальные журналы, пакеты, конфигурация контроллера и история аппаратных прогонов исключены из Git.

## Кандидат после ревью

Версия задаётся в VERSION. scripts/test.ps1 включает 13 native-групп, 54 managed-сценария и 48 UI layout/state-сценариев с синтетическими областями просмотра. Повышенный scripts/qa-install-path.ps1 использует отдельную службу, pipe, реестр и защищённую папку с пробелами/кириллицей/скобками; TestDrive задаёт локальный диск. Настоящие драйверы fixture не устанавливает. scripts/qa-cleanup-fixtures.ps1 удаляет его проверенные собственные каталоги, сохраняя отчёты.

Для тестового EXE с изменённым native: scripts/build-setup.ps1 -CandidateNativeDirectory <artifacts/native-Release>. Метаданные сохраняют candidate=true и hardwareAccepted=false; это не снимает защиту принятого native-хеша в обычном профиле. qa-setup проверяет выбранный режим и хеши. Принятие нового native и публикация требуют отдельного решения после аппаратной проверки.

Для Xbox используется исходный ViGEmClient из third_party/ViGEmClient (ревизия и лицензия указаны рядом). scripts/test-dependencies.ps1 проверяет обнаружение драйверов на подставном инвентаре; qa-setup.ps1 дополнительно запускает настоящий отчёт Dependencies.ps1 через Windows PowerShell 5.1.

Пример сборки кандидата 0.1.6 с сохранённым базовым пакетом: `scripts/build-setup.ps1 -PackageDirectory artifacts/packages/DS5Link-0.1.0-20261101-064430 -CandidateNativeDirectory artifacts/native-Release`. Пакеты драйверов берутся из последнего комплекта, созданного package.ps1; исходные скрипты мастера — из installer.

Оформление EXE-мастера находится в installer/SetupStyle.cs; bootstrap компилируется вместе с installer/Setup.cs. Опубликованные сборки с новым движком сохраняют candidate=true и hardwareAccepted=false до завершения физической приёмки.

Неразрушающий сбор с намеренно отсутствующим IPC endpoint: `dotnet run --project tests/DS5Link.Tests -c Release -- --diagnostic-smoke <новый каталог>`. Служба и драйверы не изменяются; отчёт сохраняется в заданном каталоге.
