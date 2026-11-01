#!/usr/bin/env python3
"""Author minimal theme-loss and unchecked-link DOCX policy controls."""
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
    output = parser.parse_args().output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if output == fixtures or fixtures in output.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    output.mkdir(parents=True, exist_ok=False)
    cases = {
        'theme': "<w:p><w:r><w:rPr><w:rFonts w:asciiTheme='minorHAnsi' w:hAnsiTheme='minorHAnsi'/></w:rPr><w:t>Alpha123</w:t></w:r></w:p>",
        'theme-second': "<w:p><w:r><w:rPr><w:rFonts w:asciiTheme='minorHAnsi' w:hAnsiTheme='minorHAnsi'/></w:rPr><w:t>Bravo456</w:t></w:r></w:p>",
        'missing-link': "<w:p><w:hyperlink w:anchor='Missing'><w:r><w:t>Linked123</w:t></w:r></w:hyperlink></w:p>",
        'plain': '<w:p><w:r><w:t>Plain123</w:t></w:r></w:p>',
    }
    hashes = {}
    for name, body in cases.items():
        parts = reductions.package(body, None)
        parts['word/document.xml'] = ('<w:document xmlns:w="' + reductions.W + '" xmlns:r="' + reductions.R
                                      + '"><w:body>' + body + '</w:body></w:document>')
        path = output / (name + '.docx')
        reductions.write_zip(path, parts)
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with the MIT fixture helper; no wordnet serializer.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes,
                'expectedText': {'theme': 'Alpha123', 'theme-second': 'Bravo456', 'missing-link': 'Linked123', 'plain': 'Plain123'}}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print('Authored four policy loss and missing-link controls.')


if __name__ == '__main__':
    main()
