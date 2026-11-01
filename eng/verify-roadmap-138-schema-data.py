#!/usr/bin/env python3
"""Independently check JSON declarations against reports and observed merges."""
import argparse
import copy
import json
import re
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

import fitz

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
CASES = ('valid', 'fallback', 'optional', 'missing-scope', 'empty', 'wrong-type',
         'scope-type', 'typed-non-json', 'malformed', 'limit')
MERGED = {
    'valid': ['root', 'child', 'other', 'end'],
    'fallback': ['root', 'child', 'root', 'end'],
    'optional': ['root', 'child', 'root', 'end'],
    'missing-scope': ['cached', 'cached', 'end'],
    'empty': ['root', 'end'],
    'wrong-type': ['bad', 'end'],
    'scope-type': ['cached', 'cached', 'end'],
}


def finding(code, path, scope=None, index=None, unknown=False):
    return {'Code': code, 'path': path, 'scope': scope, 'ItemIndex': index, 'Unknown': unknown}


EXPECTED = {
    'fallback': [finding('Data.MissingItemPath', 'Name', 'Items', 1)],
    'missing-scope': [finding('Data.ScopeMissing', 'Name', 'Items', unknown=True)],
    'wrong-type': [finding('Data.ItemPathType', 'Amount', 'Items', 0)],
    'scope-type': [finding('Data.PathType', 'Items'), finding('Data.ScopeType', 'Name', 'Items')],
    'typed-non-json': [finding('Data.PathType', 'When')],
}


def values(value):
    if isinstance(value, dict):
        return [item for child in value.values() for item in values(child)]
    if isinstance(value, list):
        return [item for child in value for item in values(child)]
    return [value]


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate JSON key')
        result[key] = value
    return result


def check(observations):
    for name in CASES:
        item = observations[name]
        report = item['result']
        if name == 'malformed':
            try:
                json.loads(item['fixture'], object_pairs_hook=unique_pairs)
            except ValueError:
                pass
            else:
                raise ValueError('Malformed control no longer has a duplicate key')
        else:
            data = json.loads(item['fixture'], object_pairs_hook=unique_pairs)
            if name in ('fallback', 'optional') and ('Name' in data['Items'][1] or data['Name'] != 'root'):
                raise ValueError('Fallback control no longer lacks the item value')
            if name == 'limit' and len(data['Items']) <= 1:
                raise ValueError('Row limit control no longer exceeds the limit')
            if name == 'typed-non-json' and (not isinstance(data['When'], str) or data['Logo'] is not None):
                raise ValueError('Typed JSON control changed')
        error = {'malformed': 'Template.Json', 'limit': 'Limit.TemplateData'}.get(name)
        if report['inspectionError'] != error:
            raise ValueError('Inspection failure changed: ' + name)
        if error:
            if item['default'] is not None or report['findings'] is not None or report['HasErrors'] is not None or report['HasUnknowns'] is not None:
                raise ValueError('A failed inspection returned a partial report: ' + name)
            continue
        expected = EXPECTED.get(name, [])
        if report['findings'] != expected:
            raise ValueError('Declaration finding changed: ' + name)
        if report['HasErrors'] != any(not entry['Unknown'] for entry in expected) or report['HasUnknowns'] != any(entry['Unknown'] for entry in expected):
            raise ValueError('Finding summary changed: ' + name)
        default = item['default']
        if default is None or default['HasErrors'] != report['HasErrors'] or default['HasUnknowns'] != report['HasUnknowns']:
            raise ValueError('Default report summary changed: ' + name)
        private = {'Name', 'Items', 'Amount', 'When', 'Logo', 'root', 'child', 'other', 'bad'}
        if any(value in private for value in values(default) if isinstance(value, str)):
            raise ValueError('Default report disclosed a data name or value: ' + name)
        if len(default['Findings']) != len(expected):
            raise ValueError('Default report finding count changed: ' + name)
        aliases = {}
        for public, resolved in zip(default['Findings'], expected):
            for key in ('Code', 'ItemIndex', 'Unknown'):
                if public[key] != resolved[key]:
                    raise ValueError('Default report finding changed: ' + name)
            for alias, path in ((public['PathId'], resolved['path']), (public['ScopePathId'], resolved['scope'])):
                if path is None:
                    if alias is not None:
                        raise ValueError('Unexpected scope alias: ' + name)
                elif not isinstance(alias, str) or not re.fullmatch(r'path-\d{4}', alias):
                    raise ValueError('Invalid path alias: ' + name)
                elif alias in aliases and aliases[alias] != path:
                    raise ValueError('Different names share a path alias: ' + name)
                else:
                    aliases[alias] = path
        if name in MERGED:
            expected_merge_error = {'missing-scope': 'Template.MissingValue', 'scope-type': 'Template.Collection'}.get(name)
            if report['mergeError'] != expected_merge_error or report['after'] != MERGED[name]:
                raise ValueError('Merge result or error changed: ' + name)
            if expected_merge_error and report['before'] != report['after']:
                raise ValueError('Failed merge changed the input: ' + name)
            if item['mergedText'] != MERGED[name] or item['officeText'] != MERGED[name]:
                raise ValueError('Saved DOCX or independent office text changed: ' + name)
    return {'jsonScenarios': len(CASES), 'independentOfficeReads': len(MERGED),
            'itemFallbackContrastObserved': True, 'failedInspectionsReturnNoPartialReport': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('fixtures', 'observations', 'office'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    observations = {}
    for name in CASES:
        directory = args.observations / name
        default = directory / 'default.json'
        item = {'fixture': (args.fixtures / (name + '.json')).read_text(),
                'result': json.loads((directory / 'result.json').read_text()),
                'default': json.loads(default.read_text()) if default.exists() else None}
        if name in MERGED:
            with ZipFile(directory / 'merged.docx') as archive:
                root = ET.fromstring(archive.read('word/document.xml'))
            item['mergedText'] = [node.text or '' for node in root.iter(W + 't')]
            with fitz.open(args.office / (name + '.pdf')) as document:
                item['officeText'] = ''.join(page.get_text() for page in document).split()
        observations[name] = item
    result = check(observations)
    controls = {}
    for name in ('missing item index', 'missing item path', 'missing item scope', 'missing item code',
                 'missing item certainty', 'missing item omitted', 'optional fallback rejected',
                 'unknown scope made definite', 'unknown scope omitted', 'wrong item type admitted',
                 'root scope type admitted', 'repeat scope type admitted', 'null image rejected',
                 'JSON date string admitted', 'error summary changed', 'unknown summary changed',
                 'default name leaked', 'default alias changed', 'malformed error changed',
                 'limit error changed', 'partial failure report', 'failed merge mutated',
                 'independent office output changed', 'saved DOCX output changed'):
        changed = copy.deepcopy(observations)
        fallback = changed['fallback']['result']['findings'][0]
        if name == 'missing item index': fallback['ItemIndex'] = 0
        if name == 'missing item path': fallback['path'] = 'Other'
        if name == 'missing item scope': fallback['scope'] = 'Other'
        if name == 'missing item code': fallback['Code'] = 'Data.ItemPathType'
        if name == 'missing item certainty': fallback['Unknown'] = True
        if name == 'missing item omitted': changed['fallback']['result']['findings'].clear()
        if name == 'optional fallback rejected': changed['optional']['result']['findings'].append(finding('Data.MissingItemPath', 'Name', 'Items', 1))
        if name == 'unknown scope made definite': changed['missing-scope']['result']['findings'][0]['Unknown'] = False
        if name == 'unknown scope omitted': changed['missing-scope']['result']['findings'].clear()
        if name == 'wrong item type admitted': changed['wrong-type']['result']['findings'].clear()
        if name == 'root scope type admitted': changed['scope-type']['result']['findings'].pop(0)
        if name == 'repeat scope type admitted': changed['scope-type']['result']['findings'].pop()
        if name == 'null image rejected': changed['typed-non-json']['result']['findings'].append(finding('Data.PathType', 'Logo'))
        if name == 'JSON date string admitted': changed['typed-non-json']['result']['findings'].clear()
        if name == 'error summary changed': changed['fallback']['result']['HasErrors'] = False
        if name == 'unknown summary changed': changed['missing-scope']['result']['HasUnknowns'] = False
        if name == 'default name leaked': changed['fallback']['default']['leak'] = 'Name'
        if name == 'default alias changed': changed['fallback']['default']['Findings'][0]['ScopePathId'] = 'Items'
        if name == 'malformed error changed': changed['malformed']['result']['inspectionError'] = None
        if name == 'limit error changed': changed['limit']['result']['inspectionError'] = None
        if name == 'partial failure report': changed['limit']['default'] = {'Findings': []}
        if name == 'failed merge mutated': changed['missing-scope']['result']['before'] = ['changed']
        if name == 'independent office output changed': changed['fallback']['officeText'][2] = 'other'
        if name == 'saved DOCX output changed': changed['fallback']['mergedText'][2] = 'other'
        try:
            check(changed)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': controls,
                      'controlMethod': 'Reports, parsed inputs and saved-output observations changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
