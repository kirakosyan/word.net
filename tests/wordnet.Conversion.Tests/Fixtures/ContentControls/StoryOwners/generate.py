"""Assemble a synthetic header/footer control fixture from licensed Word XML."""
import argparse
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZIP_DEFLATED, ZipFile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, default=Path(__file__).parent / 'stories.docx')
parser.add_argument('--word-variants', type=Path, default=Path(__file__).parent.parent / 'OfficeWordVariants')
args = parser.parse_args()
w = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
r = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
ns = '{' + w + '}'
ET.register_namespace('w', w)
ET.register_namespace('r', r)

def run(text):
    result = ET.Element(ns + 'r')
    props = ET.SubElement(result, ns + 'rPr')
    ET.SubElement(props, ns + 'rFonts', {ns + 'ascii': 'Noto Sans', ns + 'hAnsi': 'Noto Sans'})
    ET.SubElement(props, ns + 'sz', {ns + 'val': '22'})
    ET.SubElement(result, ns + 't', {'{http://www.w3.org/XML/1998/namespace}space': 'preserve'}).text = text
    return result

inline = ET.parse(args.word_variants / 'rich-inline.xml').getroot()
properties = inline.find(ns + 'sdtPr')
placeholder = properties.find(ns + 'placeholder')
if placeholder is not None:
    properties.remove(placeholder)
inline.find(ns + 'sdtContent').clear()
inline.find(ns + 'sdtContent').append(run('SdtRun'))
header = ET.Element(ns + 'hdr')
paragraph = ET.SubElement(header, ns + 'p')
paragraph.append(run('Reference '))
paragraph.append(inline)
paragraph.append(run(' header'))

footer = ET.Element(ns + 'ftr')
control = ET.SubElement(footer, ns + 'sdt')
properties = ET.SubElement(control, ns + 'sdtPr')
ET.SubElement(properties, ns + 'id', {ns + 'val': '26000001'})
ET.SubElement(properties, ns + 'text')
content = ET.SubElement(control, ns + 'sdtContent')
ET.SubElement(content, ns + 'p').append(run('Footer label'))

document = ET.Element(ns + 'document')
body = ET.SubElement(document, ns + 'body')
p = ET.SubElement(body, ns + 'p')
p.append(run('First page'))
page_run = ET.SubElement(p, ns + 'r')
ET.SubElement(page_run, ns + 'br', {ns + 'type': 'page'})
p.append(run('Second page'))
section = ET.SubElement(body, ns + 'sectPr')
ET.SubElement(section, ns + 'headerReference', {ns + 'type': 'default', '{' + r + '}id': 'header'})
ET.SubElement(section, ns + 'footerReference', {ns + 'type': 'default', '{' + r + '}id': 'footer'})
ET.SubElement(section, ns + 'pgSz', {ns + 'w': '12240', ns + 'h': '15840'})
ET.SubElement(section, ns + 'pgMar', {ns + 'top': '1440', ns + 'right': '1440', ns + 'bottom': '1440',
                                      ns + 'left': '1440', ns + 'header': '720', ns + 'footer': '720', ns + 'gutter': '0'})
parts = {
    '[Content_Types].xml': '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
        '<Default Extension="xml" ContentType="application/xml"/>'
        '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
        '<Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>'
        '<Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>'
        '</Types>',
    '_rels/.rels': '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="root" Type="' + r + '/officeDocument" Target="word/document.xml"/></Relationships>',
    'word/_rels/document.xml.rels': '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="header" Type="' + r + '/header" Target="header1.xml"/>'
        '<Relationship Id="footer" Type="' + r + '/footer" Target="footer1.xml"/></Relationships>',
    'word/document.xml': ET.tostring(document, encoding='unicode'),
    'word/header1.xml': ET.tostring(header, encoding='unicode'),
    'word/footer1.xml': ET.tostring(footer, encoding='unicode'),
}
args.output.parent.mkdir(parents=True, exist_ok=True)
with ZipFile(args.output, 'w', ZIP_DEFLATED) as archive:
    for name, content in parts.items():
        archive.writestr(name, content)
