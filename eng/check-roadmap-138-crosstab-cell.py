"""Inspect the placed cross-tab and compare independent PDF column origins."""
import argparse
import json
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
H = '{http://www.w3.org/1999/xhtml}'
ROWS = [['Row', 'Q1', 'Q2', 'Total'], ['North', '3.5', '0.0', '3.5'],
        ['South', '0.0', '4.0', '4.0'], ['Total', '3.5', '4.0', '7.5']]


def words(path):
    return [node for node in ET.parse(path).iter(H + 'word')]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('docx', type=Path)
    parser.add_argument('wordnet_text', type=Path)
    parser.add_argument('reference_text', type=Path)
    parser.add_argument('wordnet_bbox', type=Path)
    parser.add_argument('reference_bbox', type=Path)
    args = parser.parse_args()
    with zipfile.ZipFile(args.docx) as archive:
        root = ET.fromstring(archive.read('word/document.xml'))
    body = root.find(W + 'body')
    outer = body.find(W + 'tbl')
    cells = outer.find(W + 'tr').findall(W + 'tc')
    if len(cells) != 2 or ''.join(cells[1].itertext()) != 'Neighbor':
        raise ValueError('Outer cell ownership changed.')
    cell = cells[0]
    properties = cell.find(W + 'tcPr')
    if properties.find(W + 'tcW').get(W + 'w') != '4000' or properties.find(W + 'shd').get(W + 'fill') != 'DDEEFF':
        raise ValueError('Outer cell width or fill changed.')
    if [e.tag for e in cell] != [W + 'tcPr', W + 'p', W + 'tbl', W + 'p']:
        raise ValueError('Surrounding paragraph order changed.')
    table = cell.find(W + 'tbl')
    grid = [int(column.get(W + 'w')) for column in table.find(W + 'tblGrid')]
    if len(grid) != 4 or sum(grid) > 4000 - 240:
        raise ValueError('Nested grid exceeds the explicit usable cell width.')
    rows = [[''.join(item.itertext()) for item in row.findall(W + 'tc')] for row in table.findall(W + 'tr')]
    if rows != ROWS or table.findall('.//' + W + 'tblHeader'):
        raise ValueError(f'Unexpected rows or repeating nested header: {rows}')
    if root.findall('.//' + W + 'bookmarkStart'):
        raise ValueError('The placeholder bookmark survived insertion.')
    for item in table.findall('.//' + W + 'tc'):
        margins = item.find('./' + W + 'tcPr/' + W + 'tcMar')
        actual = {side.tag: side.get(W + 'w') for side in margins}
        if actual != {W + 'top': '0', W + 'left': '0', W + 'bottom': '0', W + 'right': '240'}:
            raise ValueError(f'Nested cell margins are not explicit: {actual}')
    for path in (args.wordnet_text, args.reference_text):
        lines = [line.split() for line in path.read_text(encoding='utf-8').splitlines()]
        actual = [line for line in lines if line and line[0] in ('Row', 'North', 'South', 'Total')]
        if actual != ROWS:
            raise ValueError(f'Unexpected PDF table text in {path}: {actual}')
    first = {word.text: float(word.get('xMin')) for word in words(args.wordnet_bbox) if word.text in ('Row', 'Q1', 'Q2', 'North', 'South', '7.5')}
    second = {word.text: float(word.get('xMin')) for word in words(args.reference_bbox) if word.text in first}
    if set(first) != {'Row', 'Q1', 'Q2', 'North', 'South', '7.5'} or set(first) != set(second):
        raise ValueError('Missing independent PDF labels.')
    differences = {key: round(abs(first[key] - second[key]), 4) for key in sorted(first)}
    if max(differences.values()) > 0.2:
        raise ValueError(f'Column origins differ by more than 0.2 points: {differences}')
    print(json.dumps({'gridTwips': grid, 'rows': rows, 'columnOriginDifferencesPt': differences}))


if __name__ == '__main__':
    main()
