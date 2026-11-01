"""Independently verify the emitted advances of a kerning-heavy chart label."""
import argparse
import copy
import hashlib
import json
import math
from pathlib import Path
import re
import xml.etree.ElementTree as ET
from zipfile import ZipFile

import fitz
from fontTools.ttLib import TTFont

LABEL = "AVAVAVAVAVAV"
FONT_SHA = "b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5"


def read(output, font_path):
    digest = hashlib.sha256(font_path.read_bytes()).hexdigest()
    if digest != FONT_SHA:
        raise ValueError("Unexpected font identity")
    font = TTFont(font_path)
    cmap, units = font.getBestCmap(), font["head"].unitsPerEm
    advances, ink = [], []
    for character in LABEL:
        name = cmap[ord(character)]
        glyph = font["glyf"][name]
        glyph.recalcBounds(font["glyf"])
        advances.append(font["hmtx"][name][0] * 8 / units)
        ink.append([glyph.xMin * 8 / units, glyph.yMin * 8 / units,
                    glyph.xMax * 8 / units, glyph.yMax * 8 / units])
    with fitz.open(output / "advance.pdf") as document:
        page = document[0]
        drawings = [drawing for drawing in page.get_drawings() if drawing.get("fill") and
                    tuple(round(value * 255) for value in drawing["fill"]) == (36, 104, 172)]
        if len(drawings) != 1:
            raise ValueError("Expected one independently decoded sector")
        rect = drawings[0]["rect"]
        chars = [character for block in page.get_text("rawdict")["blocks"] for line in block.get("lines", [])
                 for span in line["spans"] for character in span["chars"]]
        if "".join(character["c"] for character in chars) != LABEL:
            raise ValueError("PDF label content changed")
        pdf = [tuple(character["origin"]) for character in chars]
        circle = [rect.x0, rect.y0, rect.x1, rect.y1]
    root = ET.parse(output / "advance.svg").getroot()
    groups = [node for node in root.iter() if any(child.tag.endswith("}title") and child.text == LABEL for child in node)]
    if len(groups) != 1:
        raise ValueError("SVG label content changed")
    svg = []
    for node in groups[0]:
        if node.tag.endswith("}path"):
            match = re.match(r"translate\(([-\d.]+) ([-\d.]+)\)", node.get("transform", ""))
            if not match:
                raise ValueError("Unexpected SVG glyph transform")
            svg.append(tuple(float(value) for value in match.groups()))
    if len(svg) != len(LABEL):
        raise ValueError("SVG glyph count changed")
    with ZipFile(output / "advance.xps") as package:
        page = ET.fromstring(package.read("Documents/1/Pages/1.fpage"))
        labels = [node for node in page.iter() if node.get("UnicodeString") == LABEL]
        if len(labels) != 1 or labels[0].get("Indices") is not None:
            raise ValueError("Unexpected XPS label or explicit glyph shaping")
        node = labels[0]
        embedded = package.read(node.get("FontUri").lstrip("/"))
        if hashlib.sha256(embedded).hexdigest() != FONT_SHA:
            raise ValueError("XPS font identity changed")
        xps = [float(node.get("OriginX")) * .75, float(node.get("OriginY")) * .75]
    return {"pdf":pdf, "svg":svg, "xps":xps, "circle":circle, "advances":advances, "ink":ink, "fontSha256":digest}


def evaluate(data):
    pdf, svg, xps, circle, advances, ink = (data[key] for key in ("pdf","svg","xps","circle","advances","ink"))
    cx, cy = (circle[0] + circle[2]) / 2, (circle[1] + circle[3]) / 2
    radius = (circle[2] - circle[0]) / 2
    if abs((circle[3] - circle[1]) / 2 - radius) > .01:
        raise ValueError("Plot is not circular")
    if abs(pdf[0][0] + sum(advances) / 2 - cx) > .01:
        raise ValueError("Emitted PDF label is not centered")
    if max(abs(pdf[0][i] - xps[i]) for i in range(2)) > .01:
        raise ValueError("XPS and PDF origins differ")
    offset = 0
    maximum = 0
    for index, width in enumerate(advances):
        expected = (pdf[0][0] + offset, pdf[0][1])
        delta = max(abs(pdf[index][i] - expected[i]) for i in range(2))
        delta = max(delta, max(abs(svg[index][i] - expected[i]) for i in range(2)))
        maximum = max(maximum, delta)
        if delta > .01:
            raise ValueError("PDF or SVG glyph advances differ from font metrics")
        left, bottom, right, top = ink[index]
        for x in (pdf[index][0] + left, pdf[index][0] + right):
            for y in (pdf[index][1] - top, pdf[index][1] - bottom):
                if math.hypot(x - cx, y - cy) >= radius - .5:
                    raise ValueError("Label ink exceeds sector clearance")
        offset += width
    return {"characters":len(LABEL), "fontSha256":data["fontSha256"],
            "fontAdvanceWidthPt":sum(advances), "maximumGlyphOriginDeltaPt":maximum,
            "centerDeltaPt":abs(pdf[0][0] + sum(advances) / 2 - cx), "glyphInkWithinSector":True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("font", type=Path)
    args = parser.parse_args()
    data = read(args.output, args.font)
    result = evaluate(data)
    controls = {}
    for name in ("svg", "xps", "pdf", "advance"):
        changed = copy.deepcopy(data)
        if name == "svg": changed["svg"][3] = (changed["svg"][3][0] + 1, changed["svg"][3][1])
        if name == "xps": changed["xps"][0] += 1
        if name == "pdf": changed["pdf"][3] = (changed["pdf"][3][0] + 1, changed["pdf"][3][1])
        if name == "advance": changed["advances"][0] += 1
        try:
            evaluate(changed)
        except ValueError as error:
            controls[name] = str(error)
        else:
            raise ValueError("Negative control unexpectedly passed: " + name)
    result["negativeControls"] = controls
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
