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

    public byte[] CreateWorkbook(ContactsWorkbook data)
    {
        using var workbook = new XLWorkbook();
        var columns = data.Columns;

        var help = workbook.AddWorksheet("Instructions");
        help.Cell(1, 1).Value = "Your contacts: change them here and import the file again";
        help.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        help.Cell(2, 1).Value = $"The Contacts sheet has your {data.Rows.Count} saved contacts, with a column for each of your groups, occasions and contact fields. "
            + "Change any cell, or add new people in new rows. Only the phone number is required (and the name in the contact's language).";
        help.Cell(3, 1).Value = "Pick from the lists: categories, salutations, groups and choices come from what you set up in the app, so a typing mistake cannot get in. "
            + "Add new ones in the app, then download this file again.";
        help.Cell(4, 1).Value = "Import: rows are matched by phone number, so saved contacts are updated and new numbers are added. An empty cell keeps what is saved. "
            + "Yes in a group column adds the person; to take someone out of a group, or to delete a contact, use the app.";
        help.Cell(5, 1).Value = "What you fill in must be complete: an occasion's second name needs its date. The import shows a preview first; rows with mistakes are listed and not saved. Up to 5000 rows per file.";
        help.Cell(7, 1).Value = "Column";
        help.Cell(7, 2).Value = "What to enter";
        help.Range(7, 1, 7, 2).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#d1fae5"));
        for (var i = 0; i < columns.Count; i++)
        {
            help.Cell(8 + i, 1).Value = columns[i].Header;
            help.Cell(8 + i, 2).Value = columns[i].Help;
        }

        help.Column(1).Width = 26;
        help.Column(2).Width = 110;

        var sheet = workbook.AddWorksheet(ContactsSheet);
        var lastRow = Math.Max(2000, data.Rows.Count + 1000);
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var cell = sheet.Cell(1, i + 1);
            cell.Value = column.Header;
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(XLColor.FromHtml(column.Required ? "#047857" : column.List is not null ? "#1d4ed8" : "#334155"));
            cell.CreateComment().AddText(column.Help);

            // Text format, so Excel keeps "+91…" and does not turn "06-05" into a date its own way.
            sheet.Column(i + 1).Style.NumberFormat.Format = column.Number ? "General" : "@";
            sheet.Column(i + 1).Width = column.Key == ImportColumns.Notes ? 40 : Math.Clamp(column.Header.Length + 4, 14, 30);
        }

        // The real contacts, one per row.
        for (var r = 0; r < data.Rows.Count; r++)
        {
            for (var k = 0; k < columns.Count; k++)
            {
                if (data.Rows[r].TryGetValue(columns[k].Key, out var value) && value.Length > 0)
                {
                    var target = sheet.Cell(r + 2, k + 1);
                    if (columns[k].Number && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                    {
                        target.SetValue(number);
                    }
                    else
                    {
                        target.SetValue(value);
                    }
                }
            }
        }

        // Pick-lists on a hidden sheet, one list per column of that sheet.
        var lists = workbook.AddWorksheet("Lists");
        var listRanges = new Dictionary<string, (IXLRange? Range, string Empty)>();
        for (var l = 0; l < data.Lists.Count; l++)
        {
            var list = data.Lists[l];
            lists.Cell(1, l + 1).Value = list.Name;
            for (var v = 0; v < list.Values.Count; v++)
            {
                lists.Cell(v + 2, l + 1).SetValue(list.Values[v]);
            }

            listRanges[list.Name] = (list.Values.Count > 0 ? lists.Range(2, l + 1, list.Values.Count + 1, l + 1) : null, list.EmptyMessage);
        }

        lists.Protect();
        lists.Visibility = XLWorksheetVisibility.VeryHidden;

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var range = sheet.Range(2, i + 1, lastRow, i + 1);
            if (column.List is { } listName && listRanges.TryGetValue(listName, out var source))
            {
                var rule = range.CreateDataValidation();
                rule.IgnoreBlanks = true;
                rule.ErrorStyle = XLErrorStyle.Stop;
                if (source.Range is { } values)
                {
                    rule.List(values, true);
                    rule.ErrorTitle = "Pick from the list";
                    rule.ErrorMessage = $"{column.Header}: pick one of the values in the list.";
                }
                else
                {
                    // Nothing set up yet: nothing can be typed in this column.
                    rule.TextLength.EqualTo(0);
                    rule.ErrorTitle = "Nothing to pick yet";
                    rule.ErrorMessage = source.Empty;
                }
            }
            else if (column.Number)
            {
                var rule = range.CreateDataValidation();
                rule.IgnoreBlanks = true;
                rule.Decimal.Between(-1e12, 1e12);
                rule.ErrorTitle = "A number";
                rule.ErrorMessage = $"{column.Header}: type a number.";
            }
            else if (column.MaxLength is { } max)
            {
                var rule = range.CreateDataValidation();
                rule.IgnoreBlanks = true;
                rule.TextLength.EqualOrLessThan(max);
                rule.ErrorTitle = "Too long";
                rule.ErrorMessage = $"{column.Header}: at most {max} characters.";
            }
        }

        // The header row is locked, so columns cannot be renamed, moved or deleted; rows can be added, sorted and filtered.
        sheet.Columns(1, columns.Count).Style.Protection.SetLocked(false);
        sheet.Row(1).Style.Protection.SetLocked(true);
        sheet.Protect()
            .AllowElement(XLSheetProtectionElements.FormatColumns)
            .AllowElement(XLSheetProtectionElements.FormatRows)
            .AllowElement(XLSheetProtectionElements.FormatCells)
            .AllowElement(XLSheetProtectionElements.InsertRows)
            .AllowElement(XLSheetProtectionElements.DeleteRows)
            .AllowElement(XLSheetProtectionElements.Sort)
            .AllowElement(XLSheetProtectionElements.AutoFilter)
            .AllowElement(XLSheetProtectionElements.SelectLockedCells)
            .AllowElement(XLSheetProtectionElements.SelectUnlockedCells);
        sheet.Range(1, 1, Math.Max(2, data.Rows.Count + 1), columns.Count).SetAutoFilter();

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
