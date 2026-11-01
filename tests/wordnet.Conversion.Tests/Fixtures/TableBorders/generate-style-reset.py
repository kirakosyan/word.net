from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

source = Path(__file__).with_name('table-style.docx')
target = Path(__file__).with_name('table-style-reset.docx')
with ZipFile(source) as archive:
    parts = {name: archive.read(name).decode('utf-8') for name in archive.namelist()}
style = parts['word/styles.xml']
style = style.replace('<w:basedOn w:val="Base"/>', '<w:basedOn w:val="Base"/><w:tblPr><w:tblBorders><w:top w:val="nil"/></w:tblBorders></w:tblPr>')
parts['word/styles.xml'] = style
with ZipFile(target, 'w', ZIP_DEFLATED) as archive:
    for name, data in parts.items():
        archive.writestr(name, data)
