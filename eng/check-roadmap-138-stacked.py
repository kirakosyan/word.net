"""Check editable stacked charts, managed vectors and independent office geometry."""
import argparse
import hashlib
import json
import re
from io import BytesIO
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

import fitz
from pypdf import PdfReader

C = '{http://schemas.openxmlformats.org/drawingml/2006/chart}'
S = '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
COLORS = ('2468AC', 'C03040')


def graph(path, kind):
    with ZipFile(path) as archive:
        chart = archive.read(next(n for n in archive.namelist() if n.endswith('/chart1.xml')))
        workbook = archive.read(next(n for n in archive.namelist() if n.endswith('.xlsx')))
    data = ET.fromstring(chart).find('.//' + C + 'barChart')
    if data.find(C + 'barDir').get('val') != ('bar' if kind == 'bar' else 'col') or data.find(C + 'grouping').get('val') != 'stacked' or data.find(C + 'overlap').get('val') != '100':
        raise ValueError('Stacked type, direction or overlap changed')
    caches = []
    for series in data.findall(C + 'ser'):
        cache = series.find('.//' + C + 'numCache')
        caches.append({int(p.get('idx')): float(p.find(C + 'v').text) for p in cache.findall(C + 'pt')})
    if caches != [{0: 10, 1: 0, 2: -2, 3: 6}, {0: -5, 1: 8, 3: 4}]:
        raise ValueError('Editable chart data changed')
    with ZipFile(BytesIO(workbook)) as package:
        sheet = ET.fromstring(package.read('xl/worksheets/sheet1.xml'))
    cells = {c.get('r'): c.find(S + 'v').text for c in sheet.iter(S + 'c') if c.find(S + 'v') is not None}
    if [float(cells[k]) for k in ('B2', 'B3', 'B4', 'B5', 'C2', 'C3', 'C5')] != [10, 0, -2, 6, -5, 8, 4] or 'C4' in cells:
        raise ValueError('Workbook values or missing-value semantics changed')
    return {'chartSha256': hashlib.sha256(chart).hexdigest(), 'workbookSha256': hashlib.sha256(workbook).hexdigest()}


def order(rectangles, kind):
    for color in COLORS:
        rectangles[color] = sorted((r for r in rectangles[color] if r[2] - r[0] > 10 and r[3] - r[1] > 8),
                                   key=lambda r: (-r[1], r[0]) if kind == 'bar' else (r[0], r[1]))
        if len(rectangles[color]) != 3:
            raise ValueError('Missing or extra nonzero chart rectangles: ' + color)
    return rectangles


def pdf_rectangles(path, kind):
    with fitz.open(path) as document:
        if len(document) != 1:
            raise ValueError('Expected one PDF page')
        page = document[0]; result = {c: [] for c in COLORS}
        for draw in page.get_drawings():
            fill = draw.get('fill')
            if fill:
                color = ''.join(f'{round(v * 255):02X}' for v in fill)
                if color in result:
                    result[color].append(tuple(draw['rect']))
        text = page.get_text()
    words = PdfReader(str(path)).pages[0].extract_text()
    if any(label not in text or label not in words for label in ('First', 'Second', 'Third', 'Fourth', 'North', 'South')):
        raise ValueError('Missing logical category or legend text')
    return order(result, kind)


def svg_rectangles(path, kind):
    result = {c: [] for c in COLORS}
    for node in ET.parse(path).iter():
        color = node.get('fill', '').lstrip('#').upper()
        if node.tag.endswith('}rect') and color in result:
            x, y, w, h = (float(node.get(k)) for k in ('x', 'y', 'width', 'height'))
            result[color].append((x, y, x + w, y + h))
    return order(result, kind)


def xps_rectangles(path, kind):
    result = {c: [] for c in COLORS}
    with ZipFile(path) as archive:
        page = ET.fromstring(archive.read('Documents/1/Pages/1.fpage'))
    for node in page.iter():
        color = node.get('Fill', '')[-6:].upper()
        if color not in result:
            continue
        nums = [float(v) for v in re.findall(r'-?\d+(?:\.\d+)?', node.get('Data', ''))]
        if len(nums) != 8:
            raise ValueError('Unexpected XPS rectangle path')
        x, y, width, _, _, height, _, _ = (n * 0.75 for n in nums)
        result[color].append((x, y, x + width, y + height))
    return order(result, kind)


def analytic(rectangles, kind):
    axis = 2 if kind == 'bar' else 3
    start = 0 if kind == 'bar' else 1
    for color, expected in (('2468AC', (0.2, 0.6, 1)), ('C03040', (0.5, 0.625, 1))):
        lengths = sorted(r[axis] - r[start] for r in rectangles[color])
        if any(abs(a / lengths[-1] - b) > 0.002 for a, b in zip(lengths, expected)):
            raise ValueError('Signed stack lengths differ from input values')
    blue, red = rectangles['2468AC'], rectangles['C03040']
    if kind == 'bar':
        if abs(blue[0][0] - red[0][2]) > 0.1 or abs(blue[-1][2] - red[-1][0]) > 0.1 or abs(blue[-1][1] - red[-1][1]) > 0.1:
            raise ValueError('Signed horizontal stacks do not share their boundaries')
        return blue[0][0]
    if abs(blue[0][3] - red[0][1]) > 0.1 or abs(blue[-1][1] - red[-1][3]) > 0.1 or abs(blue[-1][0] - red[-1][0]) > 0.1:
        raise ValueError('Signed vertical stacks do not share their boundaries')
    return blue[0][3]


def delta(a, b):
    return max(abs(x - y) for color in COLORS for first, second in zip(a[color], b[color]) for x, y in zip(first, second))


def legend_order(path):
    with fitz.open(path) as document:
        labels = [(word[1], word[0], word[4]) for word in document[0].get_text('words') if word[4] in ('North', 'South')]
    if len(labels) != 2 or {item[2] for item in labels} != {'North', 'South'}:
        raise ValueError('Missing or duplicate legend entries')
    return [item[2] for item in sorted(labels)]


def check(kind, source, saved, pdf, xps, svg, reference, diagnostics):
    source_graph = graph(source, kind)
    if source_graph != graph(saved, kind):
        raise ValueError('Editable graph or local workbook bytes changed')
    actual = pdf_rectangles(pdf, kind); expected = pdf_rectangles(reference, kind)
    actual_axis, reference_axis = analytic(actual, kind), analytic(expected, kind)
    svg_delta = delta(actual, svg_rectangles(svg, kind)); xps_delta = delta(actual, xps_rectangles(xps, kind))
    if svg_delta > 0.1 or xps_delta > 0.1:
        raise ValueError('PDF, SVG and XPS stacked geometry differ')
    actual_legend, reference_legend = legend_order(pdf), legend_order(reference)
    if actual_legend != reference_legend:
        raise ValueError('Stacked legend order differs from the independent reference')
    reference_delta = delta(actual, expected); axis_delta = abs(actual_axis - reference_axis)
    passed = reference_delta <= 0.5
    if not passed and 'Chart.StackedLayoutApproximation' not in Path(diagnostics).read_text().splitlines():
        raise ValueError('Missing independent reference-difference diagnostic')
    return {'kind': kind, 'graph': source_graph, 'pdfSvgMaxDeltaPt': svg_delta, 'pdfXpsMaxDeltaPt': xps_delta,
            'referenceMaxDeltaPt': reference_delta, 'referenceAxisDeltaPt': axis_delta, 'geometryTolerancePt': 0.5,
            'referenceFidelityPassed': passed, 'actualAxisPt': actual_axis, 'referenceAxisPt': reference_axis,
            'actualLegendOrder': actual_legend, 'referenceLegendOrder': reference_legend}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('kind', choices=('bar', 'column'))
    for name in ('source', 'saved', 'pdf', 'xps', 'svg', 'reference', 'diagnostics'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args(); print(json.dumps(check(args.kind, args.source, args.saved, args.pdf, args.xps, args.svg, args.reference, args.diagnostics)))


if __name__ == '__main__':
    main()
