"""Independently inspect reading order, ParentTree and link ownership for one PDF."""
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
    parser.add_argument('--pages', type=int, choices=(1, 2), default=1)
    parser.add_argument('--selected', action='store_true')
    parser.add_argument('--two', action='store_true')
    args = parser.parse_args()
    if pypdf.__version__ != '6.10.0':
        raise RuntimeError('This independent checker requires pypdf 6.10.0.')
    reader = PdfReader(args.pdf)
    if len(reader.pages) != args.pages:
        raise ValueError('Unexpected physical page count in the independent specimen.')
    root = reader.trailer['/Root']['/StructTreeRoot']
    nums = root['/ParentTree']['/Nums']
    if len(nums) % 2:
        raise ValueError('Malformed ParentTree.')
    parents = {int(nums[index]): nums[index + 1] for index in range(0, len(nums), 2)}
    pages = {identity(page.indirect_reference): (index, page) for index, page in enumerate(reader.pages)}
    marks = {}
    for page_index, page in enumerate(reader.pages):
        stack = []
        def operand(operator, arguments, ctm, tm):
            if operator in (b'BDC', b'BMC'):
                value = arguments[1].get('/MCID') if operator == b'BDC' and len(arguments) > 1 and isinstance(arguments[1], dict) else None
                stack.append(value)
            elif operator == b'EMC':
                if not stack:
                    raise ValueError('Unbalanced marked content.')
                stack.pop()
        def visit(value, ctm, tm, font, size):
            if value and stack and stack[-1] is not None:
                key = (page_index, int(stack[-1]))
                marks[key] = marks.get(key, '') + value
        page.extract_text(visitor_operand_before=operand, visitor_text=visit)
        if stack:
            raise ValueError('Unbalanced marked content at page end.')
    roles, output, links, used = [], [], 0, set()
    def walk(reference, parent=None):
        nonlocal links
        if isinstance(reference, ArrayObject):
            for child in reference:
                walk(child, parent)
            return
        node = reference.get_object()
        if not isinstance(node, DictionaryObject):
            raise ValueError('Unexpected structure-tree child.')
        kind = node.get('/Type')
        if kind == '/MCR':
            page_index, page = pages[identity(node.raw_get('/Pg'))]
            mcid = int(node['/MCID']); key = (page_index, mcid)
            if key in used or key not in marks or identity(parents[int(page['/StructParents'])][mcid]) != identity(parent):
                raise ValueError('Marked content is missing, duplicated or has the wrong ParentTree owner.')
            used.add(key); output.append(marks[key]); return
        if kind == '/OBJR':
            annotation = node['/Obj']; owner = parents[int(annotation['/StructParent'])]
            if identity(owner) != identity(parent) or annotation['/Subtype'] != '/Link' or annotation['/A']['/URI'] != 'https://example.test/details':
                raise ValueError('Link annotation or ParentTree owner is incorrect.')
            links += 1; return
        if kind != '/StructElem':
            raise ValueError('Unexpected structure-tree element.')
        if parent is not None and identity(node.raw_get('/P')) != identity(parent):
            raise ValueError('Structure parent reference is incorrect.')
        roles.append(str(node['/S']))
        if '/K' in node:
            walk(node.raw_get('/K'), node)
        if node['/S'] == '/P':
            output.append('\n')
    document = root.raw_get('/K'); walk(document)
    document_node = document.get_object()
    children = [str(child.get_object()['/S']) for child in document_node['/K']]
    ordered = ''.join(output)
    expected = (['Before box', 'First box', 'Second box', 'After box'] if args.two
                else ([] if args.selected else ['Before box']) + ['Box first', 'Details', 'Box last', 'After box'])
    expected_children = (['/P', '/Div', '/Div', '/P'] if args.two
                         else ['/Div', '/P'] if args.selected else ['/P', '/Div', '/P'])
    expected_links = 0 if args.two else 1
    lines = [line.strip() for line in ordered.splitlines() if line.strip()]
    if children != expected_children or lines != expected or links != expected_links or len(used) != len(marks):
        raise ValueError(f'Unexpected logical order or semantic coverage: {children}, {ordered!r}, {links}.')
    print(json.dumps({'structureChildren': children, 'logicalLines': lines, 'markedContent': len(used), 'links': links,
                      'parentTreeEntries': len(parents), 'roles': roles}))


if __name__ == '__main__':
    main()
