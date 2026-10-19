"""Capture LibreOffice references for the existing comparison harness (test-only)."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import escape

spec = importlib.util.spec_from_file_location('comparison', Path(__file__).with_name('run-comparison.py'))
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, default=comparison.ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Comparison/manifest.json')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--soffice', default=shutil.which('libreoffice') or shutil.which('soffice'))
    parser.add_argument('--office-argument', action='append', default=[])
    parser.add_argument('--renderer', default=shutil.which('pdftoppm'))
    parser.add_argument('--cohort', choices=['all', 'baseline', 'p0-2026-10'], default='p0-2026-10')
    args = parser.parse_args()
    if not args.soffice or not args.renderer:
        parser.error('Install LibreOffice Writer and Poppler or supply explicit paths.')
    manifest = json.loads(args.manifest.read_text())
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    fonts = output / 'fonts'
    fonts.mkdir(exist_ok=True)
    expected_fonts = {Path(font['path']).name for font in manifest['fonts']}
    if any(path.name not in expected_fonts for path in fonts.iterdir()):
        parser.error('Output font directory contains unexpected files; choose a fresh output directory.')
    for font in manifest['fonts']:
        source = comparison.ROOT / font['path']
        if comparison.digest(source) != font['sha256']:
            raise ValueError('Pinned font hash mismatch: ' + font['path'])
        shutil.copyfile(source, fonts / source.name)
    # A private fontconfig catalog excludes silently substituted system fonts.
    config = output / 'fontconfig.xml'
    config.write_text('<?xml version="1.0"?><!DOCTYPE fontconfig SYSTEM "fonts.dtd"><fontconfig><dir>'
                      + escape(str(fonts)) + '</dir><cachedir>' + escape(str(output / 'font-cache'))
                      + '</cachedir></fontconfig>')
    env = os.environ.copy()
    env['FONTCONFIG_FILE'] = str(config)
    command = [args.soffice, *args.office_argument]
    version = comparison.invoke([*command, '--headless', '--version'], env=env).stdout.strip()
    if not version.startswith('LibreOffice '):
        raise ValueError('Expected an identifiable LibreOffice build.')
    banner = subprocess.run([args.renderer, '-v'], check=True, capture_output=True, text=True)
    renderer = comparison.renderer_identity(banner.stderr or banner.stdout)
    references = []
    executable = Path(args.soffice).resolve()
    binary = executable.with_name('soffice.bin')
    with tempfile.TemporaryDirectory(prefix='wordnet-office-') as profile:
        for document in manifest['documents']:
            if args.cohort != 'all' and document.get('cohort', 'baseline') != args.cohort:
                continue
            source = (args.manifest.parent / document['path']).resolve()
            if comparison.digest(source) != document['sha256']:
                raise ValueError('Input hash mismatch: ' + document['id'])
            pdf = output / (source.stem + '.pdf')
            if pdf.exists():
                pdf.unlink()  # Do not mistake a stale PDF for a successful capture.
            comparison.invoke([*command, '-env:UserInstallation=' + Path(profile).as_uri(), '--headless',
                               '--convert-to', 'pdf:writer_pdf_Export', '--outdir', str(output), str(source)], env=env)
            if not pdf.is_file():
                raise ValueError('Office did not export ' + document['id'])
            references.append(dict(documentId=document['id'], kind='libreoffice', version=version,
                                   artifactMode='open-source', inputSha256=document['sha256'], pdf=pdf.name,
                                   sha256=comparison.digest(pdf), fonts=manifest['fonts'], renderer=renderer,
                                   executableSha256=comparison.digest(executable),
                                   binarySha256=comparison.digest(binary) if binary.is_file() else None))
            (output / 'references.json').write_text(json.dumps(dict(schemaVersion=1, references=references), indent=2) + '\n')
    print(f'{len(references)} office references captured: {output / "references.json"}')


if __name__ == '__main__':
    main()
