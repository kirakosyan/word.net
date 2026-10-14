"""Original English printed-text OCR corpus, MIT; Pillow + ReportLab, fixed Noto Sans.

Synthetic degradations are reproducible engineering fixtures, not a real-world accuracy claim.
"""
from pathlib import Path
import hashlib
import json
import random
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from reportlab.pdfgen.canvas import Canvas
from reportlab.lib.utils import ImageReader

root = Path(__file__).parent
font = ImageFont.truetype(str(root.parents[2] / 'src/wordnet/Resources/Fonts/NotoSans-Regular.ttf'), 42)
lines = [
    'Annual service report',
    'The project delivered twelve reports in March.',
    'Every document contains editable English text.',
    'Please review the final results before approval.',
    'Reference number 2026 and total amount 1250.',
    'Quality checks include accuracy and reading order.'
]
base = Image.new('RGB', (1700, 1300), 'white')
draw = ImageDraw.Draw(base)
for i, text in enumerate(lines):
    draw.text((120, 120 + i * 110), text, font=font, fill='black')

variants = {'clean': base, 'rotated90': base.transpose(Image.Transpose.ROTATE_90),
            'rotated180': base.transpose(Image.Transpose.ROTATE_180),
            'skewed': base.rotate(3, resample=Image.Resampling.BICUBIC, expand=True, fillcolor='white'),
            'low-resolution': base.resize((850, 650), Image.Resampling.LANCZOS)}
noise = base.copy()
rng = random.Random(1729)
draw = ImageDraw.Draw(noise)
for _ in range(6000):
    x, y = rng.randrange(noise.width), rng.randrange(noise.height)
    level = rng.randrange(140, 240)
    draw.point((x, y), fill=(level, level, level))
variants['noisy'] = noise.filter(ImageFilter.GaussianBlur(0.35))
columns = Image.new('RGB', (2400, 1500), 'white')
draw = ImageDraw.Draw(columns)
column_lines = ['Left column first paragraph.', 'Left column second paragraph.', 'Left column final paragraph.',
                'Right column first paragraph.', 'Right column second paragraph.', 'Right column final paragraph.']
for i, text in enumerate(column_lines):
    draw.text((100 if i < 3 else 1300, 180 + (i % 3) * 160), text, font=font, fill='black')
variants['columns'] = columns
variants['phototest'] = Image.open(root / 'tesseract/phototest.tif').convert('RGB')
manifest = []
for name, image in variants.items():
    path = root / f'ocr-{name}.pdf'
    width, height = image.width * 72 / 200, image.height * 72 / 200
    # Low-resolution raster occupies the same physical page as the clean scan.
    if name == 'low-resolution': width, height = base.width * 72 / 200, base.height * 72 / 200
    if name == 'phototest': width, height = image.width * 72 / 300, image.height * 72 / 300
    pdf = Canvas(str(path), pagesize=(width, height), invariant=1)
    pdf.drawImage(ImageReader(image), 0, 0, width, height)
    pdf.save()
    expected = '\n'.join(column_lines if name == 'columns' else lines)
    if name == 'phototest': expected = (root / 'tesseract/phototest.gold.txt').read_text(encoding='utf-8').strip()
    (root / f'ocr-{name}.txt').write_text(expected + '\n', encoding='utf-8')
    manifest.append({'file': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                     'expected': f'ocr-{name}.txt', 'producer': 'Tesseract test scan / ReportLab wrapper' if name == 'phototest' else 'Pillow/ReportLab',
                     'maximumCharacterErrorRate': 0.03, 'maximumWordErrorRate': 0.08})
(root / 'ocr-corpus.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
