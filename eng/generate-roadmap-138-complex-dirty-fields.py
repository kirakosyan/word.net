#!/usr/bin/env python3
"""Independently author bounded and uninspected complex-field refresh flags."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('fixtures', ROOT / 'eng/generate-roadmap-138-preflight-cells.py')
fixtures = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixtures)


def field(value=None, instruction='MERGEFIELD Name', extra='', result=None):
    attribute = '' if value is None else ' w:dirty="' + value + '"'
    cached = '<w:r><w:t>cached-private-value</w:t></w:r>' if result is None else result
    return ('<w:p><w:r><w:fldChar w:fldCharType="begin"' + attribute + extra + '/></w:r>'
            '<w:r><w:instrText>' + instruction + '</w:instrText></w:r>'
            '<w:r><w:fldChar w:fldCharType="separate"/></w:r>' + cached
            + '<w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>')


def repeat(content):
    return ('<w:sdt><w:sdtPr><w:tag w:val="repeat:Items"/></w:sdtPr>'
            '<w:sdtContent>' + content + '</w:sdtContent></w:sdt>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    output = parser.parse_args().output.resolve()
    fixture_tree = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if output == fixture_tree or fixture_tree in output.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    output.mkdir(parents=True, exist_ok=False)
    simple_inner = '<w:fldSimple w:instr="DATE"><w:r><w:t>cached-private-value</w:t></w:r></w:fldSimple>'
    outer_simple = '<w:p><w:fldSimple w:instr="MERGEFIELD Name" w:dirty="true">' + simple_inner + '</w:fldSimple></w:p>'
    complex_inner = field().removeprefix('<w:p>').removesuffix('</w:p>')
    cross = field('true').replace('<w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>',
                                  '</w:p><w:p><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>')
    cases = {'marked': field('true') + fixtures.table(field('on')),
             'clear': field('false') + fixtures.table(field('off')),
             'absent': field(), 'invalid': field('private-invalid-value'),
             'locked': field('true', extra=' w:fldLock="true"'), 'cross': cross,
             'nested-simple': field('true', result=simple_inner),
             'nested-complex': field('true', result=complex_inner),
             'nested-simple-outer': outer_simple,
             'nonmerge': field('1', instruction='DATE'), 'repeated': repeat(field('true'))}
    hashes = {}
    for name, body in cases.items():
        path = output / (name + '.docx')
        fixtures.reductions.write_zip(path, fixtures.reductions.package(body))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with MIT fixture helpers; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                'documents': hashes, 'intentionallyInvalidDirtyValues': ['invalid.docx']}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
