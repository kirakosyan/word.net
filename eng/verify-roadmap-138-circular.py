"""Run circular-chart comparisons with deliberately corrupted controls."""
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
    parser.add_argument('--office', type=Path, help='Optional LibreOffice PDFs of saved and API-authored documents.')
    args = parser.parse_args(); args.controls.mkdir(parents=True, exist_ok=False)
    spec = importlib.util.spec_from_file_location('circular_check', Path(__file__).with_name('check-roadmap-138-circular.py'))
    checker = importlib.util.module_from_spec(spec); spec.loader.exec_module(checker)
    inputs = {}; result = []
    for kind in ('pie', 'doughnut'):
        output = args.output / kind
        values = [kind, args.source / ('circular-' + kind + '.docx'), output / 'saved.docx', output / 'output.pdf',
                  output / 'output.xps', output / 'output.svg', args.reference / ('circular-' + kind + '.pdf'), output / 'diagnostics.txt']
        inputs[kind] = values; result.append(checker.check(*values))
    failures = {}
    def reject(label, values):
        try:
            checker.check(*values)
        except (ValueError, KeyError, AssertionError, AttributeError) as error:
            failures[label] = str(error); return
        raise ValueError('Checker accepted negative control: ' + label)
    missing = args.controls / 'missing-diagnostic.txt'; missing.write_text('')
    for kind in ('pie', 'doughnut'):
        values = inputs[kind].copy(); values[7] = missing; reject('missing ' + kind + ' diagnostic', values)
    wrong_reference = inputs['pie'].copy(); wrong_reference[6] = inputs['doughnut'][6]; reject('wrong reference', wrong_reference)
    changed = args.controls / 'changed-data.docx'
    with ZipFile(inputs['pie'][2]) as source, ZipFile(changed, 'w', compression=ZIP_DEFLATED) as target:
        for entry in source.infolist():
            data = source.read(entry.filename)
            if entry.filename.endswith('/chart1.xml'):
                root = ET.fromstring(data); root.find('.//' + checker.C + 'numCache/' + checker.C + 'pt/' + checker.C + 'v').text = '9'
                data = ET.tostring(root, encoding='utf-8', xml_declaration=True)
            target.writestr(entry, data)
    values = inputs['pie'].copy(); values[2] = changed; reject('changed editable slice value', values)
    changed_svg = args.controls / 'changed.svg'; root = ET.parse(inputs['pie'][5])
    sector = next(e for e in root.iter() if e.tag.endswith('}path') and e.get('fill', '').upper() == '#2468AC')
    sector.set('d', re.sub(r'(?<=M )-?\d+(?:\.\d+)?', lambda m: str(float(m.group()) + 10), sector.get('d'), count=1))
    root.write(changed_svg, encoding='utf-8', xml_declaration=True)
    values = inputs['pie'].copy(); values[5] = changed_svg; reject('changed SVG sector', values)
    changed_xps = args.controls / 'changed.xps'
    with ZipFile(inputs['pie'][4]) as source, ZipFile(changed_xps, 'w', compression=ZIP_DEFLATED) as target:
        for entry in source.infolist():
            data = source.read(entry.filename)
            if entry.filename == 'Documents/1/Pages/1.fpage':
                root = ET.fromstring(data); sector = next(e for e in root.iter() if e.get('Fill', '').upper() == '#FF2468AC')
                sector.set('Data', re.sub(r'(?<=M )-?\d+(?:\.\d+)?', lambda m: str(float(m.group()) + 20), sector.get('Data'), count=1))
                data = ET.tostring(root, encoding='utf-8', xml_declaration=True)
            target.writestr(entry, data)
    values = inputs['pie'].copy(); values[4] = changed_xps; reject('changed XPS sector', values)
    swapped = args.controls / 'swapped-legend.pdf'; document = fitz.open(inputs['pie'][6]); page = document[0]
    first, second = page.search_for('First')[0], page.search_for('Second')[0]
    page.add_redact_annot(first, fill=False); page.add_redact_annot(second, fill=False); page.apply_redactions(images=0, graphics=0)
    page.insert_text((first.x0, first.y1 - 2), 'Second', fontsize=8); page.insert_text((second.x0, second.y1 - 2), 'First', fontsize=8)
    document.save(swapped); document.close()
    values = inputs['pie'].copy(); values[6] = swapped; reject('changed legend order', values)
    blank = args.controls / 'blank.pdf'; document = fitz.open(); document.new_page(); document.save(blank); document.close()
    values = inputs['pie'].copy(); values[3] = blank; reject('missing PDF content', values)
    office = {}
    if args.office:
        for kind in ('pie', 'doughnut'):
            graph = checker.graph(args.output / kind / 'authored/authored.docx', kind)
            paths = [inputs[kind][6], args.office / ('saved-' + kind + '.pdf'), args.office / ('authored-' + kind + '.pdf')]
            for path in paths:
                checker.pdf_text(path)
            with fitz.open(paths[0]) as original, fitz.open(paths[1]) as saved, fitz.open(paths[2]) as authored:
                pages = [original[0], saved[0], authored[0]]
                if any(page.get_text() != pages[0].get_text() for page in pages[1:]):
                    raise ValueError('Office text changed after saving or API authoring')
                bounds = [checker.raster(page, checker.vectors(page), kind) for page in pages]
                if any(max(abs(a - b) for a, b in zip(bounds[0], other)) > .5 for other in bounds[1:]):
                    raise ValueError('Office circular bounds changed after saving or API authoring')
                for name in checker.NAMES:
                    first = pages[0].search_for(name)[0]
                    for page in pages[1:]:
                        if max(abs(a - b) for a, b in zip(first, page.search_for(name)[0])) > .001:
                            raise ValueError('Office legend bounds changed after saving or API authoring')
            office[kind] = {'authoredGraph': graph, 'referenceBoundsPt': bounds[0], 'savedBoundsPt': bounds[1],
                            'authoredBoundsPt': bounds[2], 'legendTolerancePt': .001}
    print(json.dumps({'specimens': result, 'negativeControls': failures, 'officeRoundtrips': office}, indent=2))


if __name__ == '__main__':
    main()
