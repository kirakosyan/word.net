#!/usr/bin/env python3
"""Independently author root-repeat and unresolved-scope contrasts."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)


def repeat(path, content):
    return '<w:sdt><w:sdtPr><w:tag w:val="repeat:' + path + '"/></w:sdtPr><w:sdtContent>' + content + '</w:sdtContent></w:sdt>'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    destination = parser.parse_args().output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    destination.mkdir(parents=True, exist_ok=False)
    paragraph = '<w:p><w:r><w:t>cached</w:t></w:r></w:p>'
    field = '<w:p><w:fldSimple w:instr="MERGEFIELD Items"><w:r><w:t>cached</w:t></w:r></w:fldSimple></w:p>'
    cases = {'simple': repeat('Items', paragraph), 'mixed': repeat('Items', paragraph) + field,
             'complex': repeat('Other', paragraph + paragraph)}
    hashes = {}
    for name, body in cases.items():
        path = destination / (name + '.docx')
        reductions.write_zip(path, reductions.package(body))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with the MIT fixture helper; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
