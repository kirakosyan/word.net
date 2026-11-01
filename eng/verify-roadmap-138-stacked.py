"""Run stacked-chart checks and deliberately corrupted independent controls."""
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
    spec = importlib.util.spec_from_file_location('stacked_check', Path(__file__).with_name('check-roadmap-138-stacked.py'))
    checker = importlib.util.module_from_spec(spec); spec.loader.exec_module(checker)
    inputs = {}; result = []
    for kind in ('bar', 'column'):
        output = args.output / kind
        values = [kind, args.source / ('stacked-' + kind + '.docx'), output / 'saved.docx', output / 'output.pdf',
                  output / 'output.xps', output / 'output.svg', args.reference / ('stacked-' + kind + '.pdf'), output / 'diagnostics.txt']
        inputs[kind] = values; result.append(checker.check(*values))
    failures = {}
    def reject(label, values):
        try:
            checker.check(*values)
        except (ValueError, KeyError, AssertionError) as error:
            failures[label] = str(error); return
        raise ValueError('Checker accepted negative control: ' + label)
    missing = args.controls / 'missing-diagnostic.txt'; missing.write_text('')
    for kind in ('bar', 'column'):
        values = inputs[kind].copy(); values[7] = missing; reject('missing ' + kind + ' diagnostic', values)
    wrong_reference = inputs['bar'].copy(); wrong_reference[6] = inputs['column'][6]; reject('wrong reference', wrong_reference)
    changed = args.controls / 'changed-cache.docx'
    with ZipFile(inputs['bar'][2]) as source, ZipFile(changed, 'w', compression=ZIP_DEFLATED) as target:
        for entry in source.infolist():
            data = source.read(entry.filename)
            if entry.filename.endswith('/chart1.xml'):
                root = ET.fromstring(data); value = root.find('.//' + checker.C + 'numCache/' + checker.C + 'pt/' + checker.C + 'v')
                value.text = '99'; data = ET.tostring(root, encoding='utf-8', xml_declaration=True)
            target.writestr(entry, data)
    values = inputs['bar'].copy(); values[2] = changed; reject('changed editable data', values)
    changed_svg = args.controls / 'changed.svg'; root = ET.parse(inputs['bar'][5])
    rectangle = next(e for e in root.iter() if e.tag.endswith('}rect') and e.get('fill', '').upper() == '#2468AC')
    rectangle.set('x', str(float(rectangle.get('x')) + 10)); root.write(changed_svg, encoding='utf-8', xml_declaration=True)
    values = inputs['bar'].copy(); values[5] = changed_svg; reject('changed SVG geometry', values)
    changed_xps = args.controls / 'changed.xps'
    with ZipFile(inputs['bar'][4]) as source, ZipFile(changed_xps, 'w', compression=ZIP_DEFLATED) as target:
        for entry in source.infolist():
            data = source.read(entry.filename)
            if entry.filename == 'Documents/1/Pages/1.fpage':
                root = ET.fromstring(data)
                rectangle = next(e for e in root.iter() if e.get('Fill', '').upper().endswith('2468AC'))
                rectangle.set('Data', re.sub(r'(?<=M )-?\d+(?:\.\d+)?', lambda m: str(float(m.group()) + 20), rectangle.get('Data'), count=1))
                data = ET.tostring(root, encoding='utf-8', xml_declaration=True)
            target.writestr(entry, data)
    values = inputs['bar'].copy(); values[4] = changed_xps; reject('changed XPS geometry', values)
    swapped = args.controls / 'swapped-legend.pdf'; document = fitz.open(inputs['bar'][6]); page = document[0]
    north, south = page.search_for('North')[0], page.search_for('South')[0]
    page.add_redact_annot(north, fill=False); page.add_redact_annot(south, fill=False)
    page.apply_redactions(images=0, graphics=0)
    page.insert_text((north.x0, north.y1 - 2), 'South', fontsize=8)
    page.insert_text((south.x0, south.y1 - 2), 'North', fontsize=8)
    document.save(swapped); document.close()
    values = inputs['bar'].copy(); values[6] = swapped; reject('changed legend order', values)
    blank = args.controls / 'blank.pdf'; document = fitz.open(); document.new_page(); document.save(blank); document.close()
    values = inputs['bar'].copy(); values[3] = blank; reject('missing PDF content', values)
    print(json.dumps({'specimens': result, 'negativeControls': failures}, indent=2))


if __name__ == '__main__':
    main()
