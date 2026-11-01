#!/usr/bin/env python3
"""Independently author small-grid border, margin and boundary contrasts."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('plain_tables', ROOT / 'eng/generate-roadmap-138-plain-tables.py')
plain = importlib.util.module_from_spec(spec)
spec.loader.exec_module(plain)


def borders(size, sides=('top', 'left', 'bottom', 'right', 'insideH', 'insideV')):
    value = 'w:val="nil"' if size is None else 'w:val="single" w:sz="%d" w:color="000000"' % size
    return '<w:tblBorders>' + ''.join('<w:%s %s/>' % (side, value) for side in sides) + '</w:tblBorders>'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    output = parser.parse_args().output.resolve()
    fixture_tree = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if output == fixture_tree or fixture_tree in output.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    output.mkdir(parents=True, exist_ok=False)
    shapes = {'single': [['NNNN']], 'rows': [['MMMM'], ['NNNN']], 'columns': [['MMMM', 'NNNN']],
              'grid': [['MMMM', 'NNNN'], ['PPPP', 'QQQQ']]}
    cases = {}
    for shape, rows in shapes.items():
        for margin in ('implicit', 'zero'):
            cases['nil-' + shape + '-' + margin] = (plain.table(rows, '' if margin == 'implicit' else plain.margins(0), borders=borders(None)), 1)
        for size in (4, 8, 12, 16, 20, 24):
            cases['implicit-%d-%s' % (size, shape)] = (plain.table(rows, borders=borders(size)), 1)
        for size in (2, 6, 8, 24):
            cases['zero-%d-%s' % (size, shape)] = (plain.table(rows, plain.margins(0), borders=borders(size)), 1)
    top = '<w:tblCellMar><w:top w:w="40" w:type="dxa"/></w:tblCellMar>'
    left = '<w:tcMar><w:left w:w="0" w:type="dxa"/></w:tcMar>'
    grid = shapes['grid']
    mixed = borders(8).replace('<w:bottom w:val="single" w:sz="8"', '<w:bottom w:val="single" w:sz="16"')
    cases.update({
        'grid-top': (plain.table(grid, top, borders=borders(8)), 1),
        'grid-override': (plain.table(grid, plain.margins(115), ['', left], borders(8)), 1),
        'implicit-2-rows': (plain.table(shapes['rows'], borders=borders(2)), 1),
        'zero-3-rows': (plain.table(shapes['rows'], plain.margins(0), borders=borders(3)), 1),
        'three-rows': (plain.table([['MMMM'], ['NNNN'], ['PPPP']], borders=borders(8)), 1),
        'three-columns': (plain.table([['MMMM', 'NNNN', 'PPPP']], borders=borders(8)), 1),
        'mixed-borders': (plain.table(grid, borders=mixed), 1),
        'horizontal-only': (plain.table(grid, borders=borders(8, ('top', 'bottom'))), 1),
        'long-wrap': (plain.table([['MMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM'], ['NNNN']], borders=borders(8)), 1),
        'multiple-pages': (plain.table(grid, borders=borders(8)), 45),
        'section-columns': (plain.table(grid, borders=borders(8)), 1),
    })
    hashes = {}
    for name, (table, preceding) in cases.items():
        path = output / (name + '.docx')
        body = plain.paragraph('AAAA') * preceding + table + plain.paragraph('TTTT')
        parts = plain.reductions.package(body)
        if name == 'section-columns':
            parts['word/document.xml'] = parts['word/document.xml'].replace('</w:sectPr>', '<w:cols w:num="2"/></w:sectPr>')
        plain.reductions.write_zip(path, parts)
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with the MIT fixture helper; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
