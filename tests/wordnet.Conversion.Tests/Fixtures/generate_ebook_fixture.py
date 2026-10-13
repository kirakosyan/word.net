"""Original synthetic EPUB and PalmDOC-compressed MOBI6 fixtures; MIT; Python standard library only."""
from pathlib import Path
import struct
import zipfile

root = Path(__file__).parent
body = b'<html xmlns="http://www.w3.org/1999/xhtml"><head><title>Fixture</title></head><body><p>Wordnet conversion test</p><p>English printed text 12345.</p></body></html>'
with zipfile.ZipFile(root / 'plain.epub', 'w') as z:
    z.writestr('mimetype', 'application/epub+zip', compress_type=zipfile.ZIP_STORED)
    z.writestr('META-INF/container.xml', '<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0"><rootfiles><rootfile full-path="book/book.opf" media-type="application/oebps-package+xml"/></rootfiles></container>')
    z.writestr('book/book.opf', '<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">urn:wordnet:fixture</dc:identifier><dc:title>Fixture</dc:title><dc:language>en</dc:language><meta property="dcterms:modified">2000-01-01T00:00:00Z</meta></metadata><manifest><item id="body" href="chapter.xhtml" media-type="application/xhtml+xml"/><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/></manifest><spine><itemref idref="body"/></spine></package>')
    z.writestr('book/chapter.xhtml', body)
    z.writestr('book/nav.xhtml', '<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>Contents</title></head><body><nav epub:type="toc"><ol><li><a href="chapter.xhtml">Fixture</a></li></ol></nav></body></html>')

# PalmDOC encodes a leading run of eight literal bytes, then ordinary ASCII literals.
compressed = bytes([8]) + body[:8] + body[8:]
header = bytearray(264)
struct.pack_into('>HHIHHHH', header, 0, 2, 0, len(body), 1, 4096, 0, 0)
header[16:20] = b'MOBI'
for offset, value in {20:232, 24:2, 28:65001, 32:123, 36:6, 80:2, 84:248, 88:7, 92:1033, 104:6, 108:0xffffffff, 164:0xffffffff, 168:0xffffffff, 196:1, 200:0xffffffff, 208:0xffffffff, 224:0xffffffff, 232:0xffffffff, 236:0xffffffff, 244:0xffffffff}.items():
    struct.pack_into('>I', header, offset, value)
for offset in range(40, 80, 4): struct.pack_into('>I', header, offset, 0xffffffff)
struct.pack_into('>HH', header, 192, 1, 1)
header[248:255] = b'Fixture'
records = [header, compressed, bytes.fromhex('e98e0d0a')]
pdb = bytearray(78 + len(records)*8 + 2)
pdb[:7] = b'Fixture'
pdb[60:68] = b'BOOKMOBI'
struct.pack_into('>H', pdb, 76, len(records))
offset = len(pdb)
for i, record in enumerate(records):
    struct.pack_into('>II', pdb, 78 + 8*i, offset, i*2)
    offset += len(record)
(root/'plain.mobi').write_bytes(pdb + b''.join(records))
