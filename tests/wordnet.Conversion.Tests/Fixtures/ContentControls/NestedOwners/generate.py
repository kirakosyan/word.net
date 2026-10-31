"""Create an independently assembled nested-control DOCX from licensed Word subtrees."""
import argparse
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZIP_DEFLATED, ZipFile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, default=Path(__file__).parent / 'nested.docx')
args = parser.parse_args()
directory = Path(__file__).parent.parent / 'OfficeWordVariants'
w = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
ns = '{' + w + '}'
ET.register_namespace('w', w)

outer = ET.parse(directory / 'rich-block.xml').getroot()
inner = ET.parse(directory / 'rich-inline.xml').getroot()
for control in (outer, inner):
    properties = control.find(ns + 'sdtPr')
    placeholder = properties.find(ns + 'placeholder')
    if placeholder is not None:
        properties.remove(placeholder)
paragraph = outer.find('./' + ns + 'sdtContent/' + ns + 'p')
paragraph.append(inner)

def run(text):
    r = ET.Element(ns + 'r')
    t = ET.SubElement(r, ns + 't', {'{http://www.w3.org/XML/1998/namespace}space': 'preserve'})
    t.text = text
    return r

paragraph.append(run(' and '))
plain = ET.SubElement(paragraph, ns + 'sdt')
properties = ET.SubElement(plain, ns + 'sdtPr')
ET.SubElement(properties, ns + 'id', {ns + 'val': '26000000'})
ET.SubElement(properties, ns + 'text')
ET.SubElement(plain, ns + 'sdtContent').append(run('Plain'))
paragraph.append(run('.'))
for r in paragraph.iter(ns + 'r'):
    properties = ET.Element(ns + 'rPr')
    ET.SubElement(properties, ns + 'rFonts', {ns + 'ascii': 'Noto Sans', ns + 'hAnsi': 'Noto Sans'})
    ET.SubElement(properties, ns + 'sz', {ns + 'val': '22'})
    r.insert(0, properties)
document = ET.Element(ns + 'document')
body = ET.SubElement(document, ns + 'body')
body.append(outer)
section = ET.SubElement(body, ns + 'sectPr')
ET.SubElement(section, ns + 'pgSz', {ns + 'w': '12240', ns + 'h': '15840'})
ET.SubElement(section, ns + 'pgMar', {ns + 'top': '1440', ns + 'right': '1440', ns + 'bottom': '1440',
                                        ns + 'left': '1440', ns + 'header': '720', ns + 'footer': '720', ns + 'gutter': '0'})
parts = {
    '[Content_Types].xml': '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
        '<Default Extension="xml" ContentType="application/xml"/>'
        '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
        '</Types>',
    '_rels/.rels': '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="root" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>'
        '</Relationships>',
    'word/document.xml': ET.tostring(document, encoding='unicode'),
}
args.output.parent.mkdir(parents=True, exist_ok=True)
with ZipFile(args.output, 'w', ZIP_DEFLATED) as archive:
    for name, text in parts.items():
        archive.writestr(name, text)
