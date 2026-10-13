"""Original MIT synthetic regression fixtures. Pillow and ReportLab are test tools only."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from reportlab.pdfgen.canvas import Canvas
from reportlab.lib.utils import ImageReader

root = Path(__file__).parent
Image.new('RGB', (1600, 2560), '#336699').save(root / 'large-cover.png')

pdf = Canvas(str(root / 'matrix-scaled.pdf'), pagesize=(400, 500), invariant=1)
text = pdf.beginText()
text.setFont('Helvetica', 1)
text.setTextTransform(12, 0, 0, 12, 50, 440)
text.setCharSpace(0.05)  # 0.6 pt: larger than Tf/4, smaller than actual point-size/4.
text.textOut('Scaled')
text.moveCursor(4.1, 0)  # Real word gap in the scaled text coordinate system.
text.textOut('words')
pdf.drawText(text)
pdf.drawImage(str(root / 'color-grid.png'), 50, 340, width=120, height=60)
pdf.save()

image = Image.new('RGB', (1700, 2200), 'white')
draw = ImageDraw.Draw(image)
font = ImageFont.truetype(str(root.parents[2] / 'src/wordnet/Resources/Fonts/NotoSans-Regular.ttf'), 50)
for i, line in enumerate(['Wordnet conversion test', 'English printed text 12345.']):
    draw.text((180, 220 + i * 100), line, font=font, fill='black')
pdf = Canvas(str(root / 'multipage-ocr.pdf'), pagesize=(612, 792), invariant=1)
blank = Image.new('RGB', (1700, 2200), 'white')
for page in range(3):
    pdf.drawImage(ImageReader(blank if page == 2 else image), 0, 0, 612, 792)
    if page == 1:
        pdf.setFont('Helvetica', 16)
        pdf.drawString(72, 500, 'Digital footer remains editable.')
    pdf.showPage()
pdf.save()
pdf = Canvas(str(root / 'blank-scan.pdf'), pagesize=(612, 792), invariant=1)
pdf.drawImage(ImageReader(blank), 0, 0, 612, 792)
pdf.save()
