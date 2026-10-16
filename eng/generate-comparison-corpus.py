"""Author synthetic business inputs from published WordprocessingML (no Office needed)."""
import hashlib
import json
from pathlib import Path
from xml.sax.saxutils import escape
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Comparison'
W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
R = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
PR = 'http://schemas.openxmlformats.org/package/2006/relationships'
CT = 'http://schemas.openxmlformats.org/package/2006/content-types'
DEST.mkdir(exist_ok=True)


def p(text, props='', run=''):
    return f'<w:p><w:pPr>{props}</w:pPr><w:r><w:rPr><w:rFonts w:ascii="Noto Sans" w:hAnsi="Noto Sans"/>{run}</w:rPr><w:t xml:space="preserve">{escape(text)}</w:t></w:r></w:p>'


def table(rows, width='dxa', decoration=False):
    cell = '<w:tcBorders><w:bottom w:val="single" w:sz="8" w:color="224466"/></w:tcBorders><w:shd w:val="clear" w:fill="DDEEFF"/>' if decoration else ''
    return f'<w:tbl><w:tblPr><w:tblW w:type="{width}" w:w="5000"/></w:tblPr><w:tblGrid><w:gridCol w:w="2500"/><w:gridCol w:w="2500"/></w:tblGrid>' + ''.join('<w:tr>' + ''.join(f'<w:tc><w:tcPr>{cell}</w:tcPr>{value}</w:tc>' for value in row) + '</w:tr>' for row in rows) + '</w:tbl>'


cases = [
 ('invoice-services', 'invoice', ['INVOICE S-101', 'Consulting services', 'Discovery workshop', '1200 EUR', 'Payment due in 30 days'], [],
  p('INVOICE S-101', run='<w:b/><w:sz w:val="32"/>') + p('Consulting services') + table([[p('Discovery workshop'), p('1200 EUR')]]) + p('Payment due in 30 days'), {}, '', ''),
 ('invoice-nested', 'invoice', ['INVOICE N-202', 'Hardware bundle', 'Adapter', '80 EUR', 'Total 480 EUR'], ['Table.NestedFlattened', 'Table.Decoration'],
  p('INVOICE N-202') + table([[p('Hardware bundle') + table([[p('Adapter'), p('80 EUR')]]), p('Total 480 EUR')]], decoration=True), {}, '', ''),
 ('invoice-relative', 'invoice', ['INVOICE P-303', 'Delivery', '250 EUR', 'Tax 50 EUR', 'Total 300 EUR'], ['Docx.RelativeWidth'],
  p('INVOICE P-303') + table([[p('Delivery'), p('250 EUR')], [p('Tax 50 EUR'), p('Total 300 EUR')]], width='pct'), {}, '', ''),
 ('contract-services', 'contract', ['SERVICE AGREEMENT', 'Parties: Example North and Example South', 'Scope of work', 'Deliver the reviewed prototype.', 'Signature: Authorised representative'], [],
  p('SERVICE AGREEMENT', run='<w:b/>') + p('Parties: Example North and Example South') + p('Scope of work') + p('Deliver the reviewed prototype.') + p('Signature: Authorised representative'), {}, '', ''),
 ('contract-legal', 'contract', ['SUPPLY CONTRACT', 'Delivery obligations', 'Acceptance criteria', 'Liability cap is 5000 EUR.'], ['Docx.ListFormat', 'Docx.ListLabel'],
  p('SUPPLY CONTRACT') + p('Delivery obligations', '<w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr>') + p('Acceptance criteria', '<w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr>') + p('Liability cap is 5000 EUR.'),
  {'word/numbering.xml': f'<w:numbering xmlns:w="{W}"><w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="upperRoman"/><w:lvlText w:val="%1)"/></w:lvl></w:abstractNum><w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num></w:numbering>'}, 'numbering', ''),
 ('contract-revisions', 'contract', ['LEASE ADDENDUM', 'Monthly rent 900 EUR', 'Accepted term: twelve months', 'End of addendum'], ['Revisions.Accepted'],
  p('LEASE ADDENDUM') + p('Monthly rent 900 EUR') + '<w:p><w:ins w:id="1" w:author="Synthetic"><w:r><w:t>Accepted term: twelve months</w:t></w:r></w:ins><w:del w:id="2" w:author="Synthetic"><w:r><w:delText>Superseded term</w:delText></w:r></w:del></w:p>' + p('End of addendum'), {}, '', ''),
 ('report-quarterly', 'report', ['QUARTERLY OPERATIONS', 'Revenue 24000 EUR', 'Completed projects 18', 'Next quarter: expand support.'], [],
  p('QUARTERLY OPERATIONS', run='<w:b/><w:sz w:val="32"/>') + table([[p('Revenue 24000 EUR'), p('Completed projects 18')]]) + p('Next quarter: expand support.'), {}, '', ''),
 ('report-columns', 'report', ['RESEARCH SUMMARY', 'Observation one: response improved.', 'Observation two: review remains open.'], ['Docx.PageLayout'],
  p('RESEARCH SUMMARY') + p('Observation one: response improved.') + p('Observation two: review remains open.'), {}, '', '<w:cols w:num="2"/>'),
 ('report-pages', 'report', ['ANNUAL REVIEW', 'Results for the year', 'APPENDIX', 'Independent audit completed'], [],
  p('ANNUAL REVIEW') + p('Results for the year') + p('APPENDIX', '<w:pageBreakBefore/>') + p('Independent audit completed'), {}, '', ''),
 ('letterhead-offer', 'letterhead', ['OFFER OF SERVICES', 'Dear procurement team,', 'We offer scheduled maintenance.', 'Sincerely, Example Engineering'], [],
  p('OFFER OF SERVICES') + p('Dear procurement team,') + p('We offer scheduled maintenance.') + p('Sincerely, Example Engineering'), {'word/header.xml': f'<w:hdr xmlns:w="{W}">{p("EXAMPLE ENGINEERING")}</w:hdr>'}, 'header', '<w:headerReference w:type="default" r:id="header"/>'),
 ('letterhead-brand', 'letterhead', ['BRAND NOTICE', 'Our service address has changed.', 'Please update your records.'], ['Font.Substituted'],
  p('BRAND NOTICE', run='<w:rFonts w:ascii="Synthetic Missing Brand" w:hAnsi="Synthetic Missing Brand"/>') + p('Our service address has changed.') + p('Please update your records.'), {}, '', ''),
 ('letterhead-textbox', 'letterhead', ['DELIVERY NOTICE', 'Routing label', 'Dispatch reference SYN-404', 'Retain this notice.'], ['Docx.Inline'],
  p('DELIVERY NOTICE') + '<w:p><w:r><w:pict xmlns:v="urn:schemas-microsoft-com:vml"><v:shape id="synthetic-box" style="position:absolute;left:200pt;top:40pt"><v:textbox><w:txbxContent>' + p('Routing label') + '</w:txbxContent></v:textbox></v:shape></w:pict></w:r></w:p>' + p('Dispatch reference SYN-404') + p('Retain this notice.'), {}, '', ''),
]

rows = []
for name, category, text, losses, body, extras, relation, section in cases:
    # One distinct font request per rPr (last wins in this authored helper).
    import re
    body = re.sub(r'(<w:rFonts[^>]*/>)(<w:rFonts[^>]*/>)', r'\2', body)
    rel = f'<Relationship Id="{relation}" Type="{R}/{relation}" Target="{relation}.xml"/>' if relation else ''
    overrides = ''.join(f'<Override PartName="/{part}" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.{relation}+xml"/>' for part in extras)
    parts = {
        '[Content_Types].xml': f'<Types xmlns="{CT}"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>{overrides}</Types>',
        '_rels/.rels': f'<Relationships xmlns="{PR}"><Relationship Id="main" Type="{R}/officeDocument" Target="word/document.xml"/></Relationships>',
        'word/_rels/document.xml.rels': f'<Relationships xmlns="{PR}">{rel}</Relationships>',
        'word/document.xml': f'<w:document xmlns:w="{W}" xmlns:r="{R}"><w:body>{body}<w:sectPr>{section if relation == 'header' else ''}<w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720"/>{section if relation != 'header' else ''}</w:sectPr></w:body></w:document>', **extras}
    path = DEST / (name + '.docx')
    with ZipFile(path, 'w', ZIP_DEFLATED, compresslevel=9) as archive:
        for key, value in sorted(parts.items()):
            info = ZipInfo(key, (2000, 1, 1, 0, 0, 0)); info.compress_type = ZIP_DEFLATED
            archive.writestr(info, value.encode())
    rows.append(dict(id=name, category=category, path=path.name, sha256=hashlib.sha256(path.read_bytes()).hexdigest(), requiredText=text, knownLosses=losses, expectedPages=2 if name == 'report-pages' else 1, references=[]))
fonts = [dict(path=str(p.relative_to(ROOT)), sha256=hashlib.sha256(p.read_bytes()).hexdigest(), version='Noto Sans 2.008; SIL OFL 1.1') for p in sorted((ROOT / 'src/wordnet/Resources/Fonts').glob('*.ttf'))]
manifest = dict(schemaVersion=1, provenance='Independently authored synthetic examples, MIT; no customer data or proprietary resources.', generator='eng/generate-comparison-corpus.py', generatorSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), fonts=fonts, documents=rows)
(DEST / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
