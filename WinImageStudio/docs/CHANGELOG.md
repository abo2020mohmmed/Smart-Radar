# Changes

## 0.5.0

- Freshly export the final single-index installation payload before ISO creation: recovery-compressed install.esd by default, or a fresh max-compressed install.wim. This reclaims unreferenced WIM data and avoids always replacing a source ESD with a larger working WIM. No fixed reduction percentage is promised. Original ISO/working WIM remain intact; old install payload alternatives are excluded from the media.
- Record source ISO/output ISO/working WIM/final install payload byte counts in iso-build-summary.json, and display source/output sizes. Offer rebuilding an existing committed project to a new output filename without repeating customization.
- Add saved-image inventory auditing: mount the project WIM, query inventory/Win32 uninstall keys, unload/discard the temporary session, and write encoded offline HTML plus JSON. Includes provisioned apps, HKLM Win32 registrations, staged installers, features, packages, drivers, services and languages. Does not certify bootability, malware absence or detect every portable/per-user app.
- Integrate ADK Deployment Tools setup into the output page. Download the Microsoft bootstrapper over exact allowlisted HTTPS hosts, bound its size, verify valid Microsoft Corporation Authenticode before execution, install only Deployment Tools, and discover oscdimg automatically. Uses Microsoft ADK terms and needs internet; no ADK binaries are added to the customized Windows image.
- Test fresh export orchestration/media cleanup, native DISM recovery ESD, offline x86/x64 Win32 registry fixtures, HTML encoding, audit discard, download host boundaries and real Microsoft download/signature/ADK setup readiness.

## 0.4.0

- Replace the duplicated navigation/tab interface with one page per phase and a fixed primary action footer. Next now creates/imports, prepares/analyzes, queues a review, applies with explicit consent, commits, and builds ISO according to the current phase. In particular, Next after application executes commit rather than navigating to the same review tab.
- Keep manual feature/package/driver/app/service, integration and registry controls inside the customization page; keep output filename and oscdimg together on the output page. Fold detailed logs and optional controls by default.
- Automatically analyze the strong preset after mounting. Changed options require a fresh visible analysis before review; never apply on analysis or navigation alone.
- Restore existing project progress directly to the correct wizard page. Support --project <folder> for opening a validated project journal. Recovery remains explicit and project-scoped; saving never discards changes.
- Add pure primary-action transition tests and a desktop regression that launches the actual app with a tiny mounted WIM fixture, invokes Next, verifies native commit/journal completion and output navigation, then explicitly finishes WIM-only output without inventing an ISO.

## 0.3.1

- Fix the strong-path empty-plan button rejecting a session whose plan already applied successfully and whose old analysis was correctly invalidated. The review gate is now idempotent after confirmed application, never inferred from an empty queue alone.
- Disable the no-change button after review/application, emphasize saving, and keep an on-screen result in the review step. Reopening an applied project restores its saved report.
- Clearing an already-empty queue and revisiting preparation no longer erase completed application progress. New pending operations still invalidate completion.
- Show native Windows error codes and messages for OneDrive file access/deletion failures; distinguish access denial, sharing locks and unsupported operations. A refusal does not prove a symbolic link, and protected files remain explicitly reported. No ownership/ACL bypass was added.
- Add workflow regression coverage for post-apply review, empty queues, stale reports, recovery state, pending changes and completion invalidation; add a real Windows locked-file diagnostic test.

## 0.3.0

- Added five numbered steps and separate strong/manual paths, contextual next-step guidance and completion indicators persisted per project.
- Preparation exports the selected edition and mounts it in one action. Apply, commit and ISO creation remain explicit operations; unreviewed plans cannot be committed through the guided interface.
- Reworked dark controls, including combo-box dropdowns and readable status panels.
- Fixed OneDrive removal failing before its error handler on WIM/WOF storage-backed reparse files. Only known storage tags on the two exact offline installer paths are accepted, opened without following leaf links and deleted through the validated handle. Parent links, symbolic links, directory and unknown tags remain refused. Protected/refused installers are reported and do not abort unrelated preset steps.
- Added Windows regression tests for readonly files, leaf/parent links, a compacted WOF file and capture/mount/commit/remount of a tiny fixture WIM, plus a WPF startup/navigation smoke check. These fixtures do not certify a complete customized Windows installation.

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
