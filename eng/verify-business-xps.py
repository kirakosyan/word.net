"""Independently raster-check the authored XPS samples with PyMuPDF 1.26.6.

Optional qualification tool; PyMuPDF/MuPDF is installed separately under its own
AGPL/commercial license and is not a wordnet runtime or package dependency.
Generate inputs by running BusinessFidelityTests with WORDNET_BUSINESS_ARTIFACTS.
"""
import argparse
import json
import math
from pathlib import Path
import re
import zipfile

import fitz


def verify(directory: Path):
    cases = sorted(directory.glob("transform-*.xps"))
    assert len(cases) == 12, f"Expected 12 transform cases, got {len(cases)}"
    samples = 0
    for path in cases:
        match = re.fullmatch(r"transform-(0|90|180|270)-(True|False)-(True|False)", path.stem)
        assert match, path.name
        rotation, flip_h, flip_v = int(match[1]), match[2] == "True", match[3] == "True"
        with fitz.open(path) as document:
            assert len(document) == 1
            page = document[0]
            assert abs(page.rect.width - 400) < 0.3 and abs(page.rect.height - 300) < 0.3
            raster = page.get_pixmap(alpha=False)
            width, height = (120, 60) if rotation % 180 == 0 else (60, 120)
            c, s = round(math.cos(math.radians(rotation))), round(math.sin(math.radians(rotation)))
            for u in (0.15, 0.8):
                for v in (0.2, 0.7):
                    x, y = (u - 0.5) * 120 * (-1 if flip_h else 1), (v - 0.5) * 60 * (-1 if flip_v else 1)
                    dx, dy = round(30 + width / 2 + c * x - s * y), round(30 + height / 2 + s * x + c * y)
                    source_x = 0.25 + u * 0.75
                    expected = ((255, 0, 0) if v < 0.5 else (0, 0, 255)) if source_x < 0.5 else ((0, 255, 0) if v < 0.5 else (255, 255, 0))
                    actual = raster.pixel(dx, dy)
                    assert sum(abs(a - b) for a, b in zip(actual, expected)) <= 20, (path.name, dx, dy, actual, expected)
                    samples += 1
            assert raster.pixel(29, 29) == (255, 255, 255)
            raster.save(directory / (path.stem + "-mupdf.png"))
    with fitz.open(directory / "nested-tables.xps") as document:
        assert re.sub(r"\s", "", document[0].get_text()) == "BeforeInnerRightAfterTail"
        raster = document[0].get_pixmap(alpha=False)
        assert raster.pixel(100, 54) == (0, 255, 0)
        assert raster.pixel(225, 35) == (255, 255, 0)
        assert raster.pixel(29, 60) == (255, 255, 255)
        raster.save(directory / "nested-tables-mupdf.png")
    density_cases = sorted(directory.glob("density-*.xps"))
    assert len(density_cases) == 8, f"Expected 8 density cases, got {len(density_cases)}"
    for path in density_cases:
        dpi = int(path.stem.rsplit("-", 1)[1])
        with zipfile.ZipFile(path) as package:
            resource = next(name for name in package.namelist() if name.startswith("Resources/Images/"))
            pixels = fitz.Pixmap(package.read(resource))
            assert abs(pixels.xres - dpi) <= 1 and abs(pixels.yres - dpi) <= 1, (path.name, pixels.xres, pixels.yres)
        with fitz.open(path) as document:
            raster = document[0].get_pixmap(alpha=False)
            for x, y, expected in ((48, 42, (255, 0, 0)), (126, 42, (0, 255, 0)), (48, 72, (0, 0, 255)), (126, 72, (255, 255, 0))):
                actual = raster.pixel(x, y)
                assert sum(abs(a - b) for a, b in zip(actual, expected)) <= 20, (path.name, actual, expected)
            raster.save(directory / (path.stem + "-mupdf.png"))
    result = {"pymupdf": fitz.version[0], "mupdf": fitz.version[1], "transform_cases": len(cases), "pixel_samples": samples, "density_cases": len(density_cases), "nested_table": "passed"}
    (directory / "xps-verification.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    verify(parser.parse_args().directory)
