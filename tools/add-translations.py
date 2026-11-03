"""Add or update many reviewed sources and translations using only Python's standard library.

Example: python tools/add-translations.py batch.json
The file is a UTF-8 JSON object of English sources, each with its translations by language:
  {"Play": {"ru": "Играть", "zh-CN": "开始游戏"}}
or, with --language, of one language's text: python tools/add-translations.py --language zh-CN batch.json
  {"Play": "开始游戏"}
It works as tools/update-translation.py does for one source (same keys, same catalogs, same ordering), and refuses the
whole batch when a text is empty, its placeholders differ, a language has no catalog, or a new source lacks a language.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

PLACEHOLDER = re.compile(r"(?<!\{)\{(\d+)(?=[,:}])")
DIRECTORY = Path(__file__).resolve().parents[1] / "src/Launcher.Core/Resources"


def key_of(source):
    return "Text_" + hashlib.sha256(source.encode("utf-8")).hexdigest()[:12]


def normalized(text):
    return text.replace("\r\n", "\n").replace("\r", "\n")


def languages():
    return sorted(path.name[len("Strings."):-len(".resx")] for path in DIRECTORY.glob("Strings.*.resx"))


def keys(name):
    return {d.attrib["name"] for d in ET.parse(DIRECTORY / name).getroot().findall("data")}


def problems(batch, known):
    for source, translations in batch.items():
        if not source.strip():
            yield "empty source"
            continue
        for language, translation in translations.items():
            if language not in languages():
                yield f"no catalog Strings.{language}.resx: {source!r}"
            elif not isinstance(translation, str) or not translation.strip():
                yield f"empty {language} text: {source!r}"
            elif sorted(PLACEHOLDER.findall(source)) != sorted(PLACEHOLDER.findall(translation)):
                yield f"{language} placeholders differ: {source!r} / {translation!r}"
        lacking = [l for l in languages() if l not in translations]
        if key_of(source) not in known and lacking:
            yield f"new source lacks {', '.join(lacking)}: {source!r}"


def update(path, values):
    tree = ET.parse(path)
    root = tree.getroot()
    items = {d.attrib["name"]: d for d in root.findall("data")}
    added = changed = 0
    for key, value in values.items():
        item = items.get(key)
        if item is None:
            item = ET.SubElement(root, "data", {"name": key, "xml:space": "preserve"})
            ET.SubElement(item, "value").text = value
            items[key] = item
            added += 1
        elif item.find("value").text != value:
            item.find("value").text = value
            changed += 1
    headers = [e for e in root if e.tag != "data"]
    root[:] = headers + sorted(root.findall("data"), key=lambda d: d.attrib["name"])
    ET.indent(tree, space="  ")
    tree.write(path, encoding="utf-8", xml_declaration=True)
    return added, changed


def main():
    # The examples hold every language, which a Windows console's code page cannot print.
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("batch", type=Path, help="UTF-8 JSON object: {english: {language: text}}, or {english: text}")
    parser.add_argument("--language", help="the language of a batch of plain texts, as in Strings.<language>.resx")
    args = parser.parse_args()
    raw = json.loads(args.batch.read_text(encoding="utf-8-sig"))
    if not isinstance(raw, dict):
        parser.error("The batch must be a JSON object.")
    batch = {}
    for source, value in raw.items():
        if isinstance(value, dict):
            translations = value
        elif args.language:
            translations = {args.language: value}
        else:
            parser.error(f"{source!r} has a plain text: say its language with --language, or give {{language: text}}.")
        batch[normalized(source)] = {l: normalized(t) if isinstance(t, str) else t for l, t in translations.items()}
    wrong = list(problems(batch, keys("Strings.resx")))
    if wrong:
        print("\n".join(wrong), file=sys.stderr)
        return 1
    update(DIRECTORY / "Strings.resx", {key_of(source): source for source in batch})
    for language in languages():
        values = {key_of(s): t[language] for s, t in batch.items() if language in t}
        if values:
            added, changed = update(DIRECTORY / f"Strings.{language}.resx", values)
            print(f"{language}: {len(values)} texts, {added} added, {changed} changed, {len(values) - added - changed} already there")
    return 0


if __name__ == "__main__":
    sys.exit(main())
