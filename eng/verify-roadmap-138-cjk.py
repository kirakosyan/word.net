"""Run East Asian geometry checks and deliberately corrupted controls."""
import argparse
import json
from pathlib import Path
import subprocess
import sys
from zipfile import ZIP_DEFLATED, ZipFile

from pypdf import PdfWriter


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('source', 'output', 'reference', 'controls'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args(); args.controls.mkdir(parents=True, exist_ok=False)
    checker = Path(__file__).with_name('check-roadmap-138-cjk.py'); commands = {}; results = []
    for name, suffix in (('japanese-plain', 'JP'), ('japanese', 'JP'), ('simplified', 'SC'), ('traditional', 'TC')):
        output = args.output / name
        command = [sys.executable, str(checker), str(args.source / (name + '.docx')), str(output / 'saved.docx'),
                   str(output / 'output.pdf'), str(args.reference / (name + '.pdf')), str(output / 'diagnostics.txt'), suffix]
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

    wrong_reference = commands['japanese'].copy(); wrong_reference[5] = str(args.reference / 'traditional.pdf'); reject('wrong reference', wrong_reference)
    wrong_font = commands['japanese'].copy(); wrong_font[7] = 'SC'; reject('wrong font', wrong_font)
    empty = args.controls / 'missing-diagnostic.txt'; empty.write_text('')
    for name in ('japanese', 'simplified'):
        missing = commands[name].copy(); missing[6] = str(empty); reject('missing ' + name + ' diagnostic', missing)
    changed = args.controls / 'changed-editable.docx'
    with ZipFile(args.output / 'japanese' / 'saved.docx') as original, ZipFile(changed, 'w', compression=ZIP_DEFLATED) as target:
        for entry in original.infolist():
            data = original.read(entry.filename)
            if entry.filename == 'word/document.xml':
                data = data.replace('日本語'.encode(), '日本文'.encode())
            target.writestr(entry, data)
    edited = commands['japanese'].copy(); edited[3] = str(changed); reject('changed editable text', edited)
    blank = args.controls / 'blank.pdf'; writer = PdfWriter(); writer.add_blank_page(width=150, height=792); writer.write(blank)
    missing_page = commands['traditional'].copy(); missing_page[4] = str(blank); reject('missing PDF text', missing_page)
    print(json.dumps({'specimens': results, 'negativeControls': failures}, ensure_ascii=False))


if __name__ == '__main__':
    main()
