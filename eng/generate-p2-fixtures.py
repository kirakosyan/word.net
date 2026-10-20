#!/usr/bin/env python3
"""Independently authored object inputs; XlsxWriter supplies actual XLSX/chart XML."""
from datetime import datetime
from io import BytesIO
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED
import hashlib
import json
import xml.etree.ElementTree as ET
import xlsxwriter

assert xlsxwriter.__version__ == '3.2.9'
root = Path(__file__).resolve().parents[1]
output = root / 'tests/wordnet.Conversion.Tests/Fixtures/P2'
output.mkdir(exist_ok=True)
w = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
r = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
pr = 'http://schemas.openxmlformats.org/package/2006/relationships'
c = 'http://schemas.openxmlformats.org/drawingml/2006/chart'
a = 'http://schemas.openxmlformats.org/drawingml/2006/main'
wp = 'http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing'
m = 'http://schemas.openxmlformats.org/officeDocument/2006/math'
ct = 'http://schemas.openxmlformats.org/package/2006/content-types'
for prefix, uri in [('w', w), ('r', r), ('c', c), ('a', a), ('wp', wp), ('m', m)]:
    ET.register_namespace(prefix, uri)

def package(parts):
    stream = BytesIO()
    with ZipFile(stream, 'w') as archive:
        for name, value in parts.items():
            info = ZipInfo(name, (2000, 1, 1, 0, 0, 0))
            archive.writestr(info, value, compress_type=ZIP_DEFLATED)
    return stream.getvalue()

def workbook(kind=None):
    stream = BytesIO()
    book = xlsxwriter.Workbook(stream, {'in_memory': True})
    book.set_properties({'title': 'Independent synthetic chart data', 'author': 'Fixture generator', 'created': datetime(2000, 1, 1)})
    sheet = book.add_worksheet('Data')
    for row, values in enumerate([['Category', 'North', 'South'], ['First', 10, -5], ['Second', 0, 8], ['Third', -2, None], ['Fourth', 6, 4]]):
        sheet.write_row(row, 0, values)
    if kind:
        chart = book.add_chart({'type': kind})
        chart.set_title({'name': 'Independent values'})
        for column, color in [(1, '#2468AC'), (2, '#C03040')]:
            chart.add_series({'name': ['Data', 0, column], 'categories': ['Data', 1, 0, 4, 0], 'values': ['Data', 1, column, 4, column],
                              'fill': {'color': color}, 'line': {'none': True} if kind == 'column' else {'color': color, 'width': 1}})
        sheet.insert_chart('E1', chart)
    book.close()
    return stream.getvalue()

plain = workbook()
manifest = {'generator': 'generate-p2-fixtures.py', 'license': 'MIT synthetic inputs', 'producer': 'XlsxWriter 3.2.9 (BSD-2-Clause)', 'files': {}}
for kind in ('column', 'line'):
    with ZipFile(BytesIO(workbook(kind))) as excel:
        chart = ET.fromstring(excel.read('xl/charts/chart1.xml'))
    data = ET.Element('{'+c+'}externalData', {'{'+r+'}id': 'workbook'})
    ET.SubElement(data, '{'+c+'}autoUpdate', {'val': '0'})
    position = next((i for i, node in enumerate(chart) if node.tag == '{'+c+'}printSettings'), len(chart))
    chart.insert(position, data)
    document = ET.Element('{'+w+'}document')
    body = ET.SubElement(document, '{'+w+'}body')
    p = ET.SubElement(body, '{'+w+'}p')
    drawing = ET.SubElement(ET.SubElement(p, '{'+w+'}r'), '{'+w+'}drawing')
    inline = ET.SubElement(drawing, '{'+wp+'}inline')
    ET.SubElement(inline, '{'+wp+'}extent', {'cx': '3810000', 'cy': '2286000'})
    ET.SubElement(inline, '{'+wp+'}docPr', {'id': '1', 'name': 'Independent chart', 'descr': 'North and South values by category'})
    ET.SubElement(inline, '{'+wp+'}cNvGraphicFramePr')
    graphic = ET.SubElement(inline, '{'+a+'}graphic')
    graphic_data = ET.SubElement(graphic, '{'+a+'}graphicData', {'uri': c})
    ET.SubElement(graphic_data, '{'+c+'}chart', {'{'+r+'}id': 'chart'})
    p = ET.SubElement(body, '{'+w+'}p')
    math = ET.SubElement(p, '{'+m+'}oMath')
    fraction = ET.SubElement(math, '{'+m+'}f')
    for part, text in [('num', 'x'), ('den', '2')]:
        run = ET.SubElement(ET.SubElement(fraction, '{'+m+'}'+part), '{'+m+'}r')
        ET.SubElement(run, '{'+m+'}t').text = text
    section = ET.SubElement(body, '{'+w+'}sectPr')
    ET.SubElement(section, '{'+w+'}pgSz', {'{'+w+'}w': '8000', '{'+w+'}h': '6000'})
    ET.SubElement(section, '{'+w+'}pgMar', {'{'+w+'}'+key: value for key, value in [('top','600'),('bottom','600'),('left','600'),('right','600'),('header','0'),('footer','0'),('gutter','0')]})
    types = ('<Types xmlns="'+ct+'"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
             '<Default Extension="xml" ContentType="application/xml"/>'
             '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
             '<Override PartName="/word/charts/chart1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.chart+xml"/>'
             '<Override PartName="/word/embeddings/data.xlsx" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"/></Types>')
    parts = {'[Content_Types].xml': types, '_rels/.rels': '<Relationships xmlns="'+pr+'"><Relationship Id="main" Type="'+r+'/officeDocument" Target="word/document.xml"/></Relationships>',
             'word/document.xml': ET.tostring(document, encoding='utf-8'), 'word/_rels/document.xml.rels': '<Relationships xmlns="'+pr+'"><Relationship Id="chart" Type="'+r+'/chart" Target="charts/chart1.xml"/></Relationships>',
             'word/charts/chart1.xml': ET.tostring(chart, encoding='utf-8'), 'word/charts/_rels/chart1.xml.rels': '<Relationships xmlns="'+pr+'"><Relationship Id="workbook" Type="'+r+'/package" Target="../embeddings/data.xlsx"/></Relationships>',
             'word/embeddings/data.xlsx': plain}
    result = package(parts)
    name = kind+'.docx'
    (output / name).write_bytes(result)
    manifest['files'][name] = {'sha256': hashlib.sha256(result).hexdigest(), 'workbook_sha256': hashlib.sha256(plain).hexdigest(), 'kind': kind, 'values': [[10,0,-2,6],[-5,8,None,4]]}
(output / 'manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
