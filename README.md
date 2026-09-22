# ComBridge

ComBridge — локальный HTTP-мост между ИИ-агентом и интерактивным рабочим столом Windows 10/11 x64. Приложение делает скриншоты и отправляет события мыши/клавиатуры через Win32 API. Оно не знает о конкретных тестируемых приложениях.

## Архитектура и безопасность

`ComBridge.exe` работает в интерактивном сеансе Windows и по умолчанию слушает только `http://127.0.0.1:8088`. Внешний доступ не открывается. Linux-агент вызывает API через SSH-туннель:

```text
Linux/Codex -> SSH tunnel -> 127.0.0.1:8088 -> ComBridge -> Win32 -> Windows desktop
```

Все GUI-операции последовательно проходят через один асинхронный шлюз. Успешный ответ подтверждает отправку событий, но не завершение операции целевым приложением.

## Системные требования

- Windows 10/11 x64.
- Интерактивный, разблокированный пользовательский сеанс.
- Для сборки: .NET 10 SDK на Linux x64 и доступ к NuGet.
- Runtime .NET на Windows не нужен: сборка self-contained.

## Координаты и DPI

Приложение включает Per-Monitor DPI Awareness V2. `/screen` возвращает весь виртуальный экран. API мыши принимает координаты **пикселей PNG**: `(0,0)` — левый верхний пиксель `/screen`. ComBridge прибавляет `virtualScreen.left/top` перед вызовом Win32. Поэтом отрицательные Win32-координаты не передаются агентом. `/screen/info` возвращает и границы Win32, и `imageOffset`.

## HTTP API

Все ошибки имеют JSON-форму `{ "error": "code", "message": "description" }`. Ошибки валидации — HTTP 400, недоступный desktop — 503, ошибка Win32 — 500.

| Method | Path | Body/result |
|---|---|---|
| GET | `/health` | status, version, sessionId, desktopAvailable, screenWidth, screenHeight |
| GET | `/screen` | PNG/JPEG всего desktop, монитора или прямоугольной области |
| GET | `/screen/info` | virtualScreen, imageOffset, monitors |
| GET | `/windows` | Видимые верхнеуровневые окна: id, title, processId, bounds, minimized |
| POST | `/windows/activate` | `{ "id": "0x123ABC" }`; восстанавливает и выводит окно на передний план |
| GET | `/mouse/position` | Win32-координаты курсора и координаты PNG |
| POST | `/mouse/move` | `{ "x": 540, "y": 380 }` |
| POST | `/mouse/click` | `{ "x": 540, "y": 380, "button": "left", "count": 1 }`; button: left/right/middle, count: 1/2 |
| POST | `/mouse/scroll` | `{ "delta": -120, "x": 540, "y": 380 }`; x/y оба опциональны |
| POST | `/mouse/drag` | `{ "fromX": 100, "fromY": 100, "toX": 500, "toY": 500, "durationMs": 500 }` |
| POST | `/keyboard/type` | `{ "text": "Привет, 1С!" }`; Unicode через `KEYEVENTF_UNICODE` |
| POST | `/keyboard/hotkey` | `{ "keys": ["CTRL", "SHIFT", "S"] }` |
| GET | `/clipboard/text` | Читает `CF_UNICODETEXT`; ответ `{ "text": "...", "length": 3 }` |
| POST | `/clipboard/text` | `{ "text": "Новый текст" }`; полностью заменяет текст в буфе |

Горячие клавиши: CTRL, ALT, SHIFT, WIN, ENTER, ESC, TAB, BACKSPACE, DELETE, SPACE, UP, DOWN, LEFT, RIGHT, HOME, END, PAGEUP, PAGEDOWN, F1–F12, A–Z, 0–9. Имена нечувствительны к регистру. Клавиши отпускаются в обратном порядке.

Буфер обмена работает только с Unicode-текстом. Если `CF_UNICODETEXT` отсутствует, GET возвращает HTTP 409 и `clipboard_text_unavailable`. Ошибка открытия буфера не маскируется повторами. Максимальная длина при POST — 10 000 000 UTF-16 code units. Сам текст в лог не записывается.

### Снимки без масштабирования и поиск окон

`GET /screen?monitor=0` возвращает только указанный монитор. `GET /screen?x=0&y=0&width=800&height=600` возвращает область в координатах полного `/screen`. `monitor` нельзя смешивать с x/y/width/height; для области обязательны все четыре параметра. Допустимы `format=png` и `format=jpeg`; для JPEG есть `quality=1..100` (по умолчанию 80).

`/windows` возвращает `desktopBounds` в Win32-координатах и `imageBounds` в координатах полного `/screen`. ID окна — непрозрачная hex-строка, действительная только до закрытия окна.

## Сборка на Linux

```bash
bash scripts/build-linux.sh
```

Результат: `dist/win-x64/ComBridge.exe`. Скрипт сначала запускает тесты, затем `dotnet publish` для `win-x64`, self-contained single-file.

## Установка и запуск на Windows

1. Скопировать `ComBridge.exe`, например в `C:\Tools\ComBridge\`.
2. Войти в нужную учётную запись Windows и запустить из обычного Terminal, не из SSH-сеанса:

```powershell
C:\Tools\ComBridge\ComBridge.exe --port 8088
```

Адрес можно задать `--address`; не-loopback адрес отклоняется. Лог пишется в `%LOCALAPPDATA%\ComBridge\logs\combridge-YYYYMMDD.log`.
Отсутствующие и некорректные значения `--port`/`--address` приводят к немедленному отказу запуска, а не к подстановке значений по умолчанию.

Для автозапуска при входе создать в Task Scheduler задачу `At log on` для нужного пользователя с `Run only when user is logged on`. Не включать `Run whether user is logged on or not`: такая задача не получит видимый desktop.

## SSH-туннель и curl

```bash
ssh -N -L 18088:127.0.0.1:8088 testpc
```

В другом Linux-терминале:

```bash
curl --fail http://127.0.0.1:18088/health
curl --fail http://127.0.0.1:18088/screen --output screen.png
curl --fail 'http://127.0.0.1:18088/screen?monitor=0&format=jpeg&quality=70' --output monitor.jpg
curl --fail http://127.0.0.1:18088/windows
curl --fail -H 'Content-Type: application/json' \
  -d '{"id":"0x123ABC"}' http://127.0.0.1:18088/windows/activate
curl --fail -H 'Content-Type: application/json' \
  -d '{"x":540,"y":380,"button":"left","count":1}' \
  http://127.0.0.1:18088/mouse/click
curl --fail -H 'Content-Type: application/json' \
  -d '{"text":"Привет, 1С!"}' \
  http://127.0.0.1:18088/keyboard/type
curl --fail http://127.0.0.1:18088/clipboard/text
curl --fail -H 'Content-Type: application/json' \
  -d '{"text":"Текст для буфера"}' \
  http://127.0.0.1:18088/clipboard/text
```

## Известные ограничения

- ComBridge не работает с экраном блокировки, UAC secure desktop и окнами процессов с более высоким integrity level.
- SSH-сеанс сам по себе не даёт доступ к интерактивному desktop; EXE нужно запустить при входе пользователя.
- GDI-захват может не видеть DRM-защищённое и некоторое GPU-ускоренное содержимое.
- Linux-тесты не подтверждают фактическую работу Win32, DPI и GUI. Это проверяется на Windows отдельно.

## Фактические проверки

22.09.2026 self-contained сборка 0.1.0 проверена на Windows 10 x64 в интерактивном сеансе через обратный SSH-туннель. Успешно проверены `/health`, `/screen/info`, PNG-скриншот 1920×1080, move, left click, scroll, drag, Unicode-ввод английского/русского текста и emoji, `CTRL+A` и `BACKSPACE`. Тестовая строка вводилась в Telegram, не отправлялась и была удалена через API.
Также проверен полный clipboard round-trip через GET/POST с английским, русским текстом, emoji и переносом строки; исходный буфер после теста восстановлен.

На Windows пока не проверены неосновные кнопки мыши, двойной клик, несколько мониторов, отрицательные координаты и DPI-масштабы 125–200%.
