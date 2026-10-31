"""Check independently authored scalar-field retention and native PDF evidence."""
import argparse
import json
from pathlib import Path
import subprocess
from xml.etree import ElementTree as ET
from zipfile import ZipFile

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
DC = '{http://purl.org/dc/elements/1.1/}'
H = '{http://www.w3.org/1999/xhtml}'


def field(path):
    with ZipFile(path) as archive:
        title = ET.fromstring(archive.read('docProps/core.xml')).find(DC + 'title').text
        document = ET.fromstring(archive.read('word/document.xml'))
    fields = document.findall('.//' + W + 'fldSimple')
    if len(fields) != 1:
        raise ValueError(f'Expected one scalar field in {path}.')
    return title, fields[0].get(W + 'instr').strip(), ''.join(node.text or '' for node in fields[0].iter(W + 't'))


def boxes(path):
    return {node.text: {key: float(node.get(key)) for key in ('xMin', 'yMin', 'xMax', 'yMax')}
            for node in ET.parse(path).iter(H + 'word')}


def text(path):
    return subprocess.check_output(['pdftotext', '-layout', str(path), '-']).decode('utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    directory = args.directory
    for name, title in [('hindi', 'क्षि'), ('arabic', 'مرحبا')]:
        original = field(directory / f'{name}-stale.docx')
        saved = field(directory / f'{name}-saved-final.docx')
        reference = field(directory / f'{name}-reference.docx')
        if original != (title, 'DOCPROPERTY Title', 'old-cache') or saved != (title, 'DOCPROPERTY Title', title) or reference != saved:
            raise ValueError(f'The {name} field, title or refreshed cache changed unexpectedly.')
    actual = boxes(directory / 'hindi-wordnet-final-bbox.html')['क्षि']
    expected = boxes(directory / 'office/hindi-reference-bbox.html')['क्षि']
    x_difference = abs(actual['xMin'] - expected['xMin'])
    width_difference = abs((actual['xMax'] - actual['xMin']) - (expected['xMax'] - expected['xMin']))
    y_difference = abs(actual['yMin'] - expected['yMin'])
    if x_difference > 0.5 or width_difference > 0.5:
        raise ValueError('Hindi field width or horizontal position exceeded the original 0.5-point bound.')
    if y_difference <= 0.5:
        raise ValueError('The recorded Hindi vertical failure changed; review the reference evidence.')
    hindi_actual = text(directory / 'hindi-wordnet-final.pdf')
    hindi_reference = text(directory / 'office/hindi-reference.pdf')
    if 'Label: क्षि End 42' not in hindi_actual or 'Label: क्षि End 42' not in hindi_reference:
        raise ValueError('Hindi PDF text differs from the authored data.')
    arabic_actual = text(directory / 'arabic-wordnet-final.pdf')
    arabic_reference = text(directory / 'office/arabic-reference.pdf')
    if 'ابحرم' not in arabic_actual or 'مرحبا' not in arabic_reference:
        raise ValueError('The recorded Arabic extraction difference changed; review the reference evidence.')
    print(json.dumps({'hindi': {'xDifferencePt': x_difference, 'widthDifferencePt': width_difference,
                                'yDifferencePt': y_difference, 'originalTolerancePt': 0.5, 'verticalPassed': False},
                      'arabic': {'wordnetPopplerText': 'ابحرم', 'referencePopplerText': 'مرحبا', 'orderPassed': False}}, ensure_ascii=False))


if __name__ == '__main__':
    main()
