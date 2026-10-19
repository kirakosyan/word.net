#!/usr/bin/env python3
"""Validate retained ODT XML with pinned OASIS 1.3 schemas and an external reader.

Schemas are development-only downloads retained in ignored artifacts; not shipped.
Usage: python3 eng/verify-odt.py artifacts/p1-odt
Prerequisites: lxml and the documented pinned LibreOffice command-line reader.
"""
from pathlib import Path
from zipfile import ZipFile
import hashlib
import json
import subprocess
import sys
import urllib.request
from lxml import etree

root = Path(__file__).resolve().parent.parent
evidence = Path(sys.argv[1]).resolve()
schemas = root / 'artifacts/odf-schemas'
schemas.mkdir(parents=True, exist_ok=True)
pins = json.loads((root / 'eng/test-assets/odf13-schemas.json').read_text())
for filename, metadata in pins.items():
    target = schemas / filename
    if not target.exists():
        urllib.request.urlretrieve(metadata['url'], target)
    if hashlib.sha256(target.read_bytes()).hexdigest() != metadata['sha256']:
        raise SystemExit('ODF schema checksum mismatch: ' + filename)
validators = {key: etree.RelaxNG(etree.parse(str(schemas / key))) for key in pins}
version = subprocess.check_output(['soffice', '--version'], text=True).strip()
expected = {
    'text': ['Café  Ω\tend', 'next'],
    'list': ['Three', 'Four'],
    'table-image': ['Merged', 'Left', 'Right'],
    'nine-levels': ['Level' + str(i) for i in range(1, 10)],
    'masters': ['First body', 'Second body'],
}
report = {'reader': version, 'validator': '.'.join(map(str, etree.LXML_VERSION)), 'libxml': '.'.join(map(str, etree.LIBXML_VERSION)),
          'source': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip(),
          'working_tree_dirty': bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=root, text=True).strip()), 'schemas': pins, 'files': []}
for path in sorted(evidence.glob('*.odt')):
    with ZipFile(path) as archive:
        names = archive.namelist()
        assert names[0] == 'mimetype'
        assert archive.getinfo('mimetype').compress_type == 0
        assert archive.read('mimetype') == b'application/vnd.oasis.opendocument.text'
        for part in ['content.xml', 'styles.xml', 'meta.xml', 'META-INF/manifest.xml']:
            validator = validators['OpenDocument-v1.3-manifest-schema.rng' if part.startswith('META-INF') else 'OpenDocument-v1.3-schema.rng']
            validator.assertValid(etree.fromstring(archive.read(part)))
    output = evidence / 'office'
    output.mkdir(exist_ok=True)
    profile = evidence / 'office-profile'
    subprocess.run(['soffice', '-env:UserInstallation=' + profile.as_uri(), '--headless', '--convert-to', 'docx', '--outdir', str(output), str(path)], check=True, timeout=60)
    external = output / (path.stem + '.docx')
    with ZipFile(external) as archive:
        xml = etree.fromstring(archive.read('word/document.xml'))
        ns = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
        text = ''.join(xml.xpath('//w:t/text()', namespaces=ns))
        for fragment in expected[path.stem]:
            # Tabs/breaks remain explicit nodes in DOCX; inspect their presence separately.
            for token in fragment.split('\t'):
                assert token in text, (path, token, text)
        if path.stem == 'table-image':
            assert xml.xpath('//w:gridSpan[@w:val="2"]', namespaces=ns)
            wp = {'wp': 'http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing'}
            extent = xml.xpath('//wp:extent', namespaces=wp)[0]
            assert abs(int(extent.get('cx')) / 12700 - 60) <= 0.5
            assert abs(int(extent.get('cy')) / 12700 - 30) <= 0.5
            image_bytes = [archive.read(name) for name in archive.namelist() if name.startswith('word/media/')]
            original = (root / 'tests/wordnet.Conversion.Tests/Fixtures/color-grid.png').read_bytes()
            assert original in image_bytes
        if path.stem == 'list':
            numbering = etree.fromstring(archive.read('word/numbering.xml'))
            assert numbering.xpath('//w:start[@w:val="3"] | //w:startOverride[@w:val="3"]', namespaces=ns)
        if path.stem == 'nine-levels':
            assert {int(value) for value in xml.xpath('//w:numPr/w:ilvl/@w:val', namespaces=ns)} == set(range(9))
        if path.stem == 'masters':
            assert len(xml.xpath('//w:sectPr', namespaces=ns)) == 2
            widths = [int(value) for value in xml.xpath('//w:sectPr/w:pgSz/@w:w', namespaces=ns)]
            assert abs(widths[0] / 20 - 400) <= 0.5 and abs(widths[1] / 20 - 500) <= 0.5
            stories = ''.join(archive.read(name).decode() for name in archive.namelist() if name.startswith('word/header') or name.startswith('word/footer'))
            for phrase in ['First header', 'Second header', 'First footer', 'Second footer']:
                assert phrase in stories
    report['files'].append({'file': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'schema': 'pass', 'office': 'pass', 'external_sha256': hashlib.sha256(external.read_bytes()).hexdigest()})
assert len(report['files']) == 5
(evidence / 'verification.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps({'reader': version, 'validated': len(report['files'])}))
