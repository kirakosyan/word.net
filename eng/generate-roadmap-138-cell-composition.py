#!/usr/bin/env python3
"""Independently author template and bookmark owners for cell-control composition."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('cells', ROOT / 'eng/generate-roadmap-138-cell-block-controls.py')
cells = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cells)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    target = parser.parse_args().output.resolve()
    fixture_tree = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if target == fixture_tree or fixture_tree in target.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    target.mkdir(parents=True, exist_ok=False)
    source = cells.reductions.p('SourceBefore') + cells.table([cells.cell() * 3, cells.cell() * 3]) + cells.reductions.p('SourceAfter')
    host = ('<w:p><w:r><w:t>HostStart</w:t></w:r><w:sdt><w:sdtPr><w:id w:val="1"/>'
            '<w:tag w:val="host"/><w:text/></w:sdtPr><w:sdtContent><w:r><w:t>Host</w:t></w:r>'
            '</w:sdtContent></w:sdt></w:p>')
    slot = ('<w:p><w:bookmarkStart w:id="1" w:name="slot"/><w:r><w:t>Placeholder</w:t></w:r>'
            '<w:bookmarkEnd w:id="1"/></w:p>')
    body = host + slot + cells.reductions.p('HostEnd')
    cell_target = host + cells.table([cells.cell(slot) + cells.cell() * 2]) + cells.reductions.p('HostEnd')
    hashes = {}
    for name, content in {'source': source, 'body-target': body, 'cell-target': cell_target}.items():
        path = target / (name + '.docx')
        cells.reductions.write_zip(path, cells.reductions.package(content))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with MIT fixture helpers; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes}
    (target / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
