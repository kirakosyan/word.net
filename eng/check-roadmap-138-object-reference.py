#!/usr/bin/env python3
"""Check an independently rendered fragment probe and its retained OOXML data."""

import argparse
import io
import json
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

from PIL import Image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("docx", type=Path)
    parser.add_argument("pdf_text", type=Path)
    parser.add_argument("pdf_raster", type=Path, help="96 dpi RGB raster from the reference PDF")
    args = parser.parse_args()
    chart_ns = "{http://schemas.openxmlformats.org/drawingml/2006/chart}"
    math_ns = "{http://schemas.openxmlformats.org/officeDocument/2006/math}"
    sheet_ns = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
    with zipfile.ZipFile(args.docx) as document:
        xml = ET.fromstring(document.read("word/document.xml"))
        fraction = xml.find(".//" + math_ns + "f")
        assert fraction is not None
        assert "".join(item.text or "" for item in fraction.find(math_ns + "num").iter(math_ns + "t")) == "x"
        assert "".join(item.text or "" for item in fraction.find(math_ns + "den").iter(math_ns + "t")) == "4"
        chart = ET.fromstring(document.read("word/charts/chart1.xml"))
        values = [point.find(chart_ns + "v").text for point in chart.findall(".//" + chart_ns + "numCache/" + chart_ns + "pt")]
        assert values == ["10", "20"]
        with zipfile.ZipFile(io.BytesIO(document.read("word/embeddings/chart1.xlsx"))) as workbook:
            sheet = ET.fromstring(workbook.read("xl/worksheets/sheet1.xml"))
            cells = {cell.attrib["r"]: "".join(cell.itertext()) for cell in sheet.iter(sheet_ns + "c")}
            assert [cells[key] for key in ("A2", "B2", "A3", "B3")] == ["Q1", "10", "Q2", "20"]
    text = args.pdf_text.read_text(encoding="utf-8")
    positions = [text.index(item) for item in ("Before", "Quarterly", "Sales", "Q1", "Q2", "\nx\n4\n", "After")]
    assert positions == sorted(positions)
    image = Image.open(args.pdf_raster).convert("RGB")
    pixels, columns = image.load(), {}
    for y in range(image.height):
        for x in range(image.width):
            if pixels[x, y] == (36, 104, 172):
                columns.setdefault(x, []).append(y)
    groups = []
    for x in sorted(columns):
        if not groups or x != groups[-1][-1] + 1:
            groups.append([])
        groups[-1].append(x)
    bars = []
    for group in groups:
        if len(group) < 10:
            continue
        ys = [y for x in group for y in columns[x]]
        bars.append([group[0], min(ys), group[-1], max(ys)])
    assert len(bars) == 2 and bars[0][3] == bars[1][3]
    heights = [bar[3] - bar[1] + 1 for bar in bars]
    assert 1.95 <= heights[1] / heights[0] <= 2.05
    print(json.dumps({"rasterSize": list(image.size), "rgb": "2468AC", "barBoundsPixels": bars,
                      "barHeightRatio": heights[1] / heights[0], "workbookValues": [10, 20],
                      "referenceText": "Before, Quarterly, Sales, Q1, Q2, x/4, After"}))


if __name__ == "__main__":
    main()
