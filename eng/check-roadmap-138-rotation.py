"""Independently inspect rotated box geometry, editable content and reference differences."""
import argparse
import base64
import hashlib
import json
import math
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

import pymupdf

A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'
W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
WPS = '{http://schemas.microsoft.com/office/word/2010/wordprocessingShape}'
R = '{http://schemas.openxmlformats.org/officeDocument/2006/relationships}'
REL = '{http://schemas.openxmlformats.org/package/2006/relationships}'
SVG = '{http://www.w3.org/2000/svg}'
XLINK = '{http://www.w3.org/1999/xlink}'
URL = 'https://example.test/callout'


def digest(data):
    return hashlib.sha256(data).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def document(path, raw, linked):
    with ZipFile(path) as package:
        xml = ET.fromstring(package.read('word/document.xml'))
        shape = xml.find('.//' + WPS + 'spPr')
        require(shape is not None and shape.find(A + 'xfrm').get('rot') == str(raw), 'Editable angle changed.')
        require(shape.find(A + 'prstGeom').get('prst') == 'rect', 'Editable rectangle changed.')
        content = xml.find('.//' + W + 'txbxContent')
        require(content is not None and ''.join(e.text or '' for e in content.iter(W + 't')) == ('Linked' if linked else 'Rotate'), 'Editable box text changed.')
        images = [package.read(n) for n in package.namelist() if n.startswith('word/media/') and n.lower().endswith('.png')]
        links = list(content.iter(W + 'hyperlink'))
        if linked:
            require(len(images) == len(links) == 1, 'Editable image or link is missing.')
            rels = ET.fromstring(package.read('word/_rels/document.xml.rels'))
            require(any(r.get('Id') == links[0].get(R + 'id') and r.get('Target') == URL and r.get('TargetMode') == 'External' for r in rels.iter(REL + 'Relationship')), 'Editable link target changed.')
        else:
            require(not images and not links, 'Unexpected editable image or link.')
        return images


def shape_bounds(page):
    shapes = [drawing for drawing in page.get_drawings() if drawing.get('fill') == (0, 1, 1)]
    require(len(shapes) == 1, 'Expected exactly one cyan vector frame.')
    return tuple(shapes[0]['rect'])


def line(page, text):
    lines = [line for block in page.get_text('dict')['blocks'] for line in block.get('lines', [])
             if ''.join(span['text'] for span in line['spans']) == text]
    require(len(lines) == 1, 'Expected selectable box text.')
    return lines[0]


def check(raw, source, saved, pdf, xps, svg, diagnostic, reference, linked, saved_again=None):
    angle = raw / 60000; radians = math.radians(angle); c, s = math.cos(radians), math.sin(radians)
    half_w, half_h = (abs(c) * 140 + abs(s) * 80) / 2, (abs(s) * 140 + abs(c) * 80) / 2
    expected = (170 - half_w, 140 - half_h, 170 + half_w, 140 + half_h)
    original_images = document(source, raw, linked)
    require(document(saved, raw, linked) == original_images, 'Editable image bytes changed.')
    if saved_again is not None:
        require(document(saved_again, raw, linked) == original_images, 'Repeated save changed editable image bytes.')
    docs = {name: pymupdf.open(path) for name, path in (('pdf', pdf), ('xps', xps), ('svg', svg), ('reference', reference))}
    require(all(len(doc) == 1 for doc in docs.values()), 'Expected one page per output.')
    bounds = {name: shape_bounds(doc[0]) for name, doc in docs.items()}
    deviations = {name: max(abs(x - y) for x, y in zip(bound, expected)) for name, bound in bounds.items()}
    require(max(deviations.values()) <= .5, 'Vector frame differs from analytic geometry by more than 0.5 pt.')
    points = [(110, 170), (170, 170), (230, 170), (95, 185), (95, 140)]
    page_points = [(170 + c * (x - 170) - s * (y - 140), 140 + s * (x - 170) + c * (y - 140)) for x, y in points]
    raster = {}
    for name, doc in docs.items():
        bitmap = doc[0].get_pixmap(matrix=pymupdf.Matrix(2, 2), alpha=False)
        raster[name] = [bitmap.pixel(round(2 * x), round(2 * y)) for x, y in page_points]
        expected_colors = [(0, 255, 255)] * 3 + [(255, 255, 255)] * 2
        require(all(abs(channel - expected_channel) <= 8 for actual, target in zip(raster[name], expected_colors)
                    for channel, expected_channel in zip(actual, target)), 'Rasterized frame or clipping changed.')
    text = 'Linked' if linked else 'Rotate'
    actual_line, reference_line = line(docs['pdf'][0], text), line(docs['reference'][0], text)
    actual_angle = math.degrees(math.atan2(actual_line['dir'][1], actual_line['dir'][0])) % 360
    reference_angle = math.degrees(math.atan2(reference_line['dir'][1], reference_line['dir'][0])) % 360
    angle_error = abs((actual_angle - angle + 180) % 360 - 180)
    require(angle_error < .02, 'Selectable PDF text direction does not match the requested rotation.')
    origin = actual_line['spans'][0]['origin']; reference_origin = reference_line['spans'][0]['origin']
    origin_difference = math.dist(origin, reference_origin)
    reference_angle_difference = abs((actual_angle - reference_angle + 180) % 360 - 180)
    reference_passed = origin_difference <= .5 and reference_angle_difference <= .02
    codes = set(diagnostic.read_text().splitlines())
    if not reference_passed:
        require('Shape.RotationReference' in codes, 'Missing reference-disagreement diagnostic.')
    links = docs['pdf'][0].get_links(); reference_links = docs['reference'][0].get_links()
    if linked:
        require(len(links) == len(reference_links) == 1 and links[0].get('uri') == reference_links[0].get('uri') == URL, 'PDF or reference link missing or changed.')
        bbox = actual_line['bbox']; target = links[0]['from']
        require(target.x0 <= bbox[0] + 2 and target.y0 <= bbox[1] + 2 and target.x1 >= bbox[2] - 2 and target.y1 >= bbox[3] - 2, 'PDF link does not cover its rotated text.')
        require(('Shape.RotatedLinkBounds' in codes) == (raw % 5400000 != 0), 'Incorrect non-cardinal link-bounds diagnostic.')
        info = docs['pdf'][0].get_images(); require(len(info) == 1, 'PDF image is missing.')
        pixels = pymupdf.Pixmap(docs['pdf'], info[0][0])
        require((pixels.width, pixels.height, pixels.pixel(0, 0), pixels.pixel(7, 0)) == (8, 4, (220, 40, 40), (40, 70, 220)), 'PDF image pixels changed.')
        require(len(docs['xps'][0].get_image_info()) == 1, 'XPS image is missing.')
        with ZipFile(xps) as package:
            images = [package.read(n) for n in package.namelist() if n.lower().endswith('.png')]
            require(images == original_images, 'XPS image bytes changed.')
        elements = list(ET.parse(svg).iter(SVG + 'image'))
        require(len(elements) == 1 and base64.b64decode(elements[0].get(XLINK + 'href').split(',', 1)[1]) == original_images[0], 'SVG image bytes changed.')
    else:
        require(not links and not reference_links, 'Unexpected PDF link.')
        require(not docs['pdf'][0].get_images() and not docs['xps'][0].get_image_info(), 'Unexpected rasterized frame.')
    return {'rawAngle': raw, 'linkedImage': linked, 'repeatedSaveChecked': saved_again is not None,
            'editableImageSha256': digest(original_images[0]) if linked else None,
            'shapeBounds': bounds, 'shapeMaxDeviationPt': deviations, 'geometryTolerancePt': .5,
            'rasterSamples144Dpi': raster, 'rasterChannelTolerance': 8,
            'textDirectionDegrees': actual_angle, 'referenceDirectionDegrees': reference_angle,
            'textOrigin': origin, 'referenceTextOrigin': reference_origin, 'textOriginDifferencePt': origin_difference,
            'referenceTextFidelityPassed': reference_passed, 'referenceTextTolerancePt': .5,
            'referenceTextAngleDifferenceDegrees': reference_angle_difference,
            'pdfLinkBounds': list(links[0]['from']) if linked else None,
            'referenceLinkBounds': list(reference_links[0]['from']) if linked else None,
            'diagnostics': sorted(codes)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('raw_angle', type=int)
    for name in ('source', 'saved', 'pdf', 'xps', 'svg', 'diagnostic', 'reference'):
        parser.add_argument(name, type=Path)
    parser.add_argument('--linked', action='store_true')
    parser.add_argument('--saved-again', type=Path)
    args = parser.parse_args()
    if pymupdf.VersionBind != '1.27.2.2':
        raise RuntimeError('Requires PyMuPDF 1.27.2.2.')
    print(json.dumps(check(args.raw_angle, args.source, args.saved, args.pdf, args.xps, args.svg, args.diagnostic, args.reference, args.linked, args.saved_again)))


if __name__ == '__main__':
    main()
