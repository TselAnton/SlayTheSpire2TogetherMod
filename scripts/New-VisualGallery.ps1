param([string]$OutputDirectory = 'artifacts/visual-audit-1.2.2')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
if (-not $destination.StartsWith((Join-Path $repo 'artifacts') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Gallery must stay inside artifacts.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
# Explicit inventory: never silently replace missing evidence with an older/different mode.
$cases = @(
    @('combat-design', 'Бой: рука друга', 'combat-source', 'Исходный кадр — собственный Ironclad, demo — Silent. Это разные параметры игроков, не попиксельная эталонная пара.'),
    @('combat-arrow', 'Бой: стрелка намерения', '', 'Статичная стрелка на нативных ассетах; синтетическая поза друга.'),
    @('controller-combat', 'Бой: подсказки контроллера', '', 'Лабораторный Xbox-профиль ввода, не физический Steam Deck.'),
    @('dead-observer', 'Бой: наблюдатель погиб', '', 'Живой друг остаётся доступен для просмотра.'),
    @('reward-list', 'Награды: список', 'reward-list-source', 'Панель исправлена. Фон и верхняя панель ещё не соответствуют сцене друга; подсветка второй строки задана стендом.'),
    @('audit-reward-hover-0', 'Наградные карты: левая', 'audit-reward-cards-source', 'Одинаковый набор; в demo задан курсор над левой картой. Источник снят без этого наведения. Заголовок и фон пока отличаются.'),
    @('audit-reward-hover-1', 'Наградные карты: средняя', 'audit-reward-cards-source', 'Локальная подсветка средней карты по координатам, несмотря на устаревший индекс правой.'),
    @('audit-reward-hover-2', 'Наградные карты: правая', 'audit-reward-cards-source', 'Локальная подсветка правой карты.'),
    @('audit-reward-upgraded', 'Наградные карты: улучшения', '', 'Галочка включена наблюдателем; все три карты улучшены только для отображения.'),
    @('controller-rewards', 'Награды: контроллер', '', 'Нативные значки и локальное управление галочкой.'),
    @('predictions', 'Предсказания: закрытое поле', 'predictions-source', 'Поле, инструменты и текст присутствуют. Нет верхней панели друга; декоративные фазы могут различаться.'),
    @('predictions-partial', 'Предсказания: открытая клетка', 'predictions-partial-source', 'Одна клетка раскрыта стендом; скрытые части предмета не отправляются. Требуется оценка совпадения маски.'),
    @('map', 'Карта', 'map-source', 'НЕ СООТВЕТСТВУЕТ: отсутствуют значки узлов и часть оформления. Это блокирующее расхождение, не успешный тест.'),
    @('merchant-room', 'Комната торговца', 'merchant-room-source', 'НЕ СООТВЕТСТВУЕТ: отсутствует большая часть сцены торговца/персонажей.'),
    @('shop', 'Магазин: товары', 'shop-source', 'Товары, цены и карты видны. Нет сцены за окном и верхней панели друга; проверьте оттенки/геометрию.'),
    @('event', 'Событие: текст и варианты', 'event-source', 'Нативные иллюстрация, текст и варианты. Верхняя панель друга отсутствует.'),
    @('choose-card', 'Выбор одной карты', 'choose-card-source', 'Карты и подписи восстановлены. Лента заголовка/фон отличаются. Видимые кнопки друга не интерактивны.'),
    @('choose-bundle', 'Выбор набора карт', 'choose-bundle-source', 'Два набора. Лента заголовка/фон отличаются; декоративное свечение не полностью повторяется.'),
    @('select-deck', 'Удаление / выбор из колоды', 'select-deck-source', 'Сетка и инструкция; фон и верхняя панель отличаются.'),
    @('select-simple', 'Множественный выбор', 'select-simple-source', 'Сетка выбора 1–2 карт; чужие кнопки не исполняют действий.'),
    @('select-upgrade', 'Выбор для улучшения', 'select-upgrade-source', 'Экран вне костра. Костёр по требованиям запрещён.'),
    @('select-transform', 'Выбор для преобразования', 'select-transform-source', 'Сетка выбора. Подтверждение преобразования не выполняется.'),
    @('select-enchant', 'Выбор для зачарования', 'select-enchant-source', 'Нативный экран с Vigorous; никаких зачарований живой колоды стенд не выполняет.'),
    @('select-upgrade-preview', 'Улучшение: предпросмотр', 'select-upgrade-preview-source', 'Нативное окно подтверждения; подтверждение не нажимается.'),
    @('select-transform-preview', 'Преобразование: предпросмотр', 'select-transform-preview-source', 'Нативное окно подтверждения, без изменения колоды.'),
    @('select-enchant-preview', 'Зачарование: предпросмотр', 'select-enchant-preview-source', 'Сравнение карты до и после; никакое зачарование не применяется к живой колоде.'),
    @('deck', 'Просмотр колоды', 'deck-source', 'Сортировки, карты, галочка. Фон/верхняя панель отличаются.'),
    @('cards-view', 'Просмотр списка карт', 'cards-view-source', 'Отдельный простой экран списка, не экран колоды.'),
    @('inspect', 'Одна карта: обычная', 'inspect-source', 'Крупная карта, стрелки и галочка. Фон/верхняя панель отличаются.'),
    @('inspect-upgraded', 'Одна карта: улучшенная', 'inspect-upgraded-source', 'Улучшенный предпросмотр на стороне источника.'),
    @('fake-merchant', 'Событие: ложный торговец', 'fake-merchant-source', 'Отдельная сцена. Проверить недостающую сценографию; не считать полным совпадением.'),
    @('fake-merchant-inventory', 'Ложный торговец: товары', 'fake-merchant-inventory-source', 'Отдельная разновидность торгового интерфейса.'),
    @('blocked-relic-choice', 'Запрещено: выбор реликвии', '', 'Это НЕ demo: экран снят после проверки запрета входа и автоматического выхода.'),
    @('blocked-rest-site', 'Запрещено: костёр', '', 'Это НЕ demo: глаз скрыт, вход запрещён, просмотр автоматически закрыт.')
)
$html = [Text.StringBuilder]::new()
[void]$html.AppendLine('<!doctype html><html lang="ru"><meta charset="utf-8"><title>Friend View 1.2.2 — визуальная проверка</title><style>body{background:#111820;color:#edf1f5;font:16px system-ui;margin:24px;line-height:1.5}main{max-width:1800px;margin:auto}h1{font-size:28px}article{border-top:1px solid #536375;margin:28px 0;padding-top:14px}.pair{display:flex;gap:12px}figure{flex:1;min-width:0;margin:0}img{width:100%;height:auto}figcaption{color:#b7c7d6}a{color:#8ddbc6}.note{background:#342c1d;padding:16px}nav{columns:3}li{break-inside:avoid}@media(max-width:900px){.pair{display:block}nav{columns:1}}</style><main><h1>Friend View 1.2.2: визуальная проверка</h1><p class="note">Результат: НЕ полное визуальное соответствие. Расхождения перечислены под каждым случаем. Это настоящие кадры из изолированной игры 1280×720 с синтетической передачей метаданных; не запись двух клиентов Steam. Для пар вне боя один нативный экран служит источником метаданных, затем воспроизводится как экран игрока 2. Такая пара проверяет рендер, но не независимость состояния двух настоящих клиентов. Клик по картинке открывает оригинал.</p><nav><ol>')
foreach ($case in $cases) { [void]$html.AppendLine('<li><a href="#' + $case[0] + '">' + $case[1] + '</a></li>') }
[void]$html.AppendLine('</ol></nav>')
$manifest = @()
foreach ($case in $cases) {
    [void]$html.AppendLine('<article id="' + $case[0] + '"><h2>' + $case[1] + '</h2><p>' + $case[3] + '</p><div class="pair">')
    $items = @()
    if ($case[2]) { $items += ,@($case[2], 'Обычный интерфейс / источник') }
    $items += ,@($case[0], $(if ($case[0].StartsWith('blocked-')) { 'Запрет demo' } else { 'Demo / наблюдатель' }))
    foreach ($item in $items) {
        $name = 'lab-' + $item[0] + '.png'
        $source = Join-Path $destination $name
        if (-not (Test-Path -LiteralPath $source)) { throw "Missing screenshot: $name" }
        $manifest += [pscustomobject]@{ case = $case[0]; file = $name; captured = (Get-Item -LiteralPath $source).LastWriteTimeUtc.ToString('o'); sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash }
        [void]$html.AppendLine('<figure><figcaption>' + $item[1] + '</figcaption><a href="' + $name + '"><img loading="lazy" src="' + $name + '" alt="' + $case[1] + '"></a></figure>')
    }
    [void]$html.AppendLine('</div></article>')
}
[void]$html.AppendLine('</main></html>')
[IO.File]::WriteAllText((Join-Path $destination 'index.html'), $html.ToString(), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $destination 'manifest.json'), ($manifest | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
Write-Output "Gallery: $destination/index.html; cases=$($cases.Count); unique PNGs=$(($manifest.file | Sort-Object -Unique).Count)"
