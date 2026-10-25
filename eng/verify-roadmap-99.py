"""Independent logical text, structure and negative-control checks for roadmap #99.

Uses the existing pinned P2 verification environment. No production provider is loaded.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path
import fitz


def qualify(directory):
    results = []
    for script, word, language in [('arabic', 'مرحبا', 'ar'), ('hindi', 'हिन्दी', 'hi-IN')]:
        for suffix in ['', '-footer']:
            path = directory / f'native-{script}-table{suffix}.pdf'
            with fitz.open(path) as pdf:
                text = ''.join(page.get_text() for page in pdf)
                assert re.findall(r'Row\s+(\d+)', text) == [str(i) for i in range(12)]
                assert text.count(word) == 72 + len(pdf), (path, text.count(word), len(pdf))
                roles = [pdf.xref_get_key(i, 'S')[1] for i in range(1, pdf.xref_length())
                         if pdf.xref_get_key(i, 'Type')[1] == '/StructElem']
                assert roles.count('/Table') == 1 and roles.count('/TH') == 2 and roles.count('/TD') == 24
                assert pdf.xref_get_key(pdf.pdf_catalog(), 'Lang')[1] == language
                for page in pdf:
                    assert page.get_text().count('Item') == 1 and page.get_text().count('Value') == 1
                    assert len(page.get_drawings()) > 10  # visible outlines remain vectors
                    content = b''.join(pdf.xref_stream(n) for n in page.get_contents())
                    ids = re.findall(rb'/MCID (\d+)', content)
                    assert len(ids) == len(set(ids))
                results.append(dict(file=path.name, pages=len(pdf), bodyRows=12, logicalOccurrences=72,
                                    tableHeaderOwners=2, tableCellOwners=24, sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    with fitz.open(directory / 'objects-ua.pdf') as pdf:
        text = ''.join(page.get_text() for page in pdf)
        for role, expected in [('/Figure', 'Revenue in euros by region. North; Revenue = 0. South; Revenue = missing'),
                               ('/Formula', 'Half of x. Equation: (x)/(2)')]:
            owners = [i for i in range(1, pdf.xref_length()) if pdf.xref_get_key(i, 'S')[1] == role]
            assert len(owners) == 1 and pdf.xref_get_key(owners[0], 'Alt')[1] == expected
            assert text.count(expected) == 1
        assert len(pdf[0].get_drawings()) > 5
        results.append(dict(file='objects-ua.pdf', figureOwners=1, formulaOwners=1, faithfulAlternatives=True))
        pdf.xref_set_key(pdf.pdf_catalog(), 'Lang', 'null')
        pdf.save(directory / 'objects-ua-negative.pdf')
    with fitz.open(directory / 'objects-pdfa.pdf') as pdf:
        assert all(pdf.extract_font(font[0])[3] for page in pdf for font in page.get_fonts())
        pdf.xref_set_key(pdf.pdf_catalog(), 'Metadata', 'null')
        pdf.save(directory / 'objects-pdfa-negative.pdf')
    (directory / 'verification.json').write_text(json.dumps(dict(reader='PyMuPDF', version=fitz.VersionBind, results=results), indent=2) + '\n')
    print('Native tables/stories: logical content, unique MCIDs and table ownership PASS; object alternatives and vector output PASS. Negative controls generated.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    qualify(parser.parse_args().directory)
