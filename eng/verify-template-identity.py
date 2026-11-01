#!/usr/bin/env python3
"""Validate generated OTT/DOTX artifacts with pinned ODF schemas and a separate office reader.

The caller first runs roadmap-138-template-probe.cs, then converts generated-odf.ott
and generated-word.dotx to DOCX using the named external reader in a separate output
directory. This tool downloads only the pinned development schemas if absent.
"""

import argparse
import hashlib
import json
from pathlib import Path
import urllib.request
from xml.etree import ElementTree
from zipfile import ZIP_STORED, ZipFile

from lxml import etree


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def check_archive(archive):
    entries = archive.infolist()
    if len(entries) > 1024 or sum(entry.file_size for entry in entries) > 64 * 1024 * 1024:
        raise ValueError("Archive exceeds the independent verification limit")
    if archive.testzip() is not None:
        raise ValueError("Archive failed CRC verification")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("probe", type=Path, help="directory created by the source probe")
    parser.add_argument("office", type=Path, help="directory containing independently converted DOCX files")
    parser.add_argument("schemas", type=Path, help="development-only schema cache directory")
    parser.add_argument("--reader", required=True, help="external reader and exact version")
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    probe, office, schemas = args.probe.resolve(), args.office.resolve(), args.schemas.resolve()
    pins = json.loads((root / "eng/test-assets/odf13-schemas.json").read_text(encoding="utf-8"))
    schemas.mkdir(parents=True, exist_ok=True)
    for name, metadata in pins.items():
        path = schemas / name
        if not path.exists():
            with urllib.request.urlopen(metadata["url"], timeout=30) as response:
                data = response.read(5 * 1024 * 1024 + 1)
            if len(data) > 5 * 1024 * 1024 or sha256(data) != metadata["sha256"]:
                raise ValueError("ODF schema size or checksum mismatch: " + name)
            with path.open("xb") as target:
                target.write(data)
        if sha256(path.read_bytes()) != metadata["sha256"]:
            raise ValueError("ODF schema checksum mismatch: " + name)
    validators = {name: etree.RelaxNG(etree.parse(str(schemas / name))) for name in pins}
    ott = probe / "generated-odf.ott"
    with ZipFile(ott) as archive:
        check_archive(archive)
        if not archive.namelist() or archive.namelist()[0] != "mimetype":
            raise ValueError("Invalid OTT archive or mimetype position")
        if archive.getinfo("mimetype").compress_type != ZIP_STORED or archive.read("mimetype") != b"application/vnd.oasis.opendocument.text-template":
            raise ValueError("Invalid OTT mimetype")
        for part in ("content.xml", "styles.xml", "meta.xml", "META-INF/manifest.xml"):
            schema = "OpenDocument-v1.3-manifest-schema.rng" if part.startswith("META-INF/") else "OpenDocument-v1.3-schema.rng"
            xml = etree.fromstring(archive.read(part), etree.XMLParser(resolve_entities=False, no_network=True, load_dtd=False))
            validators[schema].assertValid(xml)
    word = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}
    checks = []
    for stem in ("generated-odf", "generated-word"):
        path = office / (stem + ".docx")
        with ZipFile(path) as archive:
            check_archive(archive)
            document = ElementTree.fromstring(archive.read("word/document.xml"))
            text = "".join(element.text or "" for element in document.findall(".//w:t", word))
            for token in ("Café", "Ω", "end", "next"):
                if token not in text:
                    raise ValueError("External reader omitted expected synthetic text: " + token)
        checks.append({"file": path.name, "sha256": sha256(path.read_bytes()), "expectedText": "pass"})
    report = {"reader": args.reader, "odfSchema": "pass", "ottSha256": sha256(ott.read_bytes()),
              "dotxSha256": sha256((probe / "generated-word.dotx").read_bytes()), "office": checks}
    with (office / "verification.json").open("x", encoding="utf-8") as output:
        json.dump(report, output, indent=2, ensure_ascii=False)
        output.write("\n")
    print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()
