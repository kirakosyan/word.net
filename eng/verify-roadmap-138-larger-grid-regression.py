#!/usr/bin/env python3
"""Recheck the previous small-grid corpus after widening its admitted geometry."""
import importlib.util
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('small_grid', ROOT / 'eng/verify-roadmap-138-multicell-borders.py')
small = importlib.util.module_from_spec(spec)
spec.loader.exec_module(small)

if len(sys.argv) != 6:
    raise SystemExit('Expected sources, parent, candidate, office and saved directories.')
parent, candidate = Path(sys.argv[2]), Path(sys.argv[3])
for name in small.POSITIVE:
    old_glyphs, old_strokes = small.pdf(parent / name / 'output.pdf')
    new_glyphs, new_strokes = small.pdf(candidate / name / 'output.pdf')
    small.same_glyphs(old_glyphs, new_glyphs, .002, 'Previously qualified case changed ' + name)
    small.same_strokes(old_strokes, new_strokes, .002, 'Previously qualified case changed ' + name)
small.POSITIVE = small.POSITIVE + ('three-rows', 'three-columns')
small.CONTROLS = tuple(name for name in small.CONTROLS if name not in ('three-rows', 'three-columns'))
small.main()
