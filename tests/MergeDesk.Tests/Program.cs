using ClosedXML.Excel;
using MergeDesk.Core;
using MergeDesk.Infrastructure;

var root = Path.Combine(Path.GetTempPath(), "MergeDesk-Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
Recipient R(int row, string email, string name = "Alex & <Team>") => new(row, new(StringComparer.OrdinalIgnoreCase) { ["Email"] = email, ["Name"] = name, ["Account"] = "00123" });
var data = new RecipientData(["Email", "Name", "Account"], [R(2, "alex@example.com")]);
FieldMapping[] mappings = [new("Email", "Email"), new("FirstName", "Name"), new("AccountNo", "Account")];
var template = new MergeTemplate("{{Email}}", "", "", "Statement {{AccountNo}}", "<p>{{FirstName}}</p>", "Hello {{FirstName}}", [], []);
var engine = new MergeEngine();
var batch = engine.Merge(data, mappings, template);
Check(batch.Issues.Count == 0, "valid merge with alias mappings");
Check(batch.Messages[0].Html == "<p>Alex &amp; &lt;Team&gt;</p>", "HTML encodes recipient values");
Check(batch.Messages[0].Text.Contains("Alex & <Team>"), "plain text preserves values");
Check(batch.Messages[0].Subject == "Statement 00123", "leading zeros preserved");
Check(engine.Merge(data, mappings, template with { To = "" }).Issues.Any(i => i.Area == "To"), "missing To rejected");
Check(engine.Merge(data, mappings, template with { Cc = "broken@" }).Issues.Any(i => i.Area == "CC"), "invalid CC rejected");
Check(engine.Merge(data, mappings, template with { Bcc = "no-email" }).Issues.Any(i => i.Area == "BCC"), "invalid BCC rejected");
Check(engine.Merge(data, mappings, template with { To = "a@example.com; b@example.com" }).Issues.Count == 0, "multiple bare addresses accepted");
Check(engine.Merge(data, mappings, template with { Subject = "{{Missing}}" }).Issues.Any(i => i.Area == "Subject"), "unmapped field rejected");
Check(engine.Merge(new(data.Columns, [R(2, "alex@example.com", "")]), mappings, template).Issues.Any(i => i.Area == "HTML"), "empty merge value rejected");
Check(engine.Merge(data, mappings, template with { Html = "{{broken" }).Issues.Any(i => i.Area == "HTML"), "malformed token rejected");
Check(engine.Merge(data, mappings.Concat([new FieldMapping("email", "Email")]).ToArray(), template).Issues.Any(i => i.Area == "Mapping"), "duplicate aliases rejected");
Check(engine.Merge(new(data.Columns, [R(2, "alex@example.com"), R(3, "ALEX@example.com")]), mappings, template).Issues.Any(i => i.Area == "Duplicates"), "case insensitive duplicates rejected");
Check(engine.Merge(data, mappings, template with { Subject = "Hi\r\nBcc: bad@example.com" }).Issues.Any(i => i.Area == "Subject"), "header line breaks rejected");
Check(engine.Merge(data, mappings, template with { AttachmentPatterns = [Path.Combine(root, "{{AccountNo}}.pdf")] }).Issues.Any(i => i.Area == "Attachments"), "missing patterned attachment rejected");
var attachment = Path.Combine(root, "00123.pdf");
await File.WriteAllTextAsync(attachment, "fixture attachment bytes");
batch = engine.Merge(data, mappings, template with { GlobalAttachments = [attachment], AttachmentPatterns = [Path.Combine(root, "{{AccountNo}}.pdf")] });
Check(batch.Issues.Count == 0 && batch.Messages[0].Attachments.Count == 1, "attachment patterns resolve and deduplicate");

var csv = Path.Combine(root, "recipients.csv");
await File.WriteAllTextAsync(csv, "Email,Name,Account\r\nalex@example.com,\"Alex, Smith\",00123\r\nbob@example.com,\"Bob\r\nJones\",00456\r\n");
var reader = new RecipientReader();
var imported = reader.Read(csv);
Check(imported.Recipients.Count == 2 && imported.Recipients[0].Values["Name"] == "Alex, Smith", "quoted CSV delimiter parsed");
Check(imported.Recipients[1].Values["Name"].Contains('\n'), "multiline CSV value parsed");
await File.WriteAllTextAsync(csv, "Email,Email\na@example.com,b@example.com");
try { reader.Read(csv); Check(false, "duplicate headers rejected"); } catch (InvalidDataException) { Check(true, "duplicate headers rejected"); }
await File.WriteAllTextAsync(csv, "Email,Name\na@example.com");
try { reader.Read(csv); Check(false, "ragged CSV rejected"); } catch (InvalidDataException) { Check(true, "ragged CSV rejected"); }
var xlsx = Path.Combine(root, "recipients.xlsx");
using (var workbook = new XLWorkbook())
{
    var sheet = workbook.AddWorksheet("Recipients");
    sheet.Cell(1, 1).Value = "Email"; sheet.Cell(1, 2).Value = "Account";
    sheet.Cell(2, 1).Value = "alex@example.com"; sheet.Cell(2, 2).Value = 123;
    sheet.Cell(2, 2).Style.NumberFormat.Format = "00000";
    workbook.AddWorksheet("Second").Cell(1, 1).Value = "Name";
    workbook.Worksheet(2).Cell(2, 1).Value = "Jordan";
    workbook.SaveAs(xlsx);
}
Check(reader.Read(xlsx).Recipients[0].Values["Account"] == "00123", "Excel formatted account numbers preserved");
Check(reader.Read(xlsx, 2).Recipients[0].Values["Name"] == "Jordan", "Excel worksheet selection works");

var sender = new DryRunMailSender(Path.Combine(root, "export"));
var destination = await sender.SendAsync(batch.Messages[0]);
var eml = Directory.GetFiles(destination, "*.eml").Single();
var mime = await File.ReadAllTextAsync(eml);
Check(mime.Contains("multipart/") && mime.Contains("00123.pdf"), "dry run MIME includes attachment");
Check(File.Exists(Path.Combine(destination, "message.json")) && File.Exists(Path.Combine(destination, "body.html")) && File.Exists(Path.Combine(destination, "body.txt")), "dry run exports bodies and metadata");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await sender.SendAsync(batch.Messages[0], cancelled.Token); Check(false, "cancelled export stops"); } catch (OperationCanceledException) { Check(true, "cancelled export stops"); }
var repository = new SqliteRunRepository(Path.Combine(root, "history.db"));
var record = new RunRecord("fixture-run", DateTimeOffset.UtcNow, destination, [new(2, "alex@example.com", "Test", "Exported", destination, DateTimeOffset.UtcNow)]);
await repository.SaveAsync(record);
var history = await repository.LoadAsync();
Check(history.Count == 1 && history[0].Results[0].Recipient == "alex@example.com", "SQLite history round trip");
await repository.SaveAsync(record);
Check((await repository.LoadAsync()).Count == 1, "SQLite run upsert avoids duplicates");
await repository.SaveAsync(record with { Id = "second-run" });
await repository.ClearAsync();
Check((await new SqliteRunRepository(Path.Combine(root, "history.db")).LoadAsync()).Count == 0, "history clearing persists across repository restart");
Check(File.Exists(eml), "history clearing preserves exported messages");
await repository.SaveAsync(record);
Check((await repository.LoadAsync()).Count == 1, "new runs can be saved after clearing history");
Console.WriteLine($"{passed} checks passed. Fixtures: {root}");
