"""Check independent horizontal-chart data and normalized vector geometry."""
import argparse
import hashlib
import json
import re
from io import BytesIO
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

C = '{http://schemas.openxmlformats.org/drawingml/2006/chart}'
S = '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
H = '{http://www.w3.org/1999/xhtml}'
SVG = '{http://www.w3.org/2000/svg}'


def graph(path):
    with ZipFile(path) as archive:
        chart_name = next(name for name in archive.namelist() if name.endswith('/chart1.xml'))
        book_name = next(name for name in archive.namelist() if name.endswith('.xlsx'))
        chart = archive.read(chart_name)
        workbook = archive.read(book_name)
    root = ET.fromstring(chart)
    data = root.find('.//' + C + 'barChart')
    if data.find(C + 'barDir').get('val') != 'bar' or data.find(C + 'grouping').get('val') != 'clustered':
        raise ValueError('The chart is not an editable clustered horizontal bar graph.')
    series = data.findall(C + 'ser')
    values = []
    for entry in series:
        cache = entry.find('.//' + C + 'numCache')
        values.append({int(point.get('idx')): float(point.find(C + 'v').text) for point in cache.findall(C + 'pt')})
    if values != [{0: 10, 1: 0, 2: -2, 3: 6}, {0: -5, 1: 8, 3: 4}]:
        raise ValueError(f'Unexpected semantic caches: {values}')
    with ZipFile(BytesIO(workbook)) as book:
        sheet = ET.fromstring(book.read('xl/worksheets/sheet1.xml'))
    cells = {cell.get('r'): cell.find(S + 'v').text for cell in sheet.iter(S + 'c') if cell.find(S + 'v') is not None}
    if [float(cells[key]) for key in ('B2', 'B3', 'B4', 'B5', 'C2', 'C3', 'C5')] != [10, 0, -2, 6, -5, 8, 4] or 'C4' in cells:
        raise ValueError('Workbook values or the missing value changed.')
    return hashlib.sha256(chart).hexdigest(), hashlib.sha256(workbook).hexdigest()


def geometry(path, reference):
    root = ET.parse(path).getroot()
    result = {'2468AC': [], 'C03040': []}
    for node in root.iter():
        if reference and node.tag == SVG + 'path':
            fill = node.get('fill', '')
            if not fill.startswith('rgb('):
                continue
            color = ''.join(f'{round(float(value) * 2.55):02X}' for value in re.findall(r'([0-9.]+)%', fill))
            if color not in result:
                continue
            numbers = [float(value) for value in re.findall(r'-?\d+(?:\.\d+)?', node.get('d', ''))]
            if len(numbers) < 8 or len(numbers) % 2:
                continue
            xs, ys = numbers[::2], numbers[1::2]
            x, y, width, height = min(xs), min(ys), max(xs) - min(xs), max(ys) - min(ys)
        elif not reference and node.tag == SVG + 'rect':
            color = node.get('fill', '').lstrip('#').upper()
            if color not in result:
                continue
            x, y, width, height = (float(node.get(key)) for key in ('x', 'y', 'width', 'height'))
        else:
            continue
        if width > 10 and height > 0:
            result[color].append((x, y, width, height))
    if any(len(bars) != 3 for bars in result.values()):
        raise ValueError(f'Unexpected bar count: {result}')
    return result


def labels(path):
    wanted = {'First', 'Second', 'Third', 'Fourth', 'North', 'South'}
    result = {element.text: element for element in ET.parse(path).iter(H + 'word') if element.text in wanted}
    if set(result) != wanted:
        raise ValueError('Missing category or legend labels.')
    return result


def inspect(bars, text):
    expected = {'2468AC': [0.2, 0.6, 1.0], 'C03040': [0.5, 0.625, 1.0]}
    for color, items in bars.items():
        widths = sorted(item[2] for item in items)
        if any(abs(actual / widths[-1] - target) > 0.001 for actual, target in zip(widths, expected[color])):
            raise ValueError(f'Incorrect relative bar lengths: {color} {widths}')
    order = [float(text[key].get('yMin')) for key in ('Fourth', 'Third', 'Second', 'First')]
    if order != sorted(order) or float(text['South'].get('yMin')) >= float(text['North'].get('yMin')):
        raise ValueError('Category or legend order changed.')
    blue = sorted(bars['2468AC'], key=lambda item: item[1])
    red = sorted(bars['C03040'], key=lambda item: item[1])
    if not (red[0][1] < blue[0][1] and red[-1][1] < blue[-1][1]):
        raise ValueError('Series order within a category changed.')
    baseline = blue[-1][0]
    if abs(blue[1][0] + blue[1][2] - baseline) > 0.02 or abs(red[-1][0] + red[-1][2] - baseline) > 0.02:
        raise ValueError('Signed bars do not share a zero axis.')
    slot = float(text['First'].get('yMin')) - float(text['Second'].get('yMin'))
    thickness = blue[-1][3] / slot
    if abs(thickness - 1 / 3.5) > 0.002:
        raise ValueError(f'Unexpected default gap/series thickness: {thickness}')
    return {'zeroX': baseline, 'firstLabelGap': baseline - float(text['First'].get('xMax')), 'heightToSlot': thickness}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('source', 'saved', 'wordnet_svg', 'reference_svg', 'wordnet_bbox', 'reference_bbox'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    source, saved = graph(args.source), graph(args.saved)
    if source != saved:
        raise ValueError('Chart XML or local workbook bytes changed during save.')
    actual = inspect(geometry(args.wordnet_svg, False), labels(args.wordnet_bbox))
    reference = inspect(geometry(args.reference_svg, True), labels(args.reference_bbox))
    label_gap_difference = abs(actual['firstLabelGap'] - reference['firstLabelGap'])
    origin_difference = abs(actual['zeroX'] - reference['zeroX'])
    if label_gap_difference > 3:
        raise ValueError(f'Category label does not track the zero axis: {actual}, {reference}')
    # Preserve the existing independent 0.5-point geometry tolerance. These
    # comparisons currently fail; relative checks above do not replace them.
    if origin_difference <= 0.5 or label_gap_difference <= 0.5:
        raise ValueError('A recorded 0.5-point failure changed; review the reference evidence.')
    print(json.dumps({'chartSha256': source[0], 'workbookSha256': source[1], 'wordnet': actual, 'libreOffice': reference,
                      'retainedAbsoluteGeometryFailures': {'tolerancePt': 0.5, 'zeroOriginDifferencePt': origin_difference,
                                                           'labelGapDifferencePt': label_gap_difference, 'passed': False}}))


if __name__ == '__main__':
    main()
