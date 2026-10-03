# MergeDesk — first-pass Windows mail merge

A .NET 8 WPF application for Windows 10 (22H2 recommended) and Windows 11. MVVM presentation, a UI-independent merge engine, Excel/CSV import, SQLite history and visual email composition. **Export a local dry run, create Outlook drafts, or send directly through classic Outlook or Microsoft 365/Outlook.com after confirmation.**

## Recipient selection, test emails and recovery

Recipients has an Include tick for every row, literal text search across all columns, Include shown / Exclude shown / Exclude all, and included/excluded/shown counts. Search affects visibility only; hidden ticked rows remain in the batch. Validation, drafts, export and direct sending operate on the included snapshot. Preview and test sending can use any source row. Unchanged Reload keeps mappings and ticks. Projects record a fingerprint of the source data; changed sources reset every tick so the user must review the list again. Older project files remain readable.

In Results, choose a source row and enter a single test address. Send test email merges the row with To redirected and CC/BCC cleared before validation, prefixes the subject with `[TEST]`, and asks for explicit confirmation. Images and attachments remain included. It uses the same staged delivery and recovery machinery with a separate `test-send-ledger.db`; test submissions cannot consume production-send claims. Matching previously submitted tests are skipped, and uncertain tests require review. Provider calls in automated tests are simulated; the user must confirm a small real test on the intended mailbox.

Workspace edits autosave after a two-second debounce to atomic, per-session files under `%LOCALAPPDATA%/MergeDesk/recovery`. Exclusive leases prevent recovering active app instances or two instances selecting the same recovery file. Startup presents Restore autosave / Start fresh. Restore does not sign in, send mail, or restart an interrupted batch. Missing sources retain template, mappings and saved selection; changed sources require new inclusion choices. Normal closure flushes pending saves. Failures stay visible in the footer and allow manual project saving. Recipient files and ordinary attachments remain external; embedded pictures are saved in the template. A crash can lose changes since the last completed save.

Focused checks:

```powershell
dotnet run --project tests/MergeDesk.DraftTests -c Release -- --workspace
```

## Help and walkthrough

Click **Help (F1)** or press **F1** to open the searchable user guide. Search matches topic titles and instructions. Help remains available during a batch. Every main option and visual formatting control has a tooltip, including disabled controls. Select a topic for instructions, troubleshooting and keyboard shortcuts.

**Watch walkthrough video** opens the bundled `HelpAssets/MergeDesk-Walkthrough.mp4` in the default video player. This is a 4 minute 26 second, captioned walkthrough with cheerful instrumental background music using application screenshots. Pause or replay any step. The complete application ZIP must be extracted, including HelpAssets. A timestamped transcript is alongside the video, and `USER-GUIDE.md` provides the written guide.

The walkthrough covers importing and mapping data, subject and body fields, formatting, pictures, attachments, validation, both Outlook connections, local export, drafts, sending and results. Demo addresses are examples only. The recording does not sign into a mailbox or send messages.

To repeat the focused help/search checks and render demo screenshots without a real mailbox:

```powershell
dotnet run --project tests/MergeDesk.DraftTests -c Release -- --help ./help-screens
```

## Build and run

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows, or use Visual Studio 2022 with the .NET desktop development workload. The SDK includes the desktop runtime needed for development. Open `MergeDesk.sln`, select `MergeDesk.App` as the startup project, and press F5; alternatively run these commands from the extracted project folder:

```powershell
dotnet restore MergeDesk.sln
dotnet build MergeDesk.sln -c Release
dotnet run --project src/MergeDesk.App -c Release
```

Run the executable regression suite (no test framework installation required):

```powershell
dotnet run --project tests/MergeDesk.Tests -c Release
dotnet run --project tests/MergeDesk.EditorTests -c Release
dotnet run --project tests/MergeDesk.DraftTests -c Release
```

Create a portable x64 distribution that includes .NET:

```powershell
dotnet publish src/MergeDesk.App -c Release -r win-x64 --self-contained true -o publish/win-x64
```

Copy the entire published folder to the target Windows computer. This first pass does not include an installer, signing or automatic updates. For ARM64 use `-r win-arm64`; for x86 use `-r win-x86`. Those architectures require their own compatibility testing.

## Try the workflow

1. In **Recipients**, load `samples/recipients.csv` or `samples/recipients.xlsx`. The first non-empty row must contain unique column headers. For Excel, choose the worksheet number (1-based) and click Reload. CSV uses UTF-8, comma separators, quoted fields and multiline quoted values. Legacy `.xls` files must be saved as `.xlsx` first.
2. Review **Field mapping**. Headers initially map to the same field names. Rename a merge field or select a different source column. Add aliases with Add merge field; select a mapping row and press Delete to remove it. Match your template tokens to these names. Field lookup ignores case.
3. In **Compose**, set To/CC/BCC and Subject using tokens such as `{{Email}}` and `{{FirstName}}`. Multiple addresses use commas or semicolons; use bare addresses without display names. Write the email in the visual **Email body** editor: select text and choose a font, point size, bold, italic, underline, colour, alignment, bullets or numbering. The app writes the HTML automatically. Ctrl+B, Ctrl+I and Ctrl+U work too. Choose a mapped field and click **Insert merge field** to personalise text without typing tokens. Use **Use email text for plain-text version** to generate the text alternative, then edit it separately if needed. Either body can be used alone; when both are supplied the exported MIME message has alternative bodies. **Advanced HTML source** is optional and collapsed by default.
4. Add global files or enter one absolute attachment pattern per line, for example `C:\Statements\{{AccountNo}}.pdf`. All files must exist and be readable. Token values are treated as path text; the app does not invent filenames or search for files. Use trusted account/path columns. Identical resolved paths are included only once per message.
5. In **Validate & preview**, validate the complete batch and inspect individual source rows. Any issue blocks the whole batch. Row 0 identifies a batch-wide issue. Duplicates are detected across To addresses, ignoring case; correct or remove duplicate source rows and reload. Repeated CC/BCC values across rows are allowed. The app does not automatically remove recipients.
6. For a local test, in **Results** click **Export dry run** and choose an output folder. A unique batch subfolder is created, then a `row-000002` folder for each source row. Cancel stops future exports and logs the remaining rows as cancelled. Open the output path in File Explorer to inspect messages.
7. To prepare messages in Outlook, use **Outlook connection** as described below. In **Results**, click **Create Outlook drafts** and confirm the recipient count and destination. Then open that account's **Drafts** folder in Outlook, review each message and click **Send**. Drafts include To, CC, BCC, subject, the formatted body (or plain text when HTML is empty), and embedded global/per-recipient attachments. Outlook and Graph store one body; the separately authored plain-text alternative is retained in the local dry-run export.

Save/Open project preserves template, mappings, source path, worksheet and attachment configuration. Account sign-in is separate from the project file.

The starter subject and body are generic editable examples, without required `AccountNo` or `FirstName` fields. Replace the square-bracket instructions and signature with your message before sending. Use **Insert merge field** to choose fields actually present in your data. To defaults to `{{Email}}`; map your email-address column to `Email` or change To to match your mapping. Existing saved projects keep their original templates.

### Subject fields and body images

For a personalised subject, place the cursor in **Subject** (or select text to replace), choose a field from the dropdown directly beneath it, and click **Insert merge field in subject**. The body editor's field button remains separate so it is clear where the token will be inserted.

To add a logo, photo or signature image, place the cursor in the visual body and click **Insert image**. Choose a **PNG, JPEG or GIF** file, up to **2 MiB** each and **10 MiB** of unique images in total. The image is embedded in the project/email; removing the original file later does not break it. Images initially use their native width up to 600 pixels, keeping their proportions. Advanced source users can change the width attribute; the editor maintains aspect ratio. GIF preview shows its first frame; animation in received mail depends on the email client. Delete an image by selecting it in the editor and pressing Delete/Backspace.

The visual editor and recipient preview display embedded images locally, without fetching web URLs. At merge time, image bytes become inline attachments referenced by content ID in the HTML. Classic Outlook, Microsoft drafts/sending and exported `.eml` messages carry those bytes. The dry-run `body.html` contains embedded image data for standalone local viewing. Plain-text alternatives do not include images. Save the project to preserve the pictures, and test a small email in your intended client before a larger batch, as client image policies/layout can differ. Prior duplicate-send identities for messages without pictures remain compatible.

## Send directly from MergeDesk

1. Replace the starter text, configure recipients and attachments, and validate/preview your batch. Test a small batch of addresses you control first.
2. Connect to classic Outlook. For a Microsoft mailbox, add delegated **Mail.Send** to your app registration, tick **Enable direct sending**, then sign in again. This requests `Mail.ReadWrite` and `Mail.Send`. Leave the checkbox off for a draft-only Microsoft connection.
3. In **Results**, set **Seconds between sends** (1–60, default 3), then click **Send emails**. Review the account, maximum recipient count and interval in the final confirmation. Choosing No makes no mailbox changes.
4. Watch per-recipient progress. **Submitted to Outlook** means Outlook accepted the message; offline Outlook may keep it in Outbox. **Accepted by Microsoft** means Graph accepted submission. Neither status proves recipient delivery. Check Outbox, Sent Items and any non-delivery reports.

Messages are prepared as drafts, recorded locally, then submitted one at a time. Cancel stops future submissions; it cannot recall an in-flight send. Outlook automation may wait for a profile/security dialog; check Outlook if the operation does not progress. Organization sending quotas still apply: pacing is not a guarantee that a provider will allow a batch.

Microsoft HTTP 429 stops the batch and records Retry-After (or a one-minute fallback). A later confirmed run is blocked until that delay expires, then can reuse the unsent draft. Other definite client rejections also retain the prepared draft and stop the batch: resolve the permission, account or message-limit problem first. There are no immediate automatic send retries. Classic Outlook errors with an uncertain outcome require checking Outlook before recovery.

### Recovery and duplicate protection

`%LOCALAPPDATA%\MergeDesk\send-ledger.db` stores a durable send record, separate from the results journal. The identity includes the selected account, To/CC/BCC, subject, active body and attachment names/bytes; source row numbers and ignored plain-text alternatives do not change that identity. Identical messages previously submitted through this feature are shown as **Already submitted** and skipped, including after restart. Concurrent app instances cannot claim the same identity for sending.

After cancellation or a definite rejection, reload the **same** recipient data/template/attachments, reconnect the **same** account/connection and confirm Send emails again. Prepared, definitely unsent messages reuse their saved draft; confirmed submissions are skipped. Check any reused drafts before confirming, as edits made in Outlook are not overwritten. Only unsent messages in the selected classic account's Drafts folder can be submitted.

A persisted **Drafting** or **Submitting** record means an operation was interrupted or its outcome is unknown. The app blocks automatic resending and shows **Needs review**. Check Drafts, Outbox, Sent Items and the result/draft identifiers. Do not delete the send ledger to bypass this protection. Automatic reconciliation or clearing of uncertain records is not included; after checking the outcome, a deliberate resend can be handled manually in Outlook. There is no exactly-once delivery guarantee from either provider.

Protection applies to sends recorded by this app on this computer. It cannot detect emails sent manually, draft-only batches sent later in Outlook, other computers, deleted ledger files, aliases treated as different sending accounts, or deliberately changed message content. Previously created test drafts are not automatically promoted or marked as submitted. Check earlier drafts/sent mail before switching your test batch to direct sending.

Reference: [Microsoft Graph sending a draft](https://learn.microsoft.com/en-us/graph/api/message-send?view=graph-rest-1.0), [Graph throttling](https://learn.microsoft.com/en-us/graph/throttling), and [classic Outlook sending account](https://learn.microsoft.com/en-us/office/vba/api/outlook.mailitem.sendusingaccount).

## Classic Outlook on this computer

1. Install **classic Outlook** and configure its mail profile first. Open Outlook and check that the intended account and its Drafts folder are available.
2. In **Outlook connection**, select **Classic Outlook on this computer**, click **Connect to classic Outlook**, and select the account shown in the account list.
3. Validate and preview, then use **Create Outlook drafts** in Results. Review and send the saved messages from the selected account's Drafts folder.

This option uses classic Outlook's local automation interface. New Outlook does not provide that interface; use the Microsoft connection below. No Microsoft app registration is needed for the classic option. It uses the account's delivery store and explicitly sets the sending account. Accounts without an address or delivery store are omitted. Outlook/profile/security prompts must be handled by the user; organization settings may restrict automation. MergeDesk releases its Outlook references when disconnected and does not close Outlook. This portable release is x64; a different architecture should be published and tested if your Office environment requires it.

## Microsoft 365 / Outlook.com: new or classic Outlook

This option creates drafts directly in the signed-in **Microsoft-hosted mailbox**. They appear in new Outlook, classic Outlook or Outlook on the web when that same mailbox is open. A Microsoft 365 Exchange Online or Outlook.com mailbox is required; adding a Gmail/IMAP account to new Outlook does not make it accessible through this connection. This first pass uses Microsoft's public global cloud and the user's own mailbox, without shared-mailbox support.

One-time setup by you or your Microsoft 365 administrator:

1. In the [Microsoft Entra admin center](https://entra.microsoft.com), create an **App registration**, for example `MergeDesk Desktop`. For both work/school and personal accounts, select **Accounts in any organizational directory and personal Microsoft accounts**. For an organization-only deployment, choose the account audience required by your administrator.
2. In **Authentication**, add the **Mobile and desktop applications** platform with redirect URI **`http://localhost`**. This is a public desktop client using the system browser. Do not create or paste a client secret.
3. Under **API permissions**, add **Microsoft Graph â†’ Delegated permissions â†’ Mail.ReadWrite** for drafts, plus **Mail.Send** if direct sending is required. MergeDesk requests Mail.Send only when Enable direct sending is checked. Your administrator may need to approve consent under your organization's policy. `Mail.ReadWrite` grants mailbox read/write access even though MergeDesk uses it for checking the Drafts folder and creating drafts/attachments.
4. Copy the **Application (client) ID** from Overview. In MergeDesk select **Microsoft 365 / Outlook.com**, paste that ID, and enter the tenant: `common` for the mixed account audience, `organizations` for work/school accounts, `consumers` for personal accounts, or your tenant's GUID for a single-tenant registration. The registration's supported account audience must match.
5. Click **Sign in to Microsoft**, select the intended account in the browser and complete consent. MergeDesk checks access to the Drafts folder and shows the destination. Review that destination before creating drafts.

The client ID, tenant and direct-sending preference are saved to `%LOCALAPPDATA%\MergeDesk\connection-settings.json`; these are nonsecret settings. Access/refresh tokens remain in MSAL's in-memory cache; MergeDesk does not save passwords or tokens to disk. Reconnect after restarting the app. Disconnect clears MergeDesk's connection; it does not sign the browser out of Microsoft. If a session needs fresh consent/sign-in during a batch, reconnect after reviewing any existing drafts.

Files below 3 MiB use a normal attachment request; larger files use an upload session with chunks. Files above 150 MiB are blocked before draft creation. Your mailbox's message/attachment limits may be lower, and the combined message can still exceed them. An attachment failure after draft creation leaves an incomplete draft marked **Needs review**. Review, finish or remove that draft yourself before starting another batch.

Reference: [Microsoft Graph draft creation](https://learn.microsoft.com/en-us/graph/api/user-post-messages?view=graph-rest-1.0), [attachment upload sessions](https://learn.microsoft.com/en-us/graph/api/attachment-createuploadsession?view=graph-rest-1.0), and [MSAL system browser sign-in](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/using-web-browsers).

## Export and logging

Every successful row contains:

- `*.eml`: full MIME email, including embedded attachment bytes and HTML/text alternatives. The From address is `dry-run@example.invalid`. BCC is represented in the local metadata and pickup headers as applicable. Live providers use the configured account and separate To/CC/BCC recipients.
- `body.html` and `body.txt`: merged bodies.
- `message.json`: To/CC/BCC, subject, bodies, source row and attachment paths.

The batch root contains `results.json` with success/failure/cancellation details and timestamps. Partial row output may remain after a failed or cancelled export; rely on the result status. Run history is stored in `%LOCALAPPDATA%\MergeDesk\history.db`; the last 100 runs are loaded on startup. History remains available after restart. Selecting a previous run displays its results and output location. Project files do not copy recipient data; the original source file must remain accessible.

Draft batches have local journals under `%LOCALAPPDATA%\MergeDesk\runs\drafts-...\results.json`, plus SQLite history. Each attempt is recorded **before** writing to the mailbox, and its result is saved after the operation. Results distinguish **Draft created**, **Failed**, **Needs review**, **Cancelled** and **Not attempted**, with draft/operation identifiers. If the app closes unexpectedly, a **Creating draft** row in the saved journal means the outcome is unknown: check Outlook first. A logging failure stops the batch.

Result logging runs on a background worker and the batch waits at most 20 seconds for each journal/database save. A logging timeout stops further mailbox operations, preserves confirmed draft results on screen, and explains why the remaining rows were not attempted. The status line identifies whether the app is saving a log or creating an Outlook draft. A timed-out disk/database operation may finish later; the batch does not automatically resume. Outlook's own automation calls may still wait for an Outlook/profile/security dialog, so check Outlook if the status says it is creating a draft.

There are no automatic retries or automatic deletion of partial drafts. An uncertain network/save outcome stops remaining rows. Cancel stops future attempts; an in-flight request may still have created a draft and will require review. Every new batch creates new drafts, including recipients from a previous batch: **re-running does not deduplicate against your mailbox**. Review previous results and Drafts before repeating a batch. The operation identifiers support investigation, not exactly-once delivery. Export dry run remains entirely local and requires no account connection.

Exports, projects, journals and the database contain recipient information and are ordinary local files. Draft creation copies email content and attachment bytes into the selected mailbox. Choose storage appropriate for your data.

## Validation rules and preview limits

Validation checks required To and Subject, bare email syntax, invalid CC/BCC, header line breaks, unmapped/empty fields, malformed/unresolved tokens, duplicate To recipients, invalid mappings, missing or unreadable attachments and a missing body. Email syntax validation does not confirm that a mailbox exists. Subject and recipient data are not silently changed. Blank merge values used by templates are errors; there is no optional-field syntax yet.

Recipient values are HTML-encoded in the HTML body and used literally elsewhere. Place HTML tokens in text content; templated HTML attributes, URLs and arbitrary HTML from recipient columns are outside this MVP. A merge token split across differently formatted text is exported as one intact token, using the formatting of its first character. The visual editor and safe WPF preview share a renderer for fonts, point sizes, bold, italic, underline, text colours, paragraph alignment, lists and embedded images. They do not execute scripts, follow links or load remote images. Existing simple HTML templates load into the visual editor. Complex source templates (tables, remote images, external CSS and custom layouts) display a notice: viewing leaves the original source unchanged, but visual editing simplifies unsupported formatting. Advanced source edits apply when the text box loses focus. Pasting uses plain text; use Insert image for picture files. Plain-text generation is an explicit button action, so separately authored text is not overwritten while editing the email. Fonts may be substituted by the recipient's email client; inspect the exported `.eml` in your intended client for final rendering. Global attachments are literal paths; token expansion applies only to per-recipient patterns.

## Structure and extension points

```text
src/MergeDesk.Core            Models, validation, tokens and interfaces
src/MergeDesk.Infrastructure  CSV/XLSX, SQLite history/send ledger, dry run, Graph drafts/sending
src/MergeDesk.App             WPF, MVVM, rich editor, MSAL connection and classic Outlook STA
tests/MergeDesk.Tests         Import, merge, validation, MIME and SQLite checks
tests/MergeDesk.EditorTests   Windows rich editor, HTML round trips and binding checks
tests/MergeDesk.DraftTests    Mock HTTP, uploads, journals, recovery, sending and UI flow checks
samples                      Illustrative recipient fixtures
```

`IMailSender` is the delivery boundary. `DryRunMailSender` uses `SmtpClient` only in pickup-directory mode. `IMailDraftCreator` prepares messages; `IMailDelivery` adds a staged SubmitDraftAsync boundary. `GraphMailSender` sends an existing draft through Graph; the classic adapter uses a dedicated STA dispatcher, checks the selected sending account and draft folder, then calls Send. The UI uses `LiveSendRunner` with `SqliteSendLedger` so draft creation and submission have separate durable states; it does not call the unjournaled convenience SendAsync methods. `DraftBatchRunner` remains the draft-only flow. `IDraftConnector` and `IConnectionSettingsStore` keep sign-in and desktop details testable independently of the merge engine. SMTP delivery and automatic mailbox reconciliation remain future work.

`IRunRepository` separates persistence from presentation; the current SQLite adapter stores version-1 run snapshots as JSON payloads with a sortable timestamp. Templates use portable JSON project files. A future SQLite project store can be added without changing the merge engine. No Microsoft Office installation is required for Excel import (ClosedXML reads `.xlsx`). The core and infrastructure target `net8.0`; only the desktop app targets `net8.0-windows`.

Large imports and per-recipient exports run off the UI thread. Batch validation and populating the grids are currently in memory; this first pass is intended for modest office batches, not million-row datasets. Workbook formulas use saved/cached values where available; recalculate and save the workbook in Excel before import. Changing source files after import requires Reload. Attachment files can change between validation and export; resulting failures are logged per recipient.

Dependencies: ClosedXML 0.104.2, Microsoft.Data.Sqlite 8.0.11, HtmlAgilityPack 1.13.0 and Microsoft.Identity.Client 4.90.1, restored from NuGet. The HTML parser is used entirely locally; it does not fetch web pages. Versions are pinned for reproducibility; review updates before production deployment.

Build and automated regression checks cover the merge engine, editor, mock Graph requests/uploads/submissions, journals, durable send claims/recovery and view-model connection/draft/send flow. Direct classic Outlook sending and Microsoft sign-in/submission require acceptance testing on your configured computer/account; no real messages were sent by this build's tests. Start with a small batch of your own test addresses and review every field/attachment before sending.


Templates and signatures: expand **Templates & signatures** in Compose. Templates keep message fields, mappings and attachment paths. Signatures have their own visual editor and optional logos. **Picture settings** changes embedded image width, alternative text and paragraph alignment. Library files stay in `%LOCALAPPDATA%/MergeDesk/library` across application upgrades.


New: Results has Clear history, with confirmation. It clears all saved runs and displayed results while keeping exports, local run logs, Outlook messages and duplicate-send protection. MergeDesk now has its own logo and Windows icon.

## Licence

MergeDesk is free to use under the MIT licence. Copyright (c) 2026 Paul Woodhouse. You may use, modify and redistribute it, including commercially, provided you preserve the licence and copyright notice. See `LICENSE.txt`. Third-party components retain their own licences; the application includes `THIRD-PARTY-NOTICES.md` and `ThirdPartyLicenses/`.
