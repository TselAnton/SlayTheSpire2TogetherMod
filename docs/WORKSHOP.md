# Публикация Friend View 1.3.0 в Workshop

Версия 1.3.0 опубликована 2026-10-14 в существующем объекте Steam Workshop `3819332749`. Отчёт о проверках и известных границах — RELEASE_1.3.0.md; сценарии внешней проверки — PRE_RELEASE_TESTS.md и COOP_TEST.md.

scripts/Prepare-Workshop.ps1 создаёт workshop/FriendView/content из четырёх файлов: Sts2Demo.dll, Sts2Demo.json, README.md и LICENSE. Игровые библиотеки, извлечённые ресурсы, тестовый мод, профили и декомпилированные исходники исключены. scripts/Test-PreRelease.ps1 проверяет версии, ZIP, allowlist и universal-modder publish check.

Английское описание и changelog подготовлены в workshop/FriendView/workshop.json. Существующий объект 3819332749 сохраняется; новый не создаётся. Обложка image.png меньше 1 MB.

Публикация выполнена scripts/Publish-Workshop.ps1 с -ConfirmRelease и официальным ModUploader.exe. Перед загрузкой universal-modder повторно проверил allowlist пакета: 4 файла, 0 ошибок, 0 предупреждений. Загрузчик подтвердил обновление объекта `3819332749`; новый объект не создавался.

Собственный код — MIT; разработан с помощью OpenAI Codex. Игровые ресурсы Mega Crit разрешаются из установленной игры. Universal Modder и ILSpy использованы для анализа и проверки. Игра-цель: v0.107.1; совместимость с будущими версиями требует проверки.
