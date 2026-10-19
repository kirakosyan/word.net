#!/usr/bin/env python3
"""Standalone specification-authored ODF 1.3 fixtures, MIT; no wordnet serializer."""
from pathlib import Path
import hashlib
import json
from zipfile import ZipFile, ZipInfo, ZIP_STORED, ZIP_DEFLATED

root = Path(__file__).resolve().parent.parent
dest = root / 'tests/wordnet.Conversion.Tests/Fixtures/Odt'
dest.mkdir(parents=True, exist_ok=True)
mime = 'application/vnd.oasis.opendocument.text'
ns = ('xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" '
      'xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" '
      'xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0" '
      'xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0" '
      'xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" '
      'xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0" '
      'xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0" '
      'xmlns:xlink="http://www.w3.org/1999/xlink" '
      'xmlns:dc="http://purl.org/dc/elements/1.1/" office:version="1.3"')


def document(kind, inner):
    return f'<office:document-{kind} {ns}>{inner}</office:document-{kind}>'


styles = document('styles', '<office:styles>'
    '<style:default-style style:family="paragraph"><style:text-properties fo:font-size="11pt" fo:color="#123456"/></style:default-style>'
    '<style:style style:name="Base" style:family="paragraph"><style:text-properties fo:font-weight="bold"/></style:style>'
    '<style:style style:name="Child" style:family="paragraph" style:parent-style-name="Base"><style:paragraph-properties fo:text-align="center"/></style:style>'
    '</office:styles><office:automatic-styles>'
    '<style:page-layout style:name="Layout"><style:page-layout-properties fo:page-width="400pt" fo:page-height="300pt" fo:margin="30pt"/></style:page-layout>'
    '</office:automatic-styles><office:master-styles><style:master-page style:name="First" style:page-layout-name="Layout"/></office:master-styles>')
meta = document('meta', '<office:meta><dc:title>Independent ODT</dc:title><dc:creator>Fixture Author</dc:creator></office:meta>')
cases = {
    'text': ('', '<text:p text:style-name="Child">Café<text:s text:c="2"/>Ω<text:tab/>end<text:line-break/>next</text:p>', {}),
    'list': ('<text:list-style style:name="Numbers"><text:list-level-style-number text:level="1" style:num-format="1" style:num-suffix="."/></text:list-style>',
             '<text:list text:style-name="Numbers"><text:list-item text:start-value="3"><text:p>Three</text:p></text:list-item><text:list-item><text:p>Four</text:p></text:list-item></text:list>', {}),
    'table-image': ('<style:style style:name="Column" style:family="table-column"><style:table-column-properties style:column-width="120pt"/></style:style>',
        '<table:table table:name="Fixture"><table:table-column table:style-name="Column" table:number-columns-repeated="2"/>'
        '<table:table-row><table:table-cell table:number-columns-spanned="2"><text:p>Merged</text:p></table:table-cell><table:covered-table-cell/></table:table-row>'
        '<table:table-row><table:table-cell><text:p>Left</text:p></table:table-cell><table:table-cell><text:p>Right</text:p></table:table-cell></table:table-row></table:table>'
        '<text:p><draw:frame text:anchor-type="as-char" svg:width="60pt" svg:height="30pt"><draw:image xlink:href="./Pictures/grid.png" xlink:type="simple" xlink:show="embed" xlink:actuate="onLoad"/><svg:desc>Fixture grid</svg:desc></draw:frame></text:p>',
        {'Pictures/grid.png': (root / 'tests/wordnet.Conversion.Tests/Fixtures/color-grid.png').read_bytes()}),
}
level_styles = ''.join(f'<text:list-level-style-number text:level="{i}" style:num-format="{["1", "a", "A", "i", "I"][((i-1) % 5)]}" style:num-suffix="."/>' for i in range(1, 10))
nested = '<text:p>Level9</text:p>'
for i in range(8, 0, -1):
    nested = f'<text:p>Level{i}</text:p><text:list><text:list-item>{nested}</text:list-item></text:list>'
cases['nine-levels'] = ('<text:list-style style:name="Nine">' + level_styles + '</text:list-style>',
                        '<text:list text:style-name="Nine"><text:list-item text:start-value="0">' + nested + '</text:list-item></text:list>', {})
cases['masters'] = ('<style:style style:name="Next" style:family="paragraph" style:master-page-name="Second"/>',
                    '<text:p>First body</text:p><text:p text:style-name="Next">Second body</text:p>', {})
manifest = []
for name, (automatic, body, assets) in cases.items():
    parts = {'content.xml': document('content', '<office:automatic-styles>' + automatic + '</office:automatic-styles><office:body><office:text>' + body + '</office:text></office:body>').encode(),
             'styles.xml': styles.encode(), 'meta.xml': meta.encode(), **assets}
    if name == 'masters':
        parts['styles.xml'] = styles.replace('</office:automatic-styles>',
            '<style:page-layout style:name="Layout2"><style:page-layout-properties fo:page-width="500pt" fo:page-height="400pt" fo:margin="40pt"/></style:page-layout></office:automatic-styles>').replace(
            '<style:master-page style:name="First" style:page-layout-name="Layout"/>',
            '<style:master-page style:name="First" style:page-layout-name="Layout"><style:header><text:p>First header</text:p></style:header><style:footer><text:p>First footer</text:p></style:footer></style:master-page>'
            '<style:master-page style:name="Second" style:page-layout-name="Layout2"><style:header><text:p>Second header</text:p></style:header><style:footer><text:p>Second footer</text:p></style:footer></style:master-page>').encode()
    declarations = ''.join(f'<manifest:file-entry manifest:full-path="{key}" manifest:media-type="{"image/png" if key.endswith(".png") else "text/xml"}"/>' for key in parts)
    parts['META-INF/manifest.xml'] = ('<manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0" manifest:version="1.3">'
        f'<manifest:file-entry manifest:full-path="/" manifest:media-type="{mime}"/>' + declarations + '</manifest:manifest>').encode()
    path = dest / (name + '.odt')
    with ZipFile(path, 'w') as archive:
        for key, data in [('mimetype', mime.encode()), *parts.items()]:
            info = ZipInfo(key, (2000, 1, 1, 0, 0, 0)); info.compress_type = ZIP_STORED if key == 'mimetype' else ZIP_DEFLATED
            archive.writestr(info, data)
    manifest.append({'file': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'license': 'MIT', 'generator': 'eng/generate-odt-fixtures.py'})
(dest / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
