"""Build a local QA gallery from untouched game captures; optionally make contact sheets."""
import argparse
import hashlib
import html
import json
import shutil
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--contacts", action="store_true")
parser.add_argument("--archive", action="store_true")
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
folder = root / "artifacts/validation-1.3.0"
files = sorted(folder.glob("lab-*.png"))
run = json.loads((folder / "run.json").read_text(encoding="utf-8-sig"))
files = [p for p in files if p.stat().st_mtime >= run["started_unix"]]
pairs = []
used = set()
for source in files:
    if not source.stem.endswith("-source"):
        continue
    demo = source.with_name(source.name.replace("-source.png", ".png"))
    if demo in files:
        name = demo.stem.removeprefix("lab-")
        pairs.append((name, source, demo))
        used.update([source, demo])
dll = root / "artifacts/lab/mods/Sts2Demo/Sts2Demo.dll"
digest = hashlib.sha256(dll.read_bytes()).hexdigest().upper()
if digest != run["dll_sha256"]:
    raise SystemExit("Lab DLL changed since this run; refusing to label screenshots with an untested hash")
manifest = {"version": "1.3.0", "dll_sha256": digest, "run": run,
            "method": "Native isolated game, synthetic peers; activity source replayed as sender 2. Not Steam two-client evidence.",
            "images": [{"file": p.name, "sha256": hashlib.sha256(p.read_bytes()).hexdigest(),
                        "modified_unix": p.stat().st_mtime} for p in files]}
(folder / "screenshots.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
shutil.copyfile(root / "docs/RELEASE_1.3.0.md", folder / "RELEASE_1.3.0.md")
parts = ["""<!doctype html><html lang="ru"><meta charset="utf-8"><title>Friend View 1.3.0 — проверка</title>
<style>body{background:#111820;color:#ecf1f5;font:16px system-ui;margin:24px}a{color:#8ad8ff}nav{display:flex;gap:14px;flex-wrap:wrap}.pair{display:grid;grid-template-columns:1fr 1fr;gap:12px}figure{margin:0}img{width:100%;height:auto}section{margin:36px 0}figcaption{padding:8px}code{overflow-wrap:anywhere}@media(max-width:800px){.pair{grid-template-columns:1fr}}</style>
<h1>Friend View 1.3.0 — кадры проверки</h1>
<p>Слева — исходное штатное окно, справа — DEMO. Кадры сохранены реальной игрой без ретуши. Это изолированный лабораторный запуск с синтетическими участниками: для проверки рендера состояние исходного окна воспроизводится от имени игрока 2. Отдельные кадры reward-owner проверяют разные награды игроков 1, 2 и 3. Галерея не доказывает работу между двумя Steam-клиентами.</p>
<p>Фазы декоративных анимаций между кадрами различаются. Точные результаты и ограничения — в <a href="RELEASE_1.3.0.md">отчёте RELEASE_1.3.0.md</a>.</p>""",
         f"<p>DLL тестовой копии: <code>{digest}</code>. <a href='screenshots.json'>Хеши и время снимков</a>.</p><nav>"]
parts.extend(f"<a href='#{html.escape(n)}'>{html.escape(n)}</a>" for n, _, _ in pairs)
parts.append("</nav>")
def figure(path, label):
    return f"<figure><figcaption>{html.escape(label)}</figcaption><a href='{path.name}'><img loading='lazy' src='{path.name}' alt='{html.escape(label)}'></a></figure>"
for name, source, demo in pairs:
    parts.append(f"<section id='{name}'><h2>{name}</h2><div class='pair'>" + figure(source, "Исходное окно") + figure(demo, "DEMO") + "</div></section>")
parts.append("<h2>Дополнительные сценарии и управление</h2><div class='pair'>")
for path in files:
    if path not in used:
        parts.append(figure(path, path.stem.removeprefix("lab-")))
parts.append("</div></html>")
(folder / "index.html").write_text("\n".join(parts), encoding="utf-8")
if args.contacts:
    from PIL import Image, ImageDraw
    for start in range(0, len(pairs), 4):
        batch = pairs[start:start + 4]
        sheet = Image.new("RGB", (1280, len(batch) * 388), "#111820")
        draw = ImageDraw.Draw(sheet)
        for row, (name, source, demo) in enumerate(batch):
            draw.text((8, row * 388 + 5), name + " | source / DEMO", fill="white")
            for col, path in enumerate([source, demo]):
                with Image.open(path) as im:
                    sheet.paste(im.resize((640, 360)), (col * 640, row * 388 + 28))
        sheet.save(folder / f"contact-{start // 4 + 1:02}.jpg", quality=92)
if args.archive:
    with zipfile.ZipFile(root / "dist/FriendView-1.3.0-validation.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        evidence = files + [folder / name for name in ["index.html", "screenshots.json", "run.json", "lab-current.log", "RELEASE_1.3.0.md"]]
        if (folder / "pre-release-checks.txt").exists():
            evidence.append(folder / "pre-release-checks.txt")
        stress_manifest = folder / "stress-run.json"
        if stress_manifest.exists() and json.loads(stress_manifest.read_text(encoding="utf-8-sig"))["dll_sha256"] == digest:
            evidence.extend([stress_manifest, folder / "lab-stress.log"])
        for path in evidence:
            archive.write(path, path.name)
print(f"Gallery: {folder / 'index.html'}; {len(pairs)} pairs; {len(files)} screenshots")
