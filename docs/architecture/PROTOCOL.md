# DSEmu Control v1

Транспорт: `\\.\pipe\DSEmu.Control.v1`, локальный message pipe с явным ACL. Формат: UTF-8 JSONL. Одна транзакция на подключение: запрос с LF → ответ с LF → закрытие. Приложение получает снимок раз в секунду; аудио и ввод не зависят от частоты обновления UI.

```json
{"protocolVersion":1,"id":"request-1","command":"getState"}
```

Ответ:

```json
{"protocolVersion":1,"id":"request-1","ok":true,"payload":{"state":"Paused","enabled":false,"batteryPercent":null,"charging":false,"muted":false,"headphones":false,"audioActive":false,"microphoneActive":false,"packetsPerSecond":0,"gapsOver10ms":0,"maxGapMs":0,"observedSeconds":0,"transport":"Bluetooth","rssi":null,"error":""}}
```

Команды: `hello`, `getState`, `enable`, `pause`, `reconnect`, `disconnect`, `getDiagnostics`. Hello возвращает тот же снимок с версией envelope. `getDiagnostics` добавляет ограниченный хвост `logLines`; маскировка перед ZIP выполняется клиентом. Произвольные пути и shell-команды протокол не принимает.

`disconnect` выключает эмуляцию и после освобождения worker разрывает Bluetooth-соединение только с единственным настроенным обычным DualSense. Адрес в запросе не принимается как цель. Адаптер и сопряжение сохраняются. Пока выполняется операция, состояние Stopping; после подтверждения Windows состояние Paused, enabled=false и bluetoothDisconnected=true. Поле добавлено совместимо с v1. Пока операция не закончена, enable/reconnect/disconnect возвращают BLUETOOTH_DISCONNECT_PENDING. Ошибки scope: CONTROLLER_NOT_CONFIGURED, CONTROLLER_BUSY. Ошибка отключения возвращается в последующем состоянии Error.

Состояния: Waiting, Connecting, Active, Paused, Stopping, Error, Wired, Conflict. Ответ на pause/reconnect подтверждает принятие команды; завершение определяется следующим снимком. Во время Stopping контроллер ещё не считается освобождённым.

Ошибки: `ok:false`, строка `error`, тот же `id`. Определённые коды: PROTOCOL_VERSION_MISMATCH, UNKNOWN_COMMAND, BRIDGE_PAUSED. Несовместимая версия не меняет состояние.

Частичные записи собираются до LF, предел — 64 КиБ; несколько JSON-объектов в одной транзакции не поддерживаются. Вложенность ограничена 32 уровнями. Запрос/ответ не может удерживать управляющий клиент бесконечно. При несовпадении версии или id клиент отклоняет ответ.

Для штатного vdsctl сохранены legacy attach/detach/list/list-targets/trace через тот же защищённый канал. Интерфейс приложения использует только v1.

Исходный план предлагал push-события и subscribe. В текущей версии выбран опрос снимка 1 Гц: он упрощает переподключение UI и не вводит очередь событий в поток движка. Это проектное решение, а не утверждение, что события уже реализованы.

Метрики относятся только к валидным пакетам игрового ввода. Интервалы >10 мс и максимум учитываются в скользящей минуте по моменту окончания интервала. Аудиопакеты микрофона не повышают частоту ввода. При новой сессии статистика очищается.
