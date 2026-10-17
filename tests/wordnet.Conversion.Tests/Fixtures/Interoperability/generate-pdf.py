"""Independent synthetic digital PDF reconstruction fixtures; original content, MIT."""
from pathlib import Path
import hashlib
import json
import reportlab
from reportlab.pdfgen.canvas import Canvas
from reportlab.lib.utils import ImageReader

ROOT = Path(__file__).parent


def canvas(name):
    return Canvas(str(ROOT / (name + '.pdf')), pagesize=(500, 700), invariant=1)


c = canvas('prose')
c.setFont('Helvetica', 12)
c.drawString(40, 640, 'First paragraph line one.')
c.drawString(40, 622, 'First paragraph line two.')
c.drawString(40, 580, 'Second paragraph starts.')
c.save()
c = canvas('columns')
c.setFont('Helvetica', 12)
# Paint alternating columns to defeat content-stream order as a reading-order oracle.
for i in range(3):
    c.drawString(40, 640 - i * 18, f'Left column line {i+1}.')
    c.drawString(290, 640 - i * 18, f'Right column line {i+1}.')
c.save()
c = canvas('grid')
c.setFont('Helvetica', 12)
c.drawString(40, 660, 'Before table.')
for x in (40, 190, 340):
    c.line(x, 500, x, 600)
for y in (500, 550, 600):
    c.line(40, y, 340, y)
for x, y, value in ((48, 578, 'Item'), (198, 578, 'Total'), (48, 528, 'Service'), (198, 528, '42.50')):
    c.drawString(x, y, value)
c.drawString(40, 460, 'After table.')
c.save()
c = canvas('borderless')
c.setFont('Helvetica', 12)
c.drawString(40, 640, 'Name')
c.drawString(300, 640, 'Example')
c.drawString(40, 600, 'Total')
c.drawString(300, 600, '42.50')
c.save()
c = canvas('rotated')
c.setFont('Helvetica', 12)
c.translate(100, 400)
c.rotate(30)
c.drawString(0, 0, 'ROTATED CONTENT')
c.save()
c = canvas('form')
c.setFont('Helvetica', 12)
c.drawString(40, 640, 'Form label')
c.acroForm.textfield(name='SyntheticField', value='Example', x=150, y=630, width=100, height=20)
c.save()
c = canvas('image-transform')
c.setFont('Helvetica', 12)
c.drawString(40, 640, 'Illustration once.')
c.saveState()
c.translate(200, 300)
c.rotate(90)
c.drawImage(ImageReader(ROOT.parent / 'color-grid.png'), 0, 0, 80, 40)
c.restoreState()
c.save()
c = canvas('indented-prose')
c.setFont('Helvetica', 12)
for x, y, value in ((76, 640, 'Indented first line.'), (40, 622, 'Continuation second line.'), (40, 604, 'Continuation third line.'),
                    (40, 540, 'Hanging first line.'), (64, 522, 'Hanging continuation.'), (64, 504, 'Hanging final line.')):
    c.drawString(x, y, value)
c.save()
c = canvas('styled-prose')
x = 40
for font, value in (('Helvetica', 'Plain '), ('Helvetica-Bold', 'bold '), ('Helvetica-Oblique', 'italic '), ('Helvetica', 'plain.')):
    c.setFont(font, 12)
    c.drawString(x, 640, value)
    x += c.stringWidth(value, font, 12)
c.setFont('Helvetica', 12)
c.drawString(40, 622, 'Second line stays editable.')
c.save()
c = canvas('many-glyphs')
c.setFont('Helvetica', 12)
for i in range(32):
    c.drawString(40, 660 - i * 18, 'Repeated editable words keep ordinary prose compact.')
c.save()
c = canvas('grid-leading')
c.setFont('Helvetica', 12)
for x in (40, 190, 340):
    c.line(x, 500, x, 600)
for y in (500, 550, 600):
    c.line(40, y, 340, y)
for x, y, value in ((48, 578, 'Item'), (198, 578, 'Total'), (48, 528, 'Service'), (198, 528, '42.50')):
    c.drawString(x, y, value)
c.drawString(40, 460, 'After table.')
c.save()
c = canvas('inline-image-flow')
c.setFont('Helvetica', 12)
c.drawString(40, 640, 'Before illustration.')
c.saveState()
clip = c.beginPath()
clip.rect(40, 500, 80, 40)
c.clipPath(clip, stroke=0)
c.drawImage(ImageReader(ROOT.parent / 'color-grid.png'), 40, 500, 80, 40)
c.restoreState()
c.drawString(40, 460, 'After illustration.')
c.save()
(ROOT / 'pdf-ground-truth.json').write_text(json.dumps({
    'producer': 'ReportLab ' + reportlab.Version + '; independent generator, invariant metadata',
    'pagePoints': [500, 700],
    'prose': {'paragraphs': ['First paragraph line one.\nFirst paragraph line two.', 'Second paragraph starts.'], 'leftPoints': 40, 'firstBaselineFromTopPoints': 60},
    'columns': {'count': 2, 'leftPoints': [40, 290], 'leadingPoints': 18},
    'grid': {'rows': 2, 'columns': 2, 'widthsPoints': [150, 150], 'leftPoints': 40},
    'image-transform': {'rotationClockwise': 270, 'framePoints': [80, 40], 'displayPoints': [40, 80], 'leftTopPoints': [160, 320]},
    'indented-prose': {'firstLinePoints': [36, -24], 'bodyLeftPoints': [40, 64], 'baselinesPoints': [640, 622, 604, 540, 522, 504]},
    'styled-prose': {'text': 'Plain bold italic plain.\nSecond line stays editable.', 'styles': ['plain', 'bold', 'italic', 'plain'], 'baselinesPoints': [640, 622]},
    'many-glyphs': {'lines': 32, 'textPerLine': 'Repeated editable words keep ordinary prose compact.', 'baselinesPoints': [660, 102]},
    'grid-leading': {'tableTopPoints': 100, 'afterBaselinePoints': 460},
    'inline-image-flow': {'imageTopPoints': 160, 'heightPoints': 40, 'afterBaselinePoints': 460},
    'sha256': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(ROOT.glob('*.pdf'))}
}, indent=2) + '\n')
