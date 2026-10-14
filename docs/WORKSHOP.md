# Подготовка Friend View 1.3.1 к Workshop

Патч подготовлен локально для существующего объекта `3819332749`. Загрузка 1.3.1 не выполнялась. Отчёт и ограничения — RELEASE_1.3.1.md.

`scripts/Prepare-Workshop.ps1` собирает четыре файла: собственную Sts2Demo.dll, Sts2Demo.json, README.md и LICENSE. `scripts/Test-PreRelease.ps1` сверяет версии и хеши ZIP/Workshop, проверяет allowlist и запускает `universal-modder publish check`.

Игровые библиотеки, извлечённые ресурсы, тестовый мод, профили и декомпилированные исходники в пакет не входят. Английский текст обновления находится в workshop/FriendView/workshop.json. Публикация выполняется отдельным действием официального ModUploader после решения владельца.
