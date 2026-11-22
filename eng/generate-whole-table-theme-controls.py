"""Generate direct and inherited whole-table fill contrasts for independent Office rendering."""
import argparse
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile, ZipInfo

W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
ET.register_namespace('w', W)
ROOT = Path(__file__).resolve().parents[1]


def write_case(original, output, placement, attributes):
    parts = original.copy()
    body = ET.fromstring(parts['word/document.xml'])
    styles = ET.fromstring(parts['word/styles.xml'])
    table = body.find('.//w:tblPr', {'w': W})
    table.remove(table.find(f'{{{W}}}tblLook'))
    style = styles.find(f'{{{W}}}style')
    style.remove(style.find(f'{{{W}}}tblStylePr'))
    if placement == 'inherited':
        owner = ET.SubElement(style, f'{{{W}}}tblPr')
        child = ET.SubElement(styles, f'{{{W}}}style', {f'{{{W}}}type': 'table', f'{{{W}}}styleId': 'Child'})
        ET.SubElement(child, f'{{{W}}}name', {f'{{{W}}}val': 'Child'})
        ET.SubElement(child, f'{{{W}}}basedOn', {f'{{{W}}}val': 'IndependentTable'})
        table.find(f'{{{W}}}tblStyle').set(f'{{{W}}}val', 'Child')
    else:
        table.remove(table.find(f'{{{W}}}tblStyle'))
        owner = body.find('.//w:tcPr', {'w': W}) if placement == 'cell' else table
    ET.SubElement(owner, f'{{{W}}}shd', {f'{{{W}}}{key}': val for key, val in attributes.items()})
    for fonts in body.iter(f'{{{W}}}rFonts'):
        for slot in ('ascii', 'hAnsi'):
            fonts.set(f'{{{W}}}{slot}', 'Arial')
    parts['word/document.xml'] = ET.tostring(body, encoding='utf-8')
    parts['word/styles.xml'] = ET.tostring(styles, encoding='utf-8')
    with ZipFile(output, 'w') as archive:
        for name, data in sorted(parts.items()):
            info = ZipInfo(name, (2000, 1, 1, 0, 0, 0))
            info.create_system = 0
            info.external_attr = 0o600 << 16
            archive.writestr(info, data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    output = parser.parse_args().output
    output.mkdir(parents=True, exist_ok=True)
    with ZipFile(ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/TableStyleThemes/theme.docx') as archive:
        original = {entry: archive.read(entry) for entry in archive.namelist()}
    for owner in ('direct', 'inherited', 'cell'):
        for kind, attributes in (
                ('theme', dict(themeFill='accent1', fill='FF0000')),
                ('tint', dict(themeFill='accent1', themeFillTint='80', fill='FF0000')),
                ('shade', dict(themeFill='accent1', themeFillShade='80', fill='FF0000')),
                ('explicit-blue', dict(fill='2266CC')), ('explicit-red', dict(fill='FF0000'))):
            write_case(original, output / f'{owner}-{kind}.docx', owner,
                       dict(val='clear', color='auto', **attributes))


if __name__ == '__main__':
    main()
