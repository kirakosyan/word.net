"""Author a horizontal chart XML graph using XlsxWriter and a synthetic OPC shell."""
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
    parser.add_argument('--output', required=True, type=Path)
    destination = parser.parse_args().output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a scratch directory outside the committed fixture tree.')
    destination.mkdir(parents=True, exist_ok=True)
    output = destination / 'horizontal-bar.docx'
    if output.exists():
        raise FileExistsError('Choose a fresh output directory.')
    if xlsxwriter.__version__ != '3.2.9':
        raise RuntimeError('This reference generator requires XlsxWriter 3.2.9.')
    stream = BytesIO()
    book = xlsxwriter.Workbook(stream, {'in_memory': True})
    book.set_properties({'title': 'Independent synthetic chart data', 'author': 'Fixture generator', 'created': datetime(2000, 1, 1)})
    sheet = book.add_worksheet('Data')
    for row, values in enumerate([['Category', 'North', 'South'], ['First', 10, -5], ['Second', 0, 8], ['Third', -2, None], ['Fourth', 6, 4]]):
        sheet.write_row(row, 0, values)
    chart = book.add_chart({'type': 'bar'})
    chart.set_title({'name': 'Independent values'})
    for column, color in [(1, '#2468AC'), (2, '#C03040')]:
        chart.add_series({'name': ['Data', 0, column], 'categories': ['Data', 1, 0, 4, 0], 'values': ['Data', 1, column, 4, column],
                          'fill': {'color': color}, 'line': {'none': True}})
    sheet.insert_chart('E1', chart)
    book.close()
    with ZipFile(BytesIO(stream.getvalue())) as workbook:
        chart_xml = ET.fromstring(workbook.read('xl/charts/chart1.xml'))
    source = ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/P2/column.docx'
    with ZipFile(source) as existing:
        original_chart = ET.fromstring(existing.read('word/charts/chart1.xml'))
        external = original_chart.find(C + 'externalData')
        if external is None:
            raise ValueError('The synthetic source must contain its local workbook relationship.')
        index = next((i for i, node in enumerate(chart_xml) if node.tag == C + 'printSettings'), len(chart_xml))
        chart_xml.insert(index, copy.deepcopy(external))
        document = ET.fromstring(existing.read('word/document.xml'))
        body = document.find(W + 'body')
        for paragraph in list(body.findall(W + 'p')):
            if paragraph.find('.//' + M + 'oMath') is not None:
                body.remove(paragraph)
        with ZipFile(output, 'w') as result:
            for name in existing.namelist():
                data = (ET.tostring(chart_xml, encoding='utf-8', xml_declaration=True) if name == 'word/charts/chart1.xml'
                        else ET.tostring(document, encoding='utf-8', xml_declaration=True) if name == 'word/document.xml'
                        else existing.read(name))
                info = ZipInfo(name, (2000, 1, 1, 0, 0, 0))
                result.writestr(info, data, compress_type=ZIP_DEFLATED)
    manifest = {'provenance': 'Synthetic XlsxWriter 3.2.9 bar chart; MIT generator, BSD-2-Clause XlsxWriter; existing synthetic MIT OPC shell.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                'sourceShellSha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                'documentSha256': hashlib.sha256(output.read_bytes()).hexdigest()}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
