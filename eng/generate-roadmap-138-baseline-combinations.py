#!/usr/bin/env python3
"""Author factorial theme-font and table-style controls for the release baseline."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('controls', ROOT / 'eng/generate-roadmap-138-controls.py')
controls = importlib.util.module_from_spec(spec)
spec.loader.exec_module(controls)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    destination = parser.parse_args().output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    destination.mkdir(parents=True, exist_ok=False)
    cases = []
    for theme in (False, True):
        for styled in (False, True):
            name = ('theme' if theme else 'explicit') + '-' + ('styled' if styled else 'direct')
            parts, text = controls.table(styled)
            old = controls.reductions.FONT
            font = ('<w:rFonts w:asciiTheme="majorAscii" w:hAnsiTheme="majorHAnsi"/>' if theme else
                    '<w:rFonts w:ascii="Liberation Mono" w:hAnsi="Liberation Mono"/>')
            document = parts['word/document.xml']
            assert document.count(old) == 4
            parts['word/document.xml'] = document.replace(old, font)
            if theme:
                parts = controls.add_theme(parts)
            path = destination / (name + '.docx')
            controls.reductions.write_zip(path, parts)
            cases.append({'id': name, 'category': 'report', 'path': path.name,
                          'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'requiredText': text,
                          'knownLosses': [], 'expectedPages': 1, 'references': [],
                          'hypothesis': 'Theme font and conditional table shading, independently varied against explicit font and direct shading.',
                          'expectationBasis': 'authored-source-expectations; warnings and reference verdicts are retained'})
    original = json.loads((ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Comparison/manifest.json').read_text())
    manifest = {'schemaVersion': 1, 'provenance': 'Synthetic factorial OOXML controls independently authored with MIT helpers; no document-library serializer.',
                'generator': 'eng/generate-roadmap-138-baseline-combinations.py',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                'dependencySha256': hashlib.sha256((ROOT / 'eng/generate-roadmap-138-controls.py').read_bytes()).hexdigest(),
                'fonts': original['fonts'], 'documents': cases}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print('Four factorial release-baseline combination controls authored.')


if __name__ == '__main__':
    main()
