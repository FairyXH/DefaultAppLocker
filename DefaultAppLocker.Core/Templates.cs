using Microsoft.Win32;

namespace DefaultAppLocker.Core;

public interface IQuickTemplate
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    bool RequiresExecutable { get; }
    bool IsAvailable();
    IReadOnlyList<string> GetExtensions();
    IReadOnlyList<OverrideAssociation> CreateOverrides(string target);
}

public abstract class ExtensionTemplate : IQuickTemplate
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public virtual bool RequiresExecutable => true;
    protected abstract IReadOnlyList<string> Extensions { get; }
    public virtual bool IsAvailable() => true;
    public virtual IReadOnlyList<string> GetExtensions() => Extensions;

    public virtual IReadOnlyList<OverrideAssociation> CreateOverrides(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("目标程序不能为空。", nameof(target));
        var progId = ProgIdResolver.ResolveApplicationProgId(target);
        return GetExtensions().Select(ext => new OverrideAssociation
        {
            Identifier = AppAssociation.NormalizeExtension(ext),
            Kind = AssociationKind.FileExtension,
            ProgId = progId,
            SourceTemplate = DisplayName
        }).ToList();
    }
}

public static class ProgIdResolver
{
    public const string DefaultEdgeBrowserProgId = "MSEdgeHTM";
    public const string DefaultEdgePdfProgId = "MSEdgePDF";

    public static string ResolveApplicationProgId(string executablePathOrName)
    {
        var trimmed = executablePathOrName.Trim();
        if (trimmed.StartsWith("Applications\\", StringComparison.OrdinalIgnoreCase)) return trimmed;
        if (trimmed.Equals(DefaultEdgeBrowserProgId, StringComparison.OrdinalIgnoreCase) || trimmed.Equals(DefaultEdgePdfProgId, StringComparison.OrdinalIgnoreCase)) return trimmed;
        var fileName = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(fileName)) return trimmed;
        return $"Applications\\{fileName}";
    }

    public static string ResolvePdfProgId(string executablePathOrProgId)
    {
        var value = executablePathOrProgId.Trim();
        var fileName = Path.GetFileName(value);
        if (value.Equals(DefaultEdgeBrowserProgId, StringComparison.OrdinalIgnoreCase) ||
            value.Equals(DefaultEdgePdfProgId, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Applications\\msedge.exe", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultEdgePdfProgId;
        }
        return value.StartsWith("Applications\\", StringComparison.OrdinalIgnoreCase) || value.Contains('.', StringComparison.Ordinal)
            ? value
            : ResolveApplicationProgId(value);
    }
}

public sealed class ImageTemplate : ExtensionTemplate
{
    public override string Id => "Image";
    public override string DisplayName => "图片查看器";
    public override string Description => "关联常见位图、相机 RAW 与矢量图片格式。";
    protected override IReadOnlyList<string> Extensions { get; } =
    [
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif",
        ".ico", ".svg", ".svgz", ".wmf", ".emf", ".dds", ".tga", ".pcx", ".pnm", ".pbm", ".pgm", ".ppm",
        ".raw", ".arw", ".cr2", ".cr3", ".crw", ".dng", ".erf", ".kdc", ".mrw", ".nef", ".nrw", ".orf", ".pef", ".raf", ".rw2", ".rwl", ".sr2", ".srf", ".x3f"
    ];
}

public sealed class VideoTemplate : ExtensionTemplate
{
    public override string Id => "Video";
    public override string DisplayName => "视频播放器";
    public override string Description => "关联常见视频容器、流媒体与光盘视频格式。";
    protected override IReadOnlyList<string> Extensions { get; } =
    [
        ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".qt", ".wmv", ".flv", ".f4v", ".webm", ".mpg", ".mpeg", ".mpe", ".m1v", ".m2v",
        ".ts", ".mts", ".m2ts", ".vob", ".ogv", ".3gp", ".3g2", ".asf", ".rm", ".rmvb", ".divx", ".xvid", ".amv", ".dvr-ms", ".wtv", ".mxf"
    ];
}

public sealed class AudioTemplate : ExtensionTemplate
{
    public override string Id => "Audio";
    public override string DisplayName => "音频播放器";
    public override string Description => "关联常见有损、无损、播放列表与模块音乐格式。";
    protected override IReadOnlyList<string> Extensions { get; } =
    [
        ".mp3", ".m4a", ".aac", ".flac", ".wav", ".wma", ".ogg", ".oga", ".opus", ".ape", ".wv", ".alac", ".aiff", ".aif", ".aifc",
        ".mid", ".midi", ".rmi", ".mka", ".mpc", ".ac3", ".dts", ".amr", ".au", ".snd", ".ra", ".m3u", ".m3u8", ".pls", ".cue", ".mod", ".xm", ".s3m", ".it"
    ];
}

public sealed class TextTemplate : ExtensionTemplate
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cmd", ".com", ".ps1", ".vbs", ".jse", ".wsf"
    };

    public override string Id => "Text";
    public override string DisplayName => "文本编辑器";
    public override string Description => "关联纯文本、配置、标记语言和源码文件；默认排除脚本执行入口。";
    protected override IReadOnlyList<string> Extensions { get; } =
    [
        ".txt", ".text", ".log", ".ini", ".inf", ".cfg", ".conf", ".cnf", ".properties", ".toml", ".yaml", ".yml", ".xml", ".json", ".jsonc", ".md", ".markdown", ".rst", ".csv", ".tsv",
        ".c", ".cpp", ".cc", ".cxx", ".h", ".hh", ".hpp", ".hxx", ".cs", ".csx", ".vb", ".fs", ".fsi", ".fsx", ".java", ".kt", ".kts", ".py", ".pyw", ".pyi",
        ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".css", ".scss", ".sass", ".less", ".html", ".htm", ".xhtml", ".php", ".go", ".rs", ".swift", ".lua", ".rb", ".erb", ".sql",
        ".ps1xml", ".psm1", ".psd1", ".reg", ".sh", ".bash", ".zsh", ".fish", ".pl", ".pm", ".r", ".jl", ".scala", ".groovy", ".gradle", ".dart", ".m", ".mm",
        ".vue", ".svelte", ".astro", ".razor", ".cshtml", ".xaml", ".resx", ".targets", ".props", ".dockerfile", ".editorconfig", ".gitignore", ".gitattributes", ".env", ".lock"
    ];

    public override IReadOnlyList<string> GetExtensions() => Extensions.Where(e => !Excluded.Contains(e)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(e => e).ToList();
}

public sealed class BrowserTemplate : IQuickTemplate
{
    public string Id => "Browser";
    public string DisplayName => "浏览器";
    public string Description => "设置默认浏览器；默认使用 Microsoft Edge，也允许选择自定义 exe。";
    public bool RequiresExecutable => false;
    public bool IsAvailable() => true;
    public IReadOnlyList<string> GetExtensions() => ["http", "https"];
    public IReadOnlyList<OverrideAssociation> CreateOverrides(string target)
    {
        var progId = string.IsNullOrWhiteSpace(target) ? ProgIdResolver.DefaultEdgeBrowserProgId : ProgIdResolver.ResolveApplicationProgId(target);
        return [
            new OverrideAssociation { Identifier = "http", Kind = AssociationKind.Protocol, ProgId = progId, SourceTemplate = DisplayName },
            new OverrideAssociation { Identifier = "https", Kind = AssociationKind.Protocol, ProgId = progId, SourceTemplate = DisplayName }
        ];
    }
}

public sealed class PdfTemplate : IQuickTemplate
{
    public string Id => "Pdf";
    public string DisplayName => "PDF";
    public string Description => "设置 PDF 默认程序；默认使用 Microsoft Edge PDF，也允许选择自定义 exe。";
    public bool RequiresExecutable => false;
    public bool IsAvailable() => true;
    public IReadOnlyList<string> GetExtensions() => [".pdf"];
    public IReadOnlyList<OverrideAssociation> CreateOverrides(string target)
    {
        var progId = string.IsNullOrWhiteSpace(target) ? ProgIdResolver.DefaultEdgePdfProgId : ProgIdResolver.ResolvePdfProgId(target);
        return [new OverrideAssociation { Identifier = ".pdf", Kind = AssociationKind.FileExtension, ProgId = progId, SourceTemplate = DisplayName }];
    }
}

public sealed record OfficeApplicationDefinition(string DisplayName, string ExecutableName, IReadOnlyList<string> SupportedTypes);

public sealed class OfficeSuiteTemplate : IQuickTemplate
{
    private static readonly HashSet<string> FixedTextOwnership = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".ini", ".cfg", ".conf", ".json", ".xml", ".yaml", ".yml", ".toml", ".md", ".csv", ".tsv"
    };

    private readonly IReadOnlyList<OfficeApplicationDefinition>? _definitions;

    public OfficeSuiteTemplate(IReadOnlyList<OfficeApplicationDefinition>? definitions = null)
    {
        _definitions = definitions;
    }

    public string Id => "Office";
    public string DisplayName => "Microsoft Office";
    public string Description => "一键设置 Word、Excel、PowerPoint 支持的全部 Office 格式；不需要选择 exe，不覆盖文本固定归属格式。";
    public bool RequiresExecutable => false;
    public bool IsAvailable() => GetDefinitions().Any(d => d.SupportedTypes.Count > 0);
    public IReadOnlyList<string> GetExtensions() => GetDefinitions().SelectMany(d => FilterFixedTextFormats(d.SupportedTypes)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

    public IReadOnlyList<OverrideAssociation> CreateOverrides(string target)
    {
        return GetDefinitions()
            .SelectMany(def => FilterFixedTextFormats(def.SupportedTypes).Select(ext => new OverrideAssociation
            {
                Identifier = ext,
                Kind = AssociationKind.FileExtension,
                ProgId = ProgIdResolver.ResolveApplicationProgId(def.ExecutableName),
                SourceTemplate = $"Office/{def.DisplayName}"
            }))
            .GroupBy(o => o.Identifier, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(o => o.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<OfficeApplicationDefinition> GetDefinitions() => _definitions ??
    [
        new OfficeApplicationDefinition("Word", "WINWORD.EXE", ReadSupportedTypes("WINWORD.EXE")),
        new OfficeApplicationDefinition("Excel", "EXCEL.EXE", ReadSupportedTypes("EXCEL.EXE")),
        new OfficeApplicationDefinition("PowerPoint", "POWERPNT.EXE", ReadSupportedTypes("POWERPNT.EXE"))
    ];

    private static IReadOnlyList<string> FilterFixedTextFormats(IEnumerable<string> extensions)
    {
        return extensions
            .Select(AppAssociation.NormalizeExtension)
            .Where(v => !FixedTextOwnership.Contains(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ReadSupportedTypes(string executableName)
    {
        using var key = Registry.ClassesRoot.OpenSubKey($@"Applications\{executableName}\SupportedTypes");
        if (key is null) return [];
        return key.GetValueNames()
            .Where(v => v.StartsWith(".", StringComparison.Ordinal))
            .Select(AppAssociation.NormalizeExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class TemplateCatalog
{
    private readonly List<IQuickTemplate> _templates =
    [
        new BrowserTemplate(),
        new PdfTemplate(),
        new ImageTemplate(),
        new VideoTemplate(),
        new AudioTemplate(),
        new TextTemplate(),
        new OfficeSuiteTemplate()
    ];

    public IReadOnlyList<IQuickTemplate> AvailableTemplates => _templates.Where(t => t.IsAvailable()).ToList();
    public IReadOnlyList<IQuickTemplate> AllTemplates => _templates;

    public IQuickTemplate? Find(string id) => _templates.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
}
