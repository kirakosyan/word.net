#!/usr/bin/env python3
"""Validate the synthetic table-shading ODT using pinned ODF 1.3 schemas.

Usage: python eng/verify-table-shading-odt.py output.odt --schemas path/to/schemas
The checked schemas and lxml are development-only tools; no network is used.
"""

import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile

from lxml import etree


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("odt", type=Path)
    parser.add_argument("--schemas", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    pins = json.loads((root / "eng/test-assets/odf13-schemas.json").read_text(encoding="utf-8"))
    validators = {}
    for name, metadata in pins.items():
        data = (args.schemas / name).read_bytes()
        if hashlib.sha256(data).hexdigest() != metadata["sha256"]:
            raise ValueError("Pinned ODF schema checksum mismatch: " + name)
        validators[name] = etree.RelaxNG(etree.fromstring(data))
    with ZipFile(args.odt) as package:
        for part in ("content.xml", "styles.xml", "meta.xml", "META-INF/manifest.xml"):
            schema = "OpenDocument-v1.3-manifest-schema.rng" if part.startswith("META-INF/") else "OpenDocument-v1.3-schema.rng"
            xml = etree.fromstring(package.read(part), etree.XMLParser(resolve_entities=False, no_network=True, load_dtd=False))
            validators[schema].assertValid(xml)
        content = etree.fromstring(package.read("content.xml"))
        ns = {"fo": "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0", "text": "urn:oasis:names:tc:opendocument:xmlns:text:1.0"}
        fills = content.xpath("//@fo:background-color", namespaces=ns)
        text = "".join(content.xpath("//text:p//text()", namespaces=ns))
        if "#FFF3C0" not in fills or text != "ABCD":
            raise ValueError("The ODF package lost its expected background color or reading order")
    print("Four ODF 1.3 XML parts passed pinned schema validation; table fill and ABCD reading order were present.")


if __name__ == "__main__":
    main()
