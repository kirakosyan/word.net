"""Independently inspect editable presets and their PDF, XPS and SVG geometry."""
import argparse
import json
import math
from pathlib import Path
import re
from xml.etree import ElementTree as ET
from zipfile import ZipFile

import pymupdf

A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'
WP = '{http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing}'
SVG = '{http://www.w3.org/2000/svg}'
XPS = '{http://schemas.microsoft.com/xps/2005/06}'
EXPECTED = {
    'rect': [(100, 100), (240, 100), (240, 180), (100, 180)],
    'triangle': [(170, 100), (240, 180), (100, 180)],
    'diamond': [(170, 100), (240, 140), (170, 180), (100, 140)],
}


def inspect_docx(path, kind):
    with ZipFile(path) as archive:
        document = ET.fromstring(archive.read('word/document.xml'))
    geometries = document.findall('.//' + A + 'prstGeom')
    extents = document.findall('.//' + WP + 'anchor/' + WP + 'extent')
    if len(geometries) != 1 or geometries[0].get('prst') != kind or len(extents) != 1:
        raise ValueError('The editable preset or its owner changed.')
    if (extents[0].get('cx'), extents[0].get('cy')) != ('1778000', '1016000'):
        raise ValueError('The editable preset extent changed.')


def point_segment(point, start, end):
    px, py = point; ax, ay = start; bx, by = end
    dx, dy = bx - ax, by - ay
    amount = max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - ax - amount * dx, py - ay - amount * dy)


def pdf_geometry(path, expected, label):
    document = pymupdf.open(path)
    if len(document) != 1 or document[0].get_images():
        raise ValueError(label + ' must be one page of vectors without images.')
    matches = [drawing for drawing in document[0].get_drawings()
               if drawing['type'] == 'f' and drawing.get('fill') is not None
               and all(abs(drawing['fill'][i] - value / 255) < 0.001 for i, value in enumerate((36, 104, 172)))]
    if len(matches) != 1:
        raise ValueError(label + ' must contain one correctly colored preset.')
    points = []
    for item in matches[0]['items']:
        if item[0] == 'l':
            points.extend([(item[1].x, item[1].y), (item[2].x, item[2].y)])
        elif item[0] == 're':
            rectangle = item[1]
            points.extend([(rectangle.x0, rectangle.y0), (rectangle.x1, rectangle.y0),
                           (rectangle.x1, rectangle.y1), (rectangle.x0, rectangle.y1)])
        else:
            raise ValueError(label + ' contains an unexpected non-polygon path.')
    tolerance = 0.5
    vertex_error = max(min(math.dist(vertex, point) for point in points) for vertex in expected)
    edges = list(zip(expected, expected[1:] + expected[:1]))
    edge_error = max(min(point_segment(point, start, end) for start, end in edges) for point in points)
    if vertex_error > tolerance or edge_error > tolerance:
        raise ValueError(label + ' vertex or edge exceeds the original 0.5-point tolerance.')
    return {'vertexErrorPt': vertex_error, 'edgeErrorPt': edge_error,
            'boundsPt': [matches[0]['rect'].x0, matches[0]['rect'].y0, matches[0]['rect'].x1, matches[0]['rect'].y1]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('kind', choices=tuple(EXPECTED))
    parser.add_argument('source', type=Path)
    parser.add_argument('saved', type=Path)
    parser.add_argument('actual', type=Path)
    parser.add_argument('reference', type=Path)
    parser.add_argument('svg', type=Path)
    parser.add_argument('xps', type=Path)
    args = parser.parse_args()
    if pymupdf.VersionBind != '1.27.2.2':
        raise RuntimeError('This checker requires PyMuPDF 1.27.2.2.')
    inspect_docx(args.source, args.kind); inspect_docx(args.saved, args.kind)
    actual = pdf_geometry(args.actual, EXPECTED[args.kind], 'native PDF')
    reference = pdf_geometry(args.reference, EXPECTED[args.kind], 'reference PDF')
    svg = ET.parse(args.svg)
    paths = [p for p in svg.iter(SVG + 'path') if p.get('fill') == '#2468AC']
    expected_path = ' '.join(('M' if index == 0 else 'L') + f' {x} {y}' for index, (x, y) in enumerate(EXPECTED[args.kind])) + ' Z'
    if len(paths) != 1 or paths[0].get('d') != expected_path:
        raise ValueError('SVG preset vertices changed.')
    with ZipFile(args.xps) as archive:
        pages = [name for name in archive.namelist() if name.endswith('.fpage')]
        if len(pages) != 1:
            raise ValueError('Expected one XPS page.')
        page = ET.fromstring(archive.read(pages[0]))
    paths = [p for p in page.iter(XPS + 'Path') if (p.get('Fill') or '').upper() == '#FF2468AC']
    if len(paths) != 1:
        raise ValueError('XPS vector fill is absent or ambiguous.')
    data = paths[0].get('Data', '')
    numbers = re.findall(r'([ML])\s+(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)', data)
    if len(numbers) != len(EXPECTED[args.kind]) or [n[0] for n in numbers] != ['M'] + ['L'] * (len(numbers) - 1) or not data.endswith(' Z'):
        raise ValueError('XPS polygon commands changed.')
    xps_error = max(math.dist((float(x) * 0.75, float(y) * 0.75), expected)
                    for (_, x, y), expected in zip(numbers, EXPECTED[args.kind]))
    if xps_error > 0.5:
        raise ValueError('XPS polygon vertices exceed the original 0.5-point tolerance.')
    print(json.dumps({'kind': args.kind, 'tolerancePt': 0.5, 'native': actual, 'reference': reference,
                      'editablePreset': True, 'svgVertices': True, 'xpsVertexErrorPt': xps_error}))


if __name__ == '__main__':
    main()
