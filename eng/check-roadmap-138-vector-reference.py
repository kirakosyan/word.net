"""Compare independently authored editable vector anchors and PDF geometry."""
import argparse
import json
import math
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

import pymupdf

A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'
WP = '{http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing}'


def owner(path):
    with ZipFile(path) as archive:
        document = ET.fromstring(archive.read('word/document.xml'))
    anchor = document.find('.//' + WP + 'anchor')
    if anchor is None or anchor.find(WP + 'wrapTopAndBottom') is None:
        raise ValueError('The source-anchor wrapping changed.')
    vertical = anchor.find(WP + 'positionV')
    if vertical.get('relativeFrom') != 'paragraph' or vertical.find(WP + 'posOffset').text != '0':
        raise ValueError('The source paragraph anchor changed.')
    shape = anchor.find('.//' + A + 'prstGeom')
    description = anchor.find(WP + 'docPr').get('descr')
    if shape.get('prst') != 'triangle' or description != 'Blue trend diagram':
        raise ValueError('The editable shape or description changed.')


def polygon(path):
    pdf = pymupdf.open(path)
    if len(pdf) != 1 or pdf[0].get_images():
        raise ValueError('Expected one page of vector content without raster images.')
    shapes = [item for item in pdf[0].get_drawings() if item['type'] == 'f' and item.get('fill') is not None
              and all(abs(item['fill'][i] - channel / 255) < 0.001 for i, channel in enumerate((36, 104, 172)))]
    if len(shapes) != 1:
        raise ValueError('Expected one colored vector figure.')
    points = []
    for command in shapes[0]['items']:
        if command[0] != 'l':
            raise ValueError('Expected a polygon without curves.')
        points.extend([(command[1].x, command[1].y), (command[2].x, command[2].y)])
    return points, tuple(shapes[0]['rect'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('saved', type=Path)
    parser.add_argument('actual', type=Path)
    parser.add_argument('reference', type=Path)
    args = parser.parse_args()
    if pymupdf.VersionBind != '1.27.2.2':
        raise RuntimeError('This checker requires PyMuPDF 1.27.2.2.')
    owner(args.source); owner(args.saved)
    actual, actual_bounds = polygon(args.actual)
    reference, reference_bounds = polygon(args.reference)
    difference = max(max(min(math.dist(p, q) for q in reference) for p in actual),
                     max(min(math.dist(p, q) for q in actual) for p in reference))
    if difference > 0.5:
        raise ValueError('The vector reference exceeds the original 0.5-point geometry tolerance.')
    print(json.dumps({'actualBoundsPt': actual_bounds, 'referenceBoundsPt': reference_bounds,
                      'maximumVertexDifferencePt': difference, 'tolerancePt': 0.5, 'passed': True}))


if __name__ == '__main__':
    main()
