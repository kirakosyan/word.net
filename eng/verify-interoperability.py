"""Optional independent RTF/SVG/page-label qualification; never a runtime dependency."""

import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

import fitz
from PIL import Image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    artifacts = args.artifacts.resolve()
    soffice = shutil.which("soffice")
    if not soffice:
        raise SystemExit("Provision a separately licensed LibreOffice reader first.")
    with tempfile.TemporaryDirectory(prefix="wordnet-rtf-reader-") as temporary:
        directory = Path(temporary)
        subprocess.run([
            soffice, "-env:UserInstallation=" + (directory / "profile").as_uri(),
            "--headless", "--convert-to", "docx:Office Open XML Text", "--outdir",
            str(directory), str(artifacts / "writer.rtf"),
        ], check=True)
        ns = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main",
              "wp": "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"}
        with zipfile.ZipFile(directory / "writer.docx") as archive:
            root = ET.fromstring(archive.read("word/document.xml"))
        text = "".join(element.text or "" for element in root.findall(".//w:t", ns))
        assert "Unicode Ω Ж 😀 {braces} \\ path" in text
        assert len(root.findall(".//w:tbl", ns)) == 1
        assert len(root.findall(".//w:tc", ns)) == 4
        assert [len(cell.findall("w:p", ns)) for cell in root.findall(".//w:tc", ns)] == [1, 1, 1, 1]
        extent, = root.findall(".//wp:extent", ns)
        assert extent.attrib == {"cx": "762000", "cy": "381000"}
        run, = [r for r in root.findall(".//w:r", ns)
                if "Unicode" in "".join(t.text or "" for t in r.findall("w:t", ns))]
        assert run.find("w:rPr/w:b", ns) is not None
        assert run.find("w:rPr/w:i", ns) is not None
        assert run.find("w:rPr/w:sz", ns).get("{" + ns["w"] + "}val") == "36"
        subprocess.run([soffice, "-env:UserInstallation=" + (directory / "profile").as_uri(),
                        "--headless", "--convert-to", "docx:Office Open XML Text", "--outdir", str(directory),
                        str(artifacts / "writer-multiparagraph.rtf")], check=True)
        with zipfile.ZipFile(directory / "writer-multiparagraph.docx") as archive:
            cells = ET.fromstring(archive.read("word/document.xml")).findall(".//w:tc", ns)
        assert [len(cell.findall("w:p", ns)) for cell in cells] == [2, 2]
        assert [["".join(t.text or "" for t in p.findall(".//w:t", ns)) for p in cell.findall("w:p", ns)] for cell in cells] == [["One", "Two"], ["Last", ""]]
    with fitz.open(artifacts / "selected.pdf") as pdf:
        assert [page.get_label() for page in pdf] == ["iv", "v"]
    with fitz.open(artifacts / "source-2.svg") as svg:
        raster = svg[0].get_pixmap()
        rendered = Image.frombytes("RGB", (raster.width, raster.height), raster.samples).convert("L")
    expected = Image.open(artifacts / "middle-72.png").convert("L")
    assert rendered.size == expected.size == (300, 200)
    a = rendered.point(lambda v: 255 if v < 180 else 0)
    b = expected.point(lambda v: 255 if v < 180 else 0)
    assert a.getbbox() == b.getbbox()
    left = list(a.get_flattened_data()); right = list(b.get_flattened_data())
    intersection = sum(x > 0 and y > 0 for x, y in zip(left, right))
    union = sum(x > 0 or y > 0 for x, y in zip(left, right))
    assert intersection / union > .8
    with fitz.open(artifacts / "image.svg") as svg:
        pixels = svg[0].get_pixmap()
        assert pixels.pixel(45, 45) == (208, 32, 32)
        assert pixels.pixel(130, 45) == (32, 64, 208)
    for rotation, flip in ((0, False), (90, False), (90, True)):
        filename = f"crop-{rotation}-{flip}.svg"
        with fitz.open(artifacts / filename) as svg:
            pix = svg[0].get_pixmap()
            assert pix.pixel(45, 45) == (32, 64, 208)
            assert pix.pixel(151 if rotation == 0 else 91, 45) == (255, 255, 255)
    print(f"Independent RTF structure/image, PDF labels, SVG bounds/pixels PASS; glyph-mask IoU={intersection/union:.4f}")
    print("PyMuPDF", fitz.VersionBind)


if __name__ == "__main__":
    main()
