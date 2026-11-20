"""Index every failed verdict from a retained refresh-fidelity-baseline capture."""
import argparse
import csv
import hashlib
import importlib.util
import json
from pathlib import Path
from zipfile import ZipFile

import fidelity_policy_v1 as policy


def load_tool(filename):
    spec = importlib.util.spec_from_file_location(filename, Path(__file__).with_name(filename + '.py'))
    tool = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(tool)
    return tool


baseline = load_tool('refresh-fidelity-baseline')


def read_report(capture, directory, kind, expected_hash):
    raw = capture.read(f'{directory}/{kind}.json')
    if hashlib.sha256(raw).hexdigest() != expected_hash:
        raise ValueError(f'Raw {kind} hash mismatch: {directory}')
    return json.loads(raw)


def index(capture):
    raw_receipt = capture.read('receipt.json')
    receipt = json.loads(raw_receipt)
    # The original 1.6.2 receipt predates per-snapshot hashing. Its supplement
    # was verified against the separately retained Linux run.
    supplement = {}
    if hashlib.sha256(raw_receipt).hexdigest() == '3ca806bfd7389ea2f86053f220d83d7e74a47c297ed293b4d9b8317660e6ef91':
        supplement = read_report(capture, 'qualification', 'reference-snapshot-hashes',
                                 'afd3a929ae93f0110b5654e473368fd7b21aa0b47d04bda28646773fa3f32634')
    rows = []
    for name, cohort in receipt['cohorts'].items():
        for mode in ('source', 'package'):
            directory = f'{name}-{mode}'
            hashes = cohort['rawHashes'][mode]
            report = read_report(capture, directory, 'results', hashes['results'])
            defects = read_report(capture, directory, 'defects', hashes['defects'])
            if report['sourceRevision'] != receipt['selectedSourceCommit'] or report['sourceDirty'] or \
                    report['manifestSha256'] != cohort['manifestSha256'] or report['tolerances'] != policy.TOLERANCES:
                raise ValueError('Comparison identity or thresholds changed: ' + directory)
            baseline.verify_measurements(report, defects, policy, cohort['manifest']['documents'],
                                         receipt['workers'][mode]['binaries'])
            if baseline.compact(report) != cohort['measurements'][mode] or defects != cohort['defects'][mode]:
                raise ValueError('Receipt and raw results disagree: ' + directory)
            references = hashes.get('referenceSnapshots', supplement.get(directory, {}))
            if set(references) != {doc['id'] for doc in cohort['manifest']['documents']}:
                raise ValueError('Reference snapshot identities differ from the captured manifest: ' + directory)
            for result in report['results']:
                document = result['documentId']
                measured = result['measurement']
                if measured['inputSha256'] != result['inputSha256']:
                    raise ValueError(f'Worker measured a different input for {directory}/{document}')
                actual = measured['snapshot']
                reference = read_report(capture, directory, document + '-wordnet-reference', references[document])
                observed = policy.snapshot_check(actual, reference)
                check = result['referenceChecks'][0]
                if any(check[key] != value for key, value in observed.items()):
                    raise ValueError(f'Reference snapshot disagrees with {directory}/{document}')
                for rank, verdict in policy.failure_verdicts(result):
                    status, _ = policy.classification(name, document, verdict, actual['pages'], reference['pages'],
                                                      result['knownLossChecks'])
                    rows.append((rank, name, document, mode, verdict, status))
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
