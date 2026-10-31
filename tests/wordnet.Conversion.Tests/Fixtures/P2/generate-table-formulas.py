from pathlib import Path
import sys
from xml.sax.saxutils import escape, quoteattr
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

root = Path(sys.argv[1] if len(sys.argv) > 1 else '/tmp/wordnet-138-external-formulas')
root.mkdir(exist_ok=True)
def cell(content): return '<w:tc><w:p>' + content + '</w:p></w:tc>'
def text(value): return '<w:r><w:t>' + escape(value) + '</w:t></w:r>'
def field(instruction): return '<w:fldSimple w:instr=' + quoteattr(instruction) + '>' + text('9999') + '</w:fldSimple>'
def row(*cells): return '<w:tr>' + ''.join(cells) + '</w:tr>'
table = '<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="3000"/><w:gridCol w:w="3000"/></w:tblGrid>' + row(cell(text('1.25')), cell(text('2'))) + row(cell(text('3.5')), cell(text('-2'))) + row(cell(field('= SUM(ABOVE)')), cell(field('= SUM(A1:B2)'))) + row(cell(field('= A3 + B3')), cell(field('= 0.15 * 100 \\# "0.00%"'))) + '</w:tbl>'
body = table + '<w:p>' + field('= 0.15 * 100 \\# "0.00%"') + '</w:p><w:p>' + field('= 0.15 \\# "0.00%"') + '</w:p>'
document = '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>' + body + '<w:sectPr/></w:body></w:document>'
parts = {
    '[Content_Types].xml': '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>',
    '_rels/.rels': '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>',
    'word/document.xml': document,
}
with ZipFile(root / 'formula-source.docx', 'w', ZIP_DEFLATED) as archive:
    for name, contents in parts.items():
        info = ZipInfo(name, (2026, 10, 31, 0, 0, 0))
        info.compress_type = ZIP_DEFLATED
        archive.writestr(info, contents)
