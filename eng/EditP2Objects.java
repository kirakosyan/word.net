// Development-only independent LibreOffice UNO acceptance driver. MIT synthetic edits.
import com.sun.star.beans.PropertyValue;
import com.sun.star.beans.XPropertySet;
import com.sun.star.bridge.UnoUrlResolver;
import com.sun.star.bridge.XUnoUrlResolver;
import com.sun.star.chart.XChartDataArray;
import com.sun.star.chart.XChartDocument;
import com.sun.star.container.XNameAccess;
import com.sun.star.document.XEmbeddedObjectSupplier;
import com.sun.star.frame.XComponentLoader;
import com.sun.star.frame.XStorable;
import com.sun.star.lang.XComponent;
import com.sun.star.text.XTextEmbeddedObjectsSupplier;
import com.sun.star.uno.UnoRuntime;
import com.sun.star.uno.XComponentContext;
import java.io.File;

public final class EditP2Objects {
    private static <T> T query(Class<T> type, Object value) {
        T result = UnoRuntime.queryInterface(type, value);
        if (result == null) throw new IllegalStateException("Missing independent editor interface " + type.getName());
        return result;
    }
    private static PropertyValue property(String name, Object value) {
        PropertyValue result = new PropertyValue(); result.Name = name; result.Value = value; return result;
    }
    public static void main(String[] args) {
        try { run(args); } catch (Throwable e) { e.printStackTrace(); System.exit(1); }
    }
    private static void run(String[] args) throws Exception {
        if (args.length != 3) throw new IllegalArgumentException("input.docx output.docx output.pdf");
        XComponentContext local = com.sun.star.comp.helper.Bootstrap.createInitialComponentContext(null);
        XUnoUrlResolver resolver = UnoUrlResolver.create(local);
        XComponentContext context = query(XComponentContext.class, resolver.resolve("uno:socket,host=127.0.0.1,port=2017;urp;StarOffice.ComponentContext"));
        Object desktop = context.getServiceManager().createInstanceWithContext("com.sun.star.frame.Desktop", context);
        XComponentLoader loader = query(XComponentLoader.class, desktop);
        XComponent document = loader.loadComponentFromURL(new File(args[0]).toURI().toString(), "_blank", 0,
            new PropertyValue[] { property("Hidden", true), property("UpdateDocMode", (short)0), property("MacroExecutionMode", com.sun.star.document.MacroExecMode.NEVER_EXECUTE) });
        if (document == null) throw new IllegalStateException("LibreOffice could not load the object document");
        int charts = 0, equations = 0;
        try {
            XPropertySet docProperties = query(XPropertySet.class, document);
            // LibreOffice retains imported DOCX XLSX blobs separately from chart data.
            // Edit that real workbook in Calc, then return Calc's saved stream to Writer.
            // This prevents the application's chart-cache update leaving stale externalData.
            PropertyValue[] bag = (PropertyValue[])docProperties.getPropertyValue("InteropGrabBag");
            for (PropertyValue p : bag) if (p.Name.equals("OOXEmbeddings")) {
                for (PropertyValue embedded : (PropertyValue[])p.Value) if (embedded.Name.endsWith(".xlsx")) {
                    com.sun.star.io.XInputStream original = query(com.sun.star.io.XInputStream.class, embedded.Value);
                    java.nio.file.Path temp = java.nio.file.Files.createTempFile("wordnet-uno-", ".xlsx");
                    try {
                        try (java.io.OutputStream out = java.nio.file.Files.newOutputStream(temp)) {
                            byte[][] chunk = new byte[1][]; int count;
                            while ((count = original.readBytes(chunk, 8192)) != 0) out.write(chunk[0], 0, count);
                        }
                        XComponent calc = loader.loadComponentFromURL(temp.toUri().toString(), "_blank", 0,
                            new PropertyValue[] { property("Hidden", true), property("MacroExecutionMode", com.sun.star.document.MacroExecMode.NEVER_EXECUTE) });
                        try {
                            com.sun.star.sheet.XSpreadsheetDocument sheetDoc = query(com.sun.star.sheet.XSpreadsheetDocument.class, calc);
                            com.sun.star.sheet.XSpreadsheet sheet = query(com.sun.star.sheet.XSpreadsheet.class, sheetDoc.getSheets().getByName("Data"));
                            double old = sheet.getCellByPosition(1, 1).getValue(); sheet.getCellByPosition(1, 1).setValue(42);
                            query(XStorable.class, calc).storeAsURL(temp.toUri().toString(), new PropertyValue[] { property("FilterName", "Calc MS Excel 2007 XML"), property("Overwrite", true) });
                            System.out.println("Calc cell editor: B2 " + old + " -> " + sheet.getCellByPosition(1, 1).getValue());
                        } finally { calc.dispose(); }
                        Object access = context.getServiceManager().createInstanceWithContext("com.sun.star.ucb.SimpleFileAccess", context);
                        com.sun.star.ucb.XSimpleFileAccess fileAccess = query(com.sun.star.ucb.XSimpleFileAccess.class, access);
                        embedded.Value = new com.sun.star.uno.Any(new com.sun.star.uno.Type(com.sun.star.io.XInputStream.class), fileAccess.openFileRead(temp.toUri().toString()));
                        // The office stream owns the file until Writer has exported the package.
                        temp.toFile().deleteOnExit();
                    } catch (Exception e) { java.nio.file.Files.deleteIfExists(temp); throw e; }
                }
            }
            docProperties.setPropertyValue("InteropGrabBag", bag);
            XNameAccess objects = query(XTextEmbeddedObjectsSupplier.class, document).getEmbeddedObjects();
            for (String name : objects.getElementNames()) {
                Object model = query(XEmbeddedObjectSupplier.class, objects.getByName(name)).getEmbeddedObject();
                XChartDocument chart = UnoRuntime.queryInterface(XChartDocument.class, model);
                if (chart != null) {
                    XChartDataArray data = query(XChartDataArray.class, chart.getData()); double[][] values = data.getData();
                    if (values.length < 1 || values[0].length < 1) throw new IllegalStateException("Chart has no editable data");
                    double before = values[0][0]; values[0][0] = 42; data.setData(values); charts++;
                    System.out.println("Chart-data editor: first value " + before + " -> " + data.getData()[0][0]);
                } else {
                    XPropertySet properties = query(XPropertySet.class, model);
                    if (properties.getPropertySetInfo().hasPropertyByName("Formula")) {
                        String before = (String)properties.getPropertyValue("Formula");
                        properties.setPropertyValue("Formula", "{x} over {3}"); equations++;
                        System.out.println("Equation editor: " + before + " -> " + properties.getPropertyValue("Formula"));
                    }
                }
            }
            if (charts != 1 || equations != 1) throw new IllegalStateException("Expected one real chart and one real equation; got " + charts + "," + equations);
            XStorable save = query(XStorable.class, document);
            save.storeAsURL(new File(args[1]).toURI().toString(), new PropertyValue[] { property("FilterName", "Office Open XML Text"), property("Overwrite", true) });
            save.storeToURL(new File(args[2]).toURI().toString(), new PropertyValue[] { property("FilterName", "writer_pdf_Export"), property("Overwrite", true) });
            System.out.println("Independent editable DOCX and visual PDF saved");
        } finally { document.dispose(); }
        System.exit(0);
    }
}
