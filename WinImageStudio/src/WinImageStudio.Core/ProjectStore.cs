using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinImageStudio.Core;

public sealed class ProjectStore : IProjectStore
{
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public async Task SaveAsync(ProjectSession session, CancellationToken token = default)
    {
        session.UpdatedUtc = DateTimeOffset.UtcNow;
        string path = Path.Combine(session.Root, "project.json");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(file, session, JsonOptions, token).ConfigureAwait(false);
                await file.FlushAsync(token).ConfigureAwait(false);
                file.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task<ProjectSession> LoadAsync(string root, CancellationToken token = default)
    {
        root = Path.GetFullPath(root);
        Validation.RejectReparseAncestors(root);
        string projectFile = Path.Combine(root, "project.json");
        if (new FileInfo(projectFile).Length > 1024 * 1024) throw new InvalidDataException("Project journal exceeds 1 MiB.");
        await using var file = File.OpenRead(projectFile);
        var session = await JsonSerializer.DeserializeAsync<ProjectSession>(file, JsonOptions, token).ConfigureAwait(false) ?? throw new InvalidDataException("Empty project.");
        if (session.SchemaVersion != 1 || !string.Equals(Path.GetFullPath(session.Root), root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Project path or version mismatch.");
        if (!Guid.TryParseExact(session.Id, "N", out _) || !Enum.IsDefined(session.State)) throw new InvalidDataException("Invalid project identity or state.");
        Validation.UnderRoot(root, session.WorkingWim);
        Validation.UnderRoot(root, session.MountPath);
        if (session.MediaPath is not null) Validation.UnderRoot(root, session.MediaPath);
        return session;
    }
}
