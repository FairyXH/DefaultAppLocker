using System.Text.Json.Serialization;

namespace DefaultAppLocker.Core;

public enum AssociationKind
{
    FileExtension,
    Protocol
}

public sealed record AppAssociation
{
    public required string Identifier { get; init; }
    public required string ProgId { get; init; }
    public AssociationKind Kind { get; init; }
    public string? ApplicationName { get; init; }

    [JsonIgnore]
    public string NormalizedIdentifier => Kind == AssociationKind.FileExtension ? NormalizeExtension(Identifier) : Identifier.Trim().ToLowerInvariant();

    public static string NormalizeExtension(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return trimmed;
        return trimmed.StartsWith('.') ? trimmed.ToLowerInvariant() : "." + trimmed.ToLowerInvariant();
    }
}

public sealed record DefaultAppSnapshot
{
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public string MachineName { get; init; } = Environment.MachineName;
    public string UserName { get; init; } = Environment.UserName;
    public List<AppAssociation> Associations { get; init; } = [];
}

public sealed record OverrideAssociation
{
    public required string Identifier { get; init; }
    public required string ProgId { get; init; }
    public AssociationKind Kind { get; init; } = AssociationKind.FileExtension;
    public string? SourceTemplate { get; init; }
}

public sealed record SnapshotProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Alias { get; set; } = "未命名快照";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DefaultAppSnapshot Snapshot { get; set; } = new();

    [JsonIgnore]
    public string DisplayName => $"{CreatedAt:yyyy-MM-dd HH:mm:ss}  {Alias}";
    public override string ToString() => DisplayName;
}

public sealed record QuickTemplateProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Alias { get; set; } = "未命名模板方案";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public List<OverrideAssociation> Overrides { get; set; } = [];

    [JsonIgnore]
    public string DisplayName => $"{Alias} ({Overrides.Count} 项)";
    public override string ToString() => DisplayName;
}

public sealed record DefaultAppLockerExportPackage
{
    public string PackageType { get; init; } = "DefaultAppLocker";
    public string SchemaVersion { get; init; } = "1.0";
    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.Now;
    public List<SnapshotProfile> SnapshotProfiles { get; init; } = [];
    public List<QuickTemplateProfile> TemplateProfiles { get; init; } = [];
}

public sealed record AppSettings
{
    public bool RestoreAtLogon { get; set; }
    public bool ContinuousLock { get; set; }
    public int ContinuousLockIntervalSeconds { get; set; } = 60;
    public string LastSnapshotPath { get; set; } = string.Empty;
    public string SelectedSnapshotProfileId { get; set; } = string.Empty;
    public string SelectedQuickTemplateProfileId { get; set; } = string.Empty;
}

public sealed record AppConfig
{
    public DefaultAppSnapshot Snapshot { get; set; } = new();
    public List<OverrideAssociation> Override { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
}

public sealed record AssociationDiff(string Identifier, AssociationKind Kind, string? CurrentProgId, string? TargetProgId)
{
    public bool IsAddition => string.IsNullOrWhiteSpace(CurrentProgId) && !string.IsNullOrWhiteSpace(TargetProgId);
    public bool IsRemoval => !string.IsNullOrWhiteSpace(CurrentProgId) && string.IsNullOrWhiteSpace(TargetProgId);
}

public sealed record TemplateOption(string Id, string DisplayName, string Description, bool RequiresExecutable, IReadOnlyList<string> Extensions);

public sealed record ApplyResult(bool Success, int ExitCode, string Output, string Error, string ConfigPath);
