# MergeDesk user guide

## Start here

Load recipients → compose → validate and preview → connect → choose dry run, drafts or sending. Start with a small test batch addressed to yourself. Open project restores a saved template and file paths. Save project stores your mappings, templates and embedded pictures; it does not copy recipient files or attachments. Keep those files available. Press F1 or Help at any time. Hover over an option for an explanation. Tick the recipients to include and send one test to yourself before the full batch. Autosave recovery is available at startup; it never restarts a send.

## Recipients and mapping

Load CSV / Excel accepts CSV and .xlsx workbooks. Save old .xls files as .xlsx first. Worksheet is numbered from 1; change it and click Reload. Reload also reads changes made to the source file. Use unique column headings. In Field mapping, edit the Merge field name and select its Source column. Add merge field creates another mapping; select a mapping row and press Delete to remove it. A field named Email is used as {{Email}}. Choose names from your own data; AccountNo is only an optional example, never a required column. Reload now preserves field mappings. If saved recipient data has changed, all rows are unticked until reviewed.

## Addresses and subject

To is required. CC and BCC are optional. Separate multiple addresses with commas or semicolons. CC is visible to recipients; BCC hides those addresses from other recipients. Each box accepts literal addresses and {{Field}} tokens. In Subject, place the cursor, choose a field in the subject picker, then click Insert merge field in subject. Selected text is replaced. This picker is separate from the body picker. Replace the starter example text and signature before sending.

## Writing and formatting

Write directly in Email body; HTML is produced automatically. Select text before changing font, size, colour, bold, italic or underline. Left, Centre and Right align the current paragraph. Bullets and Numbering create lists. Undo and Redo reverse or restore edits. Ctrl+B, Ctrl+I and Ctrl+U also work. Choose a mapped field and click Insert merge field to insert it in the body. Use email text for plain-text version copies the current body text into the separate plain-text box; update it again after later edits. Advanced HTML source is optional and intended for experienced users; visual editing may simplify custom HTML layouts. Open Templates & signatures in Compose. Choose a template then Use template to replace message fields, mappings and attachment paths; recipients and the account stay unchanged. Check mappings and validate afterwards. Enter a name and Save current message; a selected template is updated. Save as new clears the selection so you can save under another name. New signature opens a visual editor with optional logo and a separate plain-text alternative. Select a signature and Append signature to add both versions at the end. Edit updates the saved signature, not copies already inserted. Remove asks for confirmation and keeps the saved file in the local library trash. Items are stored on this computer under %LOCALAPPDATA%/MergeDesk/library; attachment paths are references, not copies.

## Images and attachments

Insert image adds a PNG, JPEG or GIF at the body cursor. Each image may be up to 2 MiB. Pictures are saved within the project and embedded in outgoing messages, so recipients do not need access to your computer. Preview may show only the first GIF frame. Add global files attaches the same files to every message. Clear files removes the global list. Per-recipient paths accepts one absolute path per line, with optional {{Field}} tokens. For example C:\Letters\{{Reference}}.pdf needs a mapped Reference field and an existing file for each row. Use only field names present in your data. Changing attachments can change the duplicate-send identity. Open Picture settings, choose the picture, enter Width (pixels) and Alternative text, then Apply size and description. Height follows width automatically. Original size restores its source width (maximum 1200 pixels). Picture left, centre and right align its entire paragraph, including nearby text; put a picture on its own line for independent alignment. Check the merged preview.

## Validation and preview

Validate all checks included recipients for missing or invalid email addresses, unresolved or empty merge fields, missing attachments and duplicate To recipients. Fix every issue before running. Row 0 means an issue with the template or batch. Email validation checks address format, not whether a mailbox exists. Select a Source row and click Refresh preview to inspect a merged message. Message details shows addresses, subject, text and files. Email preview shows supported formatting and pictures. Outlook may render HTML differently: review a real draft before sending a large batch.

## Classic and new Outlook

Classic Outlook: configure your mailbox in Outlook, select the classic connection, click Connect to classic Outlook and choose Account. Use the intended Outlook profile. New Outlook: use the Microsoft mailbox connection for Microsoft 365 or Outlook.com accounts. Enter the application client ID from your administrator or app registration, and Tenant (usually common). No client secret is needed. Sign in with Microsoft. Enable direct sending requests Mail.Send permission; reconnect after changing it. Registration details are in README.md. A Gmail account displayed in new Outlook is not a Microsoft mailbox. Disconnect clears the active connection; reconnect after restarting MergeDesk.

## Dry run and drafts

Export dry run creates local .eml, HTML, text and message details without sending or requiring an Outlook connection. Choose a folder, then copy Local results folder into File Explorer to inspect the output. Dry-run .eml files use a placeholder sender. Create Outlook drafts prepares messages in the selected mailbox Drafts folder after confirmation. Open Outlook, review them and send manually if desired. Creating drafts again can create another set. Messages sent manually in Outlook are not recorded by MergeDesk’s direct-send ledger. Exports and drafts include only ticked rows, including ticked rows hidden by search.

## Sending and cancellation

Send emails requires a connected account and successful validation. The final confirmation shows the batch and sending account. Seconds between sends accepts 1–60; the default is 3. Only confirm when you are ready. Cancel operation stops future work; it cannot recall a message already submitted. Classic Outlook may display prompts that need attention in Outlook. Submitted to Outlook or accepted by Microsoft is not proof of delivery: check Outbox, Sent Items and returned delivery errors. Mailbox sending limits still apply. Batch sends use only ticked rows. Send test email uses one source row and a separately entered address.

## Results and safe retries

Refresh history reloads saved runs. Select a run to view each row’s status and Detail / location. History times are shown in UTC. A stopped batch leaves later rows unattempted. Direct sending records submitted messages locally and skips matching messages on a later run. Definitely unsent saved drafts can be reused. An uncertain submission requires review of Outlook Drafts, Outbox and Sent Items before retrying; do not delete the send ledger to bypass this protection. Changing the account, message or attachment contents may create a different message identity. Draft-only repeats and manual Outlook sends are outside this protection. Clear history removes ALL saved runs and displayed results after confirmation; it cannot be undone. Exported files, local run logs, Outlook messages and duplicate-send protection are kept. Clear history is unavailable during an operation. Refresh history reads the current saved list again.

## Troubleshooting and shortcuts

If a merge field is unresolved, check its spelling and mapping, then reload changed data. If an attachment is missing, check the merged absolute path and file permissions. If an account is unavailable, check the Outlook profile and reconnect to the intended account. If a batch waits, look for Outlook prompts and read the row detail before retrying. Save your project before updating the app. F1 opens Help; Ctrl+B / Ctrl+I / Ctrl+U format body text; Ctrl+Z / Ctrl+Y undo and redo; Delete removes a selected mapping row. Tab moves between controls. The video is captioned and can be paused; no audio is required.

## Choosing recipients

Every new import starts with all rows ticked. The Include tick controls drafts, dry-run exports and batch sending. Search recipients searches all source values without changing the selection: ticked rows hidden by search are still included. Include shown and Exclude shown change only visible rows. Exclude all unticks the complete list, including hidden rows. To choose a small group, use Exclude all, search for the group, then Include shown. Clear the search to review the complete list and included/excluded counts before running. Source row numbers remain stable after search or sorting. Saved projects and autosave keep your ticks. Unchanged Reload preserves ticks and mapping aliases. If the saved source data changes, all rows are unticked until you review and choose them again.

## Sending a test email

Connect an account with direct sending enabled, then open Results. Choose a Source row and enter one email address you control in Your test email address. Send test email validates that row and asks for confirmation before sending ONE real email. The body, personalised subject, pictures and attachments come from the selected row; the subject starts with [TEST]. Source To, CC and BCC are replaced, so only your test address receives it. You can test an excluded row. Review the selected row’s information and attachments before confirming. A test has a separate history label and send ledger, so it cannot cause a production message to be skipped. An identical already submitted test is skipped on repeat; change the content or test address for another test. Uncertain outcomes still need review in Outlook. No automatic resend is performed.

## Autosave and recovery

Workspace changes are autosaved locally after about two seconds of idle editing. The footer shows pending, saved or failed status. Closing normally flushes pending changes before exiting. Save project remains available to keep a named project or a backup. At startup, an inactive autosaved workspace can be restored using Restore autosave; choose Start fresh to dismiss it. Recovery keeps the template, embedded pictures, mappings, source file path, recipient ticks, send interval and test address. It does not copy the recipient file or ordinary attachments, reconnect Outlook, resume sending, or remove duplicate-send protection. If the source file is missing, the template and mappings remain available; restore the file and Reload. If source data has changed, review and tick recipients again. Recovery files are stored under %LOCALAPPDATA%\MergeDesk\recovery. Separate app instances have separate recovery files; currently open workspaces are not offered for recovery. An unexpected crash may lose edits made since the latest completed autosave. If autosave fails, save a named project and read the footer error.
