# Architecture and trust boundaries

`WinImageStudio.Desktop` contains the Arabic WPF UI and operation-plan editor. It does not build shell command strings for DISM. Event handlers route through a guarded async entry point; long servicing and copying work executes off the UI thread. Tool stdout/stderr is drained concurrently, captured with a size bound, and forwarded to a bounded on-screen log and durable log file.

`WinImageStudio.Core` separates typed plans, validation, DISM argument generation, process execution, atomic project persistence, offline hive lifetime, installer staging and unattended XML. Process arguments use `ArgumentList` and `UseShellExecute=false`. A semaphore serializes operations on an engine instance. The GUI owns one engine and blocks concurrent edits while a job is active.

A new project exports a single selected image index to an application-owned WIM. Input media is read only. ISO extraction uses a temporary Windows disk-image attachment and dismounts only the attachment created by this operation. SWM joining is delegated to an explicitly configured wimlib binary. Windows boot media construction is delegated to an explicitly configured ADK oscdimg binary.

Before a mutating or hive-loading step, project.json is written through a same-directory temporary file and replaced atomically. `RecoveryRequired` is persisted before starting the tool. On success, the durable state moves to Prepared/Mounted/Committed/Discarded; on failure, the pending step and error remain. Cancellation does not terminate a started servicing process. Each completed plan operation is reported to the UI and removed from the pending plan.

Recovery is scoped to the current project's mount path and stable `WIS_<project-id>_<hive>` registry names. A NeedsRemount session is remounted before discarding. Other mount statuses are passed to DISM discard. There is no global mount cleanup and no online Windows servicing.

Registry keys are opened in scoped disposables and the hive is unloaded in finally. Services use the selected control set in offline SYSTEM; kernel and boot/system-start entries are rejected by the services editor. Generic registry edits remain powerful and can make Windows unusable; they are explicit user-authored operations, not a safe preset catalog.

ISO preparation copies the committed WIM to a staging file before replacing the media's install.wim. The final ISO is produced at a unique partial path and moved to a new destination only after oscdimg success. Source ISO/WIM/ESD/SWM files are never overwritten.

The application runs elevated, uses trusted native servicing tools, and accepts executable installers for staging inside the resulting operating system. This is not a sandbox. External executables, source images and plans require a trusted operator. Offline paths reject traversal and reparse-point ancestors; these checks reduce accidental redirection but do not create an adversarial filesystem security boundary against an administrator racing path changes. Cryptographic signing, enterprise review and Windows acceptance testing are required before production distribution.
