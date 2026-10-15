"""Requires fontTools 4.61.1; makes tiny renamed OFL test faces, never production code."""
import hashlib
import json
from pathlib import Path
from fontTools.ttLib import TTFont, TTCollection
from fontTools.subset import Subsetter, Options
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.t2CharStringPen import T2CharStringPen

root = Path(__file__).resolve().parents[1]
destination = root / 'tests/wordnet.Conversion.Tests/Fixtures/Enterprise'
fonts = []
for style in ('Regular', 'Bold'):
    font = TTFont(root / f'src/wordnet.Fonts/Resources/LiberationSans-{style}.ttf', recalcTimestamp=False)
    options = Options(); options.legacy_kern = True
    subset = Subsetter(options); subset.populate(text=' AVATAR'); subset.subset(font)
    for name in font['name'].names:
        replacements = {1: 'Wordnet Fixture Sans', 2: style, 3: 'WordnetFixtureSans-' + style,
                        4: 'Wordnet Fixture Sans ' + style, 6: 'WordnetFixtureSans-' + style,
                        16: 'Wordnet Fixture Sans', 17: style}
        if name.nameID in replacements:
            name.string = replacements[name.nameID].encode(name.getEncoding())
    fonts.append(font)
collection = TTCollection(); collection.fonts = fonts
path = destination / 'caller-fonts.ttc'; collection.save(path)
metrics = {'source': 'Bundled Liberation Sans 2.1.5, subset/renamed with fontTools 4.61.1',
           'license': 'SIL OFL 1.1; see src/wordnet.Fonts/Resources/Liberation-OFL.txt',
           'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
regular = fonts[0]
metrics['regular_av_width_12pt'] = (regular['hmtx']['A'][0] + regular['hmtx']['V'][0]
    + sum(k.kernTable.get(('A', 'V'), 0) for k in regular['kern'].kernTables)) * 12 / regular['head'].unitsPerEm
(destination / 'caller-fonts.json').write_text(json.dumps(metrics, indent=2) + '\n', encoding='utf-8')
print(metrics)

# Separate named, OFL-derived faces exercise actual GPOS records without a legacy
# kern fallback. Controlled values are independent of the production parser.
for kind, rules in {
    'pair': 'lookup PAIR { pos A V -160; } PAIR;',
    'class': 'lookup CLASSES { pos [A T] [V R] -80; } CLASSES;',
    'extension': 'lookup EXT useExtension { pos A <10 20 -160 0> V <15 -5 -30 0>; } EXT;',
}.items():
    font = TTFont(destination / 'caller-fonts.ttc', fontNumber=0, recalcTimestamp=False)
    for tag in ('GPOS', 'GSUB', 'GDEF', 'kern'):
        if tag in font:
            del font[tag]
    family = 'Wordnet Fixture Gpos ' + kind.title()
    for name in font['name'].names:
        replacements = {1: family, 2: 'Regular', 3: family.replace(' ', ''),
                        4: family, 6: family.replace(' ', ''), 16: family, 17: 'Regular'}
        if name.nameID in replacements:
            name.string = replacements[name.nameID].encode(name.getEncoding())
    addOpenTypeFeaturesFromString(font, 'languagesystem DFLT dflt; languagesystem latn dflt; '
                                  + rules + ' feature kern { '
                                  + {'pair': 'lookup PAIR;', 'class': 'lookup CLASSES;', 'extension': 'lookup EXT;'}[kind]
                                  + ' } kern;')
    path = destination / ('gpos-' + kind + '.ttf')
    font.save(path)
    print(kind, 'GPOS lookup types:', [lookup.LookupType for lookup in font['GPOS'].table.LookupList.Lookup])

# A real CFF-outline OpenType face derived from the same renamed OFL subset.
# Quadratic-to-cubic conversion is exact; the production code does not use fontTools.
source = fonts[0]
glyphs = source.getGlyphSet()
charstrings = {}
for name in source.getGlyphOrder():
    pen = T2CharStringPen(source['hmtx'][name][0], glyphs)
    glyphs[name].draw(pen)
    charstrings[name] = pen.getCharString()
units = source['head'].unitsPerEm
builder = FontBuilder(units, isTTF=False)
builder.font.recalcTimestamp = False
builder.font['head'].created = source['head'].created
builder.font['head'].modified = source['head'].modified
builder.setupGlyphOrder(source.getGlyphOrder())
builder.setupCharacterMap(source.getBestCmap())
builder.setupHorizontalMetrics({name: source['hmtx'][name] for name in source.getGlyphOrder()})
builder.setupHorizontalHeader(ascent=source['hhea'].ascent, descent=source['hhea'].descent)
builder.setupNameTable({'familyName': 'Wordnet Fixture Cff Sans', 'styleName': 'Regular',
                       'uniqueFontIdentifier': 'WordnetFixtureCffSans',
                       'fullName': 'Wordnet Fixture Cff Sans', 'psName': 'WordnetFixtureCffSans'})
builder.setupOS2(sTypoAscender=source['OS/2'].sTypoAscender, sTypoDescender=source['OS/2'].sTypoDescender,
                 usWinAscent=source['OS/2'].usWinAscent, usWinDescent=source['OS/2'].usWinDescent)
builder.setupPost()
builder.setupCFF('WordnetFixtureCffSans', {'FullName': 'Wordnet Fixture Cff Sans',
                 'FamilyName': 'Wordnet Fixture Cff Sans', 'Weight': 'Regular',
                 'FontMatrix': [1 / units, 0, 0, 1 / units, 0, 0]}, charstrings, {})
addOpenTypeFeaturesFromString(builder.font, 'languagesystem DFLT dflt; languagesystem latn dflt; feature kern { pos A V -160; } kern;')
builder.font.recalcTimestamp = False
builder.font['head'].created = source['head'].created
builder.font['head'].modified = source['head'].modified
builder.save(destination / 'caller-cff.otf')

(destination / 'font-fixtures.json').write_text(json.dumps({
    'source': 'Liberation Sans 2.1.5, renamed OFL derivatives; fontTools 4.61.1',
    'license': 'SIL OFL 1.1; see src/wordnet.Fonts/Resources/Liberation-OFL.txt',
    'sha256': {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
               for path in sorted(destination.iterdir()) if path.suffix in ('.ttf', '.otf', '.ttc')}
}, indent=2) + '\n', encoding='utf-8')
