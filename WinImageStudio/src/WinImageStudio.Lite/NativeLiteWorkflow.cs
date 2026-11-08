using System.IO;
using WinImageStudio.Core;

namespace WinImageStudio.Lite;

internal sealed class NativeLiteWorkflow(Action<string> log) : ILiteWorkflow, IDisposable
{
    private readonly ProcessRunner runner = new(log);
    private readonly ProjectStore store = new();
    private ImageEngine? engine;
    private string Script => Path.Combine(AppContext.BaseDirectory, "Scripts", "ImageTools.ps1");
    private ImageEngine Engine => engine ??= NewEngine(AdkInstaller.FindOscdimg());
    private ImageEngine NewEngine(string? oscdimg) => new(runner, store, ToolSettings.WindowsDefaults() with { Oscdimg = oscdimg }, Script);
    public IReadOnlyList<string> Notices => engine?.OperationNotices ?? [];
    public Task<ProjectSession> CreateAsync(string source, string parent, CancellationToken token) => Engine.CreateAsync(source, parent, token);
    public Task<IReadOnlyList<ImageInfo>> InspectAsync(ProjectSession project, CancellationToken token) => Engine.InspectAsync(project, token);
    public Task<ProjectSession> LoadAsync(string root, CancellationToken token) => store.LoadAsync(root, token);
    public async Task EnsureToolsAsync(CancellationToken token)
    {
        var adk = new AdkInstaller(runner, ToolSettings.WindowsDefaults().PowerShell, Path.Combine(AppContext.BaseDirectory, "Scripts", "VerifyAdk.ps1"), Path.Combine(AppContext.BaseDirectory, "Prerequisites", "adksetup.exe"));
        var tool = await adk.EnsureInstalledAsync(token).ConfigureAwait(false);
        engine?.Dispose(); engine = NewEngine(tool.Path);
        if (tool.RebootRequired) log("ADK طلب إعادة تشغيل الجهاز؛ إذا تعذر البناء، استأنف بعد إعادة التشغيل.");
    }
    public Task ImportAsync(ProjectSession p, ImageInfo i, CancellationToken t) => Engine.ImportAsync(p, i, t);
    public Task MountAsync(ProjectSession p, CancellationToken t) => Engine.MountAsync(p, t);
    public Task<AggressiveReport> AnalyzeAsync(ProjectSession p, CancellationToken t) => Engine.AnalyzeAggressiveAsync(p, new(AllowOtherClientEditions: true), t);
    public Task ApplyAsync(ProjectSession p, IReadOnlyList<PlannedOperation> ops, CancellationToken t) => Engine.ApplyAsync(p, ops, t);
    public Task CommitAsync(ProjectSession p, CancellationToken t) => Engine.UnmountAsync(p, true, t);
    public Task BuildIsoAsync(ProjectSession p, string output, CancellationToken t) => Engine.BuildIsoAsync(p, output, t, useEsd: true);
    public Task SaveAsync(ProjectSession p, CancellationToken t) => store.SaveAsync(p, t);
    public Task RecoverAsync(ProjectSession p, CancellationToken t) => Engine.RecoverDiscardAsync(p, t);
    public void Dispose() => engine?.Dispose();
}
