"""Independently author the MIT invoice/report templates from ECMA-376 markup."""
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'examples/WordNet.Automation/Templates'
W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
R = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
PR = 'http://schemas.openxmlformats.org/package/2006/relationships'
CT = 'http://schemas.openxmlformats.org/package/2006/content-types'


def p(text='', heading=False):
    return '<w:p><w:pPr><w:spacing w:after="80"/></w:pPr><w:r><w:rPr>' + ('<w:b/>' if heading else '') + '</w:rPr><w:t xml:space="preserve">' + text + '</w:t></w:r></w:p>'


def field(instruction, cached='UNFILLED'):
    return f'<w:fldSimple w:instr="{instruction}"><w:r><w:t>{cached}</w:t></w:r></w:fldSimple>'


def merge(name):
    return '<w:p>' + field('MERGEFIELD ' + name) + '</w:p>'


def control(tag, content):
    return f'<w:sdt><w:sdtPr><w:tag w:val="{tag}"/></w:sdtPr><w:sdtContent>{content}</w:sdtContent></w:sdt>'


def row(cells, header=False):
    return '<w:tr>' + ('<w:trPr><w:tblHeader/></w:trPr>' if header else '') + ''.join('<w:tc>' + value + '</w:tc>' for value in cells) + '</w:tr>'


def table(headers, fields):
    return ('<w:tbl><w:tblPr><w:tblW w:w="10000" w:type="dxa"/></w:tblPr><w:tblGrid>'
            + ''.join(f'<w:gridCol w:w="{10000 // len(headers)}"/>' for _ in headers) + '</w:tblGrid>'
            + row([p(value, True) for value in headers], True)
            + control('repeat:Items', row([merge(value) for value in fields])) + '</w:tbl>')


def section():
    return ('<w:sectPr><w:headerReference w:type="default" r:id="header"/><w:footerReference w:type="default" r:id="footer"/>'
            '<w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1200" w:right="960" w:bottom="1200" w:left="960" w:header="480" w:footer="480"/></w:sectPr>')


invoice = (p('INVOICE INV-2026-001', True) + merge('Customer') + merge('Issued')
           + control('image:Logo', p('Caller logo'))
           + table(['Description', 'Quantity', 'Unit price (EUR)', 'Line total (EUR)'], ['Description', 'Quantity', 'UnitPrice', 'LineTotal'])
           + p('Total due (EUR)', True) + merge('Total')
           + control('if:ShowTerms', p('Payment is due within 30 days.')) + section())
report = (p('OPERATIONS REPORT', True) + merge('Customer') + merge('Issued')
          + p('Executive summary', True) + merge('Summary')
          + table(['Measure', 'Result'], ['Description', 'Value'])
          + '<w:p><w:pPr>' + section() + '</w:pPr></w:p>'
          + p('Delivery plan', True) + merge('Plan') + p('Review complete.') + section())
DEST.mkdir(exist_ok=True)
documents = []
for name, body in [('invoice', invoice), ('report', report)]:
    parts = {
        '[Content_Types].xml': f'<Types xmlns="{CT}"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>' + ''.join(f'<Override PartName="/word/{part}.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.{kind}+xml"/>' for part, kind in [('document', 'document.main'), ('styles', 'styles'), ('header', 'header'), ('footer', 'footer')]) + '</Types>',
        '_rels/.rels': f'<Relationships xmlns="{PR}"><Relationship Id="main" Type="{R}/officeDocument" Target="word/document.xml"/></Relationships>',
        'word/_rels/document.xml.rels': f'<Relationships xmlns="{PR}">' + ''.join(f'<Relationship Id="{part}" Type="{R}/{part}" Target="{part}.xml"/>' for part in ['styles', 'header', 'footer']) + '</Relationships>',
        'word/styles.xml': f'<w:styles xmlns:w="{W}"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Noto Sans" w:hAnsi="Noto Sans"/><w:sz w:val="22"/></w:rPr></w:rPrDefault></w:docDefaults></w:styles>',
        'word/header.xml': f'<w:hdr xmlns:w="{W}">' + p('EXAMPLE ENGINEERING') + '</w:hdr>',
        'word/footer.xml': f'<w:ftr xmlns:w="{W}"><w:p><w:r><w:t xml:space="preserve">Page </w:t></w:r>' + field('PAGE', '99') + '<w:r><w:t xml:space="preserve"> of </w:t></w:r>' + field('NUMPAGES', '99') + '</w:p></w:ftr>',
        'word/document.xml': f'<w:document xmlns:w="{W}" xmlns:r="{R}"><w:body>{body}</w:body></w:document>'}
    path = DEST / (name + '.docx')
    with ZipFile(path, 'w', ZIP_DEFLATED, compresslevel=9) as archive:
        for key, value in sorted(parts.items()):
            info = ZipInfo(key, (2000, 1, 1, 0, 0, 0))
            info.compress_type = ZIP_DEFLATED
            archive.writestr(info, value.encode())
    documents.append(dict(id=name, path=path.name, sha256=hashlib.sha256(path.read_bytes()).hexdigest(), expectedPages=1 if name == 'invoice' else 2))
(DEST / 'manifest.json').write_text(json.dumps(dict(schemaVersion=1, provenance='Independently authored synthetic templates, MIT; no customer/private content.', generator='eng/generate-automation-templates.py', generatorSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), documents=documents), indent=2) + '\n', encoding='utf-8', newline='\n')
