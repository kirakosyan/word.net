"""Provision pinned optional Apache POI test tools; no shipped runtime dependency."""
import hashlib
import json
from pathlib import Path
import sys
import urllib.request

root = Path(__file__).resolve().parents[1]
destination = Path(sys.argv[1]) if len(sys.argv) == 2 else root / 'artifacts/comparison-poi'
destination.mkdir(parents=True, exist_ok=True)
for item in json.loads((root / 'eng/comparison-poi.json').read_text()):
    path = destination / item['name']
    if not path.is_file():
        with urllib.request.urlopen(item['url'], timeout=60) as response:
            path.write_bytes(response.read())
    if hashlib.sha256(path.read_bytes()).hexdigest() != item['sha256']:
        raise ValueError('Pinned test dependency hash mismatch: ' + item['name'])
print(destination)
