using ClosedXML.Excel;
using Microsoft.VisualBasic.FileIO;
using MergeDesk.Core;

namespace MergeDesk.Infrastructure;

public sealed class RecipientReader : IRecipientReader
{
    public RecipientData Read(string path, int worksheet = 1)
    {
        var rows = new List<(int Row, string[] Values)>();
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".csv":
                using (var parser = new TextFieldParser(path, System.Text.Encoding.UTF8, true))
                {
                    parser.SetDelimiters(",");
                    parser.HasFieldsEnclosedInQuotes = true;
                    parser.TrimWhiteSpace = false;
                    while (!parser.EndOfData)
                    {
                        var row = checked((int)parser.LineNumber);
                        rows.Add((row, parser.ReadFields() ?? []));
                    }
                }
                break;
            case ".xlsx":
                using (var book = new XLWorkbook(path))
                {
                    if (worksheet < 1 || worksheet > book.Worksheets.Count) throw new InvalidDataException("Worksheet number is outside this workbook.");
                    var sheet = book.Worksheet(worksheet);
                    var range = sheet.RangeUsed();
                    if (range != null)
                    {
                        var width = range.LastColumn().ColumnNumber();
                        for (var row = range.FirstRow().RowNumber(); row <= range.LastRow().RowNumber(); row++)
                            rows.Add((row, Enumerable.Range(1, width).Select(c => sheet.Cell(row, c).GetFormattedString()).ToArray()));
                    }
                }
                break;
            default: throw new InvalidDataException("Choose a UTF-8 comma-delimited CSV or .xlsx workbook. Save legacy .xls files as .xlsx first.");
        }
        if (rows.Count == 0) throw new InvalidDataException("The source file is empty.");
        var headers = rows[0].Values.Select(v => v.Trim().TrimStart('\uFEFF')).ToArray();
        if (headers.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Every column needs a header in the first row.");
        if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new InvalidDataException("Column headers must be unique (ignoring case).");
        var recipients = new List<Recipient>();
        foreach (var (row, values) in rows.Skip(1))
        {
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            if (values.Length != headers.Length) throw new InvalidDataException($"Row {row} has {values.Length} cells; expected {headers.Length}.");
            recipients.Add(new(row, headers.Zip(values).ToDictionary(p => p.First, p => p.Second, StringComparer.OrdinalIgnoreCase)));
        }
        if (recipients.Count == 0) throw new InvalidDataException("The file has headers but no recipients.");
        return new(headers, recipients);
    }
}
