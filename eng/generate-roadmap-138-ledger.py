"""Index retained 1.5.0 comparison failures without inventing causes or new measurements."""
import argparse
import hashlib
import fidelity_policy_v1 as policy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REVISION = 'bf5ab004d1622e66084aedf4bbc83e3cf8d9c2c7'
COHORTS = {
    'initial-existing': ('docs/measurements/roadmap-138-initial/existing-24-{mode}/defects.json',
                         'docs/measurements/roadmap-138-initial/existing-24-{mode}/results.json',
                         'docs/measurements/roadmap-138-initial/existing-24-source'),
    'initial-reductions': ('docs/measurements/roadmap-138-initial/reductions-{mode}/defects.json',
                           'docs/measurements/roadmap-138-initial/reductions-{mode}/results.json',
                           'docs/measurements/roadmap-138-initial/reductions-source'),
    'contrast-existing': ('docs/measurements/roadmap-138-contrasts/existing-24/{mode}-defects.json',
                          'docs/measurements/roadmap-138-contrasts/existing-24/{mode}-results.json',
                          None),
    'contrast-theme-table': ('docs/measurements/roadmap-138-contrasts/reductions/{mode}-defects.json',
                             'docs/measurements/roadmap-138-contrasts/reductions/{mode}-results.json',
                             'docs/measurements/roadmap-138-contrasts/reductions'),
}
REFERENCE_MANIFESTS = {
    'initial-existing': 'docs/measurements/roadmap-138-initial/existing-24-references.json',
    'initial-reductions': 'docs/measurements/roadmap-138-initial/reductions-references.json',
    'contrast-existing': 'docs/measurements/roadmap-138-contrasts/existing-24/references.json',
    'contrast-theme-table': 'docs/measurements/roadmap-138-contrasts/reductions/references.json',
}


def load(relative):
    path = ROOT / relative
    raw = path.read_bytes()
    return json.loads(raw), hashlib.sha256(raw).hexdigest()


def build():
    findings, inputs, probes = [], {}, []
    contrast_path = 'docs/measurements/roadmap-138-contrasts/pair-contrasts.json'
    contrasts, contrast_hash = load(contrast_path); inputs[contrast_path] = contrast_hash
    pairs = {(tuple(item['pair']), item['rendering']): item for item in contrasts['observations']}
    if len(pairs) != len(contrasts['observations']):
        raise ValueError('Duplicate paired contrast observation.')
    font_pair = ('theme-font', 'explicit-font')
    table_pair = ('table-style', 'table-direct')
    if (pairs[(font_pair, 'libreoffice-reference')]['differentPixels'] != 0
            or pairs[(font_pair, 'release-source')]['differentPixels'] <= 0
            or pairs[(table_pair, 'libreoffice-reference')]['firstExactColors']['accentBlue'] <= 0
            or pairs[(table_pair, 'libreoffice-reference')]['secondExactColors']['accentBlue'] <= 0
            or pairs[(table_pair, 'release-source')]['firstExactColors']['accentBlue'] != 0
            or pairs[(table_pair, 'release-source')]['secondExactColors']['accentBlue'] <= 0):
        raise ValueError('The specific paired contrasts no longer establish the recorded boundaries.')
    source_results, _ = load(COHORTS['contrast-theme-table'][1].format(mode='source'))
    package_results, _ = load(COHORTS['contrast-theme-table'][1].format(mode='package'))
    source_by_id = {item['documentId']: item for item in source_results['results']}
    package_by_id = {item['documentId']: item for item in package_results['results']}
    for document in (*font_pair, *table_pair):
        source, package = source_by_id[document], package_by_id[document]
        if (source['inputSha256'] != package['inputSha256']
                or source['measurement']['outputSha256'] != package['measurement']['outputSha256']
                or source['measurement']['snapshot'] != package['measurement']['snapshot']):
            raise ValueError('The source/package contrast outputs do not agree: ' + document)
    for cohort, (defect_template, result_template, reference_template) in COHORTS.items():
        manifest_path = REFERENCE_MANIFESTS[cohort]
        manifest, manifest_hash = load(manifest_path)
        inputs[manifest_path] = manifest_hash
        references = {entry['documentId']: entry for entry in manifest['references']}
        if len(references) != len(manifest['references']):
            raise ValueError('Duplicate reference identity in ' + manifest_path)
        for mode in ('source', 'package'):
            defect_path = defect_template.format(mode=mode)
            result_path = result_template.format(mode=mode)
            defects, defect_hash = load(defect_path)
            results, result_hash = load(result_path)
            inputs[defect_path], inputs[result_path] = defect_hash, result_hash
            if defects.get('schemaVersion') != 1 or results.get('schemaVersion') != 1 or defects.get('sourceRevision') != REVISION:
                raise ValueError('Unexpected source identity or evidence schema: ' + defect_path)
            if defects.get('sourceDirty') or results.get('sourceDirty') or results.get('sourceRevision') != REVISION:
                raise ValueError('The recorded comparison is not the clean selected release source: ' + result_path)
            policy.validate_defect_report(results, defects)
            measured = {item['documentId']: item for item in results['results']}
            if len(measured) != len(results['results']):
                raise ValueError('Duplicate measured document in ' + result_path)
            seen = set()
            for index, item in enumerate(defects['findings']):
                document = item['documentId']
                if document in seen or document not in measured or item['inputSha256'] != measured[document]['inputSha256']:
                    raise ValueError('Defect and result identity mismatch: ' + document)
                seen.add(document)
                result = measured[document]
                reference_entry = references[document]
                if reference_entry['kind'] != 'libreoffice' or reference_entry['inputSha256'] != item['inputSha256']:
                    raise ValueError('Reference and input identities disagree: ' + document)
                reference_path = None if reference_template is None else reference_template.format(mode=mode) + '/' + document + '-wordnet-reference.json'
                reference = {'pages': []}
                if reference_path is not None:
                    reference, reference_hash = load(reference_path)
                    inputs[reference_path] = reference_hash
                source_pages = result['measurement']['snapshot']['pages']
                reference_pages = reference['pages']
                checks = result['referenceChecks']
                if len(checks) != 1 or checks[0].get('kind') != 'libreoffice':
                    reference_pages = []
                diagnostics = item['knownLossChecks']
                ranks = {name: rank for rank, name in policy.failure_verdicts(result)}
                for verdict in item['failedVerdicts']:
                    status, rationale = policy.classification(cohort, document, verdict, source_pages, reference_pages, diagnostics)
                    findings.append({
                        'cohort': cohort, 'mode': mode, 'documentId': document, 'inputSha256': item['inputSha256'],
                        'verdict': verdict, 'classification': status, 'rationale': rationale,
                        'triageRank': ranks[verdict], 'primaryFinding': item['finding'],
                        'fixture': item['minimalFixture'], 'relatedFixture': item['smallerRelatedFixture'],
                        'defectEvidence': defect_path + '#/findings/' + str(index),
                        'resultEvidence': result_path, 'referenceSnapshot': reference_path,
                        'referenceManifest': manifest_path, 'referencePdfSha256': reference_entry['sha256'],
                        'observedReference': [{key: check.get(key) for key in ('kind', 'version', 'pageCountMatches', 'pageGeometryMatches',
                                              'textAndOrderMatches', 'glyphSequenceMatches', 'maximumGlyphDisplacementPoints', 'glyphPlacementPasses', 'visualPassed')}
                                              for check in checks],
                        'unexpectedDiagnostics': diagnostics['unexpected'] if verdict == 'diagnostic-expectations' else [],
                    })
    for mode in ('source', 'package'):
        path = 'docs/measurements/roadmap-138-contrasts/workflow-' + mode + '.json'
        workflow, digest = load(path); inputs[path] = digest
        for index, fragment in enumerate(workflow['fragments']):
            if not fragment['targetUnchanged'] or not fragment['sourceUnchanged'] or fragment['outcome'] not in ('NotSupportedException', 'InvalidDataException'):
                raise ValueError('Unexpected fragment probe result in ' + path)
            probes.append({'mode': mode, 'kind': 'fragment', 'classification': 'documented-unsupported',
                           'target': fragment['target'], 'targetSha256': workflow['inputs'][fragment['target'] + '.docx'],
                           'source': fragment['source'], 'sourceSha256': workflow['inputs'][fragment['source'] + '.docx'],
                           'outcome': fragment['outcome'], 'code': fragment['message'].split(':', 1)[0],
                           'assemblySha256': workflow['assemblySha256'], 'evidence': path + '#/fragments/' + str(index)})
        review = workflow['review']
        if not review['outputUntouched'] or not review['sourceUnchanged'] or review['outcome'] != 'NotSupportedException':
            raise ValueError('Unexpected review probe result in ' + path)
        probes.append({'mode': mode, 'kind': 'retained-review-export', 'classification': 'documented-unsupported',
                       'source': 'review-source', 'sourceSha256': workflow['inputs']['review-source.docx'],
                       'outcome': review['outcome'], 'code': review['message'].split(':', 1)[0],
                       'assemblySha256': workflow['assemblySha256'], 'evidence': path + '#/review'})
    findings.sort(key=lambda item: (item['triageRank'], item['cohort'], item['mode'], item['documentId'], item['verdict']))
    return {'schemaVersion': 1, 'releaseSource': REVISION, 'availability': 'Recorded 1.5.0 evidence; not current-source qualification.',
            'scope': 'Every failed verdict enumerated by the retained comparison defect files, per run and artifact mode, and the separately executed fragment/review probes. The Word-authored control probe remains in the initial measurement narrative.',
            'classificationPolicy': 'Unattributed findings remain unknown. Reference-policy classification is limited to the measured A4 unit rounding; no threshold is changed.',
            'inputs': dict(sorted(inputs.items())), 'findings': findings, 'workflowProbes': probes}


def serialize(result):
    header = {key: value for key, value in result.items() if key not in ('findings', 'workflowProbes')}
    text = json.dumps(header, indent=2, ensure_ascii=False)
    def rows(key):
        return ',\n'.join('    ' + json.dumps(item, ensure_ascii=False, separators=(',', ':')) for item in result[key])
    return text[:-2] + ',\n  "findings": [\n' + rows('findings') + '\n  ],\n  "workflowProbes": [\n' + rows('workflowProbes') + '\n  ]\n}\n'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = build()
    args.output.write_text(serialize(result), encoding='utf-8')
    print(f"Indexed {len(result['findings'])} failed verdicts from {len(result['inputs'])} retained evidence files.")


if __name__ == '__main__':
    main()
