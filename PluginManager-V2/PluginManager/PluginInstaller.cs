using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Cryptography;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Paths;
using PluginManager_V2.JSON.Objects;
using PluginManager_V2.Utils;
using UserSettings.GUIElements;
using Utf8Json;

namespace PluginManager_V2.PluginManager {

    internal static class PluginInstaller
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(45),
            DefaultRequestHeaders = { { "User-Agent", $"LocalAdmin (PluginManager-V2 {Plugin.Instance.Version} by TayTay)" } }
        };

        internal static void RefreshPat() => HttpClient.DefaultRequestHeaders.Authorization =
            Plugin.DataJson!.GitHubPersonalAccessToken == null
                ? null
                : new AuthenticationHeaderValue("Bearer", Plugin.DataJson.GitHubPersonalAccessToken);

        internal static string PluginsPath(string port, string framework)
        {
            return framework.ToLower() switch
            {
                "labapi" => $"{PluginPaths.LabApi.Plugins}/{port}/",
                "exiled" => $"{PluginPaths.Exiled.Plugins}/{(port.Equals("global")?"":$"{port}/")}",
                _ => throw new ArgumentException("Invalid framework specified.")
            };
        }

        private static string DependenciesPath(string port, string framework)
        {
            return framework.ToLower() switch
            {
                "labapi" => $"{PluginPaths.LabApi.Dependencies}/{port}/",
                "exiled" => $"{PluginPaths.Exiled.Dependencies}/{(port.Equals("global")?"":$"{port}/")}",
                _ => throw new ArgumentException("Invalid framework specified.")
            };
        }
        
        private static string TempPath(ushort port) => $"{PluginPaths.Staging}";

        internal const uint DefaultLockTime = 30000;

        private static async Task<QueryResult> QueryRelease(string name, string url, bool interactive)
        {
            try
            {
                using var response = await HttpClient.GetAsync(url);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Logger.Raw($"[PLUGIN MANAGER] Failed to query {url}! Is the GitHub Personal Access Token set correctly? (Status code: {response.StatusCode})", ConsoleColor.Red);
                    return new();
                }

                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                {
                    Logger.Raw($"[PLUGIN MANAGER] Failed to query {url}! (Status code: {response.StatusCode})", ConsoleColor.Red);
                    return new();
                }

                var data = JsonSerializer.Deserialize<GitHubRelease>(await response.Content.ReadAsStringAsync());

                if (data.tag_name == null)
                {
                    if (interactive)
                        Logger.Raw($"[PLUGIN MANAGER] Failed to process plugin {name} - response is null.", ConsoleColor.Red);

                    return new();
                }

                if (data.message != null)
                {
                    if (response.StatusCode == HttpStatusCode.NotFound || data.message.Equals("Not Found", StringComparison.Ordinal))
                    {
                        if (interactive)
                            Logger.Raw($"[PLUGIN MANAGER] Failed to process plugin {name} - plugin release not found or no public release/specified version found.", ConsoleColor.Red);
                        return new();
                    }

                    if (interactive)
                        Logger.Raw($"[PLUGIN MANAGER] Failed to process plugin {name}. Exception: {data.message}", ConsoleColor.Red);
                    return new();
                }

                if (data.assets == null || data.assets.Count == 0)
                {
                    if (interactive)
                        Logger.Raw($"[PLUGIN MANAGER] Failed to process plugin {name} - no assets found.", ConsoleColor.Red);
                    return new();
                }

                string? pluginUrl = null;
                string? dependenciesUrl = null;

                var designatedForNwApi = false;
                var nonNwApiFound = 0;

                foreach (var asset in data.assets)
                {
                    if (asset.name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        var thisNw = asset.name.EndsWith("-nw.dll", StringComparison.OrdinalIgnoreCase);

                        if (designatedForNwApi)
                        {
                            if (!thisNw)
                                continue;

                            if (interactive)
                                Logger.Raw($"[PLUGIN MANAGER] Failed to process plugin {name} - multiple plugin DLLs marked for NW API usage found.", ConsoleColor.Red);
                            return new();
                        }

                        if (thisNw)
                            nonNwApiFound = 0;
                        else
                            nonNwApiFound++;

                        pluginUrl = asset.url;
                        designatedForNwApi = thisNw;
                    }
                    else if (asset.name.Equals("dependencies.zip", StringComparison.OrdinalIgnoreCase))
                        dependenciesUrl = asset.url;
                    
                }

                if (pluginUrl == null)
                {
                    if (interactive)
                        Logger.Raw($"[PLUGIN MANAGER] Failed to process plugin {name} - no plugin DLL found.", ConsoleColor.Red);
                    return new();
                }

                if (nonNwApiFound > 1)
                {
                    if (interactive)
                        Logger.Raw($"[PLUGIN MANAGER] Failed to process plugin {name} - multiple matching plugin DLLs found, none is explicitly designated for NW API usage.", ConsoleColor.Red);
                    return new();
                }

                return new(new PluginVersionCache
                {
                    Version = data.tag_name!,
                    ReleaseId = data.id,
                    DependenciesDownloadUrl = dependenciesUrl,
                    DllDownloadUrl = pluginUrl,
                    LastRefreshed = DateTime.UtcNow,
                    PublishmentTime = data.published_at
                });
            }
            catch (Exception e)
            {
                Logger.Raw($"[PLUGIN MANAGER] Failed to process plugin {url}! Exception: {e.Message}", ConsoleColor.Red);
                return new();
            }
        }

        internal static async Task<QueryResult> TryCachePlugin(string name, bool interactive)
        {
            var response = await QueryRelease(name, $"https://api.github.com/repos/{name}/releases/latest", interactive);

            if (!response.Success)
                return response;

            if (Plugin.DataJson!.PluginVersionCache!.ContainsKey(name))
                Plugin.DataJson.PluginVersionCache![name] = response.Result;
            else Plugin.DataJson.PluginVersionCache!.Add(name, response.Result);

            return response;
        }

        internal static async Task<QueryResult>
            TryGetVersionDetails(string name, string version, bool interactive = true) => await QueryRelease(name,
            $"https://api.github.com/repos/{name}/releases/tags/{version}", interactive);

        internal static async Task<bool> TryInstallPlugin(string name, string framework, PluginVersionCache plugin, string targetVersion, string port, bool overwriteFiles)
        {
            var tempPath = TempPath(Server.Port);

            try
            {
                var pluginsPath = PluginsPath(port, framework);
                var depPath = DependenciesPath(port, framework);

                if (!Directory.Exists(pluginsPath))
                    Directory.CreateDirectory(pluginsPath);

                if (!Directory.Exists(depPath))
                    Directory.CreateDirectory(depPath);

                var safeName = name.Replace("/", "_");
                var metadataPath = pluginsPath + "metadata.json";
                var pluginPath = pluginsPath + $"{safeName}.dll";
                var abort = false;
                ServerPluginsConfig? metadata = null;

                if (!File.Exists(metadataPath))
                {
                    var mt = new ServerPluginsConfig();
                    File.WriteAllText(metadataPath, JsonSerializer.ToJsonString(mt));
                }

                if (!overwriteFiles)
                {
                    Logger.Raw("[PLUGIN MANAGER] Checking if plugin is already installed...", ConsoleColor.Blue);

                    metadata = JsonSerializer.Deserialize<ServerPluginsConfig>(File.ReadAllText(metadataPath));

                    if (metadata!.InstalledPlugins.ContainsKey(name))
                    {
                        var installedPlugin = metadata.InstalledPlugins[name];
                        if (installedPlugin.CurrentVersion == plugin.Version)
                        {
                            Logger.Raw(
                                $"[PLUGIN MANAGER] Plugin {name} is already installed (in this version)! Skipping...",
                                ConsoleColor.Yellow);

                            return true;
                        }
                    }

                    metadata = null;
                }

                if (!Directory.Exists(tempPath))
                    Directory.CreateDirectory(tempPath);

                List<string> currentDependencies = new();

                if (plugin.DependenciesDownloadUrl != null)
                {
                    Logger.Raw($"[PLUGIN MANAGER] Downloading dependencies for plugin {name}...",
                        ConsoleColor.Blue);

                    var extractDir = $"{tempPath}{safeName}-dependencies";

                    try
                    {
                        bool dwlOk = await Download(name + "-dependencies", plugin.DependenciesDownloadUrl,
                            $"{tempPath}{safeName}-dependencies.zip");

                        if (!dwlOk)
                        {
                            Logger.Raw(
                                $"[PLUGIN MANAGER] Failed to download plugin dependencies {name}! Aborting download!",
                                ConsoleColor.Red);
                            return false;
                        }

                        if (Directory.Exists(extractDir))
                            Directory.Delete(extractDir, true);

                        Directory.CreateDirectory(extractDir);

                        Logger.Raw($"[PLUGIN MANAGER] Unpacking dependencies for plugin {name}...",
                            ConsoleColor.Blue);
                        ZipFile.ExtractToDirectory($"{tempPath}{safeName}-dependencies.zip", extractDir);

                        Logger.Raw("[PLUGIN MANAGER] Loading metadata file...", ConsoleColor.Blue);
                        metadata = JsonSerializer.Deserialize<ServerPluginsConfig>(File.ReadAllText(metadataPath));
                        Logger.Raw($"[PLUGIN MANAGER] Processing dependencies for plugin {name}...",
                            ConsoleColor.Blue);

                        var deps = Directory.GetFiles(extractDir, "*.dll", SearchOption.AllDirectories);

                        foreach (var dep in deps)
                        {
                            var fn = Path.GetFileName(dep);
                            Logger.Raw($"[PLUGIN MANAGER] Processing dependency {fn}...", ConsoleColor.Blue);

                            currentDependencies.Add(fn);
                            var installed = File.Exists(depPath + fn);
                            var newHash = FileHasher.GetFileHashSha256(dep);

                            if (!installed && metadata!.Dependencies.ContainsKey(fn))
                                metadata.Dependencies.Remove(fn);

                            if (!metadata!.Dependencies.ContainsKey(fn))
                            {
                                var usedBy = new List<string> { name };

                                if (installed)
                                {
                                    if (newHash != FileHasher.GetFileHashSha256(depPath + fn))
                                    {
                                        if (!overwriteFiles)
                                        {
                                            Logger.Raw(
                                                $"[PLUGIN MANAGER] Dependency {fn} is already installed in a different version! To overwrite it run installation with -o arg.",
                                                ConsoleColor.Red);
                                            Logger.Raw("[PLUGIN MANAGER] Aborting download!", ConsoleColor.Red);
                                            return false;
                                        }

                                        Logger.Raw(
                                            $"[PLUGIN MANAGER] Dependency {fn} is already installed in a different version! Overwriting...",
                                            ConsoleColor.Yellow);
                                    }

                                    Logger.Raw(
                                        $"[PLUGIN MANAGER] Dependency {fn} is already installed, but not registered in metadata file! Adding to metadata file...",
                                        ConsoleColor.Yellow);
                                }

                                metadata.Dependencies.Add(fn, new Dependency
                                {
                                    FileHash = newHash,
                                    InstallationDate = DateTime.UtcNow,
                                    UpdateDate = DateTime.UtcNow,
                                    InstalledByPlugins = usedBy,
                                    ManuallyInstalled = installed
                                });

                                if(File.Exists(depPath + fn))
                                {
                                    File.Delete(depPath + fn);
                                }
                                File.Move(dep, depPath + fn);
                                Logger.Raw($"[PLUGIN MANAGER] Installed dependency {fn}.", ConsoleColor.Blue);
                            }
                            else
                            {
                                var depMeta = metadata.Dependencies[fn];
                                var currentHash = FileHasher.GetFileHashSha256(depPath + fn);
                                var overwrite = false;

                                if (currentHash != depMeta.FileHash)
                                {
                                    if (!overwriteFiles)
                                    {
                                        Logger.Raw(
                                            $"[PLUGIN MANAGER] Dependency {fn} has been manually modified! To overwrite it run installation with -o arg.",
                                            ConsoleColor.Red);
                                        Logger.Raw("[PLUGIN MANAGER] Aborting download!", ConsoleColor.Red);
                                        return false;
                                    }

                                    Logger.Raw(
                                        $"[PLUGIN MANAGER] Dependency {fn} has been manually modified! Overwriting...",
                                        ConsoleColor.Yellow);
                                    overwrite = true;
                                }

                                if (newHash != depMeta.FileHash)
                                {
                                    if (!overwriteFiles)
                                    {
                                        Logger.Raw(
                                            $"[PLUGIN MANAGER] Dependency {fn} is already installed in a different version! To overwrite it run installation with -o arg.",
                                            ConsoleColor.Red);
                                        Logger.Raw("[PLUGIN MANAGER] Aborting download!", ConsoleColor.Red);
                                        return false;
                                    }

                                    Logger.Raw(
                                        $"[PLUGIN MANAGER] Dependency {fn} is already installed in a different version! Overwriting...",
                                        ConsoleColor.Yellow);
                                    overwrite = true;
                                }

                                if (overwrite)
                                {
                                    metadata.Dependencies[fn].FileHash = newHash;
                                    metadata.Dependencies[fn].UpdateDate = DateTime.UtcNow;
                                }

                                if (!metadata.Dependencies[fn].InstalledByPlugins.Contains(name))
                                    metadata.Dependencies[fn].InstalledByPlugins.Add(name);

                                if (overwrite)
                                {
                                    File.Move(dep, depPath + fn);
                                    Logger.Raw($"[PLUGIN MANAGER] Installed dependency {fn}.",
                                        ConsoleColor.Blue);
                                }
                                else
                                    Logger.Raw($"[PLUGIN MANAGER] Dependency {fn} is already installed.",
                                        ConsoleColor.Blue);
                            }
                        }
                    }
                    finally
                    {
                        if (metadata != null)
                        {
                            Logger.Raw("[PLUGIN MANAGER] Writing metadata...", ConsoleColor.Blue);
                            File.WriteAllText(metadataPath, JsonSerializer.ToJsonString(metadata));

                            metadata = null;
                        }

                        Logger.Raw("[PLUGIN MANAGER] Cleaning up...", ConsoleColor.Blue);
                        Directory.Delete(extractDir);
                        Directory.Delete($"{tempPath}/dependencies.zip");
                    }

                    if (abort)
                        return false;
                }

                Logger.Raw($"[PLUGIN MANAGER] Downloading plugin {name}...", ConsoleColor.Blue);
                var runMaintenance = false;

                try
                {
                    bool dwlOk = await Download(name, plugin.DllDownloadUrl,
                        $"{tempPath}{safeName}.dll");

                    if (!dwlOk)
                    {
                        Logger.Raw(
                            $"[PLUGIN MANAGER] Failed to download plugin {name}! Aborting download!", ConsoleColor.Red);
                        return false;
                    }

                    Logger.Raw($"[PLUGIN MANAGER] Installing plugin {name}...", ConsoleColor.Blue);
                    File.Move($"{tempPath}{safeName}.dll", pluginPath);

                    var hash = FileHasher.GetFileHashSha256(pluginPath);

                    Logger.Raw("[PLUGIN MANAGER] Reading metadata...", ConsoleColor.Blue);
                    metadata = JsonSerializer.Deserialize<ServerPluginsConfig>(File.ReadAllText(metadataPath));

                    Logger.Raw("[PLUGIN MANAGER] Processing metadata...", ConsoleColor.Blue);
                    if (metadata!.InstalledPlugins.ContainsKey(name))
                    {
                        metadata.InstalledPlugins[name].FileHash = hash;
                        metadata.InstalledPlugins[name].UpdateDate = DateTime.UtcNow;
                        metadata.InstalledPlugins[name].CurrentVersion = plugin.Version;
                        metadata.InstalledPlugins[name].TargetVersion = targetVersion;
                    }
                    else
                        metadata.InstalledPlugins.Add(name, new InstalledPlugin
                        {
                            FileHash = hash,
                            InstallationDate = DateTime.UtcNow,
                            UpdateDate = DateTime.UtcNow,
                            CurrentVersion = plugin.Version,
                            TargetVersion = targetVersion
                        });

                    foreach (var dependency in metadata.Dependencies)
                    {
                        if (dependency.Value.InstalledByPlugins.Contains(name) &&
                            !currentDependencies.Contains(dependency.Key))
                        {
                            metadata.Dependencies[dependency.Key].InstalledByPlugins.Remove(name);
                            Logger.Raw(
                                $"[PLUGIN MANAGER] Dependency {dependency.Key} is no longer needed by plugin {name}.",
                                ConsoleColor.Blue);

                            if (!dependency.Value.ManuallyInstalled &&
                                metadata.Dependencies[dependency.Key].InstalledByPlugins.Count == 0)
                            {
                                runMaintenance = true;

                                Logger.Raw(
                                    $"[PLUGIN MANAGER] Dependency {dependency.Key} is no longer needed by any plugin. Maintenance will be performed.",
                                    ConsoleColor.Blue);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Raw($"[PLUGIN MANAGER] Failed to install plugin {name}! Exception: {e.Message}",
                        ConsoleColor.Red);
                    return false;
                }
                finally
                {
                    if (metadata != null)
                    {
                        Logger.Raw("[PLUGIN MANAGER] Writing metadata...", ConsoleColor.Blue);
                        File.WriteAllText(metadataPath, JsonSerializer.ToJsonString(metadata));
                    }
                }

                Logger.Raw($"[PLUGIN MANAGER] Plugin {name} has been successfully installed!", ConsoleColor.DarkGreen);

                if (runMaintenance)
                {
                    Logger.Raw($"[PLUGIN MANAGER] Performing automatic maintenance...", ConsoleColor.Blue);
                    await PluginsMaintenance(port, framework, false);
                }

                return true;
            }
            catch (Exception e)
            {
                Logger.Raw($"[PLUGIN MANAGER] Failed to download and install plugin {name}! Exception: {e.Message}", ConsoleColor.Red);
                return false;
            }
            finally
            {
                try
                {
                    Directory.Delete(tempPath);
                }
                catch (Exception e)
                {
                    Logger.Raw($"[PLUGIN MANAGER] Failed to delete temp directory {tempPath}! Exception: {e.Message}",
                        ConsoleColor.Red);
                }
            }
        }

        private static async Task<bool> Download(string name, string url, string targetPath)
        {
            var success = false;
            var octetStreamHeader = new MediaTypeWithQualityHeaderValue("application/octet-stream");
            using var fs = File.OpenWrite(targetPath);
            HttpClient.DefaultRequestHeaders.Accept.Add(octetStreamHeader);

            try
            {
                using var response = await HttpClient.GetAsync(url);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Logger.Raw($"[PLUGIN MANAGER] Failed to query {url}! Is the GitHub Personal Access Token set correctly? (Status code: {response.StatusCode})", ConsoleColor.Red);
                    return false;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Raw($"[PLUGIN MANAGER] Failed to download plugin {name}! (Status code: {response.StatusCode})", ConsoleColor.Red);
                    return false;
                }

                await response.Content.CopyToAsync(fs);
                success = true;
                return true;
            }
            catch (Exception e)
            {
                Logger.Raw($"[PLUGIN MANAGER] Failed to download plugin {name}! Exception: {e.Message}", ConsoleColor.Red);
                return false;
            }
            finally
            {
                await fs.FlushAsync();
                fs.Close();

                HttpClient.DefaultRequestHeaders.Accept.Remove(octetStreamHeader);

                if (!success)
                    File.Delete(targetPath);
            }
        }

        internal static async Task<bool> TryUninstallPlugin(string name, string framework,  string port, bool ignoreLocks, bool skipUpdate)
        {
            var performUpdate = OfficialPluginsList.IsRefreshNeeded();

            if (skipUpdate)
                performUpdate = false;

            if (performUpdate)
            {
                Logger.Raw("[PLUGIN MANAGER] Refreshing plugins list...", ConsoleColor.Yellow);
                await OfficialPluginsList.RefreshOfficialPluginsList();
            }

            name = OfficialPluginsList.ResolvePluginAlias(name, PluginAliasFlags.All);

            if (name.Count(x => x == '/') != 1)
            {
                Logger.Raw("[PLUGIN MANAGER] Plugin name is invalid!", ConsoleColor.Red);
                return false;
            }

            ServerPluginsConfig? metadata = null;
            var pluginsPath = PluginsPath(port, framework);

            if (!Directory.Exists(pluginsPath))
                Directory.CreateDirectory(pluginsPath);

            var success = false;
            var metadataPath = PluginsPath(port, framework) + "metadata.json";

            try
            {
                var depPath = DependenciesPath(port, framework);

                if (!Directory.Exists(depPath))
                    Directory.CreateDirectory(depPath);

                var safeName = name.Replace("/", "_");

                var pluginPath = PluginsPath(port, framework) + $"{safeName}.dll";

                try
                {
                    if (FileUtils.DeleteIfExists(pluginPath))
                        Logger.Raw("[PLUGIN MANAGER] Plugin DLL deleted.", ConsoleColor.Blue);
                    else Logger.Raw("[PLUGIN MANAGER] Plugin DLL does not exist.", ConsoleColor.Yellow);
                }
                catch (Exception e)
                {
                    Logger.Raw($"[PLUGIN MANAGER] Failed to delete plugin {name}! Exception: {e.Message}",
                        ConsoleColor.Red);
                    return false;
                }

                if (!File.Exists(metadataPath))
                {
                    Logger.Raw("[PLUGIN MANAGER] Metadata file does not exist.", ConsoleColor.Yellow);
                    Logger.Raw("[PLUGIN MANAGER] Uninstallation complete.", ConsoleColor.Blue);
                    return true;
                }

                Logger.Raw("[PLUGIN MANAGER] Reading metadata...", ConsoleColor.Blue);
                metadata = JsonSerializer.Deserialize<ServerPluginsConfig>(File.ReadAllText(metadataPath));

                if (metadata == null)
                {
                    Logger.Raw("[PLUGIN MANAGER] Failed to read metadata (null)!", ConsoleColor.Yellow);
                    Logger.Raw("[PLUGIN MANAGER] Uninstallation complete.", ConsoleColor.Blue);
                    return true;
                }

                Logger.Raw("[PLUGIN MANAGER] Processing metadata...", ConsoleColor.Blue);
                if (metadata.InstalledPlugins.ContainsKey(name))
                {
                    metadata.InstalledPlugins.Remove(name);
                    File.WriteAllText(metadataPath, JsonSerializer.ToJsonString(metadata));
                }

                List<string> depToRemove = new();

                foreach (var dep in metadata.Dependencies)
                {
                    if (dep.Value.InstalledByPlugins.Contains(name))
                        dep.Value.InstalledByPlugins.Remove(name);

                    if (dep.Value.InstalledByPlugins.Count == 0 && !dep.Value.ManuallyInstalled)
                        depToRemove.Add(dep.Key);
                }

                foreach (var dep in depToRemove)
                {
                    try
                    {
                        Logger.Raw($"[PLUGIN MANAGER] Removing redundant dependency {dep}...",
                            ConsoleColor.Blue);

                        if (FileUtils.DeleteIfExists(depPath + dep))
                            Logger.Raw("[PLUGIN MANAGER] Dependency deleted.", ConsoleColor.Blue);
                        else Logger.Raw("[PLUGIN MANAGER] Dependency does not exist.", ConsoleColor.Yellow);

                        metadata.Dependencies.Remove(dep);
                    }
                    catch (Exception e)
                    {
                        Logger.Raw($"[PLUGIN MANAGER] Failed to delete dependency {dep}! Exception: {e.Message}",
                            ConsoleColor.Yellow);
                    }
                }

                success = true;
                return true;
            }
            catch (Exception e)
            {
                Logger.Raw($"[PLUGIN MANAGER] Failed to remove plugin {name}! Exception: {e.Message}",
                    ConsoleColor.Red);
                return false;
            }
            finally
            {
                if (metadata != null)
                {
                    Logger.Raw("[PLUGIN MANAGER] Writing metadata...", ConsoleColor.Blue);

                    File.WriteAllText(metadataPath, JsonSerializer.ToJsonString(metadata));
                }

                if (success)
                    Logger.Raw($"[PLUGIN MANAGER] Plugin {name} has been successfully uninstalled!", ConsoleColor.DarkGreen);
            }
        }

        internal static async Task<bool> PluginsMaintenance(string port, string framework, bool ignoreLocks)
        {
            var pluginsPath = PluginsPath(port,framework);

            if (!Directory.Exists(pluginsPath))
            {
                Logger.Raw($"[PLUGIN MANAGER] Plugins path for port {port} doesn't exist. No need to perform maintenance.", ConsoleColor.Blue);
                return true;
            }

            var depPath = DependenciesPath(port,framework);

            if (!Directory.Exists(depPath))
                Directory.CreateDirectory(depPath);

            ServerPluginsConfig? metadata = null;
            var success = false;
            var metadataPath = pluginsPath + "metadata.json";

            try
            {
                if (!File.Exists(metadataPath))
                {
                    Logger.Raw($"[PLUGIN MANAGER] Metadata file for port {port} doesn't exist. No need to perform maintenance.", ConsoleColor.Blue);
                    success = true;
                    return true;
                }

                Logger.Raw("[PLUGIN MANAGER] Reading metadata...", ConsoleColor.Blue);
                metadata = JsonSerializer.Deserialize<ServerPluginsConfig>(File.ReadAllText(metadataPath));

                if (metadata == null)
                {
                    Logger.Raw($"[PLUGIN MANAGER] Failed to parse metadata file for port {port}!", ConsoleColor.Red);
                    return false;
                }

                List<string> depToRemove = new(), plToRemove = new();

                foreach (var pl in metadata.InstalledPlugins)
                {
                    var pluginPath = pluginsPath + $"{pl.Key.Replace("/", "_")}.dll";

                    if (File.Exists(pluginPath))
                        continue;

                    plToRemove.Add(pl.Key);
                    Logger.Raw($"[PLUGIN MANAGER] Plugin {pl.Key} has been manually removed.", ConsoleColor.Blue);
                }

                foreach (var pl in plToRemove)
                    metadata.InstalledPlugins.Remove(pl);

                foreach (var dep in metadata.Dependencies)
                {
                    if (!File.Exists(depPath + dep.Key))
                    {
                        depToRemove.Add(dep.Key);
                        Logger.Raw($"[PLUGIN MANAGER] Dependency {dep.Key} has been manually removed.", ConsoleColor.Blue);
                        continue;
                    }

                    plToRemove.Clear();
                    foreach (var pl in dep.Value.InstalledByPlugins)
                    {
                        if (metadata.InstalledPlugins.ContainsKey(pl))
                            continue;

                        plToRemove.Add(pl);
                        Logger.Raw($"[PLUGIN MANAGER] Removed non-existing plugin {pl} from dependency {dep.Key}.", ConsoleColor.Blue);
                    }

                    foreach (var pl in plToRemove)
                        metadata.Dependencies[dep.Key].InstalledByPlugins.Remove(pl);

                    if (dep.Value.InstalledByPlugins.Count == 0 && !dep.Value.ManuallyInstalled)
                        depToRemove.Add(dep.Key);
                }

                foreach (var dep in depToRemove)
                {
                    try
                    {
                        Logger.Raw($"[PLUGIN MANAGER] Removing redundant dependency {dep}...",
                            ConsoleColor.Blue);

                        Logger.Raw(
                            FileUtils.DeleteIfExists(depPath + dep)
                                ? "[PLUGIN MANAGER] Dependency deleted."
                                : "[PLUGIN MANAGER] Dependency does not exist.", ConsoleColor.Blue);

                        metadata.Dependencies.Remove(dep);
                    }
                    catch (Exception e)
                    {
                        Logger.Raw($"[PLUGIN MANAGER] Failed to delete dependency {dep}! Exception: {e.Message}",
                            ConsoleColor.Yellow);
                    }
                }

                success = true;
                return true;
            }
            catch (Exception e)
            {
                Logger.Raw($"[PLUGIN MANAGER] Failed to perform maintenance for port {port}! Exception: {e.Message}",
                    ConsoleColor.Red);
                return false;
            }
            finally
            {
                if (metadata != null)
                {
                    Logger.Raw("[PLUGIN MANAGER] Writing metadata...", ConsoleColor.Blue);

                    File.WriteAllText(metadataPath, JsonSerializer.ToJsonString(metadata));
                }

                if (success)
                    Logger.Raw($"[PLUGIN MANAGER] Plugins maintenance for port {port} complete!", ConsoleColor.DarkGreen);
            }
        }

        internal readonly struct QueryResult
        {
            public QueryResult()
            {
                Success = false;
                Result = default;
            }

            public QueryResult(PluginVersionCache result)
            {
                Success = true;
                Result = result;
            }

            public readonly bool Success;
            public readonly PluginVersionCache Result;
        }
    }
    }