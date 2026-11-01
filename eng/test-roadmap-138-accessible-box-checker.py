"""Ensure the independent checker rejects reordered, misowned and missing tags."""
import argparse
from pathlib import Path
import subprocess
import sys

from pypdf import PdfWriter
from pypdf.generic import NameObject, TextStringObject


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('untagged', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise FileExistsError('Choose a fresh scratch output directory.')
    args.output.mkdir(parents=True)
    checker = Path(__file__).with_name('check-roadmap-138-accessible-box.py')
    cases = {}
    expected = {'reordered': 'Unexpected logical order', 'wrong-parent': 'wrong ParentTree owner',
                'wrong-link': 'Link annotation or ParentTree owner is incorrect'}
    for kind in expected:
        writer = PdfWriter(clone_from=args.source)
        root = writer.root_object['/StructTreeRoot']
        if kind == 'reordered':
            children = root['/K']['/K']
            children[0], children[-1] = children[-1], children[0]
        elif kind == 'wrong-parent':
            owners = root['/ParentTree']['/Nums'][1]
            owners[0] = owners[-1]
        else:
            annotation = writer.pages[0]['/Annots'][0].get_object()
            annotation['/A'][NameObject('/URI')] = TextStringObject('https://example.test/wrong')
        output = args.output / (kind + '.pdf')
        with output.open('wb') as stream:
            writer.write(stream)
        result = subprocess.run([sys.executable, str(checker), str(output)], capture_output=True)
        if result.returncode == 0 or expected[kind] not in result.stderr.decode('utf-8'):
            raise ValueError(f'The checker did not reject the {kind} control for its intended reason.')
        cases[kind] = result.returncode
    result = subprocess.run([sys.executable, str(checker), str(args.untagged)], capture_output=True)
    if result.returncode == 0 or "KeyError: '/StructTreeRoot'" not in result.stderr.decode('utf-8'):
        raise ValueError('The checker accepted an untagged PDF.')
    cases['untagged'] = result.returncode
    print('Independent checker rejected all four negative controls:', cases)


if __name__ == '__main__':
    main()
