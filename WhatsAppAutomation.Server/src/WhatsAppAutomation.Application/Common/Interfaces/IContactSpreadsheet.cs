namespace WhatsAppAutomation.Application.Common.Interfaces;

/// <summary>One data row of an import file: its row number in the file and its cells by column key.</summary>
public sealed record SpreadsheetRow(int RowNumber, IReadOnlyDictionary<string, string> Cells);

/// <summary>What was read from an import file, or why it could not be read.</summary>
public sealed record SpreadsheetReadResult(IReadOnlyList<SpreadsheetRow> Rows, IReadOnlyList<string> MissingColumns, string? Problem);

/// <summary>Reads contact import files (.xlsx or .csv) and writes the import template.</summary>
public interface IContactSpreadsheet
{
    /// <summary>
    /// Reads the first sheet (or the "Contacts" sheet) of an .xlsx file, or a UTF-8 .csv file. Headers are matched
    /// to <see cref="Features.Contacts.Import.ImportColumns"/> ignoring case, spaces and "*". Other columns are kept
    /// too (by the same key), for the user's occasion columns.
    /// </summary>
    SpreadsheetReadResult Read(Stream file, string fileName, int maxRows);

    /// <summary>
    /// An .xlsx with an Instructions sheet and a Contacts sheet holding the user's real contacts (one row each), their
    /// own occasion and field columns after the standard ones, and dropdowns. Importing it again updates and adds.
    /// </summary>
    byte[] CreateWorkbook(Features.Contacts.Import.ContactsWorkbook data);
}
