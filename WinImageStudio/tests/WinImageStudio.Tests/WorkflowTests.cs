using WinImageStudio.Core;

internal static class WorkflowTests
{
    public static IEnumerable<(string Name, Action Body)> Cases()
    {
        ProjectSession Session(int stage = 2, ImageState state = ImageState.Mounted) => new()
        { Root = "fixture", Source = "fixture.iso", WorkingWim = "fixture/install.wim", MountPath = "fixture/mount", WorkflowStage = stage, State = state };
        var options = new AggressiveOptions();
        AggressiveReport Report(ProjectSession session, bool hasOperations = false, AggressiveOptions? settings = null) => new(AggressivePreset.Id, settings ?? options,
            new("Professional", "Client", "19045", "ar-SA", ["ar-SA"], false, true, true),
            hasOperations ? [new(OperationKind.DisableFeature, "TelnetClient")] : [], []);
        yield return ("Applied strong plan remains approved after analysis was invalidated", () =>
        {
            var session = Session(3);
            Check(WorkflowRules.CanApproveNoChanges(session, 0, true, null, null, options));
            Check(WorkflowRules.CanCommit(session, 0));
        });
        yield return ("An empty plan alone never proves successful strong application", () =>
        {
            var session = Session();
            Check(!WorkflowRules.CanApproveNoChanges(session, 0, true, null, null, options));
            Check(!WorkflowRules.CanCommit(session, 0));
        });
        yield return ("Fresh zero-operation strong analysis can be reviewed", () =>
        {
            var session = Session();
            Check(WorkflowRules.CanApproveNoChanges(session, 0, true, Report(session), session.Id, options));
            Check(!WorkflowRules.CanApproveNoChanges(session, 0, true, Report(session, true), session.Id, options));
            Check(!WorkflowRules.CanApproveNoChanges(session, 0, true, Report(session), "other-project", options));
            Check(!WorkflowRules.CanApproveNoChanges(session, 0, true, Report(session, settings: new(false, false)), session.Id, options));
        });
        yield return ("Pending operations block approval and commit even after earlier success", () =>
        {
            var session = Session(3);
            Check(!WorkflowRules.CanApproveNoChanges(session, 1, true, null, null, options));
            Check(!WorkflowRules.CanCommit(session, 1));
        });
        yield return ("Recovery and unmounted sessions cannot be reviewed or committed", () =>
        {
            foreach (ImageState state in new[] { ImageState.Prepared, ImageState.RecoveryRequired, ImageState.Committed, ImageState.Discarded })
            {
                var session = Session(3, state);
                Check(!WorkflowRules.CanApproveNoChanges(session, 0, false, null, null, options));
                Check(!WorkflowRules.CanCommit(session, 0));
            }
        });
        yield return ("Clearing an already-empty plan preserves applied completion", () =>
        {
            var session = Session(3); session.OutputIso = "previous.iso";
            WorkflowRules.PendingPlanChanged(session, 0, 0);
            Check(session.WorkflowStage == 3 && session.OutputIso == "previous.iso");
            WorkflowRules.PendingPlanChanged(session, 0, 1);
            Check(session.WorkflowStage == 2 && session.OutputIso is null);
            session.WorkflowStage = 3; WorkflowRules.PendingPlanChanged(session, 1, 0);
            Check(session.WorkflowStage == 2);
        });
        yield return ("Manual no-change review needs a mounted image but no strong report", () =>
        {
            var session = Session();
            Check(WorkflowRules.CanApproveNoChanges(session, 0, false, null, null, options));
            Check(!WorkflowRules.CanApproveNoChanges(null, 0, false, null, null, options));
            Check(!WorkflowRules.CanCommit(null, 0));
        });
    }
    private static void Check(bool value) { if (!value) throw new Exception("Workflow rule regression"); }
}
