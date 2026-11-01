"""Derive independent implicit/explicit style controls; ZIP timestamps can differ."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

destination = Path(__file__).parent
source = destination.parent / 'TableStyleRegions' / 'row-column-priority.docx'
with ZipFile(source) as archive:
    original = {name: archive.read(name) for name in archive.namelist()}

for name, default, explicit in [('default', '1', False), ('explicit', '1', True), ('no-default', '0', False)]:
    parts = dict(original)
    parts['word/styles.xml'] = parts['word/styles.xml'].replace(
        b'w:styleId="IndependentTable"', b'w:styleId="IndependentTable" w:default="' + default.encode() + b'"')
    if not explicit:
        parts['word/document.xml'] = parts['word/document.xml'].replace(b'<w:tblStyle w:val="IndependentTable"/>', b'')
    with ZipFile(destination / (name + '.docx'), 'w', ZIP_DEFLATED) as archive:
        for part, content in parts.items():
            archive.writestr(part, content)
