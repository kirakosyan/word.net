"""Author independent theme and table-style contrasts for roadmap #138."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)


def theme():
    a = 'http://schemas.openxmlformats.org/drawingml/2006/main'
    colors = [('dk1', '000000'), ('lt1', 'FFFFFF'), ('dk2', '222222'), ('lt2', 'EEEEEE'),
              ('accent1', '2266CC'), ('accent2', '228844'), ('accent3', 'AA4400'),
              ('accent4', '8844AA'), ('accent5', '008888'), ('accent6', '888800'),
              ('hlink', '0000FF'), ('folHlink', '800080')]
    scheme = ''.join(f'<a:{name}><a:srgbClr val="{value}"/></a:{name}>' for name, value in colors)
    font = '<a:latin typeface="Liberation Mono"/><a:ea typeface=""/><a:cs typeface=""/>'
    fill = '<a:solidFill><a:schemeClr val="phClr"/></a:solidFill>'
    line = '<a:ln w="9525">' + fill + '<a:prstDash val="solid"/></a:ln>'
    effect = '<a:effectStyle><a:effectLst/></a:effectStyle>'
    return (f'<a:theme xmlns:a="{a}" name="Independent contrast"><a:themeElements>'
            f'<a:clrScheme name="Independent">{scheme}</a:clrScheme>'
            f'<a:fontScheme name="Independent"><a:majorFont>{font}</a:majorFont><a:minorFont>{font}</a:minorFont></a:fontScheme>'
            f'<a:fmtScheme name="Independent"><a:fillStyleLst>{fill * 3}</a:fillStyleLst>'
            f'<a:lnStyleLst>{line * 3}</a:lnStyleLst><a:effectStyleLst>{effect * 3}</a:effectStyleLst>'
            f'<a:bgFillStyleLst>{fill * 3}</a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>')


def paragraph(text, properties):
    return ('<w:p><w:pPr>' + reductions.EXPLICIT + '</w:pPr><w:r><w:rPr>' + properties
            + '<w:sz w:val="22"/></w:rPr><w:t>' + escape(text) + '</w:t></w:r></w:p>')


def add_theme(parts):
    relationship = (f'<Relationship Id="theme" Type="{reductions.R}/theme" Target="theme/theme1.xml"/>')
    override = ('<Override PartName="/word/theme/theme1.xml" '
                'ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/>')
    parts['word/_rels/document.xml.rels'] = parts['word/_rels/document.xml.rels'].replace('</Relationships>', relationship + '</Relationships>')
    parts['[Content_Types].xml'] = parts['[Content_Types].xml'].replace('</Types>', override + '</Types>')
    parts['word/theme/theme1.xml'] = theme()
    return parts


def table(styled):
    text = ['Header A', 'Header B', 'Value A', 'Value B']
    cells = []
    for row in range(2):
        content = []
        for column in range(2):
            shading = '<w:shd w:val="clear" w:color="auto" w:fill="2266CC"/>' if row == 0 and not styled else ''
            props = '<w:tcPr><w:tcW w:w="2500" w:type="dxa"/>' + shading + '</w:tcPr>'
            content.append('<w:tc>' + props + reductions.p(text[row * 2 + column], reductions.EXPLICIT, 22) + '</w:tc>')
        cells.append('<w:tr>' + ''.join(content) + '</w:tr>')
    style = '<w:tblStyle w:val="IndependentTable"/>' if styled else ''
    look = '<w:tblLook w:val="0020"/>' if styled else ''
    body = ('<w:tbl><w:tblPr>' + style + '<w:tblW w:w="5000" w:type="dxa"/>' + look
            + '</w:tblPr><w:tblGrid><w:gridCol w:w="2500"/><w:gridCol w:w="2500"/></w:tblGrid>'
            + ''.join(cells) + '</w:tbl>')
    style_xml = ('<w:styles xmlns:w="' + reductions.W + '"><w:style w:type="table" w:styleId="IndependentTable">'
                 '<w:name w:val="Independent Table"/><w:tblStylePr w:type="firstRow"><w:tcPr>'
                 '<w:shd w:val="clear" w:color="auto" w:fill="2266CC"/></w:tcPr></w:tblStylePr></w:style></w:styles>')
    return reductions.package(body, {'word/styles.xml': style_xml} if styled else None), text


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    destination = args.output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a scratch output directory; committed fixtures must not be overwritten.')
    destination.mkdir(parents=True, exist_ok=True)
    cases = []

    def add(name, parts, text, hypothesis):
        path = destination / (name + '.docx')
        if path.exists():
            raise FileExistsError('Choose a fresh output directory: ' + str(path))
        reductions.write_zip(path, parts)
        cases.append(dict(id=name, category='report', path=path.name, sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                          requiredText=text, knownLosses=[], expectedPages=1, references=[], hypothesis=hypothesis,
                          expectationBasis='authored-source-expectations; warnings and reference verdicts are retained'))

    phrase = 'Independent theme font control.'
    add('theme-font', add_theme(reductions.package(paragraph(phrase, '<w:rFonts w:asciiTheme="majorAscii" w:hAnsiTheme="majorHAnsi"/>'))),
        [phrase], 'Theme major Latin family is Liberation Mono; compare the explicit-family control.')
    add('explicit-font', reductions.package(paragraph(phrase, '<w:rFonts w:ascii="Liberation Mono" w:hAnsi="Liberation Mono"/>')),
        [phrase], 'The same font family is explicitly selected without a theme reference.')
    phrase = 'Independent theme color control.'
    add('theme-color', add_theme(reductions.package(paragraph(phrase, reductions.FONT + '<w:color w:val="FF0000" w:themeColor="accent1"/>'))),
        [phrase], 'Theme accent1 is 2266CC while the stored RGB fallback is FF0000.')
    add('explicit-color', reductions.package(paragraph(phrase, reductions.FONT + '<w:color w:val="2266CC"/>')),
        [phrase], 'Explicit RGB matches the theme accent without a theme reference.')
    for styled in (True, False):
        parts, text = table(styled)
        add('table-style' if styled else 'table-direct', parts, text,
            'First-row conditional style compared with direct first-row cell shading.' if styled else 'Explicit cell shading matches the styled-table intent.')
    original = json.loads((ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Comparison/manifest.json').read_text())
    manifest = dict(schemaVersion=1, provenance='Independently authored OOXML contrasts, MIT; no document-library serializer or customer content.',
                    generator='eng/generate-roadmap-138-controls.py', generatorSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                    fonts=original['fonts'], documents=cases)
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(f'{len(cases)} independent theme/table controls authored in {destination}')


if __name__ == '__main__':
    main()
