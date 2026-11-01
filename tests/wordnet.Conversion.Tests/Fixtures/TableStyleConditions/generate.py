"""Regenerate the independently authored inputs; ZIP timestamps may differ."""
from pathlib import Path
from tempfile import TemporaryDirectory
from zipfile import ZipFile, ZIP_DEFLATED
import subprocess
import sys

root = Path(__file__).resolve().parents[4]
destination = Path(__file__).parent
with TemporaryDirectory(prefix='wordnet-table-condition-') as scratch:
    subprocess.run([sys.executable, str(root / 'eng/generate-roadmap-138-controls.py'), '--output', scratch], check=True)
    with ZipFile(Path(scratch) / 'table-style.docx') as archive:
        parts = {name: archive.read(name).decode('utf-8') for name in archive.namelist()}
    looks = [('look-on', '<w:tblLook w:val="0020"/>'), ('look-off', '<w:tblLook w:val="0000"/>'),
             ('look-absent', ''), ('look-conflict-off', '<w:tblLook w:val="0020" w:firstRow="0"/>'),
             ('look-conflict-on', '<w:tblLook w:val="0000" w:firstRow="1"/>')]
    for name, look in looks:
        document = parts['word/document.xml'].replace('<w:tblLook w:val="0020"/>', look)
        with ZipFile(destination / (name + '.docx'), 'w', ZIP_DEFLATED) as archive:
            for part, content in parts.items():
                archive.writestr(part, document if part == 'word/document.xml' else content)
    for name, direct in [('override-color', '<w:shd w:val="clear" w:color="auto" w:fill="DD4422"/>'),
                         ('override-nil', '<w:shd w:val="nil"/>')]:
        document = parts['word/document.xml'].replace('</w:tcPr>', direct + '</w:tcPr>', 1)
        with ZipFile(destination / (name + '.docx'), 'w', ZIP_DEFLATED) as archive:
            for part, content in parts.items():
                archive.writestr(part, document if part == 'word/document.xml' else content)
    styles = parts['word/styles.xml'].replace('<w:tblStylePr', '<w:tblPr><w:shd w:val="clear" w:color="auto" w:fill="FFF3C0"/></w:tblPr><w:tblStylePr', 1)
    with ZipFile(destination / 'whole-and-condition.docx', 'w', ZIP_DEFLATED) as archive:
        for part, content in parts.items():
            archive.writestr(part, styles if part == 'word/styles.xml' else content)
