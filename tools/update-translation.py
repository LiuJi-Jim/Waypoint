"""Add/update one reviewed English source and its translations using only Python's standard library.

Example: python tools/update-translation.py --source "Play" --ru "Играть" --zh-CN "开始游戏"
Every catalog beside Strings.resx (Strings.<language>.resx) has an option of its own: --<language>, or --<language>-file
for a UTF-8 file. For multiline text, use --source-file and the -file options. A new source needs every language, so that
no catalog lacks it; an existing one takes any of them.
"""
import argparse
import hashlib
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

DIRECTORY = Path(__file__).resolve().parents[1] / "src/Launcher.Core/Resources"


def languages():
    return sorted(path.name[len("Strings."):-len(".resx")] for path in DIRECTORY.glob("Strings.*.resx"))


def main():
    # The examples hold every language, which a Windows console's code page cannot print.
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    english = parser.add_mutually_exclusive_group(required=True)
    english.add_argument("--source")
    english.add_argument("--source-file", type=Path)
    for language in languages():
        group = parser.add_mutually_exclusive_group()
        group.add_argument(f"--{language}", dest=language)
        group.add_argument(f"--{language}-file", dest=language + "_file", type=Path)
    args = vars(parser.parse_args())
    source = args["source"] if args["source"] is not None else args["source_file"].read_text(encoding="utf-8")
    source = source.replace("\r\n", "\n").replace("\r", "\n")
    translations = {}
    for language in languages():
        if args[language] is not None:
            translations[language] = args[language]
        elif args[language + "_file"] is not None:
            translations[language] = args[language + "_file"].read_text(encoding="utf-8")
    if not source.strip() or not translations or not all(t.strip() for t in translations.values()):
        parser.error("The source and at least one translation must be nonempty.")
    key = "Text_" + hashlib.sha256(source.encode("utf-8")).hexdigest()[:12]
    if not any(d.attrib["name"] == key for d in ET.parse(DIRECTORY / "Strings.resx").getroot().findall("data")):
        lacking = [language for language in languages() if language not in translations]
        if lacking:
            parser.error("A new source needs every language; missing: " + ", ".join(lacking))
    for name, value in [("Strings.resx", source)] + [(f"Strings.{l}.resx", t) for l, t in translations.items()]:
        path = DIRECTORY / name
        tree = ET.parse(path)
        root = tree.getroot()
        item = next((d for d in root.findall("data") if d.attrib["name"] == key), None)
        if item is None:
            item = ET.SubElement(root, "data", {"name": key, "xml:space": "preserve"})
            ET.SubElement(item, "value")
        item.find("value").text = value
        headers = [e for e in root if e.tag != "data"]
        root[:] = headers + sorted(root.findall("data"), key=lambda d: d.attrib["name"])
        ET.indent(tree, space="  ")
        tree.write(path, encoding="utf-8", xml_declaration=True)
    print(key)


if __name__ == "__main__":
    main()
