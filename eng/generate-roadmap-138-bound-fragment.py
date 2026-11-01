"""Independently author a minimal bound typed-control fragment as OPC/XML."""

import argparse
import importlib.util
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', required=True, type=Path)
args = parser.parse_args()
destination = args.output.resolve()
fixture_root = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
if destination == fixture_root or fixture_root in destination.parents:
    parser.error('Choose a new scratch path outside the fixture tree.')
if destination.exists():
    raise FileExistsError(destination)

store_id = '{A67AC88A-A164-4ADE-8889-8826CE44DE6E}'
body = ('<w:p><w:r><w:rPr>' + reductions.FONT + '</w:rPr><w:t xml:space="preserve">Name: </w:t></w:r>'
        '<w:sdt><w:sdtPr><w:dataBinding w:storeItemID="' + store_id + '" w:xpath="/p:invoice/p:customer/@name" '
        'w:prefixMappings="xmlns:p=\'urn:wordnet:controls\'"/><w:id w:val="7"/><w:tag w:val="name"/><w:text/></w:sdtPr>'
        '<w:sdtContent><w:r><w:rPr>' + reductions.FONT + '</w:rPr><w:t>cached</w:t></w:r></w:sdtContent></w:sdt></w:p>')
parts = reductions.package(body)
parts['word/_rels/document.xml.rels'] = parts['word/_rels/document.xml.rels'].replace(
    '</Relationships>', f'<Relationship Id="data" Type="{reductions.R}/customXml" Target="../customXml/item1.xml"/></Relationships>')
parts['[Content_Types].xml'] = parts['[Content_Types].xml'].replace(
    '</Types>', '<Override PartName="/customXml/itemProps1.xml" ContentType="application/vnd.openxmlformats-officedocument.customXmlProperties+xml"/></Types>')
parts['customXml/_rels/item1.xml.rels'] = (f'<Relationships xmlns="{reductions.PR}">'
    f'<Relationship Id="props" Type="{reductions.R}/customXmlProps" Target="itemProps1.xml"/></Relationships>')
parts['customXml/item1.xml'] = '<p:invoice xmlns:p="urn:wordnet:controls"><p:customer name="Ada"/></p:invoice>'
parts['customXml/itemProps1.xml'] = ('<ds:datastoreItem xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml" '
    'ds:itemID="' + store_id + '"><ds:schemaRefs/></ds:datastoreItem>')
destination.parent.mkdir(parents=True, exist_ok=True)
reductions.write_zip(destination, parts)
print('Created one independent local-XML-bound typed-control fragment.')
