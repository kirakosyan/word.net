#!/usr/bin/env python3
"""Author independent ordinary-table margin and trailing-flow contrasts."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)


def paragraph(text):
    return ('<w:p><w:pPr>' + reductions.EXPLICIT + '</w:pPr><w:r><w:rPr>' + reductions.FONT
            + '<w:sz w:val="22"/></w:rPr><w:t>' + text + '</w:t></w:r></w:p>')


def margins(value):
    return ('<w:tblCellMar><w:left w:w="' + str(value) + '" w:type="dxa"/>'
            '<w:right w:w="' + str(value) + '" w:type="dxa"/></w:tblCellMar>')


def table(rows, table_margins='', cell_margins='', borders=''):
    width = 3000 // len(rows[0])
    grid = ('<w:gridCol w:w="' + str(width) + '"/>') * len(rows[0])
    def cell(text):
        return ('<w:tc><w:tcPr><w:tcW w:w="' + str(width) + '" w:type="dxa"/>' + cell_margins
                + '</w:tcPr>' + paragraph(text) + '</w:tc>')
    return ('<w:tbl><w:tblPr><w:tblW w:w="3000" w:type="dxa"/>' + borders
            + '<w:tblLayout w:type="fixed"/>' + table_margins + '</w:tblPr><w:tblGrid>'
            + grid + '</w:tblGrid>' + ''.join('<w:tr>' + ''.join(cell(t) for t in row) + '</w:tr>' for row in rows) + '</w:tbl>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    output = parser.parse_args().output.resolve()
    fixture_tree = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if output == fixture_tree or fixture_tree in output.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    output.mkdir(parents=True, exist_ok=False)
    zero = ('<w:tcMar><w:left w:w="0" w:type="dxa"/>'
            '<w:right w:w="0" w:type="dxa"/></w:tcMar>')
    top = '<w:tblCellMar><w:top w:w="40" w:type="dxa"/></w:tblCellMar>'
    border = '<w:tblBorders>' + ''.join('<w:' + side + ' w:val="single" w:sz="8" w:color="000000"/>'
                                      for side in ('top', 'left', 'bottom', 'right', 'insideH', 'insideV')) + '</w:tblBorders>'
    cases = {
        'implicit': table([['NNNN']]),
        'two-columns': table([['MMMM', 'NNNN']]),
        'two-rows': table([['MMMM'], ['NNNN']]),
        'explicit-115': table([['NNNN']], margins(115)),
        'explicit-108': table([['NNNN']], margins(108)),
        'explicit-zero': table([['NNNN']], margins(0)),
        'cell-zero': table([['NNNN']], margins(115), zero),
        'only-top': table([['NNNN']], top),
        'border': table([['NNNN']], borders=border),
    }
    hashes = {}
    for name, value in cases.items():
        path = output / (name + '.docx')
        reductions.write_zip(path, reductions.package(paragraph('AAAA') + value + paragraph('TTTT')))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with the MIT fixture helper; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
