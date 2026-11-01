#!/usr/bin/env python3
"""Independently author minimal and out-of-profile DOCX policy controls."""
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
    parser.add_argument('--output', type=Path, required=True)
    destination = parser.parse_args().output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    destination.mkdir(parents=True, exist_ok=False)
    paragraph = lambda value: '<w:p><w:r><w:t>' + value + '</w:t></w:r></w:p>'
    cases = {
        'short': (paragraph('Alpha123'), None),
        'maximum': (paragraph('ABCDEFGHIJKLMNOPQRSTUVWXYZ12345678901234'), None),
        'single': (paragraph('A'), None),
        'lowercase': (paragraph('abcdefghijklmnopqrstuvwxyz'), None),
        'mixed': (paragraph('A0b1C2d3E4f5G6h7I8j9K0l1M2n3O4p5Q6r7S8t9'), None),
        'two-paragraphs': (paragraph('Alpha') + paragraph('Bravo'), None),
        'formatted': ('<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Alpha</w:t></w:r></w:p>', None),
        'field': ('<w:p><w:fldSimple w:instr="MERGEFIELD Name"><w:r><w:t>Alpha</w:t></w:r></w:fldSimple></w:p>', None),
        'space': ('<w:p><w:r><w:t xml:space="preserve">Alpha Bravo</w:t></w:r></w:p>', None),
        'long': (paragraph('ABCDEFGHIJKLMNOPQRSTUVWXYZ123456789012345'), None),
        'styles': (paragraph('Alpha'), {'word/styles.xml': '<w:styles xmlns:w="' + reductions.W + '"/>'}),
    }
    hashes = {}
    for name, (body, extras) in cases.items():
        parts = reductions.package(body, extras)
        parts['word/document.xml'] = ('<w:document xmlns:w="' + reductions.W + '" xmlns:r="' + reductions.R
                                      + '"><w:body>' + body + '</w:body></w:document>')
        path = destination / (name + '.docx')
        reductions.write_zip(path, parts)
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with the MIT fixture helper; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes,
                'expectedAcceptedText': {'short': 'Alpha123', 'maximum': 'ABCDEFGHIJKLMNOPQRSTUVWXYZ12345678901234',
                                         'single': 'A', 'lowercase': 'abcdefghijklmnopqrstuvwxyz',
                                         'mixed': 'A0b1C2d3E4f5G6h7I8j9K0l1M2n3O4p5Q6r7S8t9'}}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
