#!/usr/bin/env python3
"""Independently author repeated-field scope and unsupported-owner contrasts."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('fixtures', ROOT / 'eng/generate-roadmap-138-preflight-cells.py')
fixtures = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixtures)


def repeat(content):
    return ('<w:sdt><w:sdtPr><w:tag w:val="repeat:Items"/></w:sdtPr>'
            '<w:sdtContent>' + content + '</w:sdtContent></w:sdt>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    destination = parser.parse_args().output.resolve()
    fixture_tree = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixture_tree or fixture_tree in destination.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    destination.mkdir(parents=True, exist_ok=False)
    field = fixtures.field
    plain = '<w:p><w:r><w:t>end</w:t></w:r></w:p>'
    nonmerge = '<w:p><w:fldSimple w:instr="DATE"><w:r><w:t>cached</w:t></w:r></w:fldSimple></w:p>'
    extra = field('Secret').replace('w:instr=', 'w:dirty="true" w:instr=')
    cases = {
        'both': field('Name') + repeat(field('Name')),
        'item': repeat(field('Code')) + field('Name'),
        'empty-invalid': repeat(field('Bad-Name')) + plain,
        'nonmerge': repeat(nonmerge) + plain,
        'two-blocks': repeat(field('Secret') + field('Other')) + plain,
        'field-extra': repeat(extra) + plain,
        'wrong-type': repeat(field('Name')) + plain,
    }
    hashes = {}
    for name, body in cases.items():
        path = destination / (name + '.docx')
        fixtures.reductions.write_zip(path, fixtures.reductions.package(body))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with MIT fixture helpers; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
