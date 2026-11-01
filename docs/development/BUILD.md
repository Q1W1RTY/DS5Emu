# Сборка

Нужны Windows 11 x64, Git, Visual Studio 2022 Build Tools (C++ x64, CMake, Ninja, Windows SDK) и .NET SDK из global.json. Использованные версии: MSVC 14.44, SDK 10.0.26100, .NET SDK 10.0.403.

```powershell
git clone --recurse-submodules https://github.com/Q1W1RTY/DS5Emu.git
cd DS5Emu
.\scripts\prepare-engine.ps1
.\scripts\build.ps1
.\scripts\test.ps1
```

Движок закреплён на upstream-коммите через submodule. Изменения DS5Emu применяются из patches/vds/0001-dsemu-bootstrap.patch. Экспериментальное уменьшение аудиобуфера в этот патч не входит.

Native-сборка: artifacts/native-Release. Интерфейс: src/DSEmu.App/bin/Release. Сборка и тесты не устанавливают службу или драйверы.

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
