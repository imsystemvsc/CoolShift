using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CoolShift;

public sealed record DetectedGame(string Key, string Name, bool IsManual);

public enum GameAutomationAction
{
    None,
    ActivateAlwaysOn,
    RestoreCoolIdle
}

public sealed class GameActivationTracker
{
    private static readonly TimeSpan ActivationDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromSeconds(10);
    private string? _candidateKey;
    private DateTimeOffset? _candidateSince;
    private bool _active;
    private DateTimeOffset? _closedSince;

    public string? ActiveGameName { get; private set; }

    public void Reset()
    {
        _candidateKey = null;
        _candidateSince = null;
        _active = false;
        _closedSince = null;
        ActiveGameName = null;
    }

    public GameAutomationAction Update(DateTimeOffset now, DetectedGame? manualGame, DetectedGame? automaticGame, bool onBattery)
    {
        var game = manualGame ?? automaticGame;
        if (game is null)
        {
            _candidateKey = null;
            _candidateSince = null;
            if (_active && _closedSince is null)
                _closedSince = now;

            if (_active && _closedSince is not null && now - _closedSince >= RestoreDelay)
            {
                _active = false;
                _closedSince = null;
                ActiveGameName = null;
                return GameAutomationAction.RestoreCoolIdle;
            }

            return GameAutomationAction.None;
        }

        _closedSince = null;
        if (!string.Equals(_candidateKey, game.Key, StringComparison.OrdinalIgnoreCase))
        {
            _candidateKey = game.Key;
            _candidateSince = now;
        }

        if (onBattery || _active)
            return GameAutomationAction.None;

        if (game.IsManual || now - _candidateSince >= ActivationDelay)
        {
            _active = true;
            ActiveGameName = game.Name;
            return GameAutomationAction.ActivateAlwaysOn;
        }

        return GameAutomationAction.None;
    }
}

public sealed class GameInstallationCatalog
{
    private static readonly string[] ExcludedFileNames =
    {
        "steam.exe", "steamwebhelper.exe", "epicgameslauncher.exe", "epicwebhelper.exe", "goggalaxy.exe", "eadesktop.exe",
        "ubisoftconnect.exe", "battle.net.exe", "riotclientservices.exe", "rockstargameslauncher.exe", "bethesda.net_launcher.exe",
        "playnite.desktopapp.exe", "crashreporter.exe", "crashpad_handler.exe", "unins000.exe",
        "uninstall.exe", "vc_redist.x64.exe", "vc_redist.x86.exe", "setup.exe", "msiexec.exe",
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe", "iexplore.exe", "coolshift.exe"
    };

    private readonly HashSet<string> _gameDirectories;

    public GameInstallationCatalog(IEnumerable<string> gameDirectories)
    {
        _gameDirectories = new HashSet<string>(gameDirectories.Select(NormalizeDirectory).Where(p => p is not null).Select(p => p!), StringComparer.OrdinalIgnoreCase);
    }

    public bool IsGameExecutable(string executablePath, IEnumerable<string> userExclusions)
    {
        if (IsExcluded(executablePath, userExclusions))
            return false;

        var normalized = NormalizePath(executablePath);
        return normalized is not null && _gameDirectories.Any(directory => normalized.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsExcluded(string executablePath, IEnumerable<string> userExclusions)
    {
        var fileName = Path.GetFileName(executablePath);
        if (ExcludedFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            return true;

        var normalized = NormalizePath(executablePath);
        return userExclusions.Any(exclusion =>
            string.Equals(fileName, Path.GetFileName(exclusion.Trim()), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, NormalizePath(exclusion), StringComparison.OrdinalIgnoreCase));
    }

    public static GameInstallationCatalog Load()
    {
        var directories = new List<string>();
        directories.AddRange(ReadSteamDirectories());
        directories.AddRange(ReadEpicDirectories());
        directories.AddRange(ReadGogDirectories());
        directories.AddRange(ReadPlayniteDirectories());
        return new GameInstallationCatalog(directories);
    }

    private static IEnumerable<string> ReadSteamDirectories()
    {
        var steamPath = ReadRegistryValue(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath")
            ?? ReadRegistryValue(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        if (string.IsNullOrWhiteSpace(steamPath)) yield break;

        var libraries = new List<string> { steamPath };
        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(vdfPath))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(vdfPath), "\\\"path\\\"\\s*\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                    libraries.Add(match.Groups["path"].Value.Replace("\\\\", "\\"));
            }
        }
        catch { yield break; }

        foreach (var library in libraries)
        {
            var common = Path.Combine(library, "steamapps", "common");
            IEnumerable<string> games;
            try { games = Directory.Exists(common) ? Directory.EnumerateDirectories(common) : Array.Empty<string>(); }
            catch { continue; }
            foreach (var game in games) yield return game;
        }
    }

    private static IEnumerable<string> ReadEpicDirectories()
    {
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        foreach (var file in SafeJsonFiles(manifests))
        {
            string? installLocation = ReadJsonString(file, "InstallLocation");
            if (!string.IsNullOrWhiteSpace(installLocation)) yield return installLocation;
        }
    }

    private static IEnumerable<string> ReadGogDirectories()
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            RegistryKey? games = null;
            try { games = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(@"SOFTWARE\GOG.com\Games"); }
            catch { }
            if (games is null) continue;
            using (games)
            {
                foreach (var name in games.GetSubKeyNames())
                using (var game = games.OpenSubKey(name))
                {
                    var path = game?.GetValue("path") as string;
                    if (!string.IsNullOrWhiteSpace(path)) yield return path;
                }
            }
        }
    }

    private static IEnumerable<string> ReadPlayniteDirectories()
    {
        var gamesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playnite", "library", "games");
        foreach (var file in SafeJsonFiles(gamesPath))
        {
            var installDirectory = ReadJsonString(file, "InstallDirectory");
            if (!string.IsNullOrWhiteSpace(installDirectory)) yield return installDirectory;
        }
    }

    private static IEnumerable<string> SafeJsonFiles(string directory)
    {
        try { return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.json") : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static string? ReadJsonString(string file, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            return document.RootElement.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        }
        catch { return null; }
    }

    private static string? ReadRegistryValue(RegistryHive hive, string keyPath, string valueName)
    {
        try { return RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(keyPath)?.GetValue(valueName) as string; }
        catch { return null; }
    }

    private static string? NormalizeDirectory(string path) => NormalizePath(path)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private static string? NormalizePath(string? path)
    {
        try { return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim('"', ' ')); }
        catch { return null; }
    }
}
