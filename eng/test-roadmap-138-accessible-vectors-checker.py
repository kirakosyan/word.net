"""Reject corrupted reading order, figure alternative and ParentTree ownership."""
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
    checker = Path(__file__).with_name('check-roadmap-138-accessible-vectors.py')
    expected = {'reordered': 'Unexpected logical source order', 'wrong-alt': 'Figure alternative text changed',
                'wrong-parent': 'wrong ParentTree owner'}
    for kind, reason in expected.items():
        writer = PdfWriter(clone_from=args.source)
        root = writer.root_object['/StructTreeRoot']
        children = root['/K']['/K']
        figure = children[1].get_object()
        if kind == 'reordered':
            children[0], children[-1] = children[-1], children[0]
        elif kind == 'wrong-alt':
            figure[NameObject('/Alt')] = TextStringObject('Changed description')
        else:
            mcid = int(figure['/K'][0].get_object()['/MCID'])
            owners = root['/ParentTree']['/Nums'][1]
            owners[mcid] = owners[0]
        path = args.output / (kind + '.pdf')
        with path.open('wb') as stream:
            writer.write(stream)
        result = subprocess.run([sys.executable, str(checker), str(path)], capture_output=True)
        if result.returncode == 0 or reason not in result.stderr.decode('utf-8'):
            raise ValueError('The checker did not reject ' + kind + ' for its intended reason.')
    result = subprocess.run([sys.executable, str(checker), str(args.untagged)], capture_output=True)
    if result.returncode == 0 or "KeyError: '/StructTreeRoot'" not in result.stderr.decode('utf-8'):
        raise ValueError('The checker accepted an untagged PDF.')
    print('Independent checker rejected reordered, altered, misowned and untagged controls.')


if __name__ == '__main__':
    main()
