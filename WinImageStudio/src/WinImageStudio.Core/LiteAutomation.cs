using System.Text.Json;

namespace WinImageStudio.Core;

public sealed record LiteProgress(int Completed, string Action);
public sealed record LiteResult(string IsoPath, string ReportPath, IReadOnlyList<string> Notices);
public interface ILiteWorkflow
{
    Task EnsureToolsAsync(CancellationToken token);
    Task ImportAsync(ProjectSession project, ImageInfo image, CancellationToken token);
    Task MountAsync(ProjectSession project, CancellationToken token);
    Task<AggressiveReport> AnalyzeAsync(ProjectSession project, CancellationToken token);
    Task ApplyAsync(ProjectSession project, IReadOnlyList<PlannedOperation> operations, CancellationToken token);
    Task CommitAsync(ProjectSession project, CancellationToken token);
    Task BuildIsoAsync(ProjectSession project, string output, CancellationToken token);
    Task SaveAsync(ProjectSession project, CancellationToken token);
    IReadOnlyList<string> Notices { get; }
}

public sealed class LiteAutomation(ILiteWorkflow workflow)
{
    public async Task<LiteResult> RunAsync(ProjectSession project, ImageInfo image, string output, IProgress<LiteProgress>? progress, CancellationToken token)
    {
        if (project.MediaPath is null || !Path.GetExtension(project.Source).Equals(".iso", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Lite يحتاج ملف ISO أصليًا كاملًا حتى ينتج ISO جاهزًا.");
        if (project.State == ImageState.RecoveryRequired) throw new InvalidOperationException("استعد جلسة هذا المشروع المتعثرة قبل إعادة المحاولة.");
        if (File.Exists(project.WorkingWim) && project.Image is not null &&
            (project.Image.ImageIndex != image.ImageIndex || !project.Image.EditionId.Equals(image.EditionId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("الإصدار المختار مختلف عن الصورة المحفوظة في المشروع.");
        output = Path.GetFullPath(output); Validation.RejectReparseAncestors(output);
        if (!Path.GetExtension(output).Equals(".iso", StringComparison.OrdinalIgnoreCase) || File.Exists(output)) throw new IOException("اختر اسم ISO جديدًا؛ لا يتم استبدال ملف موجود.");
        _ = DismCommands.Iso(project.MediaPath, output); // Verify boot media before applying any changes.
        var notices = new List<string>();
        token.ThrowIfCancellationRequested(); progress?.Report(new(0, "تجهيز أداة ISO من ADK المرفق عند الحاجة"));
        await workflow.EnsureToolsAsync(token).ConfigureAwait(false);
        progress?.Report(new(1, "نسخ الإصدار المختار إلى المشروع"));
        if (!(project.State == ImageState.Committed && project.WorkflowStage >= 4))
        {
            if (!File.Exists(project.WorkingWim)) await workflow.ImportAsync(project, image, token).ConfigureAwait(false);
            if (project.Image is null || project.Image.ImageIndex != image.ImageIndex || !project.Image.EditionId.Equals(image.EditionId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("الإصدار المختار لا يطابق صورة المشروع. ابدأ مشروعًا جديدًا لتغييره.");
            progress?.Report(new(2, "تركيب الصورة"));
            if (project.State != ImageState.Mounted) await workflow.MountAsync(project, token).ConfigureAwait(false);
            project.WorkflowStage = 2; await workflow.SaveAsync(project, token).ConfigureAwait(false);
            progress?.Report(new(3, "تحليل المكونات وبناء خطة التخفيف المناسبة للإصدار"));
            var report = await workflow.AnalyzeAsync(project, token).ConfigureAwait(false);
            notices.AddRange(report.Notes);
            AggressivePreset.RequireClient(report.Profile);
            if (!image.EditionId.Equals(report.Profile.EditionId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("بيانات الإصدار المركب لا تطابق الإصدار المختار.");
            if (report.PresetId != AggressivePreset.LiteId || !report.Options.AllowOtherClientEditions) throw new InvalidOperationException("خطة Lite غير صحيحة.");
            progress?.Report(new(4, "تطبيق التخفيف القوي؛ تُسجل المكونات المحمية التي تبقى"));
            if (report.Operations.Count > 0) await workflow.ApplyAsync(project, report.Operations, token).ConfigureAwait(false);
            project.WorkflowStage = 3; await workflow.SaveAsync(project, token).ConfigureAwait(false);
            progress?.Report(new(5, "حفظ الصورة وإلغاء تركيبها"));
            await workflow.CommitAsync(project, token).ConfigureAwait(false);
            if (project.State != ImageState.Committed) throw new InvalidOperationException("لم يكتمل حفظ الصورة؛ لن يُبنى ISO.");
            project.WorkflowStage = 4; await workflow.SaveAsync(project, token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested(); progress?.Report(new(6, "إعادة الضغط إلى ESD وإنشاء ISO النهائي"));
        await workflow.BuildIsoAsync(project, output, token).ConfigureAwait(false);
        if (!File.Exists(output) || new FileInfo(output).Length == 0 || project.State != ImageState.Committed) throw new IOException("لم يُنشأ ISO نهائي صالح الحجم؛ لم تكتمل المهمة.");
        string reportPath = Validation.UnderRoot(project.Root, Path.Combine(project.Root, "lite-result.json"));
        notices.AddRange(workflow.Notices);
        string after = Validation.UnderRoot(project.Root, Path.Combine(project.Root, "aggressive-after.txt"));
        if (File.Exists(after) && new FileInfo(after).Length <= 1024 * 1024) notices.Add(await File.ReadAllTextAsync(after, CancellationToken.None).ConfigureAwait(false));
        var result = new LiteResult(output, reportPath, notices.Distinct(StringComparer.Ordinal).ToArray());
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(result, ProjectStore.JsonOptions), CancellationToken.None).ConfigureAwait(false);
        project.OutputIso = output; project.WorkflowStage = 5;
        await workflow.SaveAsync(project, CancellationToken.None).ConfigureAwait(false);
        progress?.Report(new(7, "✓ اكتملت المهمة وحُفظ ISO")); return result;
    }
}
