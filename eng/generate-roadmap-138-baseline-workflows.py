#!/usr/bin/env python3
"""Independently author small 1.5.0 fragment and review baseline controls."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    destination = parser.parse_args().output.resolve()
    fixture_tree = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixture_tree or fixture_tree in destination.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    destination.mkdir(parents=True, exist_ok=False)
    target = ('<w:p><w:bookmarkStart w:id="1" w:name="slot"/>'
              '<w:r><w:t>placeholder</w:t></w:r><w:bookmarkEnd w:id="1"/></w:p>')
    plain = reductions.p('Independent fragment text.')
    field = ('<w:p><w:fldSimple w:instr="MERGEFIELD Name"><w:r>'
             '<w:t>cached field value</w:t></w:r></w:fldSimple></w:p>')
    review = ('<w:p><w:r><w:t xml:space="preserve">Before </w:t></w:r>'
              '<w:del w:id="1" w:author="Example" w:date="2020-01-01T00:00:00Z">'
              '<w:r><w:delText>old</w:delText></w:r></w:del>'
              '<w:ins w:id="2" w:author="Example" w:date="2020-01-01T00:00:00Z">'
              '<w:r><w:t>new</w:t></w:r></w:ins>'
              '<w:r><w:t xml:space="preserve"> After</w:t></w:r></w:p>')
    hashes = {}
    for name, body in {'target': target, 'plain-fragment': plain,
                       'field-fragment': field, 'review': review}.items():
        path = destination / (name + '.docx')
        reductions.write_zip(path, reductions.package(body))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML authored independently with MIT fixture helpers; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                'documents': hashes}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
