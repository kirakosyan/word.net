"""Retain the independent remapped-theme color failure under its original bound."""
import argparse
import json
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

from pypdf import PdfReader
import pymupdf
from PIL import Image

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'
SVG = '{http://www.w3.org/2000/svg}'


def docx(path):
    with ZipFile(path) as package:
        document = ET.fromstring(package.read('word/document.xml'))
        settings = ET.fromstring(package.read('word/settings.xml'))
    if document.find('.//' + A + 'gradFill/' + A + 'gsLst/' + A + 'gs/' + A + 'schemeClr').get('val') != 'accent1':
        raise ValueError('Editable theme stop changed.')
    if settings.find('.//' + W + 'clrSchemeMapping').get(W + 'accent1') != 'accent2':
        raise ValueError('Editable theme mapping changed.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('source', 'saved', 'pdf', 'reference', 'svg', 'xps', 'diagnostics'):
        parser.add_argument(name, type=Path)
    parser.add_argument('--svg-png', type=Path)
    args = parser.parse_args()
    docx(args.source); docx(args.saved)
    if 'Shape.GradientThemeMapping' not in args.diagnostics.read_text().splitlines():
        raise ValueError('The remapped-theme reference loss was not diagnosed.')
    shade = PdfReader(args.pdf).pages[0]['/Resources']['/Shading']['/WNGrad0']
    first = shade['/Function']['/Functions'][0]['/C0']
    expected = (192 / 255, 80 / 255, 77 / 255)
    if any(abs(float(a) - b) > .0001 for a, b in zip(first, expected)):
        raise ValueError('The mapped PDF color changed.')
    svg = ET.parse(args.svg); stop = next(svg.iter(SVG + 'stop'))
    if stop.get('stop-color') != '#C0504D':
        raise ValueError('The mapped SVG stop changed.')
    def sample(path):
        page = pymupdf.open(path)[0]; return page.get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False).pixel(220, 280)
    actual, reference, xps = sample(args.pdf), sample(args.reference), sample(args.xps)
    if max(abs(a - b) for a, b in zip(actual, (192, 80, 77))) > 2 or max(abs(a - b) for a, b in zip(xps, actual)) > 2:
        raise ValueError('Mapped PDF or XPS output changed.')
    difference = max(abs(a - b) for a, b in zip(actual, reference)); tolerance = 8
    if difference <= tolerance:
        raise ValueError('The retained remapping reference failure unexpectedly disappeared.')
    svg_color = None
    if args.svg_png is not None:
        image = Image.open(args.svg_png).convert('RGB')
        if image.size != (1224, 1584):
            raise ValueError('The SVG raster size changed.')
        svg_color = image.getpixel((220, 280))
        if max(abs(a - b) for a, b in zip(svg_color, actual)) > 2:
            raise ValueError('Mapped SVG output changed.')
    print(json.dumps({'actualRgb': actual, 'xpsRgb': xps, 'svgRgb': svg_color, 'referenceRgb': reference,
                      'maxChannelDifference': difference, 'channelTolerance': tolerance, 'referenceColorPassed': False,
                      'diagnostic': 'Shape.GradientThemeMapping'}))


if __name__ == '__main__':
    main()
