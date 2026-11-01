#!/usr/bin/env python3
"""Independently author bounded and unsupported table-field templates."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)


def field(path):
    return '<w:p><w:fldSimple w:instr="MERGEFIELD ' + path + '"><w:r><w:t>cached</w:t></w:r></w:fldSimple></w:p>'


def table(contents, merged=False):
    grid = '<w:gridCol w:w="1500"/>' * 2 if merged else '<w:gridCol w:w="3000"/>'
    span = '<w:gridSpan w:val="2"/>' if merged else ''
    return ('<w:tbl><w:tblPr><w:tblW w:w="3000" w:type="dxa"/><w:tblLayout w:type="fixed"/></w:tblPr><w:tblGrid>'
            + grid + '</w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w="3000" w:type="dxa"/>' + span + '</w:tcPr>'
            + contents + '</w:tc></w:tr></w:tbl>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    destination = parser.parse_args().output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    destination.mkdir(parents=True, exist_ok=False)
    cases = {'simple': field('Customer.Name') + table(field('Customer.Code')),
             'merged': table(field('Hidden'), merged=True),
             'nested': table(table(field('HiddenNested')) + '<w:p/>')}
    hashes = {}
    for name, body in cases.items():
        path = destination / (name + '.docx')
        reductions.write_zip(path, reductions.package(body))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML authored independently with the MIT fixture helper; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
