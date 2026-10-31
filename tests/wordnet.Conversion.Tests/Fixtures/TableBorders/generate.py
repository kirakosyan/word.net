from zipfile import ZipFile, ZIP_DEFLATED
from pathlib import Path
import argparse

parser = argparse.ArgumentParser()
parser.add_argument('--shading', action='store_true', help='generate the independent table-shading control')
parser.add_argument('--cell-shading', action='store_true', help='generate a direct-cell shading control')
args = parser.parse_args()
if args.shading and args.cell_shading:
    parser.error('choose only one shading control')

w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
types = '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>'
rels = '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="main" Type="' + r + '/officeDocument" Target="word/document.xml"/></Relationships>'
borders = "<w:tblBorders><w:top w:val='single' w:sz='8' w:color='FF0000'/><w:left w:val='single' w:sz='8' w:color='0000FF'/><w:bottom w:val='single' w:sz='8' w:color='00FF00'/><w:right w:val='single' w:sz='8' w:color='FFFF00'/><w:insideH w:val='single' w:sz='8' w:color='00FFFF'/><w:insideV w:val='single' w:sz='8' w:color='FF00FF'/></w:tblBorders>"
def cell(text):
    direct = "<w:shd w:val='clear' w:color='auto' w:fill='FFF3C0'/>" if args.cell_shading else ""
    return "<w:tc><w:tcPr><w:tcW w:type='dxa' w:w='2400'/>" + direct + "</w:tcPr><w:p><w:r><w:rPr><w:rFonts w:ascii='Liberation Sans' w:hAnsi='Liberation Sans'/><w:sz w:val='22'/></w:rPr><w:t>" + text + "</w:t></w:r></w:p></w:tc>"
shading = "<w:shd w:val='clear' w:color='auto' w:fill='FFF3C0'/>" if args.shading else ""
body = "<w:tbl><w:tblPr><w:tblW w:type='dxa' w:w='4800'/>" + borders + shading + "<w:tblLayout w:type='fixed'/></w:tblPr><w:tblGrid><w:gridCol w:w='2400'/><w:gridCol w:w='2400'/></w:tblGrid><w:tr>" + cell("A") + cell("B") + "</w:tr><w:tr>" + cell("C") + cell("D") + "</w:tr></w:tbl><w:sectPr><w:pgSz w:w='12240' w:h='15840'/><w:pgMar w:top='1440' w:bottom='1440' w:left='1440' w:right='1440' w:header='0' w:footer='0' w:gutter='0'/></w:sectPr>"
document = '<w:document xmlns:w="' + w + '" xmlns:r="' + r + '"><w:body>' + body + '</w:body></w:document>'
name = 'table-shading.docx' if args.shading else 'cell-shading.docx' if args.cell_shading else 'table-borders.docx'
with ZipFile(Path(__file__).with_name(name), 'w', ZIP_DEFLATED) as z:
    z.writestr('[Content_Types].xml', types)
    z.writestr('_rels/.rels', rels)
    z.writestr('word/document.xml', document)
