"""Original MIT layout corpus: independent DOCX markup and ReportLab page coordinates.

The reference encodes the documented layout profile, not Microsoft Word's pagination.
"""
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED
import hashlib
import json
from reportlab.pdfgen.canvas import Canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont

root = Path(__file__).parent
fonts = root.parents[2] / 'src/wordnet/Resources/Fonts'
pdfmetrics.registerFont(TTFont('Noto', str(fonts / 'NotoSans-Regular.ttf')))
pdfmetrics.registerFont(TTFont('NotoBold', str(fonts / 'NotoSans-Bold.ttf')))
w = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
def p(text, properties='', run=''):
    return f'<w:p><w:pPr>{properties}</w:pPr><w:r><w:rPr>{run}</w:rPr><w:t>{text}</w:t></w:r></w:p>'
def cell(text, properties=''):
    return f'<w:tc><w:tcPr>{properties}</w:tcPr>{text}</w:tc>'
letter = "<w:sectPr><w:pgSz w:w='12240' w:h='15840'/><w:pgMar w:top='1440' w:right='1200' w:bottom='1440' w:left='1440' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>"
landscape = "<w:sectPr><w:pgSz w:w='15840' w:h='12240' w:orient='landscape'/><w:pgMar w:top='720' w:right='720' w:bottom='720' w:left='720' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>"
table = "<w:tbl><w:tblGrid>" + "<w:gridCol w:w='2400'/>" * 3 + '</w:tblGrid>'
table += '<w:tr>' + cell(p('Wide cell') + p('Second line'), "<w:gridSpan w:val='2'/>") + cell(p('Right')) + '</w:tr>'
table += '<w:tr>' + cell(p('Vertical') + p('Group'), "<w:vMerge w:val='restart'/>") + cell(p('A')) + cell(p('B')) + '</w:tr>'
table += '<w:tr>' + cell('<w:p/>', '<w:vMerge/>') + cell(p('C')) + cell(p('D')) + '</w:tr></w:tbl>'
body = p('Quarterly report', "<w:spacing w:before='200' w:after='160'/><w:jc w:val='center'/>", "<w:b/><w:color w:val='336699'/><w:sz w:val='36'/>")
body += p('Indented body.', "<w:spacing w:before='120'/><w:ind w:left='480' w:firstLine='240'/>")
body += table + p('End first section', letter) + p('Landscape section') + landscape
parts = {
    '[Content_Types].xml': "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/></Types>",
    '_rels/.rels': "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='r1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/></Relationships>",
    'word/document.xml': f'<w:document xmlns:w="{w}"><w:body>{body}</w:body></w:document>'
}
with ZipFile(root / 'layout-corpus.docx', 'w') as package:
    for name, content in parts.items():
        info = ZipInfo(name, (2000, 1, 1, 0, 0, 0)); info.compress_type = ZIP_DEFLATED
        package.writestr(info, content.encode('utf-8'))
pdf = Canvas(str(root / 'layout-reference.pdf'), pagesize=(612, 792), invariant=1)
def text(value, x, top, size=11, font='Noto'):
    pdf.setFont(font, size); pdf.drawString(x, 792 - top, value)
pdf.setFillColorRGB(0x33 / 255, 0x66 / 255, 0x99 / 255)
title_width = pdfmetrics.stringWidth('Quarterly report', 'NotoBold', 18)
text('Quarterly report', 72 + (480 - title_width) / 2, 100, 18, 'NotoBold')
pdf.setFillColorRGB(0, 0, 0)
text('Indented body.', 108, 135, 12)
text('Wide cell', 72, 158); text('Second line', 72, 180.5); text('Right', 312, 158)
text('Vertical', 72, 203); text('Group', 72, 225.5); text('A', 192, 203); text('B', 312, 203)
text('C', 192, 225.5); text('D', 312, 225.5)
text('End first section', 72, 255, 12)
pdf.showPage(); pdf.setPageSize((792, 612)); pdf.setFont('Noto', 12)
pdf.drawString(36, 564, 'Landscape section'); pdf.save()
manifest = {name: hashlib.sha256((root / name).read_bytes()).hexdigest() for name in ['layout-corpus.docx', 'layout-reference.pdf']}
(root / 'layout-corpus.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
