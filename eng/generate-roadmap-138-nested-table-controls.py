#!/usr/bin/env python3
"""Author nested-table width and terminal-paragraph contrasts from the synthetic baseline."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    destination = parser.parse_args().output.resolve()
    protected = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == protected or protected in destination.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    destination.mkdir(parents=True, exist_ok=False)
    source = protected / 'Comparison/invoice-nested.docx'
    expected_source = '7889299fb8b6ee11c16ac7d0af9591411740b96e13b7f7783d29a0261c971360'
    if sha(source.read_bytes()) != expected_source:
        raise ValueError('The synthetic baseline fixture changed.')
    with ZipFile(source) as archive:
        parts = {item.filename: archive.read(item) for item in archive.infolist()}
    document = parts['word/document.xml'].decode('utf-8')
    header = '<w:tbl><w:tblPr><w:tblW w:type="dxa" w:w="5000"/></w:tblPr><w:tblGrid><w:gridCol w:w="2500"/><w:gridCol w:w="2500"/></w:tblGrid>'
    fit_header = '<w:tbl><w:tblPr><w:tblW w:type="dxa" w:w="2500"/></w:tblPr><w:tblGrid><w:gridCol w:w="1250"/><w:gridCol w:w="1250"/></w:tblGrid>'
    boundary = '</w:tbl></w:tc><w:tc>'
    if document.count(header) != 2 or document.count(boundary) != 1:
        raise ValueError('The expected nested table structure changed.')
    cases = []
    for fits in (False, True):
        for trailer in (False, True):
            name = ('fit' if fits else 'wide') + ('-trailing-p' if trailer else '-no-trailing-p')
            output = document
            if fits:
                position = output.rfind(header)
                output = output[:position] + fit_header + output[position + len(header):]
            if trailer:
                output = output.replace(boundary, '</w:tbl><w:p/></w:tc><w:tc>', 1)
            current = dict(parts); current['word/document.xml'] = output.encode('utf-8')
            path = destination / (name + '.docx'); reductions.write_zip(path, current)
            cases.append({'id': name, 'category': 'invoice', 'path': path.name, 'sha256': sha(path.read_bytes()),
                          'requiredText': ['INVOICE N-202', 'Hardware bundle', 'Adapter', '80 EUR', 'Total 480 EUR'],
                          'knownLosses': [], 'expectedPages': 1, 'references': [],
                          'hypothesis': 'Vary nested-grid width and the terminal paragraph without changing the outer table.',
                          'expectationBasis': 'authored-source-expectations; all reference differences remain retained',
                          'nestedGridTwips': 2500 if fits else 5000, 'terminalParagraph': trailer})
    original = json.loads((protected / 'Comparison/manifest.json').read_text())
    manifest = {'schemaVersion': 1,
                'provenance': 'Synthetic MIT baseline fixture with independent XML-only factorial mutations; no document-library serializer.',
                'baseFixture': 'tests/wordnet.Conversion.Tests/Fixtures/Comparison/invoice-nested.docx', 'baseSha256': expected_source,
                'generator': 'eng/generate-roadmap-138-nested-table-controls.py', 'generatorSha256': sha(Path(__file__).read_bytes()),
                'helperSha256': sha((ROOT / 'eng/generate-fidelity-reductions.py').read_bytes()),
                'fonts': original['fonts'], 'documents': cases}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print('Authored four nested-table factorial controls.')


if __name__ == '__main__':
    main()
