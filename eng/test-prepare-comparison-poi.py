"""Offline checks for rate-limit handling and pinned dependency integrity."""
from datetime import datetime, timedelta, timezone
from email.utils import format_datetime
import hashlib
import importlib.util
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import urllib.error

spec = importlib.util.spec_from_file_location('prepare_poi', Path(__file__).with_name('prepare-comparison-poi.py'))
poi = importlib.util.module_from_spec(spec)
spec.loader.exec_module(poi)


class ProvisionTests(unittest.TestCase):
    def setUp(self):
        self.data = b'independent pinned archive bytes'
        self.item = {'name': 'tool.jar', 'url': 'https://example.invalid/tool.jar',
                     'sha256': hashlib.sha256(self.data).hexdigest()}

    def error(self, status, retry_after=None):
        headers = {} if retry_after is None else {'Retry-After': retry_after}
        return urllib.error.HTTPError(self.item['url'], status, 'test response', headers, None)

    def test_rate_limit_waits_before_retry_and_caches_verified_bytes(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(poi.urllib.request, 'urlopen',
                side_effect=[self.error(429, '7'), io.BytesIO(self.data)]) as request, patch.object(poi.time, 'sleep') as sleep:
            destination = Path(directory)
            poi.provision(self.item, destination)
            poi.provision(self.item, destination)
            self.assertEqual(self.data, (destination / 'tool.jar').read_bytes())
            self.assertEqual(2, request.call_count)
            sleep.assert_called_once_with(7)
            self.assertFalse(list(destination.glob('*.download')))

    def test_http_date_retry_after_is_honored(self):
        retry_at = datetime.now(timezone.utc) + timedelta(seconds=20)
        with patch.object(poi.urllib.request, 'urlopen', side_effect=[self.error(503, format_datetime(retry_at)),
                io.BytesIO(self.data)]), patch.object(poi.time, 'sleep') as sleep:
            self.assertEqual(self.data, poi.download(self.item['url']))
            self.assertGreater(sleep.call_args.args[0], 15)
            self.assertLessEqual(sleep.call_args.args[0], 20)

    def test_persistent_rate_limit_stops_after_five_requests(self):
        with patch.object(poi.urllib.request, 'urlopen', side_effect=self.error(429)) as request, \
                patch.object(poi.time, 'sleep') as sleep:
            with self.assertRaises(urllib.error.HTTPError):
                poi.download(self.item['url'])
            self.assertEqual(5, request.call_count)
            self.assertEqual(4, sleep.call_count)

    def test_long_server_delay_fails_without_retrying_early(self):
        with patch.object(poi.urllib.request, 'urlopen', side_effect=self.error(429, '120')) as request, \
                patch.object(poi.time, 'sleep') as sleep:
            with self.assertRaisesRegex(RuntimeError, 'over 60 seconds'):
                poi.download(self.item['url'])
            self.assertEqual(1, request.call_count)
            sleep.assert_not_called()

    def test_permanent_failure_is_not_retried(self):
        with patch.object(poi.urllib.request, 'urlopen', side_effect=self.error(404)) as request:
            with self.assertRaises(urllib.error.HTTPError):
                poi.download(self.item['url'])
            self.assertEqual(1, request.call_count)

    def test_bad_download_is_not_cached(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(poi.urllib.request, 'urlopen',
                return_value=io.BytesIO(b'wrong bytes')):
            with self.assertRaisesRegex(ValueError, 'hash mismatch'):
                poi.provision(self.item, Path(directory))
            self.assertEqual([], list(Path(directory).iterdir()))

    def test_bad_cached_archive_is_not_accepted_or_silently_replaced(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(poi.urllib.request, 'urlopen') as request:
            (Path(directory) / 'tool.jar').write_bytes(b'wrong cached bytes')
            with self.assertRaisesRegex(ValueError, 'hash mismatch'):
                poi.provision(self.item, Path(directory))
            request.assert_not_called()


if __name__ == '__main__':
    unittest.main()
