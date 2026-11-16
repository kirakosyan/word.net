"""Provision pinned optional Apache POI test tools; no shipped runtime dependency."""
import hashlib
import json
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
from pathlib import Path
import sys
import time
import urllib.error
import urllib.request


RETRYABLE_STATUS = (429, 502, 503, 504)


def download(url):
    for attempt in range(5):
        try:
            with urllib.request.urlopen(url, timeout=60) as response:
                return response.read()
        except urllib.error.HTTPError as error:
            if error.code not in RETRYABLE_STATUS or attempt == 4:
                raise
            delay = 2 ** (attempt + 1)
            retry_after = error.headers.get('Retry-After')
            if retry_after:
                try:
                    required = float(retry_after)
                except ValueError:
                    required = (parsedate_to_datetime(retry_after) - datetime.now(timezone.utc)).total_seconds()
                delay = max(delay, required)
            if delay > 60:
                raise RuntimeError('Dependency server requires a retry delay over 60 seconds; retry CI later.') from error
            error.close()
            print(f'Dependency server returned HTTP {error.code}; retrying in {delay:g}s.', flush=True)
            time.sleep(delay)


def download_dependency(item):
    urls = [item['url'], *item.get('mirrors', [])]
    for index, url in enumerate(urls):
        try:
            return download(url)
        except urllib.error.HTTPError as error:
            if error.code not in RETRYABLE_STATUS or index == len(urls) - 1:
                raise
            error.close()
            print(f'Dependency server exhausted retries with HTTP {error.code}; '
                  f'trying the configured mirror for {item["name"]}.', flush=True)


def provision(item, destination):
    path = destination / item['name']
    data = path.read_bytes() if path.is_file() else download_dependency(item)
    if hashlib.sha256(data).hexdigest() != item['sha256']:
        raise ValueError('Pinned test dependency hash mismatch: ' + item['name'])
    if not path.is_file():
        temporary = path.with_suffix(path.suffix + '.download')
        try:
            temporary.write_bytes(data)
            temporary.replace(path)
        finally:
            temporary.unlink(missing_ok=True)


def main():
    root = Path(__file__).resolve().parents[1]
    destination = Path(sys.argv[1]) if len(sys.argv) == 2 else root / 'artifacts/comparison-poi'
    destination.mkdir(parents=True, exist_ok=True)
    for item in json.loads((root / 'eng/comparison-poi.json').read_text()):
        provision(item, destination)
    print(destination)


if __name__ == '__main__':
    main()
