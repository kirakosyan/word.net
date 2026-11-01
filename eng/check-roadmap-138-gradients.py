"""Check editable gradient stops, vector resources and independently rendered colors."""
import argparse
import json
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

import pypdf
from pypdf.generic import ContentStream
import pymupdf
from PIL import Image

A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'
WPS = '{http://schemas.microsoft.com/office/word/2010/wordprocessingShape}'
SVG = '{http://www.w3.org/2000/svg}'
XPS = '{http://schemas.microsoft.com/xps/2005/06}'
ENDS = {0: (100, 140, 240, 140), 5400000: (170, 100, 170, 180),
        10800000: (240, 140, 100, 140), 16200000: (170, 180, 170, 100)}
COLORS = ('FF0000', '00FF00', '0000FF')
POSITIONS = ('20000', '50000', '80000')


def close(actual, expected, tolerance=0.0001):
    return len(actual) == len(expected) and all(abs(float(a) - float(b)) <= tolerance for a, b in zip(actual, expected))


def document(path, angle, themed, geometry):
    with ZipFile(path) as package:
        xml = ET.fromstring(package.read('word/document.xml'))
        if themed:
            theme = ET.fromstring(package.read('word/theme/theme1.xml'))
            accent = theme.find('.//' + A + 'clrScheme/' + A + 'accent1/' + A + 'srgbClr')
            if accent is None or accent.get('val') != '4F81BD':
                raise ValueError('The retained local theme color changed.')
    shapes = xml.findall('.//' + WPS + 'spPr'); gradients = xml.findall('.//' + A + 'gradFill')
    if len(shapes) != 1 or shapes[0].find(A + 'prstGeom').get('prst') != geometry or len(gradients) != 1:
        raise ValueError('Editable shape geometry or gradient changed.')
    gradient = gradients[0]; stops = gradient.findall(A + 'gsLst/' + A + 'gs')
    expected = (('schemeClr', 'accent1'), ('srgbClr', '00FF00'), ('srgbClr', '0000FF')) if themed else tuple(('srgbClr', color) for color in COLORS)
    if (tuple(stop.get('pos') for stop in stops) != POSITIONS
            or tuple((list(stop)[0].tag, list(stop)[0].get('val')) for stop in stops) != tuple((A + kind, value) for kind, value in expected)
            or gradient.find(A + 'lin').get('ang') != str(angle)
            or gradient.find(A + 'lin').get('scaled') not in ('1', 'true')):
        raise ValueError('Editable gradient angle, scaling or stops changed.')


def inspect_pdf(path, angle, themed):
    reader = pypdf.PdfReader(path)
    if len(reader.pages) != 1:
        raise ValueError('Expected one PDF page.')
    page = reader.pages[0]; resources = page['/Resources']
    shades = resources['/Shading']
    if len(shades) != 1 or '/WNGrad0' not in shades:
        raise ValueError('Expected one native PDF shading resource.')
    shade = shades['/WNGrad0'].get_object(); x1, y1, x2, y2 = ENDS[angle]
    if shade['/ShadingType'] != 2 or shade['/ColorSpace'] != '/DeviceRGB' or not close(shade['/Coords'], (x1, 792 - y1, x2, 792 - y2)):
        raise ValueError('PDF axial shading coordinates or colorspace changed.')
    if not all(shade['/Extend']):
        raise ValueError('PDF endpoints must extend.')
    function = shade['/Function']; children = function['/Functions']
    if function['/FunctionType'] != 3 or not close(function['/Bounds'], (.2, .5, .8)) or not close(function['/Encode'], (0, 1) * 4) or len(children) != 4:
        raise ValueError('PDF stitching function does not retain all stops.')
    first = (79 / 255, 129 / 255, 189 / 255) if themed else (1, 0, 0)
    expected = ((first, first), (first, (0, 1, 0)),
                ((0, 1, 0), (0, 0, 1)), ((0, 0, 1), (0, 0, 1)))
    for child, (first, last) in zip(children, expected):
        child = child.get_object()
        if child['/FunctionType'] != 2 or float(child['/N']) != 1 or not close(child['/C0'], first) or not close(child['/C1'], last):
            raise ValueError('PDF color interpolation changed.')
    operators = [(operands, operator) for operands, operator in ContentStream(page.get_contents(), reader).operations]
    uses = [i for i, (operands, operator) in enumerate(operators) if operator == b'sh' and operands == ['/WNGrad0']]
    if len(uses) != 1 or [operator for _, operator in operators[uses[0] - 2:uses[0]]] != [b'W', b'n']:
        raise ValueError('PDF shading is not clipped to the vector path.')
    if page.images:
        raise ValueError('The gradient was replaced by a PDF image.')


def inspect_svg(path, angle, themed):
    xml = ET.parse(path); gradients = list(xml.iter(SVG + 'linearGradient'))
    if len(gradients) != 1:
        raise ValueError('SVG gradient missing or ambiguous.')
    gradient = gradients[0]
    if (gradient.get('gradientUnits') != 'userSpaceOnUse' or gradient.get('spreadMethod') != 'pad'
            or not close([gradient.get(k) for k in ('x1', 'y1', 'x2', 'y2')], ENDS[angle])
            or tuple(stop.get('offset') for stop in gradient.findall(SVG + 'stop')) != ('0.2', '0.5', '0.8')
            or tuple(stop.get('stop-color') for stop in gradient.findall(SVG + 'stop')) != tuple('#' + c for c in (('4F81BD', *COLORS[1:]) if themed else COLORS))):
        raise ValueError('SVG gradient coordinates or stops changed.')
    if len([path for path in xml.iter(SVG + 'path') if path.get('fill') == 'url(#' + gradient.get('id') + ')']) != 1:
        raise ValueError('SVG gradient does not paint exactly one vector path.')


def inspect_xps(path, angle, themed):
    with ZipFile(path) as archive:
        names = [name for name in archive.namelist() if name.endswith('.fpage')]
        if len(names) != 1:
            raise ValueError('Expected one XPS page.')
        xml = ET.fromstring(archive.read(names[0]))
    brushes = list(xml.iter(XPS + 'LinearGradientBrush'))
    if len(brushes) != 1:
        raise ValueError('XPS gradient missing or ambiguous.')
    brush = brushes[0]; coords = (brush.get('StartPoint') + ',' + brush.get('EndPoint')).split(',')
    stops = brush.findall(XPS + 'LinearGradientBrush.GradientStops/' + XPS + 'GradientStop')
    if (brush.get('MappingMode') != 'Absolute' or brush.get('SpreadMethod') != 'Pad'
            or brush.get('ColorInterpolationMode') != 'SRgbLinearInterpolation'
            or not close([float(v) * .75 for v in coords], ENDS[angle], .0001)
            or tuple(s.get('Offset') for s in stops) != ('0.2', '0.5', '0.8')
            or tuple(s.get('Color') for s in stops) != tuple('#FF' + c for c in (('4F81BD', *COLORS[1:]) if themed else COLORS))):
        raise ValueError('XPS gradient coordinates or stops changed.')


def compare_raster(actual, reference, xps, svg_png, angle, geometry):
    points = ([(140, 140), (155, 140), (170, 140), (185, 140), (200, 140)] if geometry == 'triangle' else
              [(110, 140), (135, 140), (170, 140), (205, 140), (230, 140)]) if angle % 10800000 == 0 else [(170, 105), (170, 120), (170, 140), (170, 160), (170, 175)]
    def pixels(path, pdf):
        page = pymupdf.open(path)[0]
        if pdf and page.get_images():
            raise ValueError('A gradient specimen contains a raster image.')
        image = page.get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False)
        if geometry != 'rect' and image.pixel(202, 202) != (255, 255, 255):
            raise ValueError('The gradient extends outside its clipped shape.')
        return [image.pixel(x * 2, y * 2) for x, y in points]
    own, other, xps_colors = pixels(actual, True), pixels(reference, True), pixels(xps, False)
    difference = max(abs(a - b) for first, second in zip(own, other) for a, b in zip(first, second))
    xps_difference = max(abs(a - b) for first, second in zip(xps_colors, other) for a, b in zip(first, second))
    if difference > 8 or xps_difference > 8:
        raise ValueError('The sampled RGB channel difference exceeds 8/255.')
    result = {'actualRgb': own, 'referenceRgb': other, 'xpsRgb': xps_colors, 'maxChannelDifference': difference,
              'xpsMaxChannelDifference': xps_difference, 'channelTolerance': 8, 'svgRasterChecked': False}
    if svg_png is not None:
        image = Image.open(svg_png).convert('RGB')
        if image.size != (1224, 1584) or geometry != 'rect' and image.getpixel((202, 202)) != (255, 255, 255):
            raise ValueError('SVG raster dimensions or clipping changed.')
        svg_colors = [image.getpixel((x * 2, y * 2)) for x, y in points]
        svg_difference = max(abs(a - b) for first, second in zip(svg_colors, other) for a, b in zip(first, second))
        if svg_difference > 8:
            raise ValueError('The sampled SVG RGB channel difference exceeds 8/255.')
        result.update({'svgRasterChecked': True, 'svgRgb': svg_colors, 'svgMaxChannelDifference': svg_difference})
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('angle', type=int, choices=tuple(ENDS))
    for name in ('source', 'saved', 'pdf', 'reference', 'svg', 'xps'):
        parser.add_argument(name, type=Path)
    parser.add_argument('--theme', action='store_true', help='expect the independently authored accent1 stop')
    parser.add_argument('--geometry', choices=('rect', 'ellipse', 'roundRect', 'triangle', 'diamond'), default='rect')
    parser.add_argument('--svg-png', type=Path, help='optional independent 144-DPI SVG raster')
    args = parser.parse_args()
    if pypdf.__version__ != '6.10.0' or pymupdf.VersionBind != '1.27.2.2':
        raise RuntimeError('Requires pinned pypdf and PyMuPDF versions.')
    document(args.source, args.angle, args.theme, args.geometry); document(args.saved, args.angle, args.theme, args.geometry)
    inspect_pdf(args.pdf, args.angle, args.theme); inspect_svg(args.svg, args.angle, args.theme); inspect_xps(args.xps, args.angle, args.theme)
    print(json.dumps({'angle': args.angle, 'geometry': args.geometry, 'localThemeStop': args.theme, 'editableStops': True, 'pdfVectorShading': True,
                      'svgAndXpsVectorBrushes': True, 'raster': compare_raster(args.pdf, args.reference, args.xps, args.svg_png, args.angle, args.geometry)}))


if __name__ == '__main__':
    main()
