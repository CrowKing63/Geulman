using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Geulman;

internal static class RecentFiles
{
    static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Geulman", "recent.json");

    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch { return new(); }
    }

    public static void Save(string path)
    {
        var list = Load();
        list.Remove(path);
        list.Insert(0, path);
        if (list.Count > 5) list = list.Take(5).ToList();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(list));
        }
        catch { /* non-critical */ }
    }
}
