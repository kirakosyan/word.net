"""Independently check printable ASCII ink and sampled cubic-arc clearance."""
import hashlib
import json
import math
from pathlib import Path

from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
FONT = ROOT / 'src/wordnet/Resources/Fonts/NotoSans-Regular.ttf'


def check_arc(radius):
    maximum = 0
    for degrees in range(1, 91):
        angle = math.radians(degrees); k = 4 / 3 * math.tan(angle / 4)
        end_x, end_y = math.cos(angle), math.sin(angle)
        for step in range(1, 1000):
            t = step / 1000; u = 1 - t
            x = u ** 3 + 3 * u * u * t + 3 * u * t * t * (end_x + k * end_y) + t ** 3 * end_x
            y = 3 * u * u * t * k + 3 * u * t * t * (end_y - k * end_x) + t ** 3 * end_y
            maximum = max(maximum, abs(math.hypot(x, y) - 1) * radius)
    if maximum > .5:
        raise ValueError('Sampled cubic-arc deviation exceeds the half-point clearance')
    return maximum


def check():
    font = TTFont(FONT); cmap = font.getBestCmap(); units = font['head'].unitsPerEm
    size, margin = 8, 2
    if units <= 0:
        raise ValueError('Invalid font units')
    glyphs = []
    for code in range(32, 127):
        name = cmap.get(code)
        if name is None or name == '.notdef':
            raise ValueError('Embedded chart font lacks printable ASCII')
        glyph = font['glyf'][name]; glyph.recalcBounds(font['glyf'])
        if not hasattr(glyph, 'xMin'):
            continue
        advance = font['hmtx'][name][0]
        left, right = glyph.xMin * size / units, (glyph.xMax - advance) * size / units
        bottom, top = glyph.yMin * size / units, glyph.yMax * size / units
        if left < -margin or right > margin or top > size / 2 + margin + size / 3 or bottom < size / 3 - size / 2 - margin:
            raise ValueError('Printable ASCII ink exceeds the circular-label padding')
        glyphs.append((left, right, bottom, top))
    if len(glyphs) < 90:
        raise ValueError('Unexpected printable ASCII outline coverage')
    return {'fontSha256': hashlib.sha256(FONT.read_bytes()).hexdigest(), 'unitsPerEm': units, 'fontSizePt': size,
            'paddingPt': margin, 'boundedGlyphs': len(glyphs), 'minimumLeftPt': min(g[0] for g in glyphs),
            'maximumRightOverhangPt': max(g[1] for g in glyphs), 'minimumBottomPt': min(g[2] for g in glyphs),
            'maximumTopPt': max(g[3] for g in glyphs), 'sampledArcRadiusPt': 720,
            'sampledMaximumArcDeviationPt': check_arc(720), 'arcClearancePt': .5}


if __name__ == '__main__':
    print(json.dumps(check(), indent=2))
