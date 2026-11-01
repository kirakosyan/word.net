"""Build independent conditional theme-fill controls; ZIP timestamps can differ."""
from pathlib import Path
from tempfile import TemporaryDirectory
from zipfile import ZipFile, ZIP_DEFLATED
import argparse
import subprocess
import sys

root = Path(__file__).resolve().parents[4]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, default=Path(__file__).parent)
destination = parser.parse_args().output
destination.mkdir(parents=True, exist_ok=True)
with TemporaryDirectory(prefix='wordnet-table-theme-') as scratch:
    subprocess.run([sys.executable, str(root / 'eng/generate-roadmap-138-controls.py'), '--output', scratch], check=True)
    with ZipFile(Path(scratch) / 'table-style.docx') as archive:
        original = {name: archive.read(name) for name in archive.namelist()}
    with ZipFile(Path(scratch) / 'theme-color.docx') as archive:
        theme = archive.read('word/theme/theme1.xml')

relationship = b'<Relationship Id="theme" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" Target="theme/theme1.xml"/>'
override = b'<Override PartName="/word/theme/theme1.xml" ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/>'
original['word/_rels/document.xml.rels'] = original['word/_rels/document.xml.rels'].replace(b'</Relationships>', relationship + b'</Relationships>')
original['[Content_Types].xml'] = original['[Content_Types].xml'].replace(b'</Types>', override + b'</Types>')
original['word/theme/theme1.xml'] = theme
cases = {
    'theme': b'w:fill="FF0000" w:themeFill="accent1"',
    'theme-tint': b'w:fill="FF0000" w:themeFill="accent1" w:themeFillTint="80"',
    'theme-shade': b'w:fill="FF0000" w:themeFill="accent1" w:themeFillShade="80"',
    'explicit-blue': b'w:fill="2266CC"',
    'explicit-red': b'w:fill="FF0000"',
}
for name, shading in cases.items():
    parts = dict(original); before = b'w:fill="2266CC"'
    if parts['word/styles.xml'].count(before) != 1:
        raise ValueError('The independent source style changed.')
    parts['word/styles.xml'] = parts['word/styles.xml'].replace(before, shading)
    with ZipFile(destination / (name + '.docx'), 'w', ZIP_DEFLATED) as archive:
        for part, content in parts.items():
            archive.writestr(part, content)
