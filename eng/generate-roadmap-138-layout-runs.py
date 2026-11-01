#!/usr/bin/env python3
"""Author an independent DOCX contrast for direct and ambiguous source runs."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)


def run(value, color='000000'):
    return '<w:r><w:rPr>' + reductions.FONT + '<w:color w:val="' + color + '"/><w:sz w:val="22"/></w:rPr><w:t>' + value + '</w:t></w:r>'


def paragraph(content):
    return '<w:p><w:pPr>' + reductions.EXPLICIT + '</w:pPr>' + content + '</w:p>'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    destination = parser.parse_args().output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    destination.mkdir(parents=True, exist_ok=False)
    body = paragraph(run('MMMM') + run('IIII', 'FF0000'))
    body += paragraph(run('OOOO') + run('HHHH'))
    body += ('<w:tbl><w:tblPr><w:tblW w:w="3000" w:type="dxa"/><w:tblLayout w:type="fixed"/></w:tblPr>'
             '<w:tblGrid><w:gridCol w:w="3000"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w="3000" w:type="dxa"/></w:tcPr>'
             + paragraph(run('NNNN')) + '</w:tc></w:tr></w:tbl>')
    body += paragraph('<w:hyperlink r:id="link">' + run('TTTT') + '</w:hyperlink>')
    cases = {
        'runs': body,
        'namespace-run': paragraph('<f:r xmlns:f="urn:foreign"/>' + run('MMMM')),
        'namespace-paragraph': '<f:p xmlns:f="urn:foreign"/>' + paragraph(run('MMMM')),
    }
    hashes = {}
    for name, content in cases.items():
        path = destination / (name + '.docx')
        parts = reductions.package(content)
        parts['word/_rels/document.xml.rels'] = parts['word/_rels/document.xml.rels'].replace(
            '</Relationships>', '<Relationship Id="link" Type="' + reductions.R + '/hyperlink" Target="https://example.com/" TargetMode="External"/></Relationships>')
        reductions.write_zip(path, parts)
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored using the MIT fixture helper; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                'documents': hashes,
                'negativeCases': 'The two namespace-collision inputs intentionally contain non-WordprocessingML siblings.'}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
