"""Author small, independent DOCX inputs for roadmap workflow baseline probes."""
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
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    destination = args.output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a scratch output directory; committed fixtures must not be overwritten.')
    destination.mkdir(parents=True, exist_ok=True)
    placeholder = ('<w:p><w:bookmarkStart w:id="1" w:name="place"/><w:r><w:t>Insert</w:t></w:r>'
                   '<w:bookmarkEnd w:id="1"/></w:p>')
    table = ('<w:tbl><w:tblPr><w:tblW w:w="3000" w:type="dxa"/></w:tblPr>'
             '<w:tblGrid><w:gridCol w:w="3000"/></w:tblGrid><w:tr><w:tc>' + placeholder + '</w:tc></w:tr></w:tbl>')
    field = ('<w:p><w:fldSimple w:instr=" MERGEFIELD Amount "><w:r><w:t>Cached amount</w:t></w:r>'
             '</w:fldSimple></w:p>')
    history = ('<w:p><w:r><w:t xml:space="preserve">Total </w:t></w:r>'
               '<w:del w:id="3" w:author="Synthetic reviewer"><w:r><w:delText>10</w:delText></w:r></w:del>'
               '<w:ins w:id="4" w:author="Synthetic reviewer"><w:r><w:t>20</w:t></w:r></w:ins></w:p>')
    cases = {
        'fragment-body-target': placeholder,
        'fragment-cell-target': table,
        'fragment-field-source': field,
        'fragment-plain-source': reductions.p('Independent clause.'),
        'review-source': history,
    }
    rows = []
    for name, body in cases.items():
        path = destination / (name + '.docx')
        if path.exists():
            raise FileExistsError('Choose a fresh output directory: ' + str(path))
        reductions.write_zip(path, reductions.package(body))
        rows.append({'id': name, 'path': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    manifest = {
        'schemaVersion': 1,
        'provenance': 'Independently authored minimal OOXML, MIT; no document-library serializer or customer content.',
        'generator': 'eng/generate-roadmap-138-workflows.py',
        'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'dependencySha256': hashlib.sha256((ROOT / 'eng/generate-fidelity-reductions.py').read_bytes()).hexdigest(),
        'documents': rows,
    }
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(f'{len(rows)} workflow controls authored in {destination}')


if __name__ == '__main__':
    main()
