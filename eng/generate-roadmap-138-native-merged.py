#!/usr/bin/env python3
"""Author a small independent multilingual table with horizontal and vertical merges."""

import argparse
import importlib.util
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', required=True, type=Path)
parser.add_argument('--hindi-only', action='store_true', help='Use Hindi in the merged cell and Latin in the adjacent cell.')
args = parser.parse_args()
destination = args.output.resolve()
fixture_root = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
if destination == fixture_root or fixture_root in destination.parents:
    parser.error('Choose a new scratch path outside the fixture tree.')
if destination.exists():
    raise FileExistsError(destination)


def paragraph(value, family='Noto Sans', rtl=False):
    ppr = ('<w:bidi/>' if rtl else '') + reductions.EXPLICIT
    fonts = f'<w:rFonts w:ascii="{family}" w:hAnsi="{family}" w:cs="{family}"/>'
    return f'<w:p><w:pPr>{ppr}</w:pPr><w:r><w:rPr>{fonts}<w:sz w:val="22"/></w:rPr><w:t>{escape(value)}</w:t></w:r></w:p>'


def cell(value, properties='', family='Noto Sans', rtl=False):
    return '<w:tc><w:tcPr>' + properties + '</w:tcPr>' + paragraph(value, family, rtl) + '</w:tc>'


body = '<w:tbl><w:tblPr><w:tblW w:w="5400" w:type="dxa"/><w:tblLayout w:type="fixed"/></w:tblPr>'
body += '<w:tblGrid>' + '<w:gridCol w:w="1800"/>' * 3 + '</w:tblGrid>'
body += '<w:tr><w:trPr><w:tblHeader/></w:trPr>' + cell('Invoice', '<w:gridSpan w:val="2"/>') + cell('Amount') + '</w:tr>'
merged = cell('हिन्दी क्षि', '<w:vMerge w:val="restart"/>', 'Noto Sans Devanagari') if args.hindi_only else cell('مرحبا عربي', '<w:vMerge w:val="restart"/>', 'Noto Sans Arabic', True)
body += '<w:tr>' + merged + cell('A-100') + cell('3.50') + '</w:tr>'
detail = cell('Detail') if args.hindi_only else cell('हिन्दी क्षि', family='Noto Sans Devanagari')
body += '<w:tr>' + cell('', '<w:vMerge/>') + detail + cell('7.25') + '</w:tr>'
body += '<w:tr>' + cell('Total', '<w:gridSpan w:val="2"/>') + cell('10.75') + '</w:tr></w:tbl>'
destination.parent.mkdir(parents=True, exist_ok=True)
reductions.write_zip(destination, reductions.package(body))
print('Created an independently authored merged multilingual table.')
