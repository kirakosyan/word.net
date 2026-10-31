"""Reproduce small OFL-licensed CJK test faces from the pinned Noto collection."""
import argparse
import hashlib
from pathlib import Path

import fontTools
from fontTools import subset
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont

SOURCE_SHA256 = 'b76b0433203017ca80401b2ee0dd69350349871c4b19d504c34dbdd80541690a'
TEXT = ('日本語の文章です段落句読点を正しく折り返す次行は継続します文字表示確認'
        '这是一个中文段落包含标点以及数字测试换行规则文本显示检查'
        '繁體中文測試標點與文字換行規則輸出檢查')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('collection', type=Path)
    parser.add_argument('output', type=Path, help='new scratch output directory')
    args = parser.parse_args()
    if fontTools.__version__ != '4.61.1':
        raise RuntimeError('Requires fonttools 4.61.1.')
    if hashlib.sha256(args.collection.read_bytes()).hexdigest() != SOURCE_SHA256:
        raise ValueError('The source Noto collection does not match the recorded SHA256.')
    args.output.mkdir(parents=True, exist_ok=False)
    codepoints = set(range(0x20, 0x7f)) | {0xa0, 0x202f} | set(range(0x3000, 0x3100)) | set(range(0xff01, 0xff61)) | {ord(c) for c in TEXT}
    for number, suffix in ((0, 'JP'), (2, 'SC'), (3, 'TC')):
        font = TTFont(args.collection, fontNumber=number, recalcTimestamp=False)
        options = subset.Options(); options.layout_features = ['*']; options.recalc_timestamp = False
        worker = subset.Subsetter(options=options); worker.populate(unicodes=codepoints); worker.subset(font)
        family = 'Roadmap CJK ' + suffix + ' Fixture'; postscript = 'RoadmapCJK' + suffix + 'Fixture'
        for record in font['name'].names:
            if record.nameID in (1, 3, 4, 6, 16, 17):
                value = postscript if record.nameID in (3, 6) else 'Regular' if record.nameID == 17 else family
                record.string = value.encode(record.getEncoding())
        source_glyphs = font.getGlyphSet(); glyphs = {}
        for name in font.getGlyphOrder():
            target = TTGlyphPen(source_glyphs)
            source_glyphs[name].draw(Cu2QuPen(target, max_err=1.0, reverse_direction=True))
            glyphs[name] = target.glyph()
        del font['CFF ']
        if 'VORG' in font:
            del font['VORG']
        font.sfntVersion = '\x00\x01\x00\x00'; font['post'].formatType = 3.0
        builder = FontBuilder(font=font); builder.isTTF = True; builder.setupGlyf(glyphs); builder.setupMaxp()
        path = args.output / ('RoadmapCJK-' + suffix + '.ttf'); font.save(path)
        print(path.name, hashlib.sha256(path.read_bytes()).hexdigest())


if __name__ == '__main__':
    main()
