"""Independently author simple single-row and single-cell control wrappers."""
import argparse
import importlib.util
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--repository', default=Path(__file__).resolve().parents[1], type=Path)
parser.add_argument('--output', required=True, type=Path)
args = parser.parse_args()
fixture_root = (args.repository / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
destination = args.output.resolve()
if destination == fixture_root or fixture_root in destination.parents:
    parser.error('Choose a fresh scratch output outside the fixture tree.')
spec = importlib.util.spec_from_file_location('reductions', args.repository / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)

def cell(text):
    return '<w:tc><w:tcPr><w:tcW w:type="dxa" w:w="2400"/></w:tcPr>' + reductions.p(text, reductions.EXPLICIT, 22) + '</w:tc>'

def wrap(content):
    return '<w:sdt><w:sdtPr><w:id w:val="7"/><w:richText/></w:sdtPr><w:sdtContent>' + content + '</w:sdtContent></w:sdt>'

row = '<w:tr>' + cell('A') + cell('B') + '</w:tr>'
second = '<w:tr>' + cell('C') + cell('D') + '</w:tr>'
prefix = '<w:tbl><w:tblPr><w:tblW w:type="dxa" w:w="4800"/><w:tblLayout w:type="fixed"/></w:tblPr><w:tblGrid><w:gridCol w:w="2400"/><w:gridCol w:w="2400"/></w:tblGrid>'
cases = {'row': prefix + wrap(row) + second + '</w:tbl>',
         'cell': prefix + '<w:tr>' + wrap(cell('A')) + cell('B') + '</w:tr>' + second + '</w:tbl>'}
args.output.mkdir(parents=True, exist_ok=True)
for name, body in cases.items():
    path = args.output / (name + '.docx')
    if path.exists():
        raise FileExistsError(path)
    reductions.write_zip(path, reductions.package(body))
print('Created two synthetic structural-control DOCX inputs.')
