"""Author an independent three-level mixed-report DOCX in a scratch directory."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)


def field(name):
    return f'<w:fldSimple w:instr="MERGEFIELD {name}"><w:r><w:t>cached</w:t></w:r></w:fldSimple>'


def region(name, identity, body):
    return (f'<w:sdt><w:sdtPr><w:id w:val="{identity}"/><w:tag w:val="repeat:{name}"/>'
            f'</w:sdtPr><w:sdtContent>{body}</w:sdtContent></w:sdt>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    destination = parser.parse_args().output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a scratch directory outside the committed fixture tree.')
    destination.mkdir(parents=True, exist_ok=True)
    path = destination / 'deep-regions.docx'
    if path.exists():
        raise FileExistsError('Choose a fresh output directory.')
    cell = ('<w:tbl><w:tblPr><w:tblW w:w="3000" w:type="dxa"/><w:tblLayout w:type="fixed"/>'
            '</w:tblPr><w:tblGrid><w:gridCol w:w="3000"/></w:tblGrid><w:tr><w:tc>'
            '<w:tcPr><w:tcW w:w="3000" w:type="dxa"/></w:tcPr><w:p>' + field('Customer') +
            '</w:p></w:tc></w:tr></w:tbl>')
    leaf = ('<w:p><w:bookmarkStart w:id="7" w:name="line"/>' + field('Product') +
            '<w:bookmarkEnd w:id="7"/></w:p>' + cell + '<w:p>' + field('Global') + '</w:p>')
    middle = '<w:p>' + field('Group') + '</w:p>' + region('Units', 30, leaf) + reductions.p('End group')
    outer = '<w:p>' + field('Customer') + '</w:p>' + region('Groups', 20, middle) + reductions.p('End customer')
    body = reductions.p('Before') + region('Orders', 10, outer) + reductions.p('After')
    reductions.write_zip(path, reductions.package(body))
    manifest = {'provenance': 'Independently authored minimal OOXML and synthetic data; MIT.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                'templateSha256': hashlib.sha256(path.read_bytes()).hexdigest()}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
