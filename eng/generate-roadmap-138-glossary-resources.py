#!/usr/bin/env python3
"""Add four local resources and a character style to an independent glossary fixture."""

import argparse
import importlib.util
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--input', required=True, type=Path)
parser.add_argument('--output', required=True, type=Path)
args = parser.parse_args()
destination = args.output.resolve()
fixture_root = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
if destination == fixture_root or fixture_root in destination.parents:
    parser.error('Choose a new scratch path outside the fixture tree.')
if destination.exists():
    raise FileExistsError(destination)

with zipfile.ZipFile(args.input) as source:
    parts = {name: source.read(name) for name in source.namelist()}
types = parts['[Content_Types].xml'].decode('utf-8')
relations = []
W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
resources = {
    'styles': f'<w:styles xmlns:w="{W}"><w:style w:type="character" w:styleId="PromptStyle"><w:name w:val="Prompt Style"/><w:rPr><w:color w:val="808080"/></w:rPr></w:style></w:styles>',
    'settings': f'<w:settings xmlns:w="{W}"><w:view w:val="print"/></w:settings>',
    'fontTable': f'<w:fonts xmlns:w="{W}"><w:font w:name="Noto Sans"><w:family w:val="swiss"/></w:font></w:fonts>',
    'webSettings': f'<w:webSettings xmlns:w="{W}"><w:allowPNG/></w:webSettings>',
}
for index, (kind, content) in enumerate(resources.items(), 1):
    path = f'word/glossary/{kind}.xml'
    parts[path] = content
    types = types.replace('</Types>', f'<Override PartName="/{path}" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.{kind}+xml"/></Types>')
    relations.append(f'<Relationship Id="g{index}" Type="{reductions.R}/{kind}" Target="{kind}.xml"/>')
parts['[Content_Types].xml'] = types
parts['word/glossary/_rels/document.xml.rels'] = f'<Relationships xmlns="{reductions.PR}">' + ''.join(relations) + '</Relationships>'
glossary = parts['word/glossary/document.xml'].decode('utf-8')
parts['word/glossary/document.xml'] = glossary.replace('<w:rPr>', '<w:rPr><w:rStyle w:val="PromptStyle"/>', 1)
destination.parent.mkdir(parents=True, exist_ok=True)
reductions.write_zip(destination, parts)
print('Created an independent glossary graph with four local resources and a character style.')
