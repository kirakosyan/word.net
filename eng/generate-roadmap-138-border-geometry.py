#!/usr/bin/env python3
"""Author independent direct-table border geometry contrasts."""
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
    cases = {'nil': plain.table([['NNNN']], borders=borders(None))}
    for size in (2, 3, 4, 6, 8, 12, 16, 20, 24):
        cases['implicit-%d' % size] = plain.table([['NNNN']], borders=borders(size))
        cases['zero-%d' % size] = plain.table([['NNNN']], plain.margins(0), borders=borders(size))
    top = '<w:tblCellMar><w:top w:w="40" w:type="dxa"/></w:tblCellMar>'
    left = '<w:tcMar><w:left w:w="0" w:type="dxa"/></w:tcMar>'
    cases.update({
        'only-top': plain.table([['NNNN']], top, borders=borders(8)),
        'mixed-margins': plain.table([['NNNN']], top, left, borders(8)),
        'empty-margin': plain.table([['NNNN']], '<w:tblCellMar/>', borders=borders(8)),
        'explicit-108': plain.table([['NNNN']], plain.margins(108), borders=borders(8)),
        'explicit-115': plain.table([['NNNN']], plain.margins(115), borders=borders(8)),
        'cell-zero': plain.table([['NNNN']], plain.margins(115),
                                 '<w:tcMar><w:left w:w="0" w:type="dxa"/><w:right w:w="0" w:type="dxa"/></w:tcMar>', borders(8)),
        'cell-108': plain.table([['NNNN']], '',
                                '<w:tcMar><w:left w:w="108" w:type="dxa"/><w:right w:w="108" w:type="dxa"/></w:tcMar>', borders(8)),
        'cell-left': plain.table([['NNNN']], plain.margins(115), left, borders(8)),
        'cell-right': plain.table([['NNNN']], plain.margins(115),
                                  '<w:tcMar><w:right w:w="0" w:type="dxa"/></w:tcMar>', borders(8)),
        'long-wrap': plain.table([['MMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM']], borders=borders(8)),
        'multiple-paragraphs': plain.table([['NNNN']], borders=borders(8)).replace(
            plain.paragraph('NNNN'), plain.paragraph('NNNN') + plain.paragraph('MMMM')),
        'two-rows': plain.table([['MMMM'], ['NNNN']], borders=borders(8)),
        'two-columns': plain.table([['MMMM', 'NNNN']], borders=borders(8)),
        'horizontal-only': plain.table([['NNNN']], borders=borders(8, ('top', 'bottom'))),
        'mixed-borders': plain.table([['NNNN']], borders=borders(8).replace('<w:bottom w:val="single" w:sz="8"', '<w:bottom w:val="single" w:sz="16"')),
    })
    hashes = {}
    for name, table in cases.items():
        path = output / (name + '.docx')
        plain.reductions.write_zip(path, plain.reductions.package(plain.paragraph('AAAA') + table + plain.paragraph('TTTT')))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with the MIT fixture helper; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
