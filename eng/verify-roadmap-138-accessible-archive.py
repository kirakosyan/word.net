"""Independently check tagged archive structure and create corrupt controls."""
import argparse
import json
from pathlib import Path
from xml.etree import ElementTree as ET

import fitz
import pypdf
from pypdf import PdfReader
from pypdf.generic import ArrayObject, DictionaryObject, IndirectObject


def identity(value):
    if isinstance(value, IndirectObject):
        return value.idnum, value.generation
    reference = getattr(value, 'indirect_reference', None)
    return None if reference is None else (reference.idnum, reference.generation)


def check(path, kind):
    reader = PdfReader(path)
    catalog = reader.trailer['/Root']
    if catalog.get('/Lang') != 'en' or not catalog.get('/OutputIntents'):
        raise ValueError('Missing document language or archive output intent.')
    packet = ET.fromstring(catalog['/Metadata'].get_data())
    aid = '{http://www.aiim.org/pdfa/ns/id/}'
    ua = '{http://www.aiim.org/pdfua/ns/id/}'
    if not any(element.get(aid + 'part') == '2' and element.get(aid + 'conformance') == 'A'
               and element.get(ua + 'part') == '1' for element in packet.iter()):
        raise ValueError('Archive and accessibility identification do not agree.')
    tree = catalog['/StructTreeRoot']; nums = tree['/ParentTree']['/Nums']
    parents = {int(nums[index]): nums[index + 1] for index in range(0, len(nums), 2)}
    pages = {identity(page.indirect_reference): page for page in reader.pages}
    roles, alternatives, marked, annotations = [], [], set(), set()

    def walk(reference, parent=None):
        if isinstance(reference, ArrayObject):
            for item in reference:
                walk(item, parent)
            return
        node = reference.get_object()
        if not isinstance(node, DictionaryObject):
            raise ValueError('Unexpected structure child.')
        if node.get('/Type') == '/MCR':
            page = pages[identity(node.raw_get('/Pg'))]; mcid = int(node['/MCID'])
            key = (identity(page.indirect_reference), mcid)
            if key in marked or identity(parents[int(page['/StructParents'])][mcid]) != identity(parent):
                raise ValueError('Wrong or repeated marked-content parent.')
            marked.add(key); return
        if node.get('/Type') == '/OBJR':
            annotation = node['/Obj']; key = int(annotation['/StructParent'])
            if identity(parents[key]) != identity(parent) or int(annotation.get('/F', 0)) & 4 == 0:
                raise ValueError('Wrong annotation parent or archive print flag.')
            annotations.add(identity(node.raw_get('/Obj'))); return
        if node.get('/Type') != '/StructElem' or (parent is not None and identity(node.raw_get('/P')) != identity(parent)):
            raise ValueError('Wrong structure element parent.')
        role = str(node['/S']); roles.append(role)
        if role in ('/Figure', '/Formula'):
            alternatives.append((role, str(node.get('/Alt', ''))))
        if '/K' in node:
            walk(node.raw_get('/K'), node)

    walk(tree.raw_get('/K'))
    actual_annotations = {identity(reference) for page in reader.pages for reference in page.get('/Annots', [])}
    if not marked or annotations != actual_annotations:
        raise ValueError('Marked content or annotation ownership is incomplete.')
    if kind == 'basic' and not all(role in roles for role in ('/H1', '/Table', '/TH', '/TD', '/Link')):
        raise ValueError('The basic document lost a semantic role.')
    if kind == 'objects' and alternatives != [('/Figure', 'Revenue in euros by region. North; Revenue = 0. South; Revenue = missing'),
                                                ('/Formula', 'Half of x. Equation: (x)/(2)')]:
        raise ValueError('The chart or equation alternative changed.')
    if kind == 'box' and alternatives != [('/Figure', 'Blue trend diagram')]:
        raise ValueError('The described figure alternative changed.')
    if kind == 'images' and alternatives != [('/Figure', 'Red and blue grid')]:
        raise ValueError('Image alternatives or decorative intent changed.')
    if kind == 'box-decorative' and alternatives:
        raise ValueError('Decorative vectors cannot become figures.')
    with fitz.open(path) as pdf:
        fonts = [font for page in pdf for font in page.get_fonts()]
        if not fonts or not all(pdf.extract_font(font[0])[3] for font in fonts):
            raise ValueError('A required font is not embedded.')
    return {'file': path.name, 'roles': roles, 'markedContent': len(marked), 'annotations': len(annotations), 'alternatives': alternatives}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('controls', type=Path)
    args = parser.parse_args()
    if pypdf.__version__ != '6.10.0' or fitz.VersionBind != '1.27.2.2':
        raise RuntimeError('Requires pinned pypdf 6.10.0 and PyMuPDF 1.27.2.2.')
    if args.controls.exists():
        raise FileExistsError('Choose a new control directory.')
    results = [check(args.directory / ('accessible-archive-' + kind + '.pdf'), kind)
               for kind in ('basic', 'objects', 'box', 'box-decorative', 'images')]
    args.controls.mkdir(parents=True)
    source = args.directory / 'accessible-archive-objects.pdf'
    for name, key in [('metadata', 'Metadata'), ('language', 'Lang')]:
        with fitz.open(source) as pdf:
            pdf.xref_set_key(pdf.pdf_catalog(), key, 'null')
            pdf.save(args.controls / ('accessible-archive-' + name + '-negative.pdf'))
    changed = args.controls / 'changed-object-alternative.pdf'
    with fitz.open(source) as pdf:
        figure = next(i for i in range(1, pdf.xref_length()) if pdf.xref_get_key(i, 'S')[1] == '/Figure')
        pdf.xref_set_key(figure, 'Alt', '(Changed description)'); pdf.save(changed)
    try:
        check(changed, 'objects')
    except ValueError as error:
        if str(error) != 'The chart or equation alternative changed.':
            raise
    else:
        raise ValueError('Changed object alternative was accepted.')
    print(json.dumps({'reader': 'pypdf 6.10.0 and PyMuPDF 1.27.2.2', 'results': results,
                      'negativeControls': 'changed alternative rejected; missing metadata and language prepared for external validators'}))


if __name__ == '__main__':
    main()
