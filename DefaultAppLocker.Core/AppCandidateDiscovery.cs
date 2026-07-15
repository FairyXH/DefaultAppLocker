using Microsoft.Win32;

namespace DefaultAppLocker.Core;

public sealed record AppCandidate(string DisplayName, string ProgId, string? ExecutableName, int MatchedAssociationCount)
{
    public string Summary => $"{DisplayName} ({ProgId}) - 匹配 {MatchedAssociationCount} 项";
}

public sealed class AppCandidateDiscovery
{
    public IReadOnlyList<AppCandidate> FindCandidates(IEnumerable<string> identifiers)
    {
        var normalized = identifiers
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.StartsWith('.') ? AppAssociation.NormalizeExtension(x) : x.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalized.Count == 0) return [];

        var candidates = new Dictionary<string, CandidateAccumulator>(StringComparer.OrdinalIgnoreCase);
        AddApplicationDeclarations(candidates, normalized);
        AddPerExtensionOpenWithData(candidates, normalized);
        AddRegisteredApplicationCapabilities(candidates, normalized);
        AddKnownMicrosoftEdgeCandidate(candidates, normalized);

        return candidates.Values
            .Select(c => new AppCandidate(c.Name, c.ProgId, c.Exe, c.Count))
            .OrderByDescending(c => c.MatchedAssociationCount)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddApplicationDeclarations(Dictionary<string, CandidateAccumulator> candidates, HashSet<string> identifiers)
    {
        using var applications = Registry.ClassesRoot.OpenSubKey("Applications");
        if (applications is null) return;
        foreach (var appName in applications.GetSubKeyNames())
        {
            using var appKey = applications.OpenSubKey(appName);
            AddSupportedTypesCandidate(candidates, appName, appKey, identifiers);
            AddCapabilitiesCandidate(candidates, appName, appKey, identifiers);
        }
    }

    private static void AddPerExtensionOpenWithData(Dictionary<string, CandidateAccumulator> candidates, HashSet<string> identifiers)
    {
        foreach (var id in identifiers.Where(i => i.StartsWith('.')))
        {
            AddOpenWithDataForExtension(candidates, Registry.CurrentUser, $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{id}", id);
            AddOpenWithDataForExtension(candidates, Registry.ClassesRoot, id, id);
        }
    }

    private static void AddOpenWithDataForExtension(Dictionary<string, CandidateAccumulator> candidates, RegistryKey root, string extensionKeyPath, string extension)
    {
        using var extensionKey = root.OpenSubKey(extensionKeyPath);
        if (extensionKey is null) return;

        using (var openWithList = extensionKey.OpenSubKey("OpenWithList"))
        {
            if (openWithList is not null)
            {
                foreach (var valueName in openWithList.GetValueNames())
                {
                    var app = openWithList.GetValue(valueName) as string;
                    if (string.IsNullOrWhiteSpace(app) || !app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    AddOrIncrement(candidates, ProgIdResolver.ResolveApplicationProgId(app), ReadApplicationFriendlyName(app), app, 1);
                }
                foreach (var subKeyName in openWithList.GetSubKeyNames().Where(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                {
                    AddOrIncrement(candidates, ProgIdResolver.ResolveApplicationProgId(subKeyName), ReadApplicationFriendlyName(subKeyName), subKeyName, 1);
                }
            }
        }

        using (var openWithProgids = extensionKey.OpenSubKey("OpenWithProgids"))
        {
            if (openWithProgids is not null)
            {
                foreach (var progId in openWithProgids.GetValueNames().Where(v => !string.IsNullOrWhiteSpace(v)))
                {
                    AddOrIncrement(candidates, progId, ReadProgIdFriendlyName(progId), TryResolveExecutableNameFromProgId(progId), 1);
                }
            }
        }

        using (var userChoice = extensionKey.OpenSubKey("UserChoice"))
        {
            var userChoiceProgId = userChoice?.GetValue("ProgId") as string;
            if (!string.IsNullOrWhiteSpace(userChoiceProgId))
                AddOrIncrement(candidates, userChoiceProgId, ReadProgIdFriendlyName(userChoiceProgId), TryResolveExecutableNameFromProgId(userChoiceProgId), 1);
        }

        var defaultProgId = extensionKey.GetValue(null) as string;
        if (!string.IsNullOrWhiteSpace(defaultProgId) && !string.Equals(defaultProgId, extension, StringComparison.OrdinalIgnoreCase))
            AddOrIncrement(candidates, defaultProgId, ReadProgIdFriendlyName(defaultProgId), TryResolveExecutableNameFromProgId(defaultProgId), 1);
    }

    private static void AddRegisteredApplicationCapabilities(Dictionary<string, CandidateAccumulator> candidates, HashSet<string> identifiers)
    {
        AddRegisteredApplicationCapabilities(candidates, Registry.CurrentUser, @"Software\RegisteredApplications", identifiers);
        AddRegisteredApplicationCapabilities(candidates, Registry.LocalMachine, @"Software\RegisteredApplications", identifiers);
    }

    private static void AddRegisteredApplicationCapabilities(Dictionary<string, CandidateAccumulator> candidates, RegistryKey root, string path, HashSet<string> identifiers)
    {
        using var registered = root.OpenSubKey(path);
        if (registered is null) return;
        foreach (var appName in registered.GetValueNames())
        {
            var capabilityPath = registered.GetValue(appName) as string;
            if (string.IsNullOrWhiteSpace(capabilityPath)) continue;
            using var capabilities = root.OpenSubKey(capabilityPath);
            if (capabilities is null) continue;
            var displayName = capabilities.GetValue("ApplicationName") as string ?? appName;
            AddAssociationMap(candidates, capabilities, "FileAssociations", identifiers, displayName, appName);
            AddAssociationMap(candidates, capabilities, "URLAssociations", identifiers, displayName, appName);
        }
    }

    private static void AddSupportedTypesCandidate(Dictionary<string, CandidateAccumulator> candidates, string appName, RegistryKey? appKey, HashSet<string> identifiers)
    {
        using var supported = appKey?.OpenSubKey("SupportedTypes");
        if (supported is null) return;
        var count = supported.GetValueNames().Count(v => identifiers.Contains(AppAssociation.NormalizeExtension(v)));
        if (count <= 0) return;
        var progId = ProgIdResolver.ResolveApplicationProgId(appName);
        AddOrIncrement(candidates, progId, ReadFriendlyName(appKey, appName), appName, count);
    }

    private static void AddCapabilitiesCandidate(Dictionary<string, CandidateAccumulator> candidates, string appName, RegistryKey? appKey, HashSet<string> identifiers)
    {
        using var capabilities = appKey?.OpenSubKey("Capabilities");
        if (capabilities is null) return;
        var appDisplayName = capabilities.GetValue("ApplicationName") as string ?? ReadFriendlyName(appKey, appName);
        AddAssociationMap(candidates, capabilities, "FileAssociations", identifiers, appDisplayName, appName);
        AddAssociationMap(candidates, capabilities, "URLAssociations", identifiers, appDisplayName, appName);
    }

    private static void AddAssociationMap(Dictionary<string, CandidateAccumulator> candidates, RegistryKey capabilities, string subKeyName, HashSet<string> identifiers, string appDisplayName, string appName)
    {
        using var key = capabilities.OpenSubKey(subKeyName);
        if (key is null) return;
        foreach (var name in key.GetValueNames())
        {
            var normalized = name.StartsWith('.') ? AppAssociation.NormalizeExtension(name) : name.Trim().ToLowerInvariant();
            if (!identifiers.Contains(normalized)) continue;
            var progId = key.GetValue(name) as string;
            if (string.IsNullOrWhiteSpace(progId)) continue;
            AddOrIncrement(candidates, progId, appDisplayName, appName, 1);
        }
    }

    private static void AddKnownMicrosoftEdgeCandidate(Dictionary<string, CandidateAccumulator> candidates, HashSet<string> identifiers)
    {
        var protocolMatches = 0;
        if (identifiers.Contains("http")) protocolMatches++;
        if (identifiers.Contains("https")) protocolMatches++;
        if (protocolMatches > 0) AddOrIncrement(candidates, "MSEdgeHTM", "Microsoft Edge", "msedge.exe", protocolMatches);
        if (identifiers.Contains(".pdf")) AddOrIncrement(candidates, "MSEdgePDF", "Microsoft Edge PDF", "msedge.exe", 1);
    }

    private static void AddOrIncrement(Dictionary<string, CandidateAccumulator> candidates, string progId, string displayName, string? exe, int count)
    {
        if (string.IsNullOrWhiteSpace(progId)) return;
        if (candidates.TryGetValue(progId, out var existing))
        {
            existing.Count += count;
            if (string.IsNullOrWhiteSpace(existing.Exe)) existing.Exe = exe;
            if (string.IsNullOrWhiteSpace(existing.Name) || existing.Name.Equals(progId, StringComparison.OrdinalIgnoreCase)) existing.Name = displayName;
            return;
        }
        candidates[progId] = new CandidateAccumulator(progId, displayName, exe, count);
    }

    private static string ReadFriendlyName(RegistryKey? appKey, string appName)
    {
        return appKey?.GetValue("FriendlyAppName") as string
            ?? appKey?.GetValue(null) as string
            ?? Path.GetFileNameWithoutExtension(appName);
    }

    private static string ReadApplicationFriendlyName(string appName)
    {
        using var appKey = Registry.ClassesRoot.OpenSubKey($@"Applications\{appName}");
        return ReadFriendlyName(appKey, appName);
    }

    private static string ReadProgIdFriendlyName(string progId)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(progId);
        return key?.GetValue(null) as string ?? progId;
    }

    private static string? TryResolveExecutableNameFromProgId(string progId)
    {
        if (progId.StartsWith("Applications\\", StringComparison.OrdinalIgnoreCase)) return progId[13..];
        using var command = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
        var text = command?.GetValue(null) as string;
        if (string.IsNullOrWhiteSpace(text)) return null;
        var exe = ExtractExecutablePath(text);
        return string.IsNullOrWhiteSpace(exe) ? null : Path.GetFileName(exe);
    }

    private static string? ExtractExecutablePath(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }
        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIndex >= 0 ? trimmed[..(exeIndex + 4)] : trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    private sealed class CandidateAccumulator
    {
        public CandidateAccumulator(string progId, string name, string? exe, int count)
        {
            ProgId = progId;
            Name = name;
            Exe = exe;
            Count = count;
        }

        public string ProgId { get; }
        public string Name { get; set; }
        public string? Exe { get; set; }
        public int Count { get; set; }
    }
}
