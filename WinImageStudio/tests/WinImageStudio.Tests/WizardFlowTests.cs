using WinImageStudio.Core;

internal static class WizardFlowTests
{
    public static IEnumerable<(string Name, Action Body)> Cases()
    {
        ProjectSession Session(int stage = 2, ImageState state = ImageState.Mounted) => new()
        { Root = "project", Source = "source.iso", WorkingWim = "project/install.wim", MountPath = "project/mount", MediaPath = "project/media", WorkflowStage = stage, State = state };
        WizardDecision Next(WizardPage page, ProjectSession? project, int pending = 0, bool strong = true, bool analyzed = true, bool approve = true, bool ack = true, bool source = true, bool iso = true) =>
            WizardFlow.Next(page, project, pending, strong, analyzed, approve, ack, source, iso);
        void Expect(WizardAction action, WizardDecision decision, bool enabled = true)
        { if (decision.Action != action || decision.Enabled != enabled) throw new Exception($"Expected {action}/{enabled}; got {decision}"); }
        yield return ("Wizard primary action commits an applied mounted image instead of navigating to the same page", () =>
            Expect(WizardAction.Commit, Next(WizardPage.Review, Session(3), analyzed: false, approve: false, ack: false)));
        yield return ("Wizard applies only after visible review consent", () =>
        {
            Expect(WizardAction.Apply, Next(WizardPage.Review, Session(), pending: 5, ack: false), false);
            Expect(WizardAction.Apply, Next(WizardPage.Review, Session(), pending: 5));
        });
        yield return ("Wizard strong happy path leads from source through real actions to output", () =>
        {
            Expect(WizardAction.Create, Next(WizardPage.Source, null));
            var session = Session(1, ImageState.Prepared);
            Expect(WizardAction.Prepare, Next(WizardPage.Prepare, session));
            session.State = ImageState.Mounted; session.WorkflowStage = 2;
            Expect(WizardAction.Review, Next(WizardPage.Customize, session));
            Expect(WizardAction.Apply, Next(WizardPage.Review, session, pending: 3));
            session.WorkflowStage = 3;
            Expect(WizardAction.Commit, Next(WizardPage.Review, session));
            session.State = ImageState.Committed; session.WorkflowStage = 4;
            Expect(WizardAction.BuildIso, Next(WizardPage.Output, session));
            session.WorkflowStage = 5;
            Expect(WizardAction.OpenFolder, Next(WizardPage.Done, session));
        });
        yield return ("Wizard changed strong options require analysis before queueing", () =>
            Expect(WizardAction.Analyze, Next(WizardPage.Customize, Session(), analyzed: false)));
        yield return ("Wizard source and ISO tools have explicit disabled states", () =>
        {
            Expect(WizardAction.Create, Next(WizardPage.Source, null, source: false), false);
            Expect(WizardAction.BuildIso, Next(WizardPage.Output, Session(4, ImageState.Committed), iso: false), false);
        });
        yield return ("Wizard recovery blocks primary execution on every page", () =>
        {
            foreach (WizardPage page in Enum.GetValues<WizardPage>()) Expect(WizardAction.None, Next(page, Session(3, ImageState.RecoveryRequired)), false);
        });
        yield return ("Wizard manual empty plan can be reviewed without strong analysis", () =>
        {
            Expect(WizardAction.Review, Next(WizardPage.Customize, Session(), strong: false, analyzed: false));
            Expect(WizardAction.Approve, Next(WizardPage.Review, Session(), strong: false));
            Expect(WizardAction.Analyze, Next(WizardPage.Review, Session(), approve: false));
        });
        yield return ("Wizard committed images cannot be remounted by pressing next on old pages", () =>
            Expect(WizardAction.Resume, Next(WizardPage.Prepare, Session(4, ImageState.Committed))));
        yield return ("Wizard finishes WIM-only output without claiming an ISO was created", () =>
        {
            var session = Session(4, ImageState.Committed); session.MediaPath = null;
            Expect(WizardAction.Finish, Next(WizardPage.Output, session, iso: false));
            Expect(WizardAction.None, Next(WizardPage.Output, Session(3)), false);
        });
    }
}
