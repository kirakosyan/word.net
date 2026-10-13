"""Original MIT synthetic images and PDF; Pillow/ReportLab are fixture tools only."""
from pathlib import Path
from PIL import Image, ImageDraw
from reportlab.pdfgen.canvas import Canvas

root = Path(__file__).parent
image = Image.new('RGB', (80, 40), '#d02020')
draw = ImageDraw.Draw(image)
draw.rectangle((40, 0, 79, 39), fill='#2040d0')
image.save(root / 'color-grid.png')
image.save(root / 'color-grid.jpg', quality=95, subsampling=0)
transparent = image.convert('RGBA')
transparent.putalpha(128)
transparent.save(root / 'color-grid-alpha.png')
pdf = Canvas(str(root / 'illustrated.pdf'), pagesize=(400, 500), invariant=1)
pdf.drawString(50, 440, 'Before the illustration')
pdf.drawImage(str(root / 'color-grid.png'), 50, 340, width=120, height=60)
pdf.drawString(50, 310, 'After the illustration')
pdf.save()
