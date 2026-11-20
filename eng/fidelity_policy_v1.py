"""Frozen replay rules for the retained 1.5.0 and 1.6.2 fidelity ledgers."""

TOLERANCES = dict(glyphPoints=0.25, significantPixelChannelSum=96, changedPixelFraction=0.002, meanChannelError=0.25)


def normalized(text):
    return ' '.join(text.split())


def failure_verdicts(result):
    """Reproduce the verdict names and ranks used by the measured comparison."""
    checks = result.get('referenceChecks', [])
    verdicts = [
        (0, 'conversion-or-reader-failure', result['status'] == 'failed'),
        (1, 'authored-text-or-order', result.get('authoredChecks', {}).get('passed') is False),
        (2, 'editable-text-or-order', result.get('editableContent', {}).get('passed') is False),
        (3, 'diagnostic-expectations', result.get('knownLossChecks', {}).get('passed') is False),
        (4, 'pagination', result.get('authoredChecks', {}).get('pageCountMatches') is False or any(c.get('pageCountMatches') is False for c in checks)),
        (5, 'reference-text-or-order', any(c.get('textAndOrderMatches') is False for c in checks)),
        (6, 'glyph-placement', any(c.get('glyphPlacementPasses') is False for c in checks)),
        (7, 'page-geometry', any(c.get('pageGeometryMatches') is False for c in checks)),
        (8, 'raster', any(c.get('visualPassed') is False for c in checks))]
    failures = [(rank, name) for rank, name, failed in verdicts if failed]
    # Keep a failed aggregate verdict visible even if its detailed flags are unavailable.
    if any(c.get('textAndGeometryPassed') is False and not any(c.get(flag) is False for flag in
           ('pageCountMatches', 'textAndOrderMatches', 'glyphPlacementPasses', 'pageGeometryMatches')) for c in checks):
        failures.append((9, 'reference-text-or-geometry'))
    return failures


def validate_defect_report(report, defects):
    expected = {(r['documentId'], r['engine']): [name for _, name in failure_verdicts(r)]
                for r in report['results'] if failure_verdicts(r)}
    actual = {(f['documentId'], f['engine']): f['failedVerdicts'] for f in defects['findings']}
    if len(actual) != len(defects['findings']) or actual != expected:
        raise ValueError('Ranked findings must retain every failed document/engine verdict exactly once.')


def snapshot_check(actual, reference):
    page_match = actual['pageCount'] == reference['pageCount']
    text_match = [normalized(p['text']) for p in actual['pages']] == [normalized(p['text']) for p in reference['pages']]
    maximum = 0.0
    glyph_match = page_match
    geometry_match = page_match
    for a, b in zip(actual['pages'], reference['pages']):
        geometry_match &= abs(a['width'] - b['width']) <= 0.01 and abs(a['height'] - b['height']) <= 0.01
        # Match identical glyph sequences only; never align away missing characters.
        if [g['text'] for g in a['glyphs']] != [g['text'] for g in b['glyphs']]:
            glyph_match = False
            continue
        for ga, gb in zip(a['glyphs'], b['glyphs']):
            maximum = max(maximum, abs(ga['x'] - gb['x']), abs(ga['y'] - gb['y']))
    return dict(pageCountMatches=page_match, pageGeometryMatches=bool(geometry_match), textAndOrderMatches=text_match,
                glyphSequenceMatches=glyph_match, maximumGlyphDisplacementPoints=maximum,
                glyphPlacementPasses=glyph_match and maximum <= TOLERANCES['glyphPoints'])


def a4_rounding(source_pages, reference_pages):
    """Recognize only the measured A4 unit-rounding contrast, not arbitrary geometry failures."""
    if not source_pages or len(source_pages) != len(reference_pages):
        return False
    pairs = zip(source_pages, reference_pages)
    return all(abs(source['width'] - 595.3) < 0.000001 and abs(source['height'] - 841.9) < 0.000001
               and abs(reference['width'] - 595.303937007874) < 0.000001
               and abs(reference['height'] - 841.889763779528) < 0.000001
               for source, reference in pairs)


KNOWN_WARNINGS = {
    'contrast-theme-table': {'theme-font': 'Docx.ThemeFont', 'theme-color': 'Docx.ThemeColor', 'table-style': 'Docx.TableFormatting'},
    'combinations': {'theme-direct': 'Docx.ThemeFont', 'theme-styled': 'Docx.ThemeFont'},
}


def classification(cohort, document, verdict, source_pages, reference_pages, diagnostics):
    if verdict == 'page-geometry' and a4_rounding(source_pages, reference_pages):
        return ('reference-policy-difference', 'The measured paired pages use the recorded A4 unit rounding; the unchanged geometry verdict remains failed.')
    known = KNOWN_WARNINGS.get(cohort, {}).get(document)
    if (known and verdict == 'diagnostic-expectations' and diagnostics.get('missing') == []
            and diagnostics.get('unexpected') == [known]):
        return ('documented-unsupported', 'The release reports the expected documented feature boundary, while the authored control expected no loss.')
    if cohort == 'contrast-theme-table' and document in ('theme-font', 'table-style') and verdict in ('glyph-placement', 'raster'):
        return ('documented-unsupported', 'A separate explicit-format contrast reproduces this specific release boundary; other differences are not attributed.')
    return ('unknown', 'The failed check is retained; its cause is not established by this comparison.')
