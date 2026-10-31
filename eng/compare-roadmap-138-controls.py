"""Record exact raster contrasts for the separately authored theme/table controls."""
import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageChops


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-results', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    pairs = [('theme-font', 'explicit-font'), ('theme-color', 'explicit-color'), ('table-style', 'table-direct')]
    observations = []
    for first, second in pairs:
        for suffix in ('wordnet-reference-1.png', 'wordnet-1.png'):
            paths = [args.source_results / (name + '-' + suffix) for name in (first, second)]
            with Image.open(paths[0]) as image_a, Image.open(paths[1]) as image_b:
                a, b = image_a.convert('RGB'), image_b.convert('RGB')
                if a.size != b.size:
                    raise ValueError('Raster dimensions differ: ' + str(paths))
                difference = ImageChops.difference(a, b)
                counts = difference.getcolors(a.width * a.height)
                if counts is None:
                    raise ValueError('Could not count raster differences: ' + str(paths))
                changed = sum(count for count, color in counts if color != (0, 0, 0))
                def exact_colors(image):
                    colors = dict((color, count) for count, color in image.getcolors(a.width * a.height))
                    return {'red': colors.get((255, 0, 0), 0), 'accentBlue': colors.get((34, 102, 204), 0)}
                observations.append({
                    'pair': [first, second], 'rendering': 'libreoffice-reference' if 'reference' in suffix else 'release-source',
                    'imageSha256': [digest(p) for p in paths], 'pixels': a.width * a.height,
                    'differentPixels': changed, 'firstExactColors': exact_colors(a), 'secondExactColors': exact_colors(b),
                })
    result = {'schemaVersion': 1, 'method': 'Exact RGB pixel comparison of one rendered page; this is a contrast, not a replacement for unchanged fidelity thresholds.',
              'observations': observations}
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(f'{len(observations)} exact raster contrasts recorded in {args.output}')


if __name__ == '__main__':
    main()
