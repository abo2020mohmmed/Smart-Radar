# Changes

## 0.2.0

- Added a Pro-only aggressive preset with typed, reviewable operations and explanations; revalidate its canonical operations against fresh mounted-image inventory immediately before execution.
- Preserve Microsoft Store and dependencies, frameworks and printing; select only cataloged bloat/Xbox packages and optional features/services.
- Add scoped OneDrive setup/autostart cleanup and explicitly report protected/remaining files.
- Add offline language-capability/LXP/language-pack selection, preserving Arabic, English, the base language and shared fonts. Unknown base language and pending servicing block language pruning. Missing language payloads are reported, not fabricated.
- Inspect targeted MUM metadata without changing it; permanent/unknown Defender packages remain protected and reported. No ownership/CBS bypass or hidden replacement with AV-disable policies.
- Add offline edition/version/language/service profiling through isolated project hives, including a DefaultUser hive for OneDrive autostart cleanup.
- Added rule, preflight, post-report, hostile XML and isolated native fixture-hive tests. Synthetic tests do not certify a serviced Windows image or its bootability.

## 0.1.2

- ISO extraction now uses literal .NET directory/file APIs instead of a PowerShell provider lookup that could fail for a copied Hidden/System file, such as sources/ws.dat.
- Source ReadOnly files are copied and made writable without removing their Hidden/System attributes. File lengths are checked and copy failures identify the relative source/destination; no file is silently skipped.
- Added native PowerShell regression tests for Hidden/System/ReadOnly files, literal bracket/Unicode filenames, empty/nested files, overwrite protection, recursive destinations, missing sources, locked files and junction rejection. Windows-specific cases run on Windows PowerShell 5.1 in CI.
- Set window background/foreground explicitly and retained a dark disabled-button template for readable status/error screens.
- Both ImageTools.ps1 and its MediaCopy.ps1 helper are shipped in the application Scripts directory.

To update, close the previous application, extract the complete new package into a new folder, and start a new project for an ISO extraction that failed before completion. The original ISO is unchanged. Do not delete a project with an active WIM mount.
