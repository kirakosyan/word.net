#!/usr/bin/env python3
"""Independently author nested item-path and root-fallback contrasts."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('fixtures', ROOT / 'eng/generate-roadmap-138-preflight-cells.py')
fixtures = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixtures)


def repeat(name, content):
    return ('<w:sdt><w:sdtPr><w:tag w:val="repeat:' + name + '"/></w:sdtPr>'
            '<w:sdtContent>' + content + '</w:sdtContent></w:sdt>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    output = parser.parse_args().output.resolve()
    fixture_tree = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if output == fixture_tree or fixture_tree in output.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    output.mkdir(parents=True, exist_ok=False)
    field = fixtures.field
    end = '<w:p><w:r><w:t>end</w:t></w:r></w:p>'
    regular = field('Customer.Name') + repeat('Items', field('Customer.Name')) + end
    deep = repeat('Items', field('Account.Contact.Name')) + end
    cases = {
        'required': (regular, '{"Customer":{"Name":"root"},"Items":[{"Customer":{"Name":"child"}},{"Customer":{"Name":"other"}}]}'),
        'fallback': (regular, '{"Customer":{"Name":"root"},"Items":[{"Customer":{"Name":"child"}},{"Customer":{}}]}'),
        'missing-parent': (regular, '{"Customer":{"Name":"root"},"Items":[{"Customer":{"Name":"child"}},{"Customer":{"Name":"other"}}]}'),
        'optional-parent': (regular, '{"Customer":{"Name":"root"},"Items":[]}'),
        'wrong-parent': (regular, '{"Customer":{"Name":"root"},"Items":[]}'),
        'optional-leaf': (regular, '{"Customer":{"Name":"root"},"Items":[{"Customer":{"Name":"child"}},{"Customer":{}}]}'),
        'null-parent': (regular, '{"Customer":{"Name":"root"},"Items":[{"Customer":null}]}'),
        'deep': (deep, '{"Items":[{"Account":{"Contact":{"Name":"deep"}}}]}'),
    }
    hashes = {}
    for name, (body, data) in cases.items():
        document = output / (name + '.docx')
        fixtures.reductions.write_zip(document, fixtures.reductions.package(body))
        dataset = output / (name + '.json')
        dataset.write_text(data, encoding='utf-8')
        hashes[name] = {'docxSha256': hashlib.sha256(document.read_bytes()).hexdigest(),
                        'jsonSha256': hashlib.sha256(dataset.read_bytes()).hexdigest()}
    manifest = {'provenance': 'Synthetic OOXML and JSON independently authored with MIT fixture helpers; no wordnet serialization or caller data.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'cases': hashes}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
