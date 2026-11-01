"""Check privacy-safe template findings against independent DOCX and actual merges."""
import argparse
import json
from pathlib import Path

from docx import Document


def check(name, source, report_text, outcome, saved):
    report = json.loads(report_text)
    codes = {item['Code'] for item in report['Findings']}
    if report['IsReusable'] or 'Plan.UninspectedCoverage' not in codes or 'Plan.ReuseUnavailable' not in codes:
        raise ValueError('Missing unknown coverage or unsafe reuse claim')
    if any(secret in report_text for secret in ('Customer', 'HiddenAccount', 'Total', 'word/document.xml', 'cached')):
        raise ValueError('Report leaked a source or schema name')
    if not Path(source).is_file():
        raise ValueError('Missing independent input')
    if name == 'valid':
        if {'Plan.MissingPath', 'Plan.PathType'} & codes or outcome != 'passed':
            raise ValueError('Valid declaration or merge failed')
        text = '\n'.join(p.text for p in Document(saved).paragraphs)
        if 'Ada' not in text or '12.50' not in text or len(report['Requirements']) != 2:
            raise ValueError('Actual merged values or requirements changed')
    elif name == 'missing':
        if 'Plan.MissingPath' not in codes or outcome != 'Template.MissingValue':
            raise ValueError('Missing path was not predicted and rejected')
    elif name == 'type':
        if 'Plan.PathType' not in codes or outcome != 'Template.Format':
            raise ValueError('Wrong number type was not predicted and rejected')
    elif name == 'repeat':
        if 'Plan.RepeatScope' not in codes or outcome != 'not-performed' or 'Plan.MissingPath' in codes:
            raise ValueError('Repeat scope was treated as known')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path); parser.add_argument('output', type=Path)
    args = parser.parse_args(); reports = {}
    for name in ('valid', 'missing', 'type', 'repeat'):
        output = args.output / name; report = (output / 'report.json').read_text()
        outcome = (output / 'result.txt').read_text()
        check(name, args.source / (name + '.docx'), report, outcome, output / 'saved.docx')
        reports[name] = {'codes': [f['Code'] for f in json.loads(report)['Findings']], 'outcome': outcome}
    negative = {}
    def reject(label, name, report, outcome, saved):
        try:
            check(name, args.source / (name + '.docx'), report, outcome, saved)
        except (ValueError, KeyError):
            negative[label] = 'rejected'; return
        raise ValueError('Checker accepted negative control: ' + label)
    missing = json.loads((args.output / 'missing/report.json').read_text())
    missing['Findings'] = [f for f in missing['Findings'] if f['Code'] != 'Plan.MissingPath']
    reject('removed missing finding', 'missing', json.dumps(missing), 'Template.MissingValue', None)
    valid = json.loads((args.output / 'valid/report.json').read_text()); valid['IsReusable'] = True
    reject('forged reuse', 'valid', json.dumps(valid), 'passed', args.output / 'valid/saved.docx')
    valid['IsReusable'] = False; valid['unexpected'] = 'Customer.Name'
    reject('leaked name', 'valid', json.dumps(valid), 'passed', args.output / 'valid/saved.docx')
    repeat = (args.output / 'repeat/report.json').read_text()
    reject('incorrect repeat outcome', 'repeat', repeat, 'passed', None)
    print(json.dumps({'specimens': reports, 'negativeControls': negative}, indent=2))


if __name__ == '__main__':
    main()
