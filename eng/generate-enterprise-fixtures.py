"""Independent deterministic WordprocessingML fixtures; no production wordnet code."""
import csv
import hashlib
import io
import re
import zipfile
from PIL import Image
from html import escape
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Enterprise'
W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
R = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
PR = 'http://schemas.openxmlformats.org/package/2006/relationships'
CT = 'http://schemas.openxmlformats.org/package/2006/content-types'
ROWS = []
GEOMETRY = "<w:pgSz w:w='11906' w:h='16838'/><w:pgMar w:top='1440' w:bottom='1440' w:left='1440' w:right='1440' w:header='360' w:footer='360'/>"


def p(text, props='', run=''):
    return f"<w:p><w:pPr>{props}</w:pPr><w:r><w:rPr>{run}</w:rPr><w:t xml:space='preserve'>{escape(text)}</w:t></w:r></w:p>"


def field(code, cached=''):
    return f'<w:fldSimple w:instr="{escape(code, quote=True)}"><w:r><w:t>{escape(cached)}</w:t></w:r></w:fldSimple>'


def table(content, cell_props=''):
    return "<w:tbl><w:tblPr><w:tblW w:w='6000' w:type='dxa'/></w:tblPr><w:tblGrid><w:gridCol w:w='6000'/></w:tblGrid><w:tr><w:tc><w:tcPr>" + cell_props + '</w:tcPr>' + content + '</w:tc></w:tr></w:tbl>'


def write(name, body, pages=1, expected='Enterprise', section='', extras=None, relationships='', outcome='convert'):
    DEST.mkdir(parents=True, exist_ok=True)
    references = re.findall(r'<w:(?:header|footer)Reference\b[^>]*/>', section)
    other = re.sub(r'<w:(?:header|footer)Reference\b[^>]*/>', '', section).replace('<w:titlePg/>', '')
    normalized_section = ''.join(sorted(references, key=lambda ref: 0 if ref.startswith('<w:header') else 1)) + other + GEOMETRY + ('<w:titlePg/>' if '<w:titlePg/>' in section else '')
    parts = {
        '[Content_Types].xml': f"<Types xmlns='{CT}'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='xml' ContentType='application/xml'/><Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/></Types>",
        '_rels/.rels': f"<Relationships xmlns='{PR}'><Relationship Id='main' Type='{R}/officeDocument' Target='word/document.xml'/></Relationships>",
        'word/document.xml': f"<w:document xmlns:w='{W}' xmlns:r='{R}'><w:body>{body}<w:sectPr>{normalized_section}</w:sectPr></w:body></w:document>",
        'word/_rels/document.xml.rels': f"<Relationships xmlns='{PR}'>{relationships}</Relationships>",
    }
    if extras:
        parts.update(extras)
        for path in extras:
            kind = 'header' if '/header' in path or path in ('word/first.xml', 'word/even.xml') else 'footer' if '/footer' in path else 'settings' if path == 'word/settings.xml' else 'footnotes' if path == 'word/footnotes.xml' else None
            if kind and path.endswith('.xml'):
                override = f"<Override PartName='/{path}' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.{kind}+xml'/>"
                parts['[Content_Types].xml'] = parts['[Content_Types].xml'].replace('</Types>', override + '</Types>')
            if '/media/' in path:
                extension = path.rsplit('.', 1)[-1]
                media_type = {'jpg': 'image/jpeg', 'png': 'image/png', 'bmp': 'image/bmp', 'gif': 'image/gif'}[extension]
                parts['[Content_Types].xml'] = parts['[Content_Types].xml'].replace('</Types>', f"<Default Extension='{extension}' ContentType='{media_type}'/></Types>")
    data = io.BytesIO()
    with zipfile.ZipFile(data, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path, value in sorted(parts.items()):
            info = zipfile.ZipInfo(path, (2000, 1, 1, 0, 0, 0)); info.compress_type = zipfile.ZIP_DEFLATED
            if isinstance(value, str) and outcome == 'convert':
                # Word discards edge whitespace in w:t unless it is explicitly preserved.
                value = re.sub(r'<w:t>(.*?)</w:t>', lambda m: '<w:t' + (' xml:space="preserve"' if m[1] and (m[1][0].isspace() or m[1][-1].isspace()) else '') + '>' + m[1] + '</w:t>', value, flags=re.DOTALL)
            archive.writestr(info, value.encode('utf-8') if isinstance(value, str) else value)
    output = data.getvalue(); (DEST / (name + '.docx')).write_bytes(output)
    ROWS.append((name + '.docx', pages, expected, outcome, hashlib.sha256(output).hexdigest()))


for code, text in {
    'no': 'Enterprise norsk: blåbær, ærlig, øvelse, årlig. «Tilbud» – 1 250 €.',
    'sv': 'Enterprise svenska: försäkring, årlig, översikt och ärende.',
    'fi': 'Enterprise suomi: päivä, yö, määrä ja käyttö.',
    'en': 'Enterprise English: customer contract, invoice and notice — “quoted”.',
    'fr': 'Enterprise français : échéance, Noël, à côté, œuvre et coût.',
    'es': 'Enterprise español: información, mañana, año y cotización.',
    'it': 'Enterprise italiano: città, perché, più, qualità e società.',
    'pt': 'Enterprise português: informação, ação, coração e amanhã.',
}.items():
    write('language-' + code, p(text), expected=text)
for count in (1, 5, 25):
    write(f'pages-{count:02}', ''.join(p(f'Enterprise page {i}', '<w:pageBreakBefore/>' if i > 1 else '') for i in range(1, count + 1)), pages=count)
write('benchmark-10-pages', ''.join(p('Enterprise business page ' + str(i), '<w:pageBreakBefore/>' if i > 1 else '')
    + ''.join(p('Customer agreement: ' + 'Text with Nordic å ä ö ø æ and business terms. ' * 7) for _ in range(4))
    + table(p('Invoice details 1 250 €')) for i in range(1, 11)), pages=10)
write('appearance', p('Enterprise decorated', run="<w:b/><w:i/><w:strike/><w:color w:val='336699'/><w:sz w:val='30'/><w:highlight w:val='yellow'/><w:u/>"))
write('font-request', p('Enterprise branded', run="<w:rFonts w:ascii='Enterprise Brand' w:hAnsi='Enterprise Brand'/>"))
write('justified', p('Enterprise ' + 'business text with justified spacing. ' * 30, "<w:jc w:val='both'/>"))
write('distributed', p('Enterprise ' + 'distributed text. ' * 20, "<w:jc w:val='distribute'/>"))
write('nested-invoice', table(p('Enterprise invoice') + table(p('Nested invoice details')) + p('Total 123 €')))
write('table-shading', table(p('Enterprise shaded cell'), "<w:tcBorders><w:bottom w:val='single' w:sz='8' w:color='336699'/></w:tcBorders><w:shd w:fill='DDEEFF'/><w:vAlign w:val='center'/>"))
write('simple-fields', '<w:p><w:r><w:t>Enterprise </w:t></w:r>' + field('DATE \\@ "yyyy-MM-dd"', 'cached date') + field('TIME', 'cached time') + '</w:p>')
write('complex-fields', "<w:p><w:r><w:t>Enterprise </w:t></w:r><w:r><w:fldChar w:fldCharType='begin'/><w:instrText>NUMPAGES</w:instrText><w:fldChar w:fldCharType='separate'/><w:t>99</w:t><w:fldChar w:fldCharType='end'/></w:r></w:p>")
write('cached-merge', '<w:p><w:r><w:t>Enterprise </w:t></w:r>' + field('MERGEFIELD Customer', 'Cached customer') + '</w:p>')
write('content-control', "<w:sdt><w:sdtPr><w:tag w:val='customer'/></w:sdtPr><w:sdtContent>" + p('Enterprise current control content') + '</w:sdtContent></w:sdt>')
write('revisions', "<w:p><w:ins w:id='1' w:author='Fixture'><w:r><w:t>Enterprise accepted insertion</w:t></w:r></w:ins><w:del w:id='2' w:author='Fixture'><w:r><w:delText>Deleted text</w:delText></w:r></w:del></w:p>")
write('hyperlink', "<w:p><w:r><w:t>Enterprise </w:t></w:r><w:hyperlink r:id='link'><w:r><w:t>Safe link</w:t></w:r></w:hyperlink></w:p>", relationships=f"<Relationship Id='link' Type='{R}/hyperlink' Target='https://example.org/enterprise' TargetMode='External'/>")
write('field-hyperlink', '<w:p><w:r><w:t>Enterprise </w:t></w:r>' + field('HYPERLINK "https://example.org/enterprise"', 'Field link') + '</w:p>')
footer = '<w:p><w:r><w:t>Page </w:t></w:r>' + field('PAGE', '99') + '<w:r><w:t> of </w:t></w:r>' + field('NUMPAGES', '99') + '</w:p>'
stories = {'word/header.xml': f"<w:hdr xmlns:w='{W}'>{p('Enterprise letterhead')}</w:hdr>", 'word/footer.xml': f"<w:ftr xmlns:w='{W}'>{footer}</w:ftr>"}
refs = "<w:headerReference w:type='default' r:id='header'/><w:footerReference w:type='default' r:id='footer'/>"
rels = f"<Relationship Id='header' Type='{R}/header' Target='header.xml'/><Relationship Id='footer' Type='{R}/footer' Target='footer.xml'/>"
write('letter-stories', ''.join(p(f'Enterprise body {i}', '<w:pageBreakBefore/>' if i > 1 else '') for i in range(1, 6)), pages=5, section=refs, extras=stories, relationships=rels)
write('first-even-stories', p('Enterprise first page') + p('Enterprise second page', '<w:pageBreakBefore/>'), pages=2, section=refs + "<w:titlePg/><w:headerReference w:type='first' r:id='first'/><w:headerReference w:type='even' r:id='even'/>", extras={**stories, 'word/first.xml': f"<w:hdr xmlns:w='{W}'>{p('FIRST HEADER')}</w:hdr>", 'word/even.xml': f"<w:hdr xmlns:w='{W}'>{p('EVEN HEADER')}</w:hdr>", 'word/settings.xml': f"<w:settings xmlns:w='{W}'><w:evenAndOddHeaders/></w:settings>"}, relationships=rels + f"<Relationship Id='first' Type='{R}/header' Target='first.xml'/><Relationship Id='even' Type='{R}/header' Target='even.xml'/><Relationship Id='settings' Type='{R}/settings' Target='settings.xml'/>")
write('tiny-runs', '<w:p>' + ''.join(f"<w:r><w:t>{'Enterprise ' if i == 0 else 'x'}</w:t></w:r>" for i in range(20000)) + '</w:p>', pages=0)
write('oversized-row', table(''.join(p(f'Enterprise row line {i}') for i in range(100))), pages=0)
write('unsupported-glyph', p('Enterprise unsupported glyph \U0001F642'))
write('no-break-space', p('Enterprise non-breaking\u00a0space and non-breaking\u2011hyphen.'))
for extension in ('png', 'jpg', 'gif', 'bmp'):
    raster = io.BytesIO()
    with Image.open(DEST.parent / 'color-grid.png') as image:
        image.convert('RGB').save(raster, format={'jpg': 'JPEG'}.get(extension, extension.upper()))
    drawing = f"""<w:p xmlns:wp='http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing' xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' xmlns:pic='http://schemas.openxmlformats.org/drawingml/2006/picture'>
    <w:r><w:t>Enterprise logo </w:t></w:r><w:r><w:drawing><wp:inline><wp:extent cx='1524000' cy='762000'/><wp:docPr id='1' name='Logo'/><a:graphic><a:graphicData uri='http://schemas.openxmlformats.org/drawingml/2006/picture'><pic:pic><pic:nvPicPr><pic:cNvPr id='0' name='Logo'/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed='logo'/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x='0' y='0'/><a:ext cx='1524000' cy='762000'/></a:xfrm><a:prstGeom prst='rect'/></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>"""
    write('logo-' + extension, drawing, extras={'word/media/logo.' + extension: raster.getvalue()}, relationships=f"<Relationship Id='logo' Type='{R}/image' Target='media/logo.{extension}'/>")
write('benchmark-10-pages-images', ''.join(p('Enterprise business page ' + str(i), '<w:pageBreakBefore/>' if i > 1 else '')
    + ''.join(p('Customer agreement: ' + 'Text with Nordic å ä ö ø æ and business terms. ' * 7) for _ in range(4))
    + table(p('Invoice details 1 250 €')) + (drawing if i in (1, 10) else '') for i in range(1, 11)), pages=10,
    extras={'word/media/logo.png': (DEST.parent / 'color-grid.png').read_bytes()},
    relationships=f"<Relationship Id='logo' Type='{R}/image' Target='media/logo.png'/>")
write('three-part-footer', p('Enterprise business letter'), section=refs, relationships=rels,
    extras={**stories, 'word/footer.xml': f"<w:ftr xmlns:w='{W}'><w:p><w:pPr><w:tabs><w:tab w:val='center' w:pos='4500'/><w:tab w:val='right' w:pos='9000'/></w:tabs></w:pPr><w:r><w:t>Left footer</w:t><w:tab/><w:t>Center footer</w:t><w:tab/><w:t>Right footer</w:t></w:r></w:p></w:ftr>"})
write('contract-notes', p('Enterprise contract clause') + "<w:p><w:r><w:t>Reference</w:t><w:footnoteReference w:id='1'/></w:r></w:p>",
    extras={'word/footnotes.xml': f"<w:footnotes xmlns:w='{W}'><w:footnote w:id='1'>{p('Independent contractual note')}</w:footnote></w:footnotes>"},
    relationships=f"<Relationship Id='notes' Type='{R}/footnotes' Target='footnotes.xml'/>")
write('malformed-xml', '', pages=0, extras={'word/document.xml': f"<w:document xmlns:w='{W}'><w:body>"}, outcome='reject')
write('expanded-limit', p('Enterprise bounded'), pages=0, extras={'word/unused.txt': b'x' * (65 * 1024 * 1024)}, outcome='reject')
with (DEST / 'manifest.csv').open('w', newline='', encoding='utf-8') as stream:
    writer = csv.writer(stream); writer.writerow(('file', 'specified_pages', 'expected_text', 'outcome', 'sha256')); writer.writerows(ROWS)
print(f'Generated {len(ROWS)} independently authored DOCX fixtures.')
