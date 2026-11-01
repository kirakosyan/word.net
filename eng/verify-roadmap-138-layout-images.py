#!/usr/bin/env python3
"""Check inline-image source identity and frame geometry with independent readers."""
import argparse
import base64
import copy
import hashlib
from io import BytesIO
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
from zipfile import ZipFile

import fitz
from PIL import Image

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
WP = '{http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing}'
A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'
R = '{http://schemas.openxmlformats.org/officeDocument/2006/relationships}'
SVG = '{http://www.w3.org/2000/svg}'
XLINK = '{http://www.w3.org/1999/xlink}'
TRANSFORM = re.compile(r'translate\(([-.0-9]+) ([-.0-9]+)\) rotate\(0\) scale\(1 1\)')


def pixels(data):
    with Image.open(BytesIO(data)) as image:
        converted = image.convert('RGB')
        return converted.size, converted.tobytes()


def read(source, output):
    with ZipFile(source) as package:
        document = ET.fromstring(package.read('word/document.xml'))
        relationships = ET.fromstring(package.read('word/_rels/document.xml.rels'))
        images = [element for element in relationships if element.get('Id') == 'image']
        if len(images) != 1 or images[0].get('Target') != 'media/grid.png' or images[0].get('TargetMode') == 'External' or not images[0].get('Type', '').endswith('/image'):
            raise ValueError('Expected one local source image relationship')
        pictures = document.findall('./' + W + 'body/' + W + 'p/' + W + 'r/' + W + 'drawing/' + WP + 'inline')
        if len(pictures) != 1:
            raise ValueError('Expected one direct inline source drawing')
        extent = pictures[0].find(WP + 'extent')
        if extent is None:
            raise ValueError('Source frame is missing')
        blips = pictures[0].findall('.//' + A + 'blip')
        if len(blips) != 1:
            raise ValueError('Expected one source image reference')
        source_embed = blips[0].get(R + 'embed')
        distances = [pictures[0].get(key) for key in ('distT', 'distB', 'distL', 'distR')]
        source_size = [int(extent.get('cx')) / 12700, int(extent.get('cy')) / 12700]
        cropped = pictures[0].find('.//' + A + 'srcRect') is not None
        encoded = package.read('word/media/grid.png')
    snapshot = json.loads((output / 'snapshot.json').read_text(encoding='utf-8'))
    with fitz.open(output / 'output.pdf') as pdf:
        if len(pdf) != 1:
            raise ValueError('Expected one PDF page')
        found = pdf[0].get_image_info(xrefs=True)
        if len(found) != 1:
            raise ValueError('Expected one PDF image')
        pdf_box = list(found[0]['bbox'])
        pdf_pixels = pixels(pdf.extract_image(found[0]['xref'])['image'])
    root = ET.parse(output / 'preview.svg').getroot()
    groups = [group for group in root.findall(SVG + 'g') if group.find('.//' + SVG + 'image') is not None]
    if len(groups) != 1:
        raise ValueError('Expected one SVG image group')
    transform = TRANSFORM.fullmatch(groups[0].get('transform', ''))
    clip = groups[0].find('./' + SVG + 'clipPath/' + SVG + 'rect')
    element = groups[0].find('.//' + SVG + 'image')
    if transform is None or clip is None or element is None:
        raise ValueError('Unexpected SVG image frame')
    center = list(map(float, transform.groups()))
    rect = [float(clip.get(key)) for key in ('x', 'y', 'width', 'height')]
    svg_box = [center[0] + rect[0], center[1] + rect[1],
               center[0] + rect[0] + rect[2], center[1] + rect[1] + rect[3]]
    href = element.get(XLINK + 'href', '')
    if not href.startswith('data:image/png;base64,'):
        raise ValueError('Expected embedded SVG PNG data')
    svg_pixels = pixels(base64.b64decode(href.split(',', 1)[1], validate=True))
    return {'sourceSize': source_size, 'cropped': cropped, 'sourceEmbed': source_embed, 'sourceDistances': distances, 'sourcePixels': pixels(encoded),
            'sourcePngSha256': hashlib.sha256(encoded).hexdigest(), 'snapshot': snapshot,
            'pdfBox': pdf_box, 'pdfPixels': pdf_pixels, 'svgBox': svg_box, 'svgPixels': svg_pixels}


def check(data, cropped, zero_dist=False):
    if data['cropped'] != cropped or len(data['snapshot']) != 1:
        raise ValueError('Unexpected source or snapshot shape')
    if data['sourceEmbed'] != 'image':
        raise ValueError('Drawing no longer references the expected local image')
    if data['sourceDistances'] != (['0'] * 4 if zero_dist else [None] * 4):
        raise ValueError('Inline distance contrast changed')
    fragment = data['snapshot'][0]
    if fragment['page'] != 1 or fragment['part'] != 'word/document.xml' or fragment['path'] != '/document[1]/body[1]/p[1]' or fragment['text'] is not None:
        raise ValueError('Paragraph identity or privacy changed')
    if fragment['cellPath'] is not None:
        raise ValueError('Body image was assigned a cell owner')
    if data['sourcePixels'] != data['pdfPixels'] or data['sourcePixels'] != data['svgPixels']:
        raise ValueError('Decoded image pixels differ')
    if cropped:
        if fragment['objectPath'] is not None or fragment['bounds'] is not None or fragment['reason'] != 'UnqualifiedOwner' or fragment['outside'] is not None:
            raise ValueError('Cropped image was assigned qualified geometry')
        return {'qualified': False, 'reason': fragment['reason']}
    if fragment['objectPart'] != 'word/document.xml' or fragment['objectPath'] != '/document[1]/body[1]/p[1]/r[1]/drawing[1]' or fragment['reason'] is not None or fragment['outside']:
        raise ValueError('Inline drawing identity or status changed')
    bounds = fragment['bounds']
    if bounds is None:
        raise ValueError('Inline image frame is unavailable')
    box = [bounds['x'], bounds['y'], bounds['x'] + bounds['width'], bounds['y'] + bounds['height']]
    if max(abs(a - b) for a, b in zip((bounds['width'], bounds['height']), data['sourceSize'])) > .002:
        raise ValueError('Image frame differs from the source extent')
    delta = max(abs(a - b) for other in (data['pdfBox'], data['svgBox']) for a, b in zip(box, other))
    if delta > .002:
        raise ValueError('Snapshot, PDF and SVG image frames differ')
    return {'qualified': True, 'boundsPt': box, 'maximumFrameDeltaPt': delta, 'pixels': list(data['sourcePixels'][0])}


def office(actual, reference):
    with fitz.open(actual) as left, fitz.open(reference) as right:
        if len(left) != 1 or len(right) != 1:
            raise ValueError('Expected one page in both image comparisons')
        actual_images, reference_images = left[0].get_image_info(), right[0].get_image_info()
        if len(actual_images) != 1 or len(reference_images) != 1:
            raise ValueError('Expected one image in both reference outputs')
        a, b = actual_images[0]['bbox'], reference_images[0]['bbox']
        delta = max(abs(x - y) for x, y in zip(a, b))
        return {'actualBoundsPt': list(a), 'referenceBoundsPt': list(b), 'maximumFrameDeltaPt': delta,
                'geometryTolerancePt': .5, 'frameComparisonPassed': delta <= .5}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sources', type=Path)
    parser.add_argument('outputs', type=Path)
    parser.add_argument('--office', type=Path)
    args = parser.parse_args()
    values = {name: read(args.sources / (name + '.docx'), args.outputs / name) for name in ('inline', 'inline-zero-dist', 'cropped')}
    result = {name: check(value, name == 'cropped', name == 'inline-zero-dist') for name, value in values.items()}
    negatives = {}
    for name in ('wrong source path', 'shifted bounds', 'moved SVG frame', 'moved PDF frame',
                 'changed source extent', 'changed image pixels', 'mapped cropped image',
                 'changed image relationship', 'changed explicit distances'):
        chosen = 'cropped' if name == 'mapped cropped image' else 'inline-zero-dist' if name == 'changed explicit distances' else 'inline'
        data = copy.deepcopy(values[chosen])
        if name == 'wrong source path': data['snapshot'][0]['objectPath'] += '/changed'
        if name == 'shifted bounds': data['snapshot'][0]['bounds']['x'] += 2
        if name == 'moved SVG frame': data['svgBox'][0] += 2
        if name == 'moved PDF frame': data['pdfBox'][0] += 2
        if name == 'changed source extent': data['sourceSize'][0] += 2
        if name == 'changed image pixels': data['svgPixels'] = ((4, 4), b'bad')
        if name == 'mapped cropped image': data['snapshot'][0]['objectPath'] = '/changed'
        if name == 'changed image relationship': data['sourceEmbed'] = 'different'
        if name == 'changed explicit distances': data['sourceDistances'][0] = None
        try:
            check(data, name == 'mapped cropped image', name == 'changed explicit distances')
        except (ValueError, KeyError, TypeError) as error:
            negatives[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    reference = None if args.office is None else {
        name: office(args.outputs / name / 'output.pdf', args.office / (name + '.pdf'))
        for name in ('inline', 'inline-zero-dist')}
    print(json.dumps({'specimens': result, 'negativeControls': negatives,
                      'controlMethod': 'Parsed source/output measurements changed in memory.', 'officeComparison': reference}, indent=2))


if __name__ == '__main__':
    main()
