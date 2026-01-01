using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Cryptography;
using LabApi.Features.Console;
using PluginManager_V2.JSON.Objects;
using PluginManager_V2.Utils;
using Utf8Json;

namespace PluginManager_V2.PluginManager;

internal static class PluginStorage
{
    internal static async Task<List<PluginListEntry>> ListPlugins(string port, string framework, bool ignoreLocks, bool skipUpdateCheck)
    {
        try
        {
            var pluginsPath = PluginInstaller.PluginsPath(port, framework);

            if (!Directory.Exists(pluginsPath))
            {
                Logger.Raw($"[PLUGIN MANAGER] Plugins path for port {port} doesn't exist. Skipped",
                    ConsoleColor.Blue);
                return null;
            }

            var metadataPath = pluginsPath + "metadata.json";

            if (!File.Exists(metadataPath))
            {
                Logger.Raw($"[PLUGIN MANAGER] Metadata file for port {port} doesn't exist. Skipped.", ConsoleColor.Blue);
                return null;
            }

            Logger.Raw("[PLUGIN MANAGER] Reading metadata...", ConsoleColor.Blue);
            var metadata = JsonSerializer.Deserialize<ServerPluginsConfig>(File.ReadAllText(metadataPath));

            if (metadata == null)
            {
                Logger.Raw($"[PLUGIN MANAGER] Failed to parse metadata file for port {port}!", ConsoleColor.Red);
                return null;
            }

            var performUpdate = false;

            if (metadata.LastUpdateCheck == null)
            {
                Logger.Raw($"[PLUGIN MANAGER] Plugins update check for port {port} was never performed.", ConsoleColor.Yellow);
                performUpdate = true;
            }
            else if ((DateTime.UtcNow - metadata.LastUpdateCheck).Value.TotalMinutes > 30)
            {
                Logger.Raw($"[PLUGIN MANAGER] Last plugins update check for port {port} was performed more than 30 minutes ago.", ConsoleColor.Yellow);
                performUpdate = true;
            }

            if (performUpdate && !skipUpdateCheck)
            {
                Logger.Raw("[PLUGIN MANAGER] Performing plugins update check...", ConsoleColor.Yellow);

                if (!await PluginUpdater.CheckForUpdates(port, ignoreLocks))
                    Logger.Raw("[PLUGIN MANAGER] Plugins update check failed! Aborting plugins update.", ConsoleColor.Yellow);

                Logger.Raw($"[PLUGIN MANAGER] Reading metadata for port {port}...", ConsoleColor.Blue);
                metadata = JsonSerializer.Deserialize<ServerPluginsConfig>(File.ReadAllText(metadataPath));

                if (metadata == null || metadata.InstalledPlugins.Count == 0)
                {
                    Logger.Raw($"[PLUGIN MANAGER] No plugins installed for port {port}. Skipped.", ConsoleColor.Blue);
                    return null;
                }
            }

            Logger.Raw("[PLUGIN MANAGER] Reading LocalAdmin config file...", ConsoleColor.Blue);
            await Plugin.Instance.LoadInternalJson();

            List<PluginListEntry> plugins = new();

            foreach (var plugin in metadata.InstalledPlugins)
            {
                var pluginPath = pluginsPath + $"{plugin.Key.Replace("/", "_")}.dll";

                if (!File.Exists(pluginPath))
                {
                    Logger.Raw($"[PLUGIN MANAGER] Plugin {plugin.Key} doesn't exist. Running plugins maintenance (\"p m\" command is recommended).", ConsoleColor.Yellow);
                    continue;
                }

                var currentHash = FileHasher.GetFileHashSha256(pluginPath);
                string? latestVersion = null;

                if (Plugin.DataJson!.PluginVersionCache!.ContainsKey(plugin.Key))
                    latestVersion = Plugin.DataJson.PluginVersionCache[plugin.Key].Version;

                List<string>? dependencies = null;

                foreach (var dep in metadata.Dependencies)
                {
                    if (!dep.Value.InstalledByPlugins.Contains(plugin.Key))
                        continue;

                    dependencies ??= new();
                    dependencies.Add(dep.Key);
                }

                plugins.Add(new PluginListEntry(plugin.Key, plugin.Value.CurrentVersion, plugin.Value.TargetVersion, latestVersion, currentHash == plugin.Value.FileHash, dependencies));
            }

            return plugins;
        }
        catch (Exception e)
        {
            Logger.Raw($"[PLUGIN MANAGER] Failed to list plugins for port {port}! Exception: {e.Message}",
                ConsoleColor.Red);
            return null;
        }
    }

    internal readonly struct PluginListEntry
    {
        internal readonly string Name;
        internal readonly string InstalledVersion;
        internal readonly string TargetVersion;
        internal readonly string LatestVersion;
        internal readonly bool IntegrityCheckPassed;
        internal readonly List<string>? Dependencies;

        internal PluginListEntry(string name, string? installedVersion, string? targetVersion, string? latestVersion, bool integrityCheckPassed, List<string>? dependencies)
        {
            Name = name;
            InstalledVersion = installedVersion ?? "(null)";
            TargetVersion = targetVersion ?? "(null)";
            LatestVersion = latestVersion ?? "(null)";
            IntegrityCheckPassed = integrityCheckPassed;
            Dependencies = dependencies;
        }

        internal bool UpToDate => InstalledVersion.Equals(LatestVersion, StringComparison.Ordinal);

        internal bool FixedVersion =>
            TargetVersion != null && !TargetVersion.Equals("latest", StringComparison.OrdinalIgnoreCase);

        internal string InstalledVersionValidated =>
            IntegrityCheckPassed ? InstalledVersion : "UNKNOWN - manually modified";
    }
}