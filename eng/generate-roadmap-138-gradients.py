"""Create synthetic gradient fixtures from independent python-docx packages."""
import argparse
import hashlib
import json
from pathlib import Path
import runpy
import tempfile
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

import docx
from lxml import etree as ET

A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'
WPS = '{http://schemas.microsoft.com/office/word/2010/wordprocessingShape}'
W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
ANGLES = (0, 5400000, 10800000, 16200000)
STOPS = ((20000, 'FF0000'), (50000, '00FF00'), (80000, '0000FF'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    output = parser.parse_args().output.resolve()
    fixture_root = (Path(__file__).resolve().parents[1] / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if output == fixture_root or fixture_root in output.parents or output.exists():
        parser.error('Choose a new scratch directory outside the fixture tree.')
    if docx.__version__ != '1.2.0':
        raise RuntimeError('This fixture requires python-docx 1.2.0.')
    output.mkdir(parents=True)
    generator = runpy.run_path(str(Path(__file__).with_name('generate-roadmap-138-preset-shapes.py')))
    with tempfile.TemporaryDirectory(prefix='wordnet-gradient-') as temp:
        variants = [('rect', angle, False, False) for angle in ANGLES] + [('rect', 0, True, False), ('rect', 0, True, True)]
        variants += [(geometry, 0, False, False) for geometry in ('ellipse', 'roundRect', 'triangle', 'diamond')]
        for geometry, angle, themed, remapped in variants:
            base = Path(temp) / (geometry + '.docx'); generator['create'](base, geometry)
            with ZipFile(base) as source:
                root = ET.fromstring(source.read('word/document.xml'))
                properties = root.find('.//' + WPS + 'spPr')
                old = properties.find(A + 'solidFill'); index = list(properties).index(old); properties.remove(old)
                gradient = ET.Element(A + 'gradFill'); stops = ET.SubElement(gradient, A + 'gsLst')
                for position, color in STOPS:
                    stop = ET.SubElement(stops, A + 'gs', {'pos': str(position)})
                    if themed and position == STOPS[0][0]:
                        ET.SubElement(stop, A + 'schemeClr', {'val': 'accent1'})
                    else:
                        ET.SubElement(stop, A + 'srgbClr', {'val': color})
                ET.SubElement(gradient, A + 'lin', {'ang': str(angle), 'scaled': '1'})
                properties.insert(index, gradient)
                target = output / ('gradient-theme-remapped.docx' if remapped else 'gradient-theme.docx' if themed else f'gradient-{angle}.docx' if geometry == 'rect' else f'gradient-{geometry}.docx')
                with ZipFile(target, 'w') as result:
                    for name in source.namelist():
                        info = ZipInfo(name, (2000, 1, 1, 0, 0, 0))
                        data = ET.tostring(root, encoding='utf-8', xml_declaration=True) if name == 'word/document.xml' else source.read(name)
                        if remapped and name == 'word/settings.xml':
                            settings = ET.fromstring(data); settings.find('.//' + W + 'clrSchemeMapping').set(W + 'accent1', 'accent2')
                            data = ET.tostring(settings, encoding='utf-8', xml_declaration=True)
                        result.writestr(info, data, compress_type=ZIP_DEFLATED)
    print(json.dumps({'producer': 'python-docx 1.2.0 (MIT), synthetic DrawingML gradients',
                      'generatorSha256': digest(Path(__file__)),
                      'documents': {path.name: digest(path) for path in sorted(output.iterdir())}}))


if __name__ == '__main__':
    main()
