"""Regenerate the public feature diagnostic catalog from literal call sites."""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
entries = {}
for path in sorted((root / 'src').rglob('*.cs')):
    if {'obj', 'bin'} & set(path.parts):
        continue
    text = path.read_text(encoding='utf-8-sig')
    for match in re.finditer(r'(Loss|Report)\(\s*"([\w.]+)"\s*,\s*"((?:[^"\\]|\\.)*)"', text):
        entries[match[2]] = (match[1], match[3])
    for match in re.finditer(r'LossAt\([^;]*?"([\w.]+)"\s*,\s*"((?:[^"\\]|\\.)*)"', text):
        entries[match[1]] = ('Loss', match[2])
entries.update({
    'Table.MergedGroupSplit': ('Report', 'An oversized vertically merged group is split at line boundaries.'),
    'Table.RowForcedSplit': ('Report', 'An oversized non-splitting row is split at line boundaries.'),
    'Text.Formatting': ('Loss', 'Plain text cannot represent rich formatting.'),
    'Markdown.RunAppearance': ('Loss', 'Markdown cannot represent font sizes and colors.'),
    'Doc.Formatting': ('Loss', 'Binary DOC import reconstructs the supported formatting subset.'),
})
# Keep the hand-maintained API/inspection guidance above the generated table.
catalog = root / 'docs/diagnostics.md'
header = catalog.read_text(encoding='utf-8').split('| Code | Policy | Meaning |', 1)[0]
header += '| Code | Policy | Meaning |\n| --- | --- | --- |\n'
lines = [f'| `{code}` | {policy} | {message.replace(chr(124), "/")} |' for code, (policy, message) in sorted(entries.items())]
(root / 'docs/diagnostics.md').write_text(header + '\n'.join(lines) + '\n', encoding='utf-8')
print(f'Documented {len(entries)} diagnostic codes.')
