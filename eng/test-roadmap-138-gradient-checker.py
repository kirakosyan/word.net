"""Prove the independent gradient checker rejects corrupted controls."""
import argparse
from pathlib import Path
import subprocess
import sys
from zipfile import ZIP_DEFLATED, ZipFile

from lxml import etree
from pypdf import PdfReader, PdfWriter
from pypdf.generic import ArrayObject, FloatObject, NameObject

A = '{http://schemas.openxmlformats.org/drawingml/2006/main}'
XPS = '{http://schemas.microsoft.com/xps/2005/06}'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('source', 'saved', 'pdf', 'reference', 'svg', 'xps', 'opposite_reference', 'opposite_svg', 'output'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    changed = args.output / 'changed-stop.docx'
    with ZipFile(args.saved) as source, ZipFile(changed, 'w') as output:
        for entry in source.infolist():
            data = source.read(entry.filename)
            if entry.filename == 'word/document.xml':
                root = etree.fromstring(data); root.find('.//' + A + 'gs').set('pos', '21000')
                data = etree.tostring(root, encoding='utf-8', xml_declaration=True)
            output.writestr(entry.filename, data, compress_type=ZIP_DEFLATED)
    wrong = args.output / 'wrong-axis.pdf'
    writer = PdfWriter(); writer.add_page(PdfReader(args.pdf).pages[0])
    shading = writer.pages[0]['/Resources']['/Shading']['/WNGrad0'].get_object()
    shading[NameObject('/Coords')] = ArrayObject([FloatObject(value) for value in (240, 652, 100, 652)])
    with wrong.open('wb') as stream:
        writer.write(stream)
    invisible = args.output / 'unwrapped-stops.xps'
    with ZipFile(args.xps) as source, ZipFile(invisible, 'w') as output:
        for entry in source.infolist():
            data = source.read(entry.filename)
            if entry.filename.endswith('.fpage'):
                root = etree.fromstring(data); brush = root.find('.//' + XPS + 'LinearGradientBrush')
                wrapper = brush.find(XPS + 'LinearGradientBrush.GradientStops'); brush.remove(wrapper)
                for stop in list(wrapper):
                    wrapper.remove(stop); brush.append(stop)
                data = etree.tostring(root, encoding='utf-8', xml_declaration=True)
            output.writestr(entry.filename, data, compress_type=ZIP_DEFLATED)
    checker = str(Path(__file__).with_name('check-roadmap-138-gradients.py'))
    valid = [str(value) for value in ('0', args.source, args.saved, args.pdf, args.reference, args.svg, args.xps)]
    def check(values):
        return subprocess.run([sys.executable, checker, *values], capture_output=True, text=True)
    if check(valid).returncode:
        raise ValueError('The unmodified specimen did not pass the gradient checker.')
    controls = []
    for index, replacement in ((2, changed), (3, wrong), (4, args.opposite_reference), (5, args.opposite_svg), (6, invisible)):
        values = valid.copy(); values[index] = str(replacement)
        if check(values).returncode == 0:
            raise ValueError('The checker accepted a corrupted gradient control.')
        controls.append(str(replacement.name))
    print('Independent checker rejected changed stop, wrong PDF axis, substituted reference, substituted SVG and invisible XPS:', ', '.join(controls))


if __name__ == '__main__':
    main()
