"""Build independent conditional table-border controls; ZIP timestamps can differ."""
import argparse
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, default=Path(__file__).parent)
target = parser.parse_args().output
target.mkdir(parents=True, exist_ok=True)
source = Path(__file__).parent.parent / 'TableStyleRegions' / 'regions.docx'
with ZipFile(source) as archive:
    original = {name: archive.read(name).decode('utf-8') for name in archive.namelist()}


def border(side, color, value='single'):
    return '<w:' + side + ' w:val="' + value + '" w:sz="8" w:color="' + color + '"/>'


def condition(name, edge):
    return '<w:tblStylePr w:type="' + name + '"><w:tcPr><w:tcBorders>' + edge + '</w:tcBorders></w:tcPr></w:tblStylePr>'


def style(name, content, parent=''):
    return '<w:style w:type="table" w:styleId="' + name + '"><w:name w:val="' + name + '"/>' + ('<w:basedOn w:val="' + parent + '"/>' if parent else '') + content + '</w:style>'


edges = condition('firstRow', border('top', 'FF0000')) + condition('lastRow', border('bottom', '00AA00'))
edges += condition('firstCol', border('left', '0000FF')) + condition('lastCol', border('right', 'AA00AA'))
variants = {
    'all-edges': (style('IndependentTable', edges), '01E0'),
    'edge-shading': (style('IndependentTable', edges.replace(border('top', 'FF0000') + '</w:tcBorders>',
        border('top', 'FF0000') + '</w:tcBorders><w:shd w:val="clear" w:color="auto" w:fill="E0E0FF"/>')), '01E0'),
    'top-enabled': (style('IndependentTable', condition('firstRow', border('top', 'FF0000'))), '0020'),
    'top-disabled': (style('IndependentTable', condition('firstRow', border('top', 'FF0000'))), '0000'),
    'bottom-interior': (style('IndependentTable', condition('firstRow', border('bottom', 'FF0000'))), '0020'),
    'corner': (style('IndependentTable', condition('nwCell', border('top', 'FF0000') + border('left', '0000FF'))), '00A0'),
    'parent-child': (style('Parent', condition('firstRow', border('top', 'FF0000'))) + style('IndependentTable', '', 'Parent'), '0020'),
    'parent-reset': (style('Parent', condition('firstRow', border('top', 'FF0000'))) + style('IndependentTable', condition('firstRow', border('top', 'FF0000', 'nil')), 'Parent'), '0020'),
}
for name, (definitions, mask) in variants.items():
    parts = dict(original)
    parts['word/styles.xml'] = '<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">' + definitions + '</w:styles>'
    parts['word/document.xml'] = parts['word/document.xml'].replace('w:val="01E0"', 'w:val="' + mask + '"')
    with ZipFile(target / (name + '.docx'), 'w', ZIP_DEFLATED) as archive:
        for part, content in parts.items():
            archive.writestr(part, content)
