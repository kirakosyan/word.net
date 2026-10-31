"""Inspect editable circular labels, vectors, fit and office references."""
import argparse
import hashlib
from io import BytesIO
import json
import math
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

import fitz
from pypdf import PdfReader

C = '{http://schemas.openxmlformats.org/drawingml/2006/chart}'
A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'
S = '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
COLORS = ((36, 104, 172), (192, 48, 64))
NAMES = ('First', 'Second')
LABELS = ('First, 6', 'Second, 6')


def graph(path, kind):
    with ZipFile(path) as archive:
        chart = archive.read(next(n for n in archive.namelist() if n.endswith('/chart1.xml')))
        workbook = archive.read(next(n for n in archive.namelist() if n.endswith('.xlsx')))
    data = ET.fromstring(chart).find('.//' + C + ('pieChart' if kind == 'pie' else 'doughnutChart'))
    if data is None or data.find(C + 'firstSliceAng').get('val') != '0':
        raise ValueError('Circular type or start angle changed')
    if kind == 'doughnut' and data.find(C + 'holeSize').get('val') != '50':
        raise ValueError('Doughnut hole size changed')
    series = data.findall(C + 'ser')
    if len(series) != 1:
        raise ValueError('Expected one circular series')
    points = series[0].findall(C + 'dPt')
    colors = [(int(p.find(C + 'idx').get('val')), p.find('.//' + A + 'srgbClr').get('val')) for p in points]
    if colors != [(0, '2468AC'), (1, 'C03040')]:
        raise ValueError('Slice colours or indices changed')
    labels = series[0].find(C + 'dLbls')
    if labels is None:
        labels = data.find(C + 'dLbls')
    if labels is None:
        raise ValueError('Missing point-label profile')
    flags = {node.tag[len(C):]: node.get('val') for node in labels if node.tag != C + 'separator'}
    if flags != {'dLblPos': 'ctr', 'showLegendKey': '0', 'showVal': '1', 'showCatName': '1', 'showSerName': '0', 'showPercent': '0', 'showBubbleSize': '0'} or labels.find(C + 'separator').text != ', ':
        raise ValueError('Point label appearance or explicit flags changed')
    cache = series[0].find('.//' + C + 'numCache')
    if {int(p.get('idx')): float(p.find(C + 'v').text) for p in cache.findall(C + 'pt')} != {0: 6, 1: 6}:
        raise ValueError('Slice values changed')
    with ZipFile(BytesIO(workbook)) as package:
        sheet = ET.fromstring(package.read('xl/worksheets/sheet1.xml'))
    cells = {cell.get('r'): cell.find(S + 'v').text for cell in sheet.iter(S + 'c') if cell.find(S + 'v') is not None}
    if (cells['B2'], cells['B3']) != ('6', '6'):
        raise ValueError('Local workbook values changed')
    return {'chartSha256': hashlib.sha256(chart).hexdigest(), 'workbookSha256': hashlib.sha256(workbook).hexdigest()}


def color(pixel):
    return next((i for i, expected in enumerate(COLORS) if max(abs(a - b) for a, b in zip(pixel, expected)) <= 2), None)


def vectors(page):
    result = {}
    for drawing in page.get_drawings():
        fill = drawing.get('fill')
        index = color(tuple(round(channel * 255) for channel in fill)) if fill else None
        if index is None or drawing['rect'].width <= 10 or drawing['rect'].height <= 10:
            continue
        if index in result:
            raise ValueError('Duplicate coloured sector')
        result[index] = drawing
    if set(result) != set(range(len(COLORS))):
        raise ValueError('Missing coloured vector sectors')
    return result


def path_delta(first, second):
    maximum = 0
    for index in range(len(COLORS)):
        left, right = first[index]['items'], second[index]['items']
        if len(left) != len(right):
            raise ValueError('Cross-format sector paths differ')
        for a, b in zip(left, right):
            if a[0] != b[0] or len(a) != len(b):
                raise ValueError('Cross-format sector commands differ')
            for point_a, point_b in zip(a[1:], b[1:]):
                maximum = max(maximum, abs(point_a.x - point_b.x), abs(point_a.y - point_b.y))
    return maximum


def raster(page, sectors, kind):
    rectangle = fitz.Rect(sectors[0]['rect'])
    for item in sectors.values():
        rectangle |= item['rect']
    scale = 2; pixmap = page.get_pixmap(matrix=fitz.Matrix(scale, scale), alpha=False)
    samples, channels = pixmap.samples, pixmap.n
    def pixel(x, y):
        if x < 0 or y < 0 or x >= pixmap.width or y >= pixmap.height:
            raise ValueError('Circular sample outside page')
        offset = (y * pixmap.width + x) * channels
        return tuple(samples[offset:offset + 3])
    xs, ys = [], []
    for y in range(max(0, int(rectangle.y0 * scale)), min(pixmap.height, math.ceil(rectangle.y1 * scale))):
        for x in range(max(0, int(rectangle.x0 * scale)), min(pixmap.width, math.ceil(rectangle.x1 * scale))):
            if color(pixel(x, y)) is not None:
                xs.append(x / scale); ys.append(y / scale)
    if not xs:
        raise ValueError('No coloured sector pixels')
    bounds = (min(xs), min(ys), max(xs) + 1 / scale, max(ys) + 1 / scale)
    width, height = bounds[2] - bounds[0], bounds[3] - bounds[1]
    if abs(width - height) > 1 or width < 20:
        raise ValueError('Circular plot is not round')
    cx, cy, radius = (bounds[0] + bounds[2]) / 2, (bounds[1] + bounds[3]) / 2, (width + height) / 4
    for degrees in range(5, 360, 10):
        if min(abs(degrees - 90), abs(degrees - 270)) < 25:
            continue
        angle = math.radians(degrees); x = round((cx + math.sin(angle) * radius * .58) * scale); y = round((cy - math.cos(angle) * radius * .58) * scale)
        expected = 0 if degrees < 180 else 1
        if color(pixel(x, y)) != expected:
            raise ValueError('Slice orientation or 1:1 proportions changed')
    inside = pixel(round((cx + .2 * radius) * scale), round(cy * scale))
    if kind == 'doughnut' and min(inside) < 250 or kind == 'pie' and color(inside) is None:
        raise ValueError('Circular centre or doughnut hole changed')
    return bounds


def pdf_text(path, actual=False):
    with fitz.open(path) as document:
        page = document[0]; spans = []
        for block in page.get_text('dict')['blocks']:
            for line in block.get('lines', []):
                spans.extend(span for span in line['spans'] if span['text'] in LABELS + NAMES + ('Independent shares',))
    extracted = PdfReader(str(path)).pages[0].extract_text()
    order = [span['text'] for span in spans]
    if any(order.count(name) != 1 or name not in extracted for name in LABELS + NAMES):
        raise ValueError('Missing or duplicated point label or category legend')
    if [item for item in order if item in LABELS] != list(LABELS) or [item for item in order if item in NAMES] != list(NAMES):
        raise ValueError('Point-label or legend order changed')
    if actual and order != ['Independent shares', *LABELS, *NAMES]:
        raise ValueError('Managed point-label/legend reading order changed')
    return order, {span['text']: span for span in spans if span['text'] in LABELS}


def svg_labels(path):
    import re
    found = {}
    for group in ET.parse(path).iter():
        title = next((child.text for child in group if child.tag.endswith('}title') and child.text in LABELS), None)
        if title:
            paths = [node for node in group if node.tag.endswith('}path')]
            if title in found or group.get('fill', '').upper() != '#FFFFFF' or not paths:
                raise ValueError('Missing or changed SVG point-label vectors')
            match = re.match(r'translate\(([-\d.]+) ([-\d.]+)\)', paths[0].get('transform', ''))
            if not match:
                raise ValueError('Unexpected SVG label transform')
            found[title] = tuple(float(value) for value in match.groups())
    if set(found) != set(LABELS):
        raise ValueError('Missing SVG point labels')
    return found


def xps_labels(path):
    with ZipFile(path) as archive:
        page = ET.fromstring(archive.read('Documents/1/Pages/1.fpage'))
    found = {}
    for node in page.iter():
        label = node.get('UnicodeString')
        if label in LABELS:
            if label in found or node.get('Fill', '').upper() != '#FFFFFFFF':
                raise ValueError('Missing or changed XPS point labels')
            found[label] = (float(node.get('OriginX')) * .75, float(node.get('OriginY')) * .75)
    if set(found) != set(LABELS):
        raise ValueError('Missing XPS point labels')
    return found


def labels_contained(page, spans):
    scale = 2; pixmap = page.get_pixmap(matrix=fitz.Matrix(scale, scale), alpha=False); samples, channels = pixmap.samples, pixmap.n
    def pixel(x, y):
        x, y = round(x * scale), round(y * scale)
        if x < 0 or y < 0 or x >= pixmap.width or y >= pixmap.height:
            return None
        offset = (y * pixmap.width + x) * channels
        return color(tuple(samples[offset:offset + 3]))
    result = []
    for i, label in enumerate(LABELS):
        x0, y0, x1, y1 = spans[label]['bbox']
        result.append(all(pixel(x, y) == i for x, y in ((x0 - 1, y0 - 1), (x1 + 1, y0 - 1), (x1 + 1, y1 + 1), (x0 - 1, y1 + 1))))
    return result


def check(kind, source, saved, pdf, xps, svg, reference, diagnostics):
    source_graph = graph(source, kind)
    if source_graph != graph(saved, kind):
        raise ValueError('Editable graph or local workbook bytes changed')
    actual_order, actual_labels = pdf_text(pdf, True); reference_order, reference_labels = pdf_text(reference)
    if any(span['color'] != 0xFFFFFF for span in actual_labels.values()):
        raise ValueError('Point-label contrast color changed')
    def difference(first, second):
        return max(abs(a - b) for a, b in zip(first, second))
    svg_text, xps_text = svg_labels(svg), xps_labels(xps)
    svg_text_delta = max(difference(actual_labels[label]['origin'], svg_text[label]) for label in LABELS)
    xps_text_delta = max(difference(actual_labels[label]['origin'], xps_text[label]) for label in LABELS)
    if svg_text_delta > .1 or xps_text_delta > .1:
        raise ValueError('PDF, SVG and XPS point-label origins differ')
    with fitz.open(pdf) as actual, fitz.open(svg) as svg_doc, fitz.open(xps) as xps_doc, fitz.open(reference) as office:
        if any(len(doc) != 1 for doc in (actual, svg_doc, xps_doc, office)):
            raise ValueError('Expected one page')
        actual_vectors, svg_vectors, xps_vectors, reference_vectors = (vectors(doc[0]) for doc in (actual, svg_doc, xps_doc, office))
        svg_delta, xps_delta = path_delta(actual_vectors, svg_vectors), path_delta(actual_vectors, xps_vectors)
        if svg_delta > .1 or xps_delta > .1:
            raise ValueError('PDF, SVG and XPS sector geometry differs')
        actual_bounds, reference_bounds = raster(actual[0], actual_vectors, kind), raster(office[0], reference_vectors, kind)
        actual_contained, reference_contained = labels_contained(actual[0], actual_labels), labels_contained(office[0], reference_labels)
        if not all(actual_contained):
            raise ValueError('Managed point label crosses its sector or hole')
    reference_delta = max(abs(a - b) for a, b in zip(actual_bounds, reference_bounds))
    codes = Path(diagnostics).read_text().splitlines()
    if 'Chart.CircularLayoutApproximation' not in codes or 'Chart.DataLabelApproximation' not in codes:
        raise ValueError('Missing independent circular or label diagnostic')
    label_delta = max(difference(actual_labels[label]['origin'], reference_labels[label]['origin']) for label in LABELS)
    return {'kind': kind, 'graph': source_graph, 'actualReadingOrder': actual_order, 'referenceReadingOrder': reference_order,
            'actualLabelsContained': actual_contained, 'referenceLabelsContained': reference_contained,
            'pdfSvgLabelOriginMaxDeltaPt': svg_text_delta, 'pdfXpsLabelOriginMaxDeltaPt': xps_text_delta,
            'referenceLabelOriginMaxDeltaPt': label_delta, 'pdfSvgMaxDeltaPt': svg_delta,
            'pdfXpsMaxDeltaPt': xps_delta, 'actualRasterBoundsPt': actual_bounds, 'referenceRasterBoundsPt': reference_bounds,
            'referenceMaxDeltaPt': reference_delta, 'rasterResolutionPt': .5, 'geometryTolerancePt': .5,
            'referenceFidelityPassed': reference_delta <= .5 and label_delta <= .5 and actual_order == reference_order,
            'sectorProportions': [1, 1], 'doughnutHoleChecked': kind == 'doughnut'}


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('kind', choices=('pie', 'doughnut'))
    for name in ('source', 'saved', 'pdf', 'xps', 'svg', 'reference', 'diagnostics'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args(); print(json.dumps(check(args.kind, args.source, args.saved, args.pdf, args.xps, args.svg, args.reference, args.diagnostics)))


if __name__ == '__main__':
    main()
