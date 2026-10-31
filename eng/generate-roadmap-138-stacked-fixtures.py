"""Author synthetic stacked-chart DOCX graphs independently with XlsxWriter."""
import argparse
import copy
from datetime import datetime
import hashlib
from io import BytesIO
import json
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED

import xlsxwriter

ROOT = Path(__file__).resolve().parents[1]
C = '{http://schemas.openxmlformats.org/drawingml/2006/chart}'
W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
M = '{http://schemas.openxmlformats.org/officeDocument/2006/math}'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    destination = parser.parse_args().output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a scratch directory outside the committed fixture tree.')
    if xlsxwriter.__version__ != '3.2.9':
        raise RuntimeError('This generator requires XlsxWriter 3.2.9.')
    destination.mkdir(parents=True, exist_ok=False)
    shell = ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/P2/column.docx'
    files = {}
    for kind in ('bar', 'column'):
        stream = BytesIO(); workbook = xlsxwriter.Workbook(stream, {'in_memory': True})
        workbook.set_properties({'title': 'Independent synthetic chart data', 'author': 'Fixture generator', 'created': datetime(2000, 1, 1)})
        sheet = workbook.add_worksheet('Data')
        for row, values in enumerate([['Category', 'North', 'South'], ['First', 10, -5], ['Second', 0, 8], ['Third', -2, None], ['Fourth', 6, 4]]):
            sheet.write_row(row, 0, values)
        chart = workbook.add_chart({'type': kind, 'subtype': 'stacked'}); chart.set_title({'name': 'Independent values'})
        for column, color in ((1, '#2468AC'), (2, '#C03040')):
            chart.add_series({'name': ['Data', 0, column], 'categories': ['Data', 1, 0, 4, 0], 'values': ['Data', 1, column, 4, column],
                              'fill': {'color': color}, 'line': {'none': True}})
        sheet.insert_chart('E1', chart); workbook.close()
        with ZipFile(BytesIO(stream.getvalue())) as package:
            chart_xml = ET.fromstring(package.read('xl/charts/chart1.xml'))
        output = destination / ('stacked-' + kind + '.docx')
        with ZipFile(shell) as source:
            original = ET.fromstring(source.read('word/charts/chart1.xml'))
            external = original.find(C + 'externalData')
            if external is None:
                raise ValueError('Expected a local workbook relationship in the synthetic shell.')
            index = next((i for i, node in enumerate(chart_xml) if node.tag == C + 'printSettings'), len(chart_xml))
            chart_xml.insert(index, copy.deepcopy(external))
            document = ET.fromstring(source.read('word/document.xml')); body = document.find(W + 'body')
            for paragraph in list(body.findall(W + 'p')):
                if paragraph.find('.//' + M + 'oMath') is not None:
                    body.remove(paragraph)
            with ZipFile(output, 'w') as target:
                for name in source.namelist():
                    data = (ET.tostring(chart_xml, encoding='utf-8', xml_declaration=True) if name == 'word/charts/chart1.xml'
                            else ET.tostring(document, encoding='utf-8', xml_declaration=True) if name == 'word/document.xml'
                            else source.read(name))
                    target.writestr(ZipInfo(name, (2000, 1, 1, 0, 0, 0)), data, compress_type=ZIP_DEFLATED)
        files[output.name] = hashlib.sha256(output.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic XlsxWriter 3.2.9 charts, BSD-2-Clause; existing synthetic MIT OPC shell and local workbook.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                'sourceShellSha256': hashlib.sha256(shell.read_bytes()).hexdigest(), 'documents': files}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
