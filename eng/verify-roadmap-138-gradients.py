"""Run the independent gradient checks over the generated corpus and controls."""
import argparse
import json
from pathlib import Path
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('source', 'output', 'reference', 'controls'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    checker = Path(__file__).with_name('check-roadmap-138-gradients.py')
    controls = Path(__file__).with_name('test-roadmap-138-gradient-checker.py')
    results = []
    for name, angle, geometry, themed in [
        ('gradient-0', 0, 'rect', False), ('gradient-5400000', 5400000, 'rect', False),
        ('gradient-10800000', 10800000, 'rect', False), ('gradient-16200000', 16200000, 'rect', False),
        ('gradient-theme', 0, 'rect', True), ('gradient-ellipse', 0, 'ellipse', False),
        ('gradient-roundRect', 0, 'roundRect', False), ('gradient-triangle', 0, 'triangle', False),
        ('gradient-diamond', 0, 'diamond', False),
    ]:
        output = args.output / name
        command = [sys.executable, str(checker), str(angle), str(args.source / (name + '.docx')),
                   str(output / 'saved.docx'), str(output / 'output.pdf'), str(args.reference / (name + '.pdf')),
                   str(output / 'output.svg'), str(output / 'output.xps'), '--geometry', geometry]
        if themed:
            command.append('--theme')
        if (output / 'output.png').exists():
            command.extend(('--svg-png', str(output / 'output.png')))
        result = subprocess.run(command, check=True, capture_output=True, text=True)
        if result.stderr:
            print(result.stderr, file=sys.stderr, end='')
        results.append(json.loads(result.stdout.strip().splitlines()[-1]))
    root = args.output / 'gradient-0'; reverse = args.output / 'gradient-10800000'
    subprocess.run([sys.executable, str(controls), str(args.source / 'gradient-0.docx'), str(root / 'saved.docx'),
                    str(root / 'output.pdf'), str(args.reference / 'gradient-0.pdf'), str(root / 'output.svg'),
                    str(root / 'output.xps'), str(args.reference / 'gradient-10800000.pdf'), str(reverse / 'output.svg'),
                    str(args.controls)], check=True)
    remap = args.output / 'gradient-theme-remapped'; remap_checker = Path(__file__).with_name('check-roadmap-138-gradient-remap.py')
    command = [sys.executable, str(remap_checker), str(args.source / 'gradient-theme-remapped.docx'), str(remap / 'saved.docx'),
               str(remap / 'output.pdf'), str(args.reference / 'gradient-theme-remapped.pdf'), str(remap / 'output.svg'),
               str(remap / 'output.xps'), str(remap / 'diagnostics.txt')]
    diagnostic_index = len(command) - 1
    if (remap / 'output.png').exists():
        command.extend(('--svg-png', str(remap / 'output.png')))
    result = subprocess.run(command, check=True, capture_output=True, text=True)
    if result.stderr:
        print(result.stderr, file=sys.stderr, end='')
    remapped = json.loads(result.stdout.strip().splitlines()[-1])
    missing = args.controls / 'missing-diagnostic.txt'; missing.write_text('')
    command[diagnostic_index] = str(missing)
    if subprocess.run(command, capture_output=True, text=True).returncode == 0:
        raise ValueError('The remapped-theme checker accepted a missing diagnostic.')
    print(json.dumps({'specimens': results, 'negativeControls': 6, 'colorChannelTolerance': 8, 'remappedTheme': remapped}))


if __name__ == '__main__':
    main()
