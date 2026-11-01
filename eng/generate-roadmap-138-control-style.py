"""Assemble a scratch Word-derived date-placeholder/character-style control."""
import argparse
import hashlib
import importlib.util
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/ContentControls/OfficeWordVariants'
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)
W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    output = args.output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if output == fixtures or fixtures in output.parents or output.exists():
        parser.error('Choose a new scratch output outside the fixture tree.')
    styles = ET.fromstring((FIXTURES / 'main-character-styles.xml').read_bytes())
    defaults = ET.Element(W + 'docDefaults')
    run = ET.SubElement(ET.SubElement(defaults, W + 'rPrDefault'), W + 'rPr')
    ET.SubElement(run, W + 'rFonts', {W + 'ascii': 'Noto Sans', W + 'hAnsi': 'Noto Sans'})
    ET.SubElement(run, W + 'sz', {W + 'val': '22'})
    styles.insert(0, defaults)
    body = (FIXTURES / 'date-placeholder.xml').read_text(encoding='utf-8')
    parts = reductions.package(body, {'word/styles.xml': ET.tostring(styles, encoding='unicode')})
    output.parent.mkdir(parents=True, exist_ok=True)
    reductions.write_zip(output, parts)
    print(hashlib.sha256(output.read_bytes()).hexdigest(), output.name)


if __name__ == '__main__':
    main()
