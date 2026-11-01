"""Run independent rotation checks and controls; retain reference discrepancies."""
import argparse
import json
from pathlib import Path
import subprocess
import sys
from xml.etree import ElementTree as ET
from zipfile import ZIP_DEFLATED, ZipFile

from pypdf import PdfWriter


def rewrite_zip(source, destination, name, edit):
    with ZipFile(source) as original, ZipFile(destination, 'w', compression=ZIP_DEFLATED) as target:
        for entry in original.infolist():
            data = original.read(entry.filename)
            target.writestr(entry, edit(data) if entry.filename == name else data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('source', 'output', 'reference', 'controls'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    args.controls.mkdir(parents=True, exist_ok=False)
    checker = Path(__file__).with_name('check-roadmap-138-rotation.py')
    commands = {}; results = []
    for name, raw, linked in (('plain-30', 1800000, False), ('plain-45', 2700000, False),
                              ('plain-fractional', 2700001, False), ('linked-image-45', 2700000, True),
                              ('linked-image-90', 5400000, True)):
        out = args.output / name
        command = [sys.executable, str(checker), str(raw), str(args.source / (name + '.docx')),
                   str(out / 'saved.docx'), str(out / 'output.pdf'), str(out / 'output.xps'), str(out / 'output.svg'),
                   str(out / 'diagnostics.txt'), str(args.reference / (name + '.pdf')), '--saved-again', str(out / 'saved-again.docx')]
        if linked:
            command.append('--linked')
        commands[name] = command
        result = subprocess.run(command, check=True, capture_output=True, text=True)
        if result.stderr:
            print(result.stderr, file=sys.stderr, end='')
        results.append(json.loads(result.stdout.strip().splitlines()[-1]))

    failures = {}
    def reject(label, command):
        result = subprocess.run(command, capture_output=True, text=True)
        if result.returncode == 0:
            raise ValueError('Checker accepted negative control ' + label)
        failures[label] = result.stderr.strip().splitlines()[-1]

    wrong_angle = commands['plain-30'].copy(); wrong_angle[2] = '2700000'; reject('wrong source angle', wrong_angle)
    wrong_reference = commands['plain-30'].copy(); wrong_reference[9] = str(args.reference / 'plain-45.pdf'); reject('wrong reference', wrong_reference)
    missing = args.controls / 'missing-diagnostic.txt'; missing.write_text('')
    no_diagnostic = commands['plain-30'].copy(); no_diagnostic[8] = str(missing); reject('missing diagnostic', no_diagnostic)
    wrong_svg = args.controls / 'wrong-frame.svg'; root = ET.parse(args.output / 'plain-45' / 'output.svg')
    frame = next(element for element in root.iter() if element.get('fill') == '#00FFFF')
    frame.set('transform', 'rotate(0 170 140)'); root.write(wrong_svg, encoding='utf-8', xml_declaration=True)
    wrong_frame = commands['plain-45'].copy(); wrong_frame[7] = str(wrong_svg); reject('wrong vector transform', wrong_frame)
    missing_link = args.controls / 'missing-link.pdf'; writer = PdfWriter(clone_from=args.output / 'linked-image-45' / 'output.pdf')
    del writer.pages[0]['/Annots']; writer.write(missing_link)
    no_link = commands['linked-image-45'].copy(); no_link[5] = str(missing_link); reject('missing PDF link', no_link)
    changed_image = args.controls / 'changed-image.docx'
    rewrite_zip(args.output / 'linked-image-45' / 'saved.docx', changed_image, 'word/media/image1.png', lambda data: data + b'changed')
    wrong_image = commands['linked-image-45'].copy(); wrong_image[4] = str(changed_image); reject('changed editable image', wrong_image)
    print(json.dumps({'specimens': results, 'negativeControls': failures}))


if __name__ == '__main__':
    main()
