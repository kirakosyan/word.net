from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

source = Path(__file__).with_name('table-borders.docx')
target = Path(__file__).with_name('table-style.docx')
with ZipFile(source) as archive:
    parts = {name: archive.read(name).decode('utf-8') for name in archive.namelist()}
document = parts['word/document.xml']
start, end = document.index('<w:tblBorders>'), document.index('</w:tblBorders>') + len('</w:tblBorders>')
borders = document[start:end]
document = document[:start] + document[end:]
document = document.replace('<w:tblW ', "<w:tblStyle w:val='Derived'/><w:tblW ", 1)
parts['word/document.xml'] = document
parts['[Content_Types].xml'] = parts['[Content_Types].xml'].replace('</Types>', '<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>')
parts['word/_rels/document.xml.rels'] = '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="styles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>'
w = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
parts['word/styles.xml'] = '<w:styles xmlns:w="' + w + '"><w:style w:type="table" w:styleId="Base"><w:name w:val="Base"/><w:tblPr>' + borders + '</w:tblPr></w:style><w:style w:type="table" w:styleId="Derived"><w:name w:val="Derived"/><w:basedOn w:val="Base"/></w:style></w:styles>'
with ZipFile(target, 'w', ZIP_DEFLATED) as archive:
    for name, data in parts.items():
        archive.writestr(name, data)
