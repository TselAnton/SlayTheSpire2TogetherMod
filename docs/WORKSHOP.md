# Подготовка Friend View 1.3.0 к Workshop

Это локальный кандидат, не опубликованное обновление. Состояние приёмки — RELEASE_1.3.0.md; внешние проверки — PRE_RELEASE_TESTS.md и COOP_TEST.md.

scripts/Prepare-Workshop.ps1 создаёт workshop/FriendView/content из четырёх файлов: Sts2Demo.dll, Sts2Demo.json, README.md и LICENSE. Игровые библиотеки, извлечённые ресурсы, тестовый мод, профили и декомпилированные исходники исключены. scripts/Test-PreRelease.ps1 проверяет версии, ZIP, allowlist и universal-modder publish check.

Английское описание и changelog подготовлены в workshop/FriendView/workshop.json. Существующий объект 3819332749 сохраняется; новый не создаётся. Обложка image.png меньше 1 MB.

После закрытия обязательных проверок и отдельного разрешения автора используется scripts/Publish-Workshop.ps1 с -ConfirmRelease и установленным официальным ModUploader.exe. Подготовка архива не запускает загрузчик, не обновляет Workshop и не отправляет изменения Git.

Собственный код — MIT; разработан с помощью OpenAI Codex. Игровые ресурсы Mega Crit разрешаются из установленной игры. Universal Modder и ILSpy использованы для анализа и проверки. Игра-цель: v0.107.1; совместимость с будущими версиями требует проверки.
