namespace WinImageStudio.Core;

/// <summary>Review gates shared by the desktop and regression tests; never infer success from an empty plan.</summary>
public static class WorkflowRules
{
    public static bool CanCommit(ProjectSession? project, int pendingCount) =>
        project?.State == ImageState.Mounted && project.WorkflowStage >= 3 && pendingCount == 0;

    public static bool CanApproveNoChanges(ProjectSession? project, int pendingCount, bool strong,
        AggressiveReport? report, string? reportProjectId, AggressiveOptions options)
    {
        if (project?.State != ImageState.Mounted || pendingCount != 0) return false;
        if (project.WorkflowStage >= 3) return true; // Applied/reviewed already; analysis is deliberately invalidated after apply.
        return !strong || (report is not null && reportProjectId == project.Id && report.Operations.Count == 0 && report.Options == options);
    }
    public static void PendingPlanChanged(ProjectSession? project, int previousCount, int currentCount)
    {
        if (project is null || (previousCount == 0 && currentCount == 0)) return;
        project.WorkflowStage = Math.Min(project.WorkflowStage, 2);
        project.OutputIso = null;
    }
}
