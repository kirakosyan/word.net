"""Author a fixed-grid cell bookmark input without a wordnet serializer."""
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
    parser.add_argument('--implicit-margins', action='store_true', help='Reproduce the retained unqualified reference case.')
    args = parser.parse_args()
    destination = args.output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a scratch directory outside the committed fixture tree.')
    destination.mkdir(parents=True, exist_ok=True)
    path = destination / 'cell-template.docx'
    if path.exists():
        raise FileExistsError('Choose a fresh output directory.')
    placeholder = ('<w:p><w:bookmarkStart w:id="1" w:name="place"/><w:r><w:t>Insert</w:t></w:r>'
                   '<w:bookmarkEnd w:id="1"/></w:p>')
    cell = (reductions.p('Inside before') + placeholder + reductions.p('Inside after'))
    margins = '' if args.implicit_margins else ('<w:tcMar><w:top w:type="dxa" w:w="0"/><w:left w:type="dxa" w:w="0"/>'
                                                 '<w:bottom w:type="dxa" w:w="0"/><w:right w:type="dxa" w:w="240"/></w:tcMar>')
    table = ('<w:tbl><w:tblPr><w:tblW w:type="dxa" w:w="6000"/><w:tblLayout w:type="fixed"/>'
             '</w:tblPr><w:tblGrid><w:gridCol w:w="4000"/><w:gridCol w:w="2000"/></w:tblGrid>'
             '<w:tr><w:tc><w:tcPr><w:tcW w:type="dxa" w:w="4000"/>'
             '<w:shd w:val="clear" w:fill="DDEEFF"/>' + margins + '</w:tcPr>' + cell + '</w:tc>'
             '<w:tc><w:tcPr><w:tcW w:type="dxa" w:w="2000"/></w:tcPr>' + reductions.p('Neighbor') +
             '</w:tc></w:tr></w:tbl>')
    reductions.write_zip(path, reductions.package(reductions.p('Outside before') + table + reductions.p('Outside after')))
    manifest = {'provenance': 'Independently authored minimal OOXML and synthetic values; MIT.', 'implicitMargins': args.implicit_margins,
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                'templateSha256': hashlib.sha256(path.read_bytes()).hexdigest()}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
