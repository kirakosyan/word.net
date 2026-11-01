"""Run independent label comparisons with corrupted controls."""
import argparse
import importlib.util
import json
from pathlib import Path
import re
from xml.etree import ElementTree as ET
from zipfile import ZIP_DEFLATED, ZipFile

import fitz


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('source', 'output', 'reference', 'controls'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args(); args.controls.mkdir(parents=True, exist_ok=False)
    spec = importlib.util.spec_from_file_location('label_check', Path(__file__).with_name('check-roadmap-138-chart-labels.py'))
    checker = importlib.util.module_from_spec(spec); spec.loader.exec_module(checker)
    inputs = {}; result = []
    for kind in ('bar', 'column'):
        output = args.output / kind
        values = [kind, args.source / ('labels-' + kind + '.docx'), output / 'saved.docx', output / 'output.pdf',
                  output / 'output.xps', output / 'output.svg', args.reference / ('labels-' + kind + '.pdf'), output / 'diagnostics.txt']
        inputs[kind] = values; result.append(checker.check(*values))
    failures = {}
    def reject(label, values):
        try:
            checker.check(*values)
        except (ValueError, KeyError, AssertionError, AttributeError) as error:
            failures[label] = str(error); return
        raise ValueError('Checker accepted negative control: ' + label)
    missing = args.controls / 'missing-diagnostic.txt'; missing.write_text('')
    for kind in ('bar', 'column'):
        values = inputs[kind].copy(); values[7] = missing; reject('missing ' + kind + ' diagnostic', values)
    wrong_reference = inputs['bar'].copy(); wrong_reference[6] = inputs['column'][6]; reject('wrong reference', wrong_reference)
    changed = args.controls / 'changed-flags.docx'
    with ZipFile(inputs['bar'][2]) as source, ZipFile(changed, 'w', compression=ZIP_DEFLATED) as target:
        for entry in source.infolist():
            data = source.read(entry.filename)
            if entry.filename.endswith('/chart1.xml'):
                root = ET.fromstring(data); root.find('.//' + checker.C + 'showSerName').set('val', '1')
                data = ET.tostring(root, encoding='utf-8', xml_declaration=True)
            target.writestr(entry, data)
    values = inputs['bar'].copy(); values[2] = changed; reject('changed series-label flag', values)
    changed_svg = args.controls / 'changed.svg'; root = ET.parse(inputs['bar'][5])
    title = next(e for e in root.iter() if e.tag.endswith('}title') and e.text == 'First, 10'); title.text = 'First, 99'
    root.write(changed_svg, encoding='utf-8', xml_declaration=True)
    values = inputs['bar'].copy(); values[5] = changed_svg; reject('changed SVG label', values)
    changed_xps = args.controls / 'changed.xps'
    with ZipFile(inputs['bar'][4]) as source, ZipFile(changed_xps, 'w', compression=ZIP_DEFLATED) as target:
        for entry in source.infolist():
            data = source.read(entry.filename)
            if entry.filename == 'Documents/1/Pages/1.fpage':
                root = ET.fromstring(data); label = next(e for e in root.iter() if e.get('UnicodeString') == 'First, 10')
                label.set('OriginX', str(float(label.get('OriginX')) + 20)); data = ET.tostring(root, encoding='utf-8', xml_declaration=True)
            target.writestr(entry, data)
    values = inputs['bar'].copy(); values[4] = changed_xps; reject('changed XPS label origin', values)
    moved = args.controls / 'moved-label.pdf'; document = fitz.open(inputs['bar'][3]); page = document[0]
    label = page.search_for('First, 10')[0]; page.add_redact_annot(label, fill=False); page.apply_redactions(images=0, graphics=0)
    page.insert_text((label.x0 + 30, label.y1 - 2), 'First, 10', fontsize=8, color=(1, 1, 1)); document.save(moved); document.close()
    values = inputs['bar'].copy(); values[3] = moved; reject('changed PDF label origin', values)
    blank = args.controls / 'blank.pdf'; document = fitz.open(); document.new_page(); document.save(blank); document.close()
    values = inputs['bar'].copy(); values[3] = blank; reject('missing PDF content', values)
    print(json.dumps({'specimens': result, 'negativeControls': failures}, indent=2))


if __name__ == '__main__':
    main()
