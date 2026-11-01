"""Inspect justified hyphenation and retain the independent geometry difference."""
import argparse
import json
import subprocess
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

import pypdf
import pymupdf
import pyphen

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
EXPECTED = 'A red extraordinary example'


def words(page):
    return {item[4]: item for item in page.get_text('words')}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('actual', type=Path); parser.add_argument('left', type=Path)
    parser.add_argument('reference_source', type=Path); parser.add_argument('reference_pdf', type=Path)
    args = parser.parse_args()
    if pypdf.__version__ != '6.10.0' or pymupdf.VersionBind != '1.27.2.2' or pyphen.__version__ != '0.17.2':
        raise RuntimeError('Requires pinned pypdf, PyMuPDF and Pyphen versions.')
    with ZipFile(args.reference_source) as package:
        xml = ET.fromstring(package.read('word/document.xml'))
    source = ''.join(element.text or '' for element in xml.iter(W + 't'))
    if source != 'A red ex\u00adtra\u00ador\u00addi\u00adnary example':
        raise ValueError('The independently authored soft-hyphen reference changed.')
    if pyphen.Pyphen(lang='en_US').positions('extraordinary') != [2, 5, 7, 9]:
        raise ValueError('Independent dictionary points changed.')
    actual = pymupdf.open(args.actual); left = pymupdf.open(args.left); reference = pymupdf.open(args.reference_pdf)
    if len(actual) != 1 or len(left) != 1 or len(reference) != 1:
        raise ValueError('Expected one physical page in each specimen.')
    for path, pdf in ((args.actual, actual), (args.left, left)):
        plain = pypdf.PdfReader(path).pages[0].extract_text()
        poppler = subprocess.run(['pdftotext', '-raw', str(path), '-'], capture_output=True, check=True).stdout.decode('utf-8')
        if any(' '.join(value.split()) != EXPECTED or '-' in value for value in (plain, poppler, pdf[0].get_text())):
            raise ValueError('Searchable source text changed or includes the display hyphen.')
    a, l, r = words(actual[0]), words(left[0]), words(reference[0])
    if not all(key in a and key in l for key in ('A', 'red', 'extraordinary', 'example')) or 'extraordi\u00ad' not in r or 'nary' not in r or 'example' not in r:
        raise ValueError('The expected justified and reference line content changed.')
    if not any('NotoSans-Regular' in font[3] for font in reference[0].get_fonts()):
        raise ValueError('The independent reference did not embed the selected font.')
    shift = a['extraordinary'][0] - l['extraordinary'][0]
    if shift <= 1 or abs(a['extraordinary'][2] - 120) > 0.1:
        raise ValueError('The justified word did not move to the expected line end.')
    origins = {key: abs(a[key][0] - r['extraordi\u00ad' if key == 'extraordinary' else key][0]) for key in ('A', 'red', 'extraordinary', 'example')}
    next_line_difference = abs(a['example'][1] - r['example'][1]); tolerance = 0.5
    if max([*origins.values(), next_line_difference]) <= tolerance:
        raise ValueError('The retained original-tolerance geometry failure unexpectedly disappeared.')
    print(json.dumps({'logicalText': EXPECTED, 'visibleBreak': 9, 'justificationShiftPt': shift,
                      'originDifferencesPt': origins, 'followingLineTopDifferencePt': next_line_difference,
                      'tolerancePt': tolerance, 'referenceGeometryPassed': False}))


if __name__ == '__main__':
    main()
