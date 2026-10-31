"""Inspect retained OMML, independent text order and measured fraction geometry."""
import argparse
import json
from pathlib import Path
import subprocess
from xml.etree import ElementTree as ET
from zipfile import ZipFile

import pymupdf

M = '{http://schemas.openxmlformats.org/officeDocument/2006/math}'
H = '{http://www.w3.org/1999/xhtml}'


def fraction(path):
    with ZipFile(path) as document:
        root = ET.fromstring(document.read('word/document.xml'))
    math = root.findall('.//' + M + 'oMath')
    if len(math) != 1:
        raise ValueError('Expected one editable equation.')
    fractions = math[0].findall('.//' + M + 'f')
    if len(fractions) != 1:
        raise ValueError('Expected one editable fraction.')
    node = fractions[0]
    return tuple(''.join(item.text or '' for item in node.find(M + part).iter(M + 't')) for part in ('num', 'den'))


def words(path):
    raw = subprocess.check_output(['pdftotext', '-bbox', str(path), '-'])
    return {node.text: {key: float(node.get(key)) for key in ('xMin', 'yMin', 'xMax', 'yMax')}
            for node in ET.fromstring(raw).iter(H + 'word')}


def rule(path, top, bottom):
    document = pymupdf.open(path)
    if len(document) != 1:
        raise ValueError('Expected one physical PDF page.')
    candidates = [drawing['rect'] for drawing in document[0].get_drawings()
                  if drawing['type'] == 'f' and len(drawing['items']) == 1 and drawing['items'][0][0] == 're'
                  and top < drawing['rect'].y0 < bottom and 1 < drawing['rect'].width < 30
                  and 0.05 < drawing['rect'].height < 2]
    if len(candidates) != 1:
        raise ValueError('Expected one visible fraction rule between the paragraphs.')
    rect = candidates[0]
    return {'x': rect.x0, 'y': rect.y0, 'width': rect.width, 'height': rect.height}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('saved', type=Path)
    parser.add_argument('actual', type=Path)
    parser.add_argument('reference', type=Path)
    args = parser.parse_args()
    if pymupdf.VersionBind != '1.27.2.2':
        raise RuntimeError('This checker requires PyMuPDF 1.27.2.2.')
    if fraction(args.source) != ('x', '2') or fraction(args.saved) != ('x', '2'):
        raise ValueError('The editable fraction changed.')
    actual, reference = words(args.actual), words(args.reference)
    if not {'Before', '(x)/(2)', 'After'} <= actual.keys() or not {'Before', 'x', '2', 'After'} <= reference.keys():
        raise ValueError('Missing expected source or reference text.')
    if not (actual['Before']['yMin'] < actual['(x)/(2)']['yMin'] < actual['After']['yMin']
            and reference['Before']['yMin'] < reference['x']['yMin'] < reference['2']['yMin'] < reference['After']['yMin']):
        raise ValueError('Native or reference text order does not match the source.')
    a = rule(args.actual, actual['Before']['yMax'], actual['After']['yMin'])
    b = rule(args.reference, reference['Before']['yMax'], reference['After']['yMin'])
    differences = {name: abs(a[name] - b[name]) for name in ('x', 'y', 'width', 'height')}
    tolerance = 0.5
    if differences['width'] <= tolerance or differences['y'] <= tolerance:
        raise ValueError('The recorded reference-geometry failure changed; review the observation.')
    print(json.dumps({'actualRulePt': a, 'referenceRulePt': b, 'differencePt': differences,
                      'afterTopDifferencePt': abs(actual['After']['yMin'] - reference['After']['yMin']),
                      'tolerancePt': tolerance, 'geometryPassed': False,
                      'referenceMathFont': 'DejaVu Serif', 'nativeMathFont': 'STIX Two Math',
                      'qualification': 'Different math fonts are an explicit limitation of this reference comparison.'}))


if __name__ == '__main__':
    main()
