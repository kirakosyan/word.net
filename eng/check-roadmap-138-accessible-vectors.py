"""Inspect tagged vector figures, paint ownership and source order with pypdf."""
import argparse
import json
from pathlib import Path

import pypdf
from pypdf import PdfReader
from pypdf.generic import ArrayObject, DictionaryObject, IndirectObject


def identity(value):
    if isinstance(value, IndirectObject):
        return value.idnum, value.generation
    reference = getattr(value, 'indirect_reference', None)
    return None if reference is None else (reference.idnum, reference.generation)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('pdf', type=Path)
    parser.add_argument('--mode', choices=('described', 'decorative', 'selection', 'two', 'stroke', 'box', 'box-decorative'), default='described')
    args = parser.parse_args()
    if pypdf.__version__ != '6.10.0':
        raise RuntimeError('This checker requires pypdf 6.10.0.')
    reader = PdfReader(args.pdf)
    if len(reader.pages) != 1:
        raise ValueError('Expected one physical output page.')
    root = reader.trailer['/Root']['/StructTreeRoot']
    nums = root['/ParentTree']['/Nums']
    if len(nums) % 2:
        raise ValueError('Malformed ParentTree.')
    parents = {int(nums[index]): nums[index + 1] for index in range(0, len(nums), 2)}
    pages = {identity(page.indirect_reference): (index, page) for index, page in enumerate(reader.pages)}
    marks, painted, artifacts = {}, set(), 0
    for page_index, page in enumerate(reader.pages):
        stack = []
        def operand(operator, arguments, ctm, tm):
            nonlocal artifacts
            if operator in (b'BDC', b'BMC'):
                mcid = arguments[1].get('/MCID') if operator == b'BDC' and len(arguments) > 1 and isinstance(arguments[1], dict) else None
                stack.append((str(arguments[0]), mcid))
            elif operator == b'EMC':
                if not stack:
                    raise ValueError('Unbalanced marked content.')
                stack.pop()
            elif operator in (b'f', b'F', b'f*', b'S', b's', b'B', b'B*', b'b', b'b*') and stack:
                role, mcid = stack[-1]
                if role == '/Figure' and mcid is not None:
                    painted.add((page_index, int(mcid)))
                elif role == '/Artifact':
                    artifacts += 1
        def visit(value, ctm, tm, font, size):
            if value and stack and stack[-1][1] is not None:
                key = (page_index, int(stack[-1][1]))
                marks[key] = marks.get(key, '') + value
        page.extract_text(visitor_operand_before=operand, visitor_text=visit)
        if stack:
            raise ValueError('Unbalanced marked content at page end.')
    used, logical, figures = set(), [], []
    def walk(reference, parent=None):
        if isinstance(reference, ArrayObject):
            for child in reference:
                walk(child, parent)
            return
        node = reference.get_object()
        if not isinstance(node, DictionaryObject):
            raise ValueError('Unexpected structure-tree child.')
        if node.get('/Type') == '/MCR':
            page_index, page = pages[identity(node.raw_get('/Pg'))]
            mcid = int(node['/MCID']); key = (page_index, mcid)
            if key in used or identity(parents[int(page['/StructParents'])][mcid]) != identity(parent):
                raise ValueError('Marked content has the wrong ParentTree owner.')
            used.add(key)
            if key in marks:
                logical.append(marks[key])
            if parent.get('/S') == '/Figure' and key not in painted:
                raise ValueError('A figure mark does not own painted vector content.')
            return
        if node.get('/Type') != '/StructElem':
            raise ValueError('Unexpected structure-tree element.')
        if parent is not None and identity(node.raw_get('/P')) != identity(parent):
            raise ValueError('Structure parent reference is incorrect.')
        if node.get('/S') == '/Figure':
            figures.append(str(node.get('/Alt', '')))
            logical.append('[Figure]')
        if '/K' in node:
            walk(node.raw_get('/K'), node)
        if node.get('/S') in ('/P', '/Figure'):
            logical.append('\n')
    document = root.raw_get('/K'); walk(document)
    children = [str(child.get_object()['/S']) for child in document.get_object()['/K']]
    lines = [line.strip() for line in ''.join(logical).splitlines() if line.strip()]
    expected = (['Before', 'After'] if args.mode == 'decorative' else
                ['[Figure]', 'After'] if args.mode == 'selection' else
                ['Before', '[Figure]', '[Figure]', 'After'] if args.mode == 'two' else
                ['Before', '[Figure]', 'Box text', 'After'] if args.mode == 'box' else
                ['Before', 'Box text', 'After'] if args.mode == 'box-decorative' else
                ['Before', '[Figure]', 'After'])
    expected_roles = (['/P', '/Figure', '/Div', '/P'] if args.mode == 'box'
                      else ['/P', '/Div', '/P'] if args.mode == 'box-decorative'
                      else ['/Figure' if line == '[Figure]' else '/P' for line in expected])
    expected_alts = ([] if args.mode in ('decorative', 'box-decorative') else
                     ['Blue trend diagram', 'Second blue diagram'] if args.mode == 'two' else ['Blue trend diagram'])
    if children != expected_roles or lines != expected:
        raise ValueError('Unexpected logical source order.')
    if figures != expected_alts:
        raise ValueError('Figure alternative text changed.')
    if args.mode in ('decorative', 'box-decorative') and (painted or artifacts == 0):
        raise ValueError('Decorative vector paint must be an artifact.')
    if args.mode == 'stroke' and len(painted) < 2:
        raise ValueError('Separate fill and stroke marks were not retained.')
    if not painted <= used:
        raise ValueError('Painted figure marks are missing from the structure tree.')
    print(json.dumps({'mode': args.mode, 'children': children, 'logicalLines': lines, 'alternatives': figures,
                      'figurePaintMarks': len(painted), 'artifactPaintOperations': artifacts, 'parentTreeEntries': len(parents)}))


if __name__ == '__main__':
    main()
