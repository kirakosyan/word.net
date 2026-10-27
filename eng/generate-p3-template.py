#!/usr/bin/env python3
"""Create a deterministic, independently authored MIT master-detail DOCX template."""
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED
import hashlib
import json

root = Path(__file__).resolve().parent.parent
destination = root / 'examples/WordNet.Automation/Templates'
w = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
pr = 'http://schemas.openxmlformats.org/package/2006/relationships'

def field(name):
    return f'<w:fldSimple w:instr="MERGEFIELD {name}"><w:r><w:t>cached</w:t></w:r></w:fldSimple>'

def paragraph(content):
    return '<w:p>' + content + '</w:p>'

def control(path, content):
    return f'<w:sdt><w:sdtPr><w:tag w:val="repeat:{path}"/></w:sdtPr><w:sdtContent>{content}</w:sdtContent></w:sdt>'

table = '<w:tbl><w:tblGrid><w:gridCol w:w="4000"/></w:tblGrid><w:tr><w:tc><w:p>' + field('Amount') + '</w:p></w:tc></w:tr></w:tbl>'
child = control('Lines', paragraph(field('Description')) + table)
body = paragraph('<w:r><w:t>Customer statement</w:t></w:r>') + control('Customers', paragraph(field('Name')) + child + paragraph(field('Total')))
parts = {
    '[Content_Types].xml': '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>',
    '_rels/.rels': f'<Relationships xmlns="{pr}"><Relationship Id="main" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>',
    'word/document.xml': f'<w:document xmlns:w="{w}"><w:body>{body}</w:body></w:document>',
}
destination.mkdir(exist_ok=True)
path = destination / 'p3-statement.docx'
with ZipFile(path, 'w') as archive:
    for name, content in sorted(parts.items()):
        entry = ZipInfo(name, (2000, 1, 1, 0, 0, 0)); entry.compress_type = ZIP_DEFLATED
        archive.writestr(entry, content.encode())
(destination / 'p3-manifest.json').write_text(json.dumps({'file': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'license': 'MIT', 'generator': 'eng/generate-p3-template.py'}, indent=2) + '\n')
