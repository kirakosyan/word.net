"""Index every failed verdict from a retained refresh-fidelity-baseline capture."""
import argparse
import csv
import hashlib
import importlib.util
import json
from pathlib import Path
from zipfile import ZipFile


def load_tool(filename):
    spec = importlib.util.spec_from_file_location(filename, Path(__file__).with_name(filename + '.py'))
    tool = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(tool)
    return tool


baseline = load_tool('refresh-fidelity-baseline')
comparison = load_tool('run-comparison')
ledger = load_tool('generate-roadmap-138-ledger')


def classify(verdict, result, reference, theme_controls):
    if verdict == 'page-geometry' and ledger.a4_rounding(result['measurement']['snapshot']['pages'], reference['pages']):
        return 'reference-policy-difference'
    if verdict == 'diagnostic-expectations' and result['inputSha256'] in theme_controls and result['knownLossChecks'] == {
            'passed': False, 'missing': [], 'unexpected': ['Docx.ThemeFont']}:
        return 'documented-unsupported'
    return 'unknown'


def read_report(capture, directory, kind, expected_hash):
    raw = capture.read(f'{directory}/{kind}.json')
    if hashlib.sha256(raw).hexdigest() != expected_hash:
        raise ValueError(f'Raw {kind} hash mismatch: {directory}')
    return json.loads(raw)


def index(capture):
    receipt = json.loads(capture.read('receipt.json'))
    manifest = baseline.ROOT / 'docs/measurements/roadmap-138-combination-baseline/fixtures/manifest.json'
    theme_controls = {d['sha256'] for d in json.loads(manifest.read_bytes())['documents']
                      if d['id'] in ('theme-direct', 'theme-styled')}
    rows = []
    for name, cohort in receipt['cohorts'].items():
        for mode in ('source', 'package'):
            directory = f'{name}-{mode}'
            hashes = cohort['rawHashes'][mode]
            report = read_report(capture, directory, 'results', hashes['results'])
            defects = read_report(capture, directory, 'defects', hashes['defects'])
            if report['sourceRevision'] != receipt['selectedSourceCommit'] or report['sourceDirty'] or \
                    report['manifestSha256'] != cohort['manifestSha256'] or report['tolerances'] != comparison.TOLERANCES:
                raise ValueError('Comparison identity or thresholds changed: ' + directory)
            baseline.verify_measurements(report, defects, comparison, cohort['manifest']['documents'],
                                         receipt['workers'][mode]['binaries'])
            if baseline.compact(report) != cohort['measurements'][mode] or defects != cohort['defects'][mode]:
                raise ValueError('Receipt and raw results disagree: ' + directory)
            for result in report['results']:
                document = result['documentId']
                measured = result['measurement']
                if measured['inputSha256'] != result['inputSha256']:
                    raise ValueError(f'Worker measured a different input for {directory}/{document}')
                actual = measured['snapshot']
                reference = json.loads(capture.read(f'{directory}/{document}-wordnet-reference.json'))
                observed = comparison.snapshot_check(actual, reference)
                check = result['referenceChecks'][0]
                if any(check[key] != value for key, value in observed.items()):
                    raise ValueError(f'Reference snapshot disagrees with {directory}/{document}')
                for rank, verdict in comparison.failure_verdicts(result):
                    rows.append((rank, name, document, mode, verdict, classify(verdict, result, reference, theme_controls)))
    return sorted(rows)


def write_index(rows, stream):
    writer = csv.writer(stream, delimiter='\t', lineterminator='\n')
    writer.writerow(('rank', 'cohort', 'documentId', 'mode', 'failedVerdict', 'classification'))
    writer.writerows(rows)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    with ZipFile(args.capture) as archive:
        rows = index(archive)
    with args.output.open('w', encoding='utf-8', newline='') as stream:
        write_index(rows, stream)
