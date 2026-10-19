#!/usr/bin/env python3
"""Author a synthetic report template directly from ECMA-376 XML (MIT)."""
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED
import hashlib
import json

root = Path(__file__).resolve().parent.parent
dest = root / 'examples/WordNet.Automation/Templates'
w = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
pr = 'http://schemas.openxmlformats.org/package/2006/relationships'


def field(name):
    return f'<w:p><w:fldSimple w:instr="MERGEFIELD {name}"><w:r><w:t>cached</w:t></w:r></w:fldSimple></w:p>'


def control(tag, content):
    return f'<w:sdt><w:sdtPr><w:tag w:val="{tag}"/></w:sdtPr><w:sdtContent>{content}</w:sdtContent></w:sdt>'


body = '<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Grouped revenue</w:t></w:r></w:p>'
body += control('repeat:Groups', field('Key') + field('Sum') + field('Count'))
body += field('Total') + control('if:ShowTerms', '<w:p><w:r><w:t>Immediate payment</w:t></w:r></w:p>')
parts = {
    '[Content_Types].xml': '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>',
    '_rels/.rels': f'<Relationships xmlns="{pr}"><Relationship Id="main" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>',
    'word/document.xml': f'<w:document xmlns:w="{w}"><w:body>{body}</w:body></w:document>',
}
dest.mkdir(exist_ok=True)
path = dest / 'p1-report.docx'
with ZipFile(path, 'w') as archive:
    for name, content in sorted(parts.items()):
        entry = ZipInfo(name, (2000, 1, 1, 0, 0, 0)); entry.compress_type = ZIP_DEFLATED
        archive.writestr(entry, content.encode())
(dest / 'p1-manifest.json').write_text(json.dumps({'file': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'license': 'MIT', 'generator': 'eng/generate-p1-templates.py'}, indent=2) + '\n')
