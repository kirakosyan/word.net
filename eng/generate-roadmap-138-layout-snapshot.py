#!/usr/bin/env python3
"""Create a small independent OOXML source for layout snapshot inspection."""

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
body = reductions.p('Alpha', reductions.EXPLICIT, 22) + reductions.p('Beta', reductions.EXPLICIT, 22)
destination.parent.mkdir(parents=True, exist_ok=True)
reductions.write_zip(destination, reductions.package(body))
print('Created an independent two-paragraph layout specimen.')
