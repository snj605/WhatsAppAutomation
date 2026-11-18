using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Contacts.Import;

namespace WhatsAppAutomation.Infrastructure.Contacts;

/// <summary>Reads contact files with ClosedXML (.xlsx) or a small CSV parser, and builds the .xlsx template.</summary>
internal sealed class ContactSpreadsheet : IContactSpreadsheet
{
    private const string ContactsSheet = "Contacts";

    public SpreadsheetReadResult Read(Stream file, string fileName, int maxRows)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".xlsx" => ReadXlsx(file, maxRows),
            ".csv" => ReadCsv(file, maxRows),
            _ => Fail("Upload an Excel (.xlsx) or CSV (.csv) file."),
        };
    }

    public byte[] CreateTemplate(IReadOnlyList<string> categoryNames, IReadOnlyList<(string Header, string Help)> extraColumns, bool withBirthday)
    {
        using var workbook = new XLWorkbook();
        var columns = ImportColumns.All.Where(c => withBirthday || c.Key != ImportColumns.Birthday).ToList();

        var help = workbook.AddWorksheet("Instructions");
        help.Cell(1, 1).Value = "How to fill in the Contacts sheet";
        help.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        help.Cell(2, 1).Value = "One row per person. Columns marked * are required. Replace or delete the three sample rows before importing.";
        help.Cell(3, 1).Value = "The real name in the contact's language is required too: messages use it. Rows with mistakes are listed in the preview and not saved.";
        help.Cell(4, 1).Value = "Existing contacts are matched by phone number and updated; empty cells keep what is saved.";
        help.Cell(6, 1).Value = "Column";
        help.Cell(6, 2).Value = "What to enter";
        help.Range(6, 1, 6, 2).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#d1fae5"));
        var line = 7;
        foreach (var (_, header, text) in columns)
        {
            help.Cell(line, 1).Value = header;
            help.Cell(line, 2).Value = text;
            line++;
        }

        foreach (var (header, text) in extraColumns)
        {
            help.Cell(line, 1).Value = header;
            help.Cell(line, 2).Value = text;
            line++;
        }

        help.Column(1).Width = 20;
        help.Column(2).Width = 100;

        var sheet = workbook.AddWorksheet(ContactsSheet);
        for (var i = 0; i < columns.Count; i++)
        {
            var (key, header, _) = columns[i];
            var cell = sheet.Cell(1, i + 1);
            cell.Value = header;
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml(ImportColumns.Required.Contains(key) ? "#047857" : "#334155"));

            // Text format, so Excel keeps "+91…" and does not turn "15-08" into a date.
            sheet.Column(i + 1).Style.NumberFormat.Format = "@";
            sheet.Column(i + 1).Width = key is ImportColumns.Notes ? 40 : 20;
        }

        for (var i = 0; i < extraColumns.Count; i++)
        {
            var cell = sheet.Cell(1, columns.Count + i + 1);
            cell.Value = extraColumns[i].Header;
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#0F766E"));
            sheet.Column(columns.Count + i + 1).Style.NumberFormat.Format = "@";
            sheet.Column(columns.Count + i + 1).Width = 22;
        }

        string[] keys = [ImportColumns.Phone, ImportColumns.SavedName, ImportColumns.NameEn, ImportColumns.NameHi, ImportColumns.NameGu,
            ImportColumns.Category, ImportColumns.Language, ImportColumns.Salutation, ImportColumns.Birthday, ImportColumns.Groups, ImportColumns.Notes];
        string[][] samples =
        [
            ["+919876543210", "Daddy", "Rameshbhai", "रमेशभाई", "રમેશભાઈ", "Elders", "gu", "Pujya", "15-08-1960", "Family, Surat", ""],
            ["+919812345678", "Rahul", "Rahul", "राहुल", "", "Youngers", "hi", "", "02-10", "Family", ""],
            ["+447700900123", "Mr Smith", "John Smith", "", "", "Others", "en", "Dear", "", "", "Met at the conference"],
        ];
        for (var r = 0; r < samples.Length; r++)
        {
            for (var k = 0; k < keys.Length; k++)
            {
                var c = columns.FindIndex(col => col.Key == keys[k]);
                if (c >= 0)
                {
                    sheet.Cell(r + 2, c + 1).SetValue(samples[r][k]);
                }
            }

            sheet.Row(r + 2).Style.Font.SetItalic().Font.SetFontColor(XLColor.FromHtml("#64748b"));
        }

        var lists = workbook.AddWorksheet("Lists");
        lists.Cell(1, 1).Value = "Languages";
        lists.Cell(2, 1).Value = "en";
        lists.Cell(3, 1).Value = "hi";
        lists.Cell(4, 1).Value = "gu";
        lists.Cell(1, 2).Value = "Categories";
        var categories = categoryNames.Count > 0 ? categoryNames : ["Elders", "Youngers", "Others"];
        for (var i = 0; i < categories.Count; i++)
        {
            lists.Cell(i + 2, 2).SetValue(categories[i]);
        }

        lists.Hide();

        var languageColumn = columns.FindIndex(c => c.Key == ImportColumns.Language) + 1;
        var languageRule = sheet.Range(2, languageColumn, 2000, languageColumn).CreateDataValidation();
        languageRule.List(lists.Range(2, 1, 4, 1));
        languageRule.IgnoreBlanks = true;

        // Not strict: a new category name is allowed and creates that category.
        var categoryColumn = columns.FindIndex(c => c.Key == ImportColumns.Category) + 1;
        var categoryRule = sheet.Range(2, categoryColumn, 2000, categoryColumn).CreateDataValidation();
        categoryRule.List(lists.Range(2, 2, categories.Count + 1, 2));
        categoryRule.ShowErrorMessage = false;

        sheet.SheetView.FreezeRows(1);
        sheet.SetTabActive();

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static SpreadsheetReadResult ReadXlsx(Stream file, int maxRows)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(file);
        }
        catch (Exception)
        {
            return Fail("This file could not be opened as an Excel workbook. Save it as .xlsx and try again.");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault(s => s.Name.Equals(ContactsSheet, StringComparison.OrdinalIgnoreCase))
                ?? workbook.Worksheets.FirstOrDefault(s => s.Visibility == XLWorksheetVisibility.Visible);
            var used = sheet?.RangeUsed();
            if (sheet is null || used is null)
            {
                return Fail("The file is empty.");
            }

            var firstRow = used.FirstRow().RowNumber();
            var lastRow = used.LastRow().RowNumber();
            var lastColumn = used.LastColumn().ColumnNumber();
            var headers = new List<string>();
            for (var c = 1; c <= lastColumn; c++)
            {
                headers.Add(Text(sheet.Cell(firstRow, c)));
            }

            var table = new List<(int Row, List<string> Cells)>();
            for (var r = firstRow + 1; r <= lastRow; r++)
            {
                var cells = new List<string>(lastColumn);
                for (var c = 1; c <= lastColumn; c++)
                {
                    cells.Add(Text(sheet.Cell(r, c)));
                }

                table.Add((r, cells));
            }

            return Build(headers, table, maxRows);
        }
    }

    private static string Text(IXLCell cell)
    {
        var value = cell.Value;
        if (value.IsDateTime)
        {
            return value.GetDateTime().ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        }

        if (value.IsNumber)
        {
            return value.GetNumber().ToString("0.##########", CultureInfo.InvariantCulture);
        }

        return value.IsBlank ? string.Empty : value.ToString(CultureInfo.InvariantCulture);
    }

    private static SpreadsheetReadResult ReadCsv(Stream file, int maxRows)
    {
        string text;
        try
        {
            using var reader = new StreamReader(file, new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            return Fail(
                "This CSV is not saved as UTF-8, so Hindi and Gujarati letters would be lost. Use the Excel template, or save as \"CSV UTF-8\".");
        }

        var records = ParseCsv(text);
        if (records.Count == 0)
        {
            return Fail("The file is empty.");
        }

        var table = records.Skip(1).Select((cells, i) => (Row: i + 2, Cells: cells)).ToList();
        return Build(records[0], table, maxRows);
    }

    /// <summary>RFC 4180: commas (or semicolons, if the header uses them), quotes, and line breaks inside quotes.</summary>
    internal static List<List<string>> ParseCsv(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        var separator = !firstLine.Contains(',') && firstLine.Contains(';') ? ';' : ',';
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (ch == separator)
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                record.Add(field.ToString());
                field.Clear();
                records.Add(record);
                record = [];
            }
            else
            {
                field.Append(ch);
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }

    private static SpreadsheetReadResult Build(IReadOnlyList<string> headers, IEnumerable<(int Row, List<string> Cells)> table, int maxRows)
    {
        var keys = headers.Select(h => ImportColumns.KeyOf(h)).ToList();
        var missing = ImportColumns.All.Where(c => ImportColumns.Required.Contains(c.Key) && !keys.Contains(c.Key))
            .Select(c => c.Header.TrimEnd('*')).ToList();
        if (missing.Count > 0)
        {
            return new SpreadsheetReadResult([], missing, null);
        }

        var rows = new List<SpreadsheetRow>();
        foreach (var (rowNumber, cells) in table)
        {
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (rows.Count == maxRows)
            {
                return Fail($"The file has more than {maxRows} contacts. Split it into smaller files.");
            }

            var values = new Dictionary<string, string>();
            for (var c = 0; c < keys.Count && c < cells.Count; c++)
            {
                if (keys[c].Length > 0 && !values.ContainsKey(keys[c]))
                {
                    values[keys[c]] = cells[c];
                }
            }

            rows.Add(new SpreadsheetRow(rowNumber, values));
        }

        return rows.Count == 0 ? Fail("The file has no contacts below the header row.") : new SpreadsheetReadResult(rows, [], null);
    }

    private static SpreadsheetReadResult Fail(string problem) => new([], [], problem);
}
