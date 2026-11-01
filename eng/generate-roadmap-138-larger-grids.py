#!/usr/bin/env python3
"""Independently author larger fixed-grid border and boundary contrasts."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('plain_tables', ROOT / 'eng/generate-roadmap-138-plain-tables.py')
plain = importlib.util.module_from_spec(spec)
spec.loader.exec_module(plain)


def borders(size):
    value = 'w:val="nil"' if size is None else 'w:val="single" w:sz="%d" w:color="000000"' % size
    return '<w:tblBorders>' + ''.join('<w:%s %s/>' % (side, value) for side in ('top', 'left', 'bottom', 'right', 'insideH', 'insideV')) + '</w:tblBorders>'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    output = parser.parse_args().output.resolve()
    fixture_tree = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if output == fixture_tree or fixture_tree in output.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    output.mkdir(parents=True, exist_ok=False)
    shapes = ((3, 1), (1, 3), (3, 3), (4, 1), (1, 4), (4, 4), (2, 4), (4, 2))
    cases = {}
    for rows, columns in shapes:
        grid = [[chr(66 + row * columns + column) for column in range(columns)] for row in range(rows)]
        for size in (None, 4, 8, 16, 24):
            for margin in ('implicit', 'zero'):
                name = '%dx%d-%s-%s' % (rows, columns, 'nil' if size is None else size, margin)
                cases[name] = plain.table(grid, '' if margin == 'implicit' else plain.margins(0), borders=borders(size))
    for rows, columns in ((5, 1), (1, 5), (5, 5)):
        grid = [[chr(66 + row * columns + column) for column in range(columns)] for row in range(rows)]
        cases['outside-%dx%d' % (rows, columns)] = plain.table(grid, borders=borders(8))
    hashes = {}
    for name, table in cases.items():
        path = output / (name + '.docx')
        plain.reductions.write_zip(path, plain.reductions.package(plain.paragraph('A') + table + plain.paragraph('Z')))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with the MIT fixture helper; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
