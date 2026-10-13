import java.io.*;
import org.apache.poi.hwpf.HWPFDocument;
import org.apache.poi.hwpf.usermodel.*;

/** Optional independent DOC validation with Apache POI 5.4.1; never a library dependency. */
public class DocInteropCheck {
    public static void main(String[] args) throws Exception {
        if (args.length == 2 && args[0].equals("--check-layout-corpus")) {
            try (HWPFDocument doc = new HWPFDocument(new FileInputStream(args[1]))) {
                Range range = doc.getRange();
                require(range.numSections() == 2, "Two sections");
                require(range.getSection(0).getPageWidth() == 12240 && range.getSection(0).getPageHeight() == 15840, "Portrait geometry");
                require(range.getSection(1).getPageWidth() == 15840 && range.getSection(1).getPageHeight() == 12240, "Landscape geometry");
                require(range.getParagraph(0).getSpacingBefore() == 200 && range.getParagraph(0).getSpacingAfter() == 160, "Title spacing");
                require(range.getParagraph(1).getIndentFromLeft() == 480 && range.getParagraph(1).getFirstLineIndent() == 240, "Body indentation");
                Table table = null;
                for (int p = 0; p < range.numParagraphs(); p++)
                    if (range.getParagraph(p).isInTable()) { table = range.getTable(range.getParagraph(p)); break; }
                require(table != null && table.numRows() == 3, "Three table rows");
                TableCell wide = table.getRow(0).getCell(0);
                require(wide.getWidth() == 4800 && wide.numParagraphs() == 2, "Wide cell with two paragraphs");
                require(wide.getParagraph(0).text().trim().equals("Wide cell") && wide.getParagraph(1).text().replace("\u0007", "").trim().equals("Second line"), "Editable paragraph text");
                require(table.getRow(1).getCell(0).isVerticallyMerged() && table.getRow(1).getCell(0).isFirstVerticallyMerged(), "Vertical merge start");
                require(table.getRow(2).getCell(0).isVerticallyMerged() && !table.getRow(2).getCell(0).isFirstVerticallyMerged(), "Vertical merge continuation");
            }
            System.out.println("PASS: independent DOC sections, spacing, indentation, multi-paragraph cells and vertical merges (Apache POI 5.4.1).");
            return;
        }
        if (args.length == 3 && args[0].equals("--generate-layout")) {
            try (HWPFDocument doc = new HWPFDocument(new FileInputStream(args[1]))) {
                Range range = doc.getRange();
                range.insertBefore("Independent layout heading.");
                Section section = range.getSection(0);
                section.setMarginLeft(1440); section.setMarginRight(1200);
                section.setMarginTop(1080); section.setMarginBottom(900);
                Paragraph first = range.getParagraph(0);
                first.setKeepWithNext(true); first.setSpacingBefore(120); first.setSpacingAfter(240);
                first.setIndentFromLeft(720); first.setIndentFromRight(360); first.setFirstLineIndent(-240); first.setKeepOnPage(true);
                try (OutputStream output = new FileOutputStream(args[2])) { doc.write(output); }
            }
            return;
        }
        if (args.length == 2 && args[0].equals("--check-layout")) {
            try (HWPFDocument doc = new HWPFDocument(new FileInputStream(args[1]))) {
                Range range = doc.getRange();
                require(range.numSections() == 2, "Two sections");
                Section first = range.getSection(0), second = range.getSection(1);
                require(first.getPageWidth() == 12240 && first.getPageHeight() == 15840, "Letter portrait");
                require(second.getPageWidth() == 15840 && second.getPageHeight() == 12240, "Letter landscape");
                require(first.getMarginLeft() == 1440 && second.getMarginLeft() == 720, "Section margins");
                Paragraph heading = range.getParagraph(0);
                require(heading.keepWithNext() && heading.keepOnPage(), "Keep flags");
                require(heading.getSpacingBefore() == 200 && heading.getSpacingAfter() == 300, "Paragraph spacing");
                require(heading.getIndentFromLeft() == 720 && heading.getIndentFromRight() == 360 && heading.getFirstLineIndent() == -240, "Paragraph indentation");
            }
            System.out.println("PASS: DOC section geometry, margins, paragraph spacing, indents and keep flags (Apache POI 5.4.1).");
            return;
        }
        if (args.length == 3 && args[0].equals("--generate-appearance")) {
            try (HWPFDocument doc = new HWPFDocument(new FileInputStream(args[1]))) {
                CharacterRun run = doc.getRange().insertBefore("Independent blue text.");
                run.setFontSize(36); run.setColor(2);
                try (OutputStream output = new FileOutputStream(args[2])) { doc.write(output); }
            }
            return;
        }
        if (args.length == 3 && args[0].equals("--check-fidelity")) {
            try (HWPFDocument doc = new HWPFDocument(new FileInputStream(args[1]))) {
                Range range = doc.getRange();
                CharacterRun title = range.getCharacterRun(0);
                require(title.text().equals("Blue title"), "Title text");
                require(title.getFontSize() == 36 && title.getIco24() == 0x996633, "18-point RGB title");
                boolean tableFound = false, listFound = false, bulletFound = false;
                for (int p = 0; p < range.numParagraphs(); p++) {
                    Paragraph paragraph = range.getParagraph(p);
                    if (paragraph.isInTable() && !tableFound) {
                        Table table = range.getTable(paragraph); tableFound = true;
                        require(table.getRow(0).getCell(0).getWidth() == 2400, "120-point first column");
                        require(table.getRow(0).getCell(1).getWidth() == 4800, "240-point second column");
                    }
                    if (paragraph.isInList()) {
                        HWPFList list = paragraph.getList();
                        if (paragraph.getIlvl() == 0) { require(list.getStartAt((char)0) == 3, "List starts at three"); listFound = true; }
                        if (paragraph.getIlvl() == 1) { require(list.getNumberFormat((char)1) == 23, "Nested bullet"); bulletFound = true; }
                    }
                }
                require(tableFound && listFound && bulletFound, "Table and nested list");
                java.util.List<Picture> pictures = doc.getPicturesTable().getAllPictures();
                require(pictures.size() == 1, "One embedded image");
                Picture image = pictures.get(0);
                require(image.getDxaGoal() == 2400 && image.getDyaGoal() == 1200, "120 by 60 point image");
                require(image.getWidth() == 80 && image.getHeight() == 40, "80 by 40 pixel image");
                require(java.util.Arrays.equals(image.getContent(), java.nio.file.Files.readAllBytes(java.nio.file.Path.of(args[2]))), "Exact image bytes");
            }
            System.out.println("PASS: DOC font size/color, unequal widths, editable nested lists and image bytes/dimensions (Apache POI 5.4.1).");
            return;
        }
        if (args.length == 3 && args[0].equals("--generate-alignment")) {
            try (HWPFDocument doc = new HWPFDocument(new FileInputStream(args[1]))) {
                Range range = doc.getRange();
                for (int p = 0; p < range.numParagraphs(); p++) {
                    if (!range.getParagraph(p).isInTable()) continue;
                    Table table = range.getTable(range.getParagraph(p));
                    for (int c = 0; c < 3; c++) table.getRow(0).getCell(c).getParagraph(0).setJustification((byte)c);
                    break;
                }
                try (OutputStream output = new FileOutputStream(args[2])) { doc.write(output); }
            }
            return;
        }
        if (args.length == 2 && args[0].equals("--check-alignment")) {
            try (HWPFDocument doc = new HWPFDocument(new FileInputStream(args[1]))) {
                for (int i = 0; i < 3; i++) {
                    require(doc.getRange().getParagraph(i).getJustification() == i, "Left, center and right alignment");
                    require(doc.getRange().getParagraph(i).text().trim().equals("Align"), "Aligned paragraph text");
                }
            }
            System.out.println("PASS: DOC left, center and right alignment (Apache POI 5.4.1).");
            return;
        }
        if (args.length == 3 && args[0].equals("--generate-emphasis")) {
            try (HWPFDocument doc = new HWPFDocument(new FileInputStream(args[1]))) {
                CharacterRun run = doc.getRange().insertBefore("Bold and italic from Apache POI.");
                run.setBold(true);
                run.setItalic(true);
                try (OutputStream output = new FileOutputStream(args[2])) { doc.write(output); }
            }
            return;
        }
        if (args.length == 3 && args[0].equals("--generate-plain")) {
            try (HWPFDocument doc = new HWPFDocument(new FileInputStream(args[1]))) {
                doc.getRange().insertBefore("Wordnet conversion test\rEnglish printed text 12345.");
                try (OutputStream output = new FileOutputStream(args[2])) { doc.write(output); }
            }
            return;
        }
        try (HWPFDocument doc = new HWPFDocument(new FileInputStream(args[0]))) {
            Range range = doc.getRange();
            require(range.getParagraph(0).getStyleIndex() == 1, "Heading 1 style");
            boolean bold = false, italic = false;
            for (int i = 0; i < range.numCharacterRuns(); i++) {
                CharacterRun run = range.getCharacterRun(i);
                if (run.text().equals("bold")) bold = run.isBold();
                if (run.text().equals("italic")) italic = run.isItalic();
            }
            require(bold && italic, "Bold and italic text runs");
            Table table = null;
            // Word 97 uses table level zero; POI's default TableIterator assumes level one.
            for (int p = 0; p < range.numParagraphs(); p++) {
                Paragraph paragraph = range.getParagraph(p);
                if (paragraph.isInTable()) { table = range.getTable(paragraph); break; }
            }
            require(table != null && table.numRows() == 3, "Three editable table rows");
            require(table.getRow(0).isTableHeader(), "Repeating header row");
            String[][] cells = {{"Name", "Value"}, {"Alpha", "One"}, {"Beta", "Two"}};
            for (int r = 0; r < 3; r++) {
                require(table.getRow(r).numCells() == 2, "Two cells per row");
                for (int c = 0; c < 2; c++)
                    require(table.getRow(r).getCell(c).text().replace("\u0007", "").trim().equals(cells[r][c]), "Cell content");
            }
            System.out.println("PASS: DOC heading, bold, italic, table rows/cells and repeating header (Apache POI 5.4.1).");
        }
    }
    private static void require(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
