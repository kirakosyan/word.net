"""Inspect editable chart labels, vector output and independent office geometry."""
import argparse
import hashlib
from io import BytesIO
import json
from pathlib import Path
import re
from xml.etree import ElementTree as ET
from zipfile import ZipFile

import fitz
from pypdf import PdfReader

C = '{http://schemas.openxmlformats.org/drawingml/2006/chart}'
S = '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
LABELS = ('First, 10', 'Second, 8')


def graph(path, kind):
    with ZipFile(path) as archive:
        chart = archive.read(next(n for n in archive.namelist() if n.endswith('/chart1.xml')))
        workbook = archive.read(next(n for n in archive.namelist() if n.endswith('.xlsx')))
    data = ET.fromstring(chart).find('.//' + C + 'barChart')
    if data.find(C + 'barDir').get('val') != ('bar' if kind == 'bar' else 'col') or data.find(C + 'grouping').get('val') != 'clustered':
        raise ValueError('Chart type or direction changed')
    series = data.findall(C + 'ser')
    if len(series) != 1:
        raise ValueError('Expected one independently authored series')
    labels = series[0].find(C + 'dLbls')
    flags = {node.tag[len(C):]: node.get('val') for node in labels if node.tag != C + 'separator'}
    if flags != {'dLblPos': 'ctr', 'showLegendKey': '0', 'showVal': '1', 'showCatName': '1', 'showSerName': '0', 'showPercent': '0', 'showBubbleSize': '0'} or labels.find(C + 'separator').text != ', ':
        raise ValueError('Point label appearance or explicit flags changed')
    cache = series[0].find('.//' + C + 'numCache')
    if {int(p.get('idx')): float(p.find(C + 'v').text) for p in cache.findall(C + 'pt')} != {0: 10, 1: 8}:
        raise ValueError('Point data changed')
    with ZipFile(BytesIO(workbook)) as package:
        sheet = ET.fromstring(package.read('xl/worksheets/sheet1.xml'))
    cells = {cell.get('r'): cell.find(S + 'v').text for cell in sheet.iter(S + 'c') if cell.find(S + 'v') is not None}
    if (cells['B2'], cells['B3']) != ('10', '8'):
        raise ValueError('Local workbook data changed')
    return {'chartSha256': hashlib.sha256(chart).hexdigest(), 'workbookSha256': hashlib.sha256(workbook).hexdigest()}


def pdf(path, require_containment, kind):
    with fitz.open(path) as document:
        if len(document) != 1:
            raise ValueError('Expected one page')
        page = document[0]; text = page.get_text(); spans = []
        for block in page.get_text('dict')['blocks']:
            for line in block.get('lines', []):
                spans.extend(span for span in line['spans'] if span['text'] in LABELS)
        rectangles = [tuple(draw['rect']) for draw in page.get_drawings() if draw.get('fill') and
                      ''.join(f'{round(channel * 255):02X}' for channel in draw['fill']) == '2468AC' and
                      draw['rect'].width > 10 and draw['rect'].height > 10]
    other_text = PdfReader(str(path)).pages[0].extract_text()
    if any(text.count(label) != 1 or label not in other_text for label in LABELS) or len(spans) != 2 or len(rectangles) != 2:
        raise ValueError('Missing, duplicated or reordered chart point labels or bars')
    if any((right - left > bottom - top) != (kind == 'bar') for left, top, right, bottom in rectangles):
        raise ValueError('Reference or actual chart orientation changed')
    if [span['text'] for span in spans] != list(LABELS):
        raise ValueError('Point-label reading order changed')
    contained = []
    for span in spans:
        x0, y0, x1, y1 = span['bbox']
        contained.append(any(x0 >= a - 0.1 and y0 >= b - 0.1 and x1 <= c + 0.1 and y1 <= d + 0.1 for a, b, c, d in rectangles))
        if require_containment and not contained[-1]:
            raise ValueError('Point label lies outside its bar')
    return spans, contained


def svg(path):
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


def xps(path):
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


def check(kind, source, saved, actual, xps_path, svg_path, reference, diagnostics):
    source_graph = graph(source, kind)
    if source_graph != graph(saved, kind):
        raise ValueError('Editable graph or workbook bytes changed')
    actual_labels, actual_contained = pdf(actual, True, kind)
    reference_labels, reference_contained = pdf(reference, False, kind)
    if any(span['color'] != 0xFFFFFF for span in actual_labels):
        raise ValueError('Point-label contrast color changed')
    svg_labels, xps_labels = svg(svg_path), xps(xps_path)
    def difference(first, second):
        return max(abs(x - y) for x, y in zip(first, second))
    svg_delta = max(difference(span['origin'], svg_labels[span['text']]) for span in actual_labels)
    xps_delta = max(difference(span['origin'], xps_labels[span['text']]) for span in actual_labels)
    if svg_delta > 0.1 or xps_delta > 0.1:
        raise ValueError('PDF, SVG and XPS label origins differ')
    reference_delta = max(difference(a['origin'], b['origin']) for a, b in zip(actual_labels, reference_labels))
    if reference_delta > 0.5 and 'Chart.DataLabelApproximation' not in Path(diagnostics).read_text().splitlines():
        raise ValueError('Missing label reference-difference diagnostic')
    return {'kind': kind, 'graph': source_graph, 'labelOrder': list(LABELS), 'pdfSvgOriginMaxDeltaPt': svg_delta,
            'pdfXpsOriginMaxDeltaPt': xps_delta, 'referenceOriginMaxDeltaPt': reference_delta,
            'geometryTolerancePt': .5, 'referenceFidelityPassed': reference_delta <= .5,
            'actualLabelsContained': actual_contained, 'referenceLabelsContained': reference_contained}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('kind', choices=('bar', 'column'))
    for name in ('source', 'saved', 'actual', 'xps', 'svg', 'reference', 'diagnostics'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    print(json.dumps(check(args.kind, args.source, args.saved, args.actual, args.xps, args.svg, args.reference, args.diagnostics)))


if __name__ == '__main__':
    main()
