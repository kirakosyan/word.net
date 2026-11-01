#!/usr/bin/env python3
"""Author synthetic inline-image DOCX inputs without wordnet serialization."""
import argparse
import hashlib
from io import BytesIO
import importlib.util
import json
from pathlib import Path

import PIL
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)


def image():
    if PIL.__version__ != '12.3.0':
        raise ValueError('This fixture generator requires Pillow 12.3.0.')
    pixels = Image.new('RGB', (4, 4))
    pixels.putdata([(208, 32, 32) if x < 2 else (32, 64, 208) for y in range(4) for x in range(4)])
    output = BytesIO()
    pixels.save(output, format='PNG', compress_level=9)
    return output.getvalue()


def drawing(cropped, zero_dist=False):
    crop = '<a:srcRect l="50000"/>' if cropped else ''
    distances = ' distT="0" distB="0" distL="0" distR="0"' if zero_dist else ''
    return (
        '<w:p><w:r><w:drawing><wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"' + distances + '>'
        '<wp:extent cx="1524000" cy="762000"/><wp:docPr id="1" name="Synthetic grid" descr="Synthetic red and blue grid"/>'
        '<a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">'
        '<a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">'
        '<pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">'
        '<pic:nvPicPr><pic:cNvPr id="0" name="Grid"/><pic:cNvPicPr/></pic:nvPicPr>'
        '<pic:blipFill><a:blip r:embed="image"/>' + crop + '<a:stretch><a:fillRect/></a:stretch></pic:blipFill>'
        '<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="1524000" cy="762000"/></a:xfrm>'
        '<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>'
        '</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    destination = parser.parse_args().output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    destination.mkdir(parents=True, exist_ok=False)
    pixels = image()
    hashes = {}
    for name in ('inline', 'inline-zero-dist', 'cropped'):
        parts = reductions.package(drawing(name == 'cropped', name == 'inline-zero-dist'))
        parts['[Content_Types].xml'] = parts['[Content_Types].xml'].replace(
            '</Types>', '<Default Extension="png" ContentType="image/png"/></Types>')
        parts['word/_rels/document.xml.rels'] = parts['word/_rels/document.xml.rels'].replace(
            '</Relationships>', '<Relationship Id="image" Type="' + reductions.R + '/image" Target="media/grid.png"/></Relationships>')
        parts['word/media/grid.png'] = pixels
        path = destination / (name + '.docx')
        reductions.write_zip(path, parts)
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML and 4x4 RGB PNG authored without wordnet serialization; MIT fixture helper and Pillow 12.3.0.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                'imageSha256': hashlib.sha256(pixels).hexdigest(), 'documents': hashes}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
