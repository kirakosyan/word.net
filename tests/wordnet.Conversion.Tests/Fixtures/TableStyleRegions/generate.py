"""Build synthetic DOCX controls independently; ZIP timestamps may differ."""
from pathlib import Path
from tempfile import TemporaryDirectory
from zipfile import ZipFile, ZIP_DEFLATED
import re
import subprocess
import sys

root = Path(__file__).resolve().parents[4]
destination = Path(__file__).parent
with TemporaryDirectory(prefix='wordnet-table-regions-') as scratch:
    subprocess.run([sys.executable, str(root / 'eng/generate-roadmap-138-controls.py'), '--output', scratch], check=True)
    with ZipFile(Path(scratch) / 'table-style.docx') as archive:
        original = {name: archive.read(name).decode('utf-8') for name in archive.namelist()}

regions = [('band1Vert', '88EE88'), ('band2Vert', 'AAFFAA'), ('band1Horz', '80AAFF'), ('band2Horz', 'AACCFF'),
           ('firstCol', 'F0E050'), ('lastCol', 'F4B050'), ('firstRow', 'FFE0E0'), ('lastRow', 'FF90B0'),
           ('nwCell', 'FF0000'), ('neCell', '00FF00'), ('swCell', '0000FF'), ('seCell', 'FF00FF')]
styles = ('<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:style w:type="table" w:styleId="IndependentTable"><w:name w:val="Regions"/>'
          '<w:tblPr><w:tblStyleRowBandSize w:val="1"/><w:tblStyleColBandSize w:val="1"/></w:tblPr>')
for name, color in regions:
    styles += '<w:tblStylePr w:type="' + name + '"><w:tcPr><w:shd w:val="clear" w:color="auto" w:fill="' + color + '"/></w:tcPr></w:tblStylePr>'
styles += '</w:style></w:styles>'

def cell(row, col):
    return '<w:tc><w:tcPr><w:tcW w:w="1250" w:type="dxa"/></w:tcPr><w:p><w:pPr><w:spacing w:after="0"/></w:pPr><w:r><w:rPr><w:rFonts w:ascii="Noto Sans" w:hAnsi="Noto Sans"/></w:rPr><w:t>' + chr(65 + col) + str(row + 1) + '</w:t></w:r></w:p></w:tc>'

table = '<w:tbl><w:tblPr><w:tblStyle w:val="IndependentTable"/><w:tblW w:w="5000" w:type="dxa"/><w:tblLook w:val="01E0"/></w:tblPr><w:tblGrid>' + '<w:gridCol w:w="1250"/>' * 4 + '</w:tblGrid>'
table += ''.join('<w:tr>' + ''.join(cell(row, col) for col in range(4)) + '</w:tr>' for row in range(4)) + '</w:tbl>'
document = original['word/document.xml']
start, end = document.index('<w:tbl>'), document.index('</w:tbl>') + len('</w:tbl>')
regions_parts = dict(original, **{'word/document.xml': document[:start] + table + document[end:], 'word/styles.xml': styles})

def write(name, parts):
    with ZipFile(destination / (name + '.docx'), 'w', ZIP_DEFLATED) as archive:
        for part, content in parts.items(): archive.writestr(part, content)

write('regions', regions_parts)
for name, changes in [('regions-overrides', {0: '<w:shd w:val="clear" w:color="auto" w:fill="DD4422"/>',
                                            10: '<w:shd w:val="clear" w:color="auto" w:fill="1122CC"/>'}),
                      ('regions-override-nil', {0: '<w:shd w:val="nil"/>'})]:
    parts = dict(regions_parts); chunks = parts['word/document.xml'].split('</w:tcPr>')
    parts['word/document.xml'] = ''.join(chunk + changes.get(index, '') + '</w:tcPr>' for index, chunk in enumerate(chunks[:-1])) + chunks[-1]
    write(name, parts)
variants = [('bands-h', '0000', ('band1Horz', 'band2Horz')),
            ('bands-v', '0000', ('band1Vert', 'band2Vert')),
            ('bands-both', '0000', ('band1Horz', 'band2Horz', 'band1Vert', 'band2Vert')),
            ('bands-header', '0020', ('band1Horz', 'band2Horz', 'firstRow')),
            ('bands-flag-only', '0020', ('band1Horz', 'band2Horz')),
            ('bands-last-flag', '0040', ('band1Horz', 'band2Horz')),
            ('bands-first-col', '0080', ('band1Vert', 'band2Vert')),
            ('bands-last-col', '0100', ('band1Vert', 'band2Vert')),
            ('bands-disable-h', '0200', ('band1Horz', 'band2Horz', 'band1Vert', 'band2Vert')),
            ('bands-disable-v', '0400', ('band1Horz', 'band2Horz', 'band1Vert', 'band2Vert')),
            ('row-column-priority', '00A0', ('firstRow', 'firstCol')),
            ('corner-first-row', '0020', ('firstRow', 'firstCol', 'nwCell')),
            ('corner-first-column', '0080', ('firstRow', 'firstCol', 'nwCell')),
            ('corner-both', '00A0', ('firstRow', 'firstCol', 'nwCell'))]
generated = {}
for name, mask, allowed in variants:
    parts = dict(regions_parts)
    parts['word/styles.xml'] = re.sub(r'<w:tblStylePr w:type="([^"]+)">.*?</w:tblStylePr>',
        lambda match: match.group(0) if match.group(1) in allowed else '', parts['word/styles.xml'])
    parts['word/document.xml'] = parts['word/document.xml'].replace('w:val="01E0"', 'w:val="' + mask + '"')
    generated[name] = parts; write(name, parts)

parts = dict(generated['bands-h'])
parts['word/styles.xml'] = parts['word/styles.xml'].replace('<w:tblStyleRowBandSize w:val="1"/>', '')
write('bands-implicit', parts)

source = generated['row-column-priority']
matches = {match.group(1): match.group(0) for match in re.finditer(r'<w:tblStylePr w:type="([^"]+)">.*?</w:tblStylePr>', source['word/styles.xml'])}
inherited = [('parent-row-child-col', matches['firstRow'], matches['firstCol']),
             ('parent-col-child-row', matches['firstCol'], matches['firstRow']),
             ('parent-row-child-empty', matches['firstRow'], ''),
             ('parent-row-child-row', matches['firstRow'], matches['firstRow'].replace('FFE0E0', '336699'))]
for name, parent, child in inherited:
    parts = dict(source)
    parts['word/styles.xml'] = ('<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
        '<w:style w:type="table" w:styleId="Parent"><w:name w:val="Parent"/>' + parent + '</w:style>'
        '<w:style w:type="table" w:styleId="Child"><w:name w:val="Child"/><w:basedOn w:val="Parent"/>' + child + '</w:style></w:styles>')
    parts['word/document.xml'] = parts['word/document.xml'].replace('w:val="IndependentTable"', 'w:val="Child"', 1)
    write(name, parts)

for name, rows, columns in [('regions-one', 1, 1), ('regions-one-row', 1, 4), ('regions-one-col', 4, 1), ('regions-two', 2, 2)]:
    parts = dict(regions_parts); document = parts['word/document.xml']
    grid = '<w:tblGrid>' + '<w:gridCol w:w="1250"/>' * columns + '</w:tblGrid>'
    document = re.sub(r'<w:tblGrid>.*?</w:tblGrid>', grid, document)
    all_rows = re.findall(r'<w:tr>.*?</w:tr>', document)
    retained = ['<w:tr>' + ''.join(re.findall(r'<w:tc>.*?</w:tc>', row)[:columns]) + '</w:tr>' for row in all_rows[:rows]]
    parts['word/document.xml'] = document.replace(''.join(all_rows), ''.join(retained)); write(name, parts)

for name, base, axis, size in [('row-size-two', 'bands-h', 'Row', '2'), ('column-size-two', 'bands-v', 'Col', '2'),
                               ('row-size-zero', 'bands-h', 'Row', '0'), ('column-size-zero', 'bands-v', 'Col', '0')]:
    parts = dict(generated[base])
    before = '<w:tblStyle' + axis + 'BandSize w:val="1"/>'
    item = '<w:tblStyle' + axis + 'BandSize w:val="' + size + '"/>'
    parts['word/styles.xml'] = parts['word/styles.xml'].replace(before, item)
    write(name, parts)
