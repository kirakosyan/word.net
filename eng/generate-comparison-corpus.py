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
 ('invoice-nested', 'invoice', ['INVOICE N-202', 'Hardware bundle', 'Adapter', '80 EUR', 'Total 480 EUR'], [],
  p('INVOICE N-202') + table([[p('Hardware bundle') + table([[p('Adapter'), p('80 EUR')]]), p('Total 480 EUR')]], decoration=True), {}, '', ''),
 ('invoice-relative', 'invoice', ['INVOICE P-303', 'Delivery', '250 EUR', 'Tax 50 EUR', 'Total 300 EUR'], [],
  p('INVOICE P-303') + table([[p('Delivery'), p('250 EUR')], [p('Tax 50 EUR'), p('Total 300 EUR')]], width='pct'), {}, '', ''),
 ('contract-services', 'contract', ['SERVICE AGREEMENT', 'Parties: Example North and Example South', 'Scope of work', 'Deliver the reviewed prototype.', 'Signature: Authorised representative'], [],
  p('SERVICE AGREEMENT', run='<w:b/>') + p('Parties: Example North and Example South') + p('Scope of work') + p('Deliver the reviewed prototype.') + p('Signature: Authorised representative'), {}, '', ''),
 ('contract-legal', 'contract', ['SUPPLY CONTRACT', 'Delivery obligations', 'Acceptance criteria', 'Liability cap is 5000 EUR.'], [],
  p('SUPPLY CONTRACT') + p('Delivery obligations', '<w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr>') + p('Acceptance criteria', '<w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr>') + p('Liability cap is 5000 EUR.'),
  {'word/numbering.xml': f'<w:numbering xmlns:w="{W}"><w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="upperRoman"/><w:lvlText w:val="%1)"/></w:lvl></w:abstractNum><w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num></w:numbering>'}, 'numbering', ''),
 ('contract-revisions', 'contract', ['LEASE ADDENDUM', 'Monthly rent 900 EUR', 'Accepted term: twelve months', 'End of addendum'], ['Revisions.Accepted'],
  p('LEASE ADDENDUM') + p('Monthly rent 900 EUR') + '<w:p><w:ins w:id="1" w:author="Synthetic"><w:r><w:t>Accepted term: twelve months</w:t></w:r></w:ins><w:del w:id="2" w:author="Synthetic"><w:r><w:delText>Superseded term</w:delText></w:r></w:del></w:p>' + p('End of addendum'), {}, '', ''),
 ('report-quarterly', 'report', ['QUARTERLY OPERATIONS', 'Revenue 24000 EUR', 'Completed projects 18', 'Next quarter: expand support.'], [],
  p('QUARTERLY OPERATIONS', run='<w:b/><w:sz w:val="32"/>') + table([[p('Revenue 24000 EUR'), p('Completed projects 18')]]) + p('Next quarter: expand support.'), {}, '', ''),
 ('report-columns', 'report', ['RESEARCH SUMMARY', 'Observation one: response improved.', 'Observation two: review remains open.'], [],
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

# P0 extension: two minimal independently authored cases per requested feature.
# Expectations are fixed here, before measuring either converter or office output.
new_pages = {}
features = {}
for suffix, count in [('short', 1), ('report', 3)]:
    name = 'mixed-fonts-' + suffix
    texts = [f'FONT STUDY {suffix.upper()}', 'Regular face', 'Bold face', 'Italic face']
    body = (p(texts[0]) + p(texts[1])
            + p(texts[2], run='<w:rFonts w:ascii="Liberation Serif" w:hAnsi="Liberation Serif"/><w:b/>')
            + p(texts[3], run='<w:rFonts w:ascii="Liberation Mono" w:hAnsi="Liberation Mono"/><w:i/>'))
    if count > 1:
        for page in range(2, count + 1):
            texts.append(f'Font appendix {page}')
            body += p(texts[-1], '<w:pageBreakBefore/>', '<w:sz w:val="28"/>')
    cases.append((name, 'report', texts, [], body, {}, '', ''))
    new_pages[name] = count
    features[name] = 'mixed-fonts'

for suffix, rows_count in [('ledger', 48), ('inventory', 54)]:
    name = 'long-table-' + suffix
    texts = [f'{suffix.upper()} REGISTER', 'Reference', 'Amount']
    data = [[p('Reference'), p('Amount')]]
    for number in range(1, rows_count + 1):
        row = [f'Entry {number:03d}', f'{number * 10} EUR']
        texts.extend(row)
        data.append([p(value) for value in row])
    body = p(texts[0]) + table(data).replace('<w:tr>', '<w:tr><w:trPr><w:tblHeader/></w:trPr>', 1)
    cases.append((name, 'report', texts, [], body, {}, '', ''))
    new_pages[name] = 2
    features[name] = 'long-tables'

for suffix, notes_count in [('contract', 1), ('report', 2)]:
    name = 'footnotes-' + suffix
    texts = [f'{suffix.upper()} WITH NOTES']
    body = p(texts[0])
    notes = ''
    for number in range(1, notes_count + 1):
        text = f'Clause {number} has a note.'
        texts.append(text)
        body += p(text).replace('</w:p>', f'<w:r><w:footnoteReference w:id="{number}"/></w:r></w:p>')
        notes += f'<w:footnote w:id="{number}">' + p(f'Independent note {number}.') + '</w:footnote>'
    texts.extend(f'Independent note {number}.' for number in range(1, notes_count + 1))
    cases.append((name, 'contract' if suffix == 'contract' else 'report', texts, [], body,
                  {'word/footnotes.xml': f'<w:footnotes xmlns:w="{W}">{notes}</w:footnotes>'}, 'footnotes', ''))
    new_pages[name] = 1
    features[name] = 'footnotes'

for suffix, columns in [('same-width', ''), ('two-columns', '<w:cols w:num="2"/>')]:
    name = 'continuous-' + suffix
    texts = ['CONTINUOUS SECTION', 'Before section boundary.', 'After section boundary.']
    boundary = '<w:sectPr><w:type w:val="continuous"/><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>' + columns + '</w:sectPr>'
    body = p(texts[0]) + p(texts[1], boundary) + p(texts[2])
    cases.append((name, 'report', texts, [], body, {}, '', '<w:type w:val="continuous"/>' + columns))
    new_pages[name] = 1
    features[name] = 'continuous-sections'

for suffix, x in [('left', 0), ('right', 3000000)]:
    name = 'floating-logo-' + suffix
    texts = ['EXAMPLE ENGINEERING', 'Service notice with positioned logo.', 'End of notice.']
    # ECMA-376 DrawingML anchor; the raster is our existing MIT-authored fixture.
    anchor = f'''<w:p><w:r><w:drawing><wp:anchor xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" distT="0" distB="0" distL="0" distR="0" simplePos="0" relativeHeight="0" behindDoc="0" locked="0" layoutInCell="1" allowOverlap="1">
    <wp:simplePos x="0" y="0"/><wp:positionH relativeFrom="column"><wp:posOffset>{x}</wp:posOffset></wp:positionH><wp:positionV relativeFrom="paragraph"><wp:posOffset>0</wp:posOffset></wp:positionV>
    <wp:extent cx="762000" cy="381000"/><wp:wrapSquare wrapText="bothSides"/><wp:docPr id="1" name="Synthetic logo"/><wp:cNvGraphicFramePr/>
    <a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="1" name="Synthetic logo"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="image"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="762000" cy="381000"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r></w:p>'''
    cases.append((name, 'letterhead', texts, [], p(texts[0]) + anchor + p(texts[1]) + p(texts[2]),
                  {'word/media/logo.png': (DEST.parent / 'color-grid.png').read_bytes()}, 'image', ''))
    new_pages[name] = 1
    features[name] = 'floating-logos'

for suffix, count in [('operations', 3), ('audit', 4)]:
    name = 'multipage-' + suffix
    texts, body = [], ''
    for page in range(1, count + 1):
        title = f'{suffix.upper()} PAGE {page}'
        detail = f'Reviewed section {page}: synthetic results only.'
        texts.extend([title, detail])
        body += p(title, '<w:pageBreakBefore/>' if page > 1 else '', '<w:b/>') + p(detail)
    cases.append((name, 'report', texts, [], body, {}, '', ''))
    new_pages[name] = count
    features[name] = 'multi-page-reports'

rows = []
for name, category, text, losses, body, extras, relation, section in cases:
    # One distinct font request per rPr (last wins in this authored helper).
    import re
    body = re.sub(r'(<w:rFonts[^>]*/>)(<w:rFonts[^>]*/>)', r'\2', body)
    rel = f'<Relationship Id="{relation}" Type="{R}/{relation}" Target="{relation}.xml"/>' if relation else ''
    if relation == 'image':
        rel = f'<Relationship Id="image" Type="{R}/image" Target="media/logo.png"/>'
        overrides = '<Default Extension="png" ContentType="image/png"/>'
    else:
        overrides = ''.join(f'<Override PartName="/{part}" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.{relation}+xml"/>' for part in extras)
    section_start = section if relation == 'header' else ''
    section_end = section if relation != 'header' else ''
    if section.startswith('<w:type'):
        section_start, section_end = section.split('/>', 1)
        section_start += '/>'
    parts = {
        '[Content_Types].xml': f'<Types xmlns="{CT}"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>{overrides}</Types>',
        '_rels/.rels': f'<Relationships xmlns="{PR}"><Relationship Id="main" Type="{R}/officeDocument" Target="word/document.xml"/></Relationships>',
        'word/_rels/document.xml.rels': f'<Relationships xmlns="{PR}">{rel}</Relationships>',
        'word/document.xml': f'<w:document xmlns:w="{W}" xmlns:r="{R}"><w:body>{body}<w:sectPr>{section_start}<w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720"/>{section_end}</w:sectPr></w:body></w:document>', **extras}
    path = DEST / (name + '.docx')
    with ZipFile(path, 'w', ZIP_DEFLATED, compresslevel=9) as archive:
        for key, value in sorted(parts.items()):
            info = ZipInfo(key, (2000, 1, 1, 0, 0, 0)); info.compress_type = ZIP_DEFLATED
            archive.writestr(info, value if isinstance(value, bytes) else value.encode())
    row = dict(id=name, category=category, path=path.name, sha256=hashlib.sha256(path.read_bytes()).hexdigest(), requiredText=text, knownLosses=losses, expectedPages=new_pages.get(name, 2 if name == 'report-pages' else 1), references=[])
    if name in features:
        row.update(feature=features[name], cohort='p0-2026-10', expectationBasis='authored-source-expectations')
    rows.append(row)
fonts = [dict(path=p.relative_to(ROOT).as_posix(), sha256=hashlib.sha256(p.read_bytes()).hexdigest(), version='Noto Sans 2.008; SIL OFL 1.1') for p in sorted((ROOT / 'src/wordnet/Resources/Fonts').glob('*.ttf'))]
fonts.extend(dict(path=p.relative_to(ROOT).as_posix(), sha256=hashlib.sha256(p.read_bytes()).hexdigest(), version='Liberation 2.1.5; SIL OFL 1.1')
             for p in sorted((ROOT / 'src/wordnet.Fonts/Resources').glob('Liberation*.ttf')))
manifest = dict(schemaVersion=1, provenance='Independently authored synthetic examples, MIT; no customer data or proprietary resources.', generator='eng/generate-comparison-corpus.py', generatorSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), fonts=fonts, documents=rows)
(DEST / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8', newline='\n')
