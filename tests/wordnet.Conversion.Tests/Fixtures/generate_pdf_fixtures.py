"""Original synthetic fixtures, MIT. Requires reportlab and Pillow; not used by the library."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from reportlab.pdfgen.canvas import Canvas
from reportlab.lib.utils import ImageReader

root = Path(__file__).parent
font_path = root.parents[2] / 'src/wordnet/Resources/Fonts/NotoSans-Regular.ttf'
lines = ['Wordnet conversion test', 'English printed text 12345.']
canvas = Canvas(str(root / 'digital.pdf'), pagesize=(612, 792), invariant=1)
for i, line in enumerate(lines):
    canvas.drawString(72, 700 - i * 32, line)
canvas.save()

image = Image.new('RGB', (1700, 2200), 'white')
draw = ImageDraw.Draw(image)
font = ImageFont.truetype(str(font_path), 50)
for i, line in enumerate(lines):
    draw.text((180, 220 + i * 100), line, font=font, fill='black')
for name, mixed in [('scanned.pdf', False), ('mixed.pdf', True)]:
    canvas = Canvas(str(root / name), pagesize=(612, 792), invariant=1)
    canvas.drawImage(ImageReader(image), 0, 0, 612, 792)
    if mixed:
        canvas.setFont('Helvetica', 16)
        canvas.drawString(72, 500, 'Digital footer remains editable.')
    canvas.save()
