"""Independently author a minimal static glossary-backed control fragment."""

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

body = ('<w:p><w:r><w:rPr>' + reductions.FONT + '</w:rPr><w:t xml:space="preserve">Name: </w:t></w:r>'
        '<w:sdt><w:sdtPr><w:placeholder><w:docPart w:val="Prompt"/></w:placeholder><w:showingPlcHdr/>'
        '<w:id w:val="7"/><w:tag w:val="name"/><w:text/></w:sdtPr>'
        '<w:sdtContent><w:r><w:rPr>' + reductions.FONT + '</w:rPr><w:t>Choose</w:t></w:r></w:sdtContent></w:sdt></w:p>')
parts = reductions.package(body)
parts['word/_rels/document.xml.rels'] = parts['word/_rels/document.xml.rels'].replace(
    '</Relationships>', f'<Relationship Id="glossary" Type="{reductions.R}/glossaryDocument" Target="glossary/document.xml"/></Relationships>')
parts['[Content_Types].xml'] = parts['[Content_Types].xml'].replace(
    '</Types>', '<Override PartName="/word/glossary/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.glossary+xml"/></Types>')
parts['word/glossary/document.xml'] = (
    '<w:glossaryDocument xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:docParts><w:docPart>'
    '<w:docPartPr><w:name w:val="Prompt"/><w:types><w:type w:val="bbPlcHdr"/></w:types>'
    '<w:behaviors><w:behavior w:val="content"/></w:behaviors><w:guid w:val="{12345678-1234-1234-1234-123456789012}"/></w:docPartPr>'
    '<w:docPartBody><w:p><w:r><w:rPr>' + reductions.FONT + '</w:rPr><w:t>Choose</w:t></w:r></w:p></w:docPartBody>'
    '</w:docPart></w:docParts></w:glossaryDocument>')
destination.parent.mkdir(parents=True, exist_ok=True)
reductions.write_zip(destination, parts)
print('Created one independent static glossary-backed control fragment.')
