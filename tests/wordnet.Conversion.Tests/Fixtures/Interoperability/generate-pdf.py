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
(ROOT / 'pdf-ground-truth.json').write_text(json.dumps({
    'producer': 'ReportLab ' + reportlab.Version + '; independent generator, invariant metadata',
    'pagePoints': [500, 700],
    'prose': {'paragraphs': ['First paragraph line one.\nFirst paragraph line two.', 'Second paragraph starts.'], 'leftPoints': 40, 'firstBaselineFromTopPoints': 60},
    'columns': {'count': 2, 'leftPoints': [40, 290], 'leadingPoints': 18},
    'grid': {'rows': 2, 'columns': 2, 'widthsPoints': [150, 150], 'leftPoints': 40},
    'image-transform': {'rotationClockwise': 270, 'framePoints': [80, 40], 'displayPoints': [40, 80], 'leftTopPoints': [160, 320]},
    'sha256': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(ROOT.glob('*.pdf'))}
}, indent=2) + '\n')
