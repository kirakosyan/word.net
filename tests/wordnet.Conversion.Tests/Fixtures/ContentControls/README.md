# Content-control fixture provenance

These XML documents and fragments were independently authored for wordnet on
2026-10-26 from ECMA-376 Part 1 §17.5.2/§22.5 and the published Office 2010 checkbox
schema. They are MIT-licensed repository test inputs, not extracted customer or
proprietary application resources. Tests package them using the existing independent
DOCX test builder and validate actual output with the test-only Open XML SDK,
direct XML/relationship inspection and independent PDF/XPS readers.

- `checkbox.xml`: inline ID 42, literal merge-looking tag, unchecked `-` / checked
  `X`, Noto Sans declaration, surrounding `Before ` / ` after` text and 12 pt size.
- `choices.xml`: ordered inline dropdown with display/stored values, including an
  actual empty stored value and XML metacharacters; separate block combo in a fixed
  table cell with free text. Unbound lastValue 999 must not select an item.
- `dates.xml`: leap-day midnight and explicit-offset instant. The latter displays
  `2025-12-31 22:30` in UTC, regardless of culture or clock; DateOnly policy admits
  the first control and rejects the non-midnight instant.
- `rich.xml`: differently formatted inline runs adjacent to a closed bookmark and
  a live scalar field; omitted-type block rich control with two paragraphs, explicit
  spacing/line rule/alignment/mark color. No field is inside the control.
- `store.xml`: inert local XML scalar/attribute targets, namespaced and unqualified
  names, XML escaping, Boolean/choice/calendar-date/instant values. Its namespace
  URI is a symbol and is never fetched.

Negative cases mutate these authored structures explicitly in test code. Placeholder
and custom-XML properties/relationship parts are authored in the fixture helper,
with exact expected graph and type/value assertions. No generated DOCX/PDF/XPS,
font binary is committed here. The separately licensed [Word-authored glossary
parts](OfficeWordParts/README.md) provide a negative compatibility control. Managed font metrics
use the existing embedded open-license Noto Sans; caller-font cases use existing
licensed test fixtures. No interactive Word UI or pixel-parity qualification is
inferred from these schema/semantic checks. The existing independent reference
failures remain unchanged.

- `word-editor.xml` was independently authored on 2026-10-27 from the same OOXML
  specifications to reproduce common Word 2010+ editor scaffolding: paragraph/text
  identities, revision-session attributes, proofing markers, a cached page marker,
  script font/size/emphasis slots and language metadata. It is a specification-based
  fixture, not output captured from Word or evidence of consumer UI qualification.
  Tests admit inactive slots for Latin/default-script content with information and
  explicitly reject active/unqualified script formatting. Primary run appearance
  and edited values are independently schema/PDF/XPS checked for all six kinds.
