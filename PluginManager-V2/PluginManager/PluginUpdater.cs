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

internal static class PluginUpdater
{
    internal static async Task<bool> CheckForUpdates(string port, string framework)
    {
        var metadataPath = PluginInstaller.PluginsPath(port,framework) + "metadata.json";

        try
        {
            if (!File.Exists(metadataPath))
            {
                Logger.Raw($"[PLUGIN MANAGER] No metadata file for port {port}. Skipped.", ConsoleColor.Blue);
                return true;
            }

            Logger.Raw($"[PLUGIN MANAGER] Reading metadata for port {port}...", ConsoleColor.Blue);
            var metadata = JsonSerializer.Deserialize<ServerPluginsConfig>(File.ReadAllText(metadataPath));

            if (metadata == null)
            {
                Logger.Raw($"[PLUGIN MANAGER] No plugins installed for port {port}. Skipped.", ConsoleColor.Blue);
                return true;
            }

            if (metadata.InstalledPlugins.Count == 0)
            {
                Logger.Raw($"[PLUGIN MANAGER] No plugins installed for port {port}. Skipped.", ConsoleColor.Blue);
                metadata.LastUpdateCheck = DateTime.UtcNow;

                Logger.Raw("[PLUGIN MANAGER] Writing metadata...", ConsoleColor.Blue);
                File.WriteAllText(metadataPath, JsonSerializer.ToJsonString(metadata));
                return true;
            }

            Logger.Raw("[PLUGIN MANAGER] Reading LocalAdmin config file...", ConsoleColor.Blue);
            await Plugin.Instance.LoadInternalJson();

            Logger.Raw("[PLUGIN MANAGER] Processing installed plugins...", ConsoleColor.Blue);

            int ok = 0, failed = 0, outdated = 0, fixedOutdated = 0, i = 0;

            foreach (var plugin in metadata.InstalledPlugins)
            {
                i++;
                Logger.Raw($"[PLUGIN MANAGER] Querying plugin {plugin.Key} ({i}/{metadata.InstalledPlugins.Count})...", ConsoleColor.Blue);
                var qr = await PluginInstaller.TryCachePlugin(plugin.Key, true);

                if (!qr.Success)
                {
                    failed++;
                    continue;
                }

                if (qr.Result.Version == plugin.Value.CurrentVersion)
                {
                    Logger.Raw($"[PLUGIN MANAGER] Plugin {plugin.Key} (v. {plugin.Value.CurrentVersion}) is up to date!", ConsoleColor.DarkGreen);
                    ok++;
                    continue;
                }

                if (plugin.Value.TargetVersion == null ||
                    plugin.Value.TargetVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Raw($"[PLUGIN MANAGER] Plugin {plugin.Key} (v. {plugin.Value.CurrentVersion}) is outdated! Latest version: {qr.Result.Version}.", ConsoleColor.Yellow);
                    outdated++;
                    continue;
                }

                Logger.Raw($"[PLUGIN MANAGER] Plugin {plugin.Key} (v. {plugin.Value.CurrentVersion}) is outdated, but a specific version was installed! Latest version: {qr.Result.Version}.", ConsoleColor.Gray);
                fixedOutdated++;
            }

            Logger.Raw($"[PLUGIN MANAGER] Finished checking for plugins updates for port {port}. Up to date: {ok}, outdated: {outdated}, outdated (with specific version set): {fixedOutdated}, failed: {failed}.", ConsoleColor.DarkGreen);

            Logger.Raw("[PLUGIN MANAGER] Writing LocalAdmin config file...", ConsoleColor.Blue);
            await Plugin.Instance.SaveInternalJson();

            metadata.LastUpdateCheck = DateTime.UtcNow;

            Logger.Raw("[PLUGIN MANAGER] Writing metadata...", ConsoleColor.Blue);
            File.WriteAllText(metadataPath, JsonSerializer.ToJsonString(metadata));
            return false;
        }
        catch (Exception e)
        {
            Logger.Raw($"[PLUGIN MANAGER] Failed to check for plugin updates for port {port}! Exception: {e.Message}",
                ConsoleColor.Red);

            return false;
        }
    }

    internal static async Task UpdatePlugins(string port, string framework, bool ignoreLocks, bool overwrite, bool skipUpdateCheck)
    {
        var pluginsPath = PluginInstaller.PluginsPath(port, framework);
        var metadataPath = pluginsPath + "metadata.json";

        try
        {
            if (!File.Exists(metadataPath) || !Directory.Exists(pluginsPath))
            {
                Logger.Raw($"[PLUGIN MANAGER] No metadata file for port {port}. Skipped.", ConsoleColor.Blue);
                return;
            }

            Logger.Raw($"[PLUGIN MANAGER] Reading metadata for port {port}...", ConsoleColor.Blue);
            var metadata = JsonSerializer.Deserialize<ServerPluginsConfig>(File.ReadAllText(metadataPath));

            if (metadata == null || metadata.InstalledPlugins.Count == 0)
            {
                Logger.Raw($"[PLUGIN MANAGER] No plugins installed for port {port}. Skipped.", ConsoleColor.Blue);
                return;
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

                if (!await CheckForUpdates(port, framework))
                {
                    Logger.Raw("[PLUGIN MANAGER] Plugins update check failed! Aborting plugins update.", ConsoleColor.Red);
                    return;
                }

                Logger.Raw($"[PLUGIN MANAGER] Reading metadata for port {port}...", ConsoleColor.Blue);
                metadata = JsonSerializer.Deserialize<ServerPluginsConfig>(File.ReadAllText(metadataPath));

                if (metadata == null || metadata.InstalledPlugins.Count == 0)
                {
                    Logger.Raw($"[PLUGIN MANAGER] No plugins installed for port {port}. Skipped.", ConsoleColor.Blue);
                    return;
                }
            }

            Logger.Raw("[PLUGIN MANAGER] Reading LocalAdmin config file...", ConsoleColor.Blue);
            await Plugin.Instance.LoadInternalJson();

            var i = 0;
            List<string> toRemove = new();

            foreach (var plugin in metadata.InstalledPlugins)
            {
                i++;

                Logger.Raw($"[PLUGIN MANAGER] Processing plugin {plugin.Key} ({i}/{metadata.InstalledPlugins.Count})...", ConsoleColor.Blue);

                var safeName = plugin.Key.Replace("/", "_");
                var pluginPath = pluginsPath + $"{safeName}.dll";

                if (!File.Exists(pluginPath))
                {
                    toRemove.Add(plugin.Key);
                    Logger.Raw($"[PLUGIN MANAGER] Plugin {plugin.Key} has been manually uninstalled. Skipped.", ConsoleColor.Gray);
                    continue;
                }

                var currentHash = FileHasher.GetFileHashSha256(pluginPath);
                var skipVersionCheck = false;

                if (currentHash != plugin.Value.FileHash)
                {
                    if (!overwrite)
                    {
                        Logger.Raw($"[PLUGIN MANAGER] Plugin {plugin.Key} has been manually updated! Run update with \"-o\" argument to overwrite. Skipped.", ConsoleColor.Yellow);
                        continue;
                    }

                    skipVersionCheck = true;
                    Logger.Raw($"[PLUGIN MANAGER] Plugin {plugin.Key} has been manually updated!", ConsoleColor.Yellow);
                }

                if (plugin.Value.TargetVersion != null &&
                    !plugin.Value.TargetVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Raw($"[PLUGIN MANAGER] Plugin {plugin.Key} has a specific version set. Skipped.", ConsoleColor.Gray);
                    continue;
                }

                if (!Plugin.DataJson!.PluginVersionCache!.ContainsKey(plugin.Key))
                {
                    Logger.Raw($"[PLUGIN MANAGER] Plugin {plugin.Key} is not cached! Skipped.", ConsoleColor.Yellow);
                    continue;
                }

                var cachedPlugin = Plugin.DataJson.PluginVersionCache[plugin.Key];

                if (!skipVersionCheck && cachedPlugin.Version.Equals(plugin.Value.CurrentVersion, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Raw($"[PLUGIN MANAGER] Plugin {plugin.Key} is up to date! Skipped.", ConsoleColor.DarkGreen);
                    continue;
                }

                Logger.Raw($"[PLUGIN MANAGER] Updating plugin {plugin.Key}...", ConsoleColor.Blue);
                await PluginInstaller.TryInstallPlugin(plugin.Key, cachedPlugin, "latest", port, overwrite, ignoreLocks);
            }

            if (toRemove.Count != 0)
            {
                Logger.Raw($"[PLUGIN MANAGER] Removing manually uninstalled plugins from metadata file...", ConsoleColor.Blue);

                Logger.Raw($"[PLUGIN MANAGER] Reading metadata for port {port}...", ConsoleColor.Blue);
                metadata = JsonSerializer.Deserialize<ServerPluginsConfig>(File.ReadAllText(metadataPath));

                if (metadata == null || metadata.InstalledPlugins.Count == 0)
                {
                    Logger.Raw("[PLUGIN MANAGER] Reading metadata filed.", ConsoleColor.Red);
                    return;
                }

                foreach (var plugin in toRemove)
                {
                    metadata.InstalledPlugins.Remove(plugin);
                }

                Logger.Raw("[PLUGIN MANAGER] Writing metadata...", ConsoleColor.Blue);
                File.WriteAllText(metadataPath, JsonSerializer.ToJsonString(metadata));
            }
        }
        catch (Exception e)
        {
            Logger.Raw($"[PLUGIN MANAGER] Failed to update plugins for port {port}! Exception: {e.Message}",
                ConsoleColor.Red);
        }
    }
}