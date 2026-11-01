#!/usr/bin/env python3
"""Independently check the pinned test MATH font's n-ary operator variants."""
import hashlib
from pathlib import Path

import fontTools
from fontTools.ttLib import TTFont


ROOT = Path(__file__).resolve().parents[1]
FONT = ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/P2/STIXTwoMath.ttf'
EXPECTED = {
    0x2211: (1646, [(1646, 1031), (1647, 1326)]),
    0x220f: (1640, [(1640, 1031), (1641, 1331)]),
    0x222b: (1698, [(1698, 954), (1699, 2347)]),
}


def main():
    assert fontTools.__version__ == '4.61.1'
    assert hashlib.sha256(FONT.read_bytes()).hexdigest() == '15896d9ad1576c35078b1019daa316a8d260f9655d18751b0006d8df9916d655'
    with TTFont(FONT) as font:
        assert font['head'].unitsPerEm == 1000
        constants = font['MATH'].table.MathConstants
        assert constants.DisplayOperatorMinHeight == 1800
        assert (constants.UpperLimitGapMin.Value, constants.UpperLimitBaselineRiseMin.Value,
                constants.LowerLimitGapMin.Value, constants.LowerLimitBaselineDropMin.Value) == (135, 300, 135, 670)
        variants = font['MATH'].table.MathVariants
        assert variants.MinConnectorOverlap == 100
        coverage = variants.VertGlyphCoverage.glyphs
        cmap = font.getBestCmap()
        for scalar, (base, expected) in EXPECTED.items():
            name = cmap[scalar]
            assert font.getGlyphID(name) == base
            records = variants.VertGlyphConstruction[coverage.index(name)].MathGlyphVariantRecord
            assert [(font.getGlyphID(item.VariantGlyph), item.AdvanceMeasurement) for item in records] == expected
        integral = variants.VertGlyphConstruction[coverage.index(cmap[0x222b])].GlyphAssembly
        assert [(font.getGlyphID(part.glyph), part.StartConnectorLength, part.EndConnectorLength,
                 part.FullAdvance, part.PartFlags) for part in integral.PartRecords] == [
            (4836, 0, 800, 1896, 0), (4815, 600, 600, 1251, 1), (4835, 800, 0, 1630, 0)]
    print('Pinned MATH operator variants and limit metrics verified independently with fontTools 4.61.1.')


if __name__ == '__main__':
    main()
