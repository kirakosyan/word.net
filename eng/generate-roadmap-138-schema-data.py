#!/usr/bin/env python3
"""Independently author synthetic JSON schema-validation and merge contrasts."""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    output = parser.parse_args().output.resolve()
    fixture_tree = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if output == fixture_tree or fixture_tree in output.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    output.mkdir(parents=True, exist_ok=False)
    cases = {
        'valid': '{"Name":"root","Items":[{"Name":"child"},{"Name":"other"}]}',
        'fallback': '{"Name":"root","Items":[{"Name":"child"},{"Other":1}]}',
        'optional': '{"Name":"root","Items":[{"Name":"child"},{"Other":1}]}',
        'missing-scope': '{"Name":"root"}',
        'empty': '{"Name":"root","Items":[]}',
        'wrong-type': '{"Items":[{"Amount":"bad"}]}',
        'scope-type': '{"Name":"root","Items":{}}',
        'typed-non-json': '{"When":"2026-01-01","Logo":null}',
        'malformed': '{"Items":[],"Items":[]}',
        'limit': '{"Items":[{},{}]}',
    }
    hashes = {}
    for name, content in cases.items():
        path = output/(name+'.json')
        path.write_text(content, encoding='utf-8')
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance':'Synthetic JSON independently authored for schema and merge contrasts; no caller data.',
                'generatorSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'documents':hashes}
    (output/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
