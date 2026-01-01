using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using LabApi.Features.Console;
using Newtonsoft.Json.Linq;
using PluginManager_V2.JSON.Objects;
using Utf8Json;

namespace PluginManager_V2.PluginManager;

public class OfficialPluginsList
{
    public static string ApiBaseUri = "https://plugins.scpslgame.com/api/v1/";
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(45),
            DefaultRequestHeaders = { { "User-Agent", "LocalAdmin v. " + Plugin.Instance.Version } }
        };

        internal static bool IsRefreshNeeded()
        {
            if (Plugin.DataJson!.LastPluginAliasesRefresh == null)
            {
                Logger.Raw("[PLUGIN MANAGER] Plugins list refresh was never performed.", ConsoleColor.Yellow);
                return true;
            }

            if ((DateTime.UtcNow - Plugin.DataJson.LastPluginAliasesRefresh).Value.TotalMinutes <= 10)
                return false;

            Logger.Raw("[PLUGIN MANAGER] Last plugins list refresh was was performed more than 10 minutes ago.",
                ConsoleColor.Yellow);
            return true;
        }

        
        internal static async Task RefreshOfficialPluginsList()
        {
            try
            {
                Logger.Raw($"[PLUGIN MANAGER] Refreshing plugins list...", ConsoleColor.Blue);
                var pageCount = await HttpClient.GetAsync($"{ApiBaseUri}plugin?limit=0");
                //{"message":"Plugins fetched successfully","data":{"data":[],"meta":{"total":99,"page":1,"limit":0,"totalPages":null}}}
                int totalPlugins = 0;

                if (!pageCount.IsSuccessStatusCode)
                {
                    var json = JsonSerializer.Deserialize<PluginResponse>(await pageCount.Content.ReadAsStringAsync());
                    totalPlugins = json.Data.Meta.Total;
                }

                if (totalPlugins <= 0)
                {
                    // Failed to find any plugins
                    Logger.Raw($"[PLUGIN MANAGER] Failed to find plugins list! (Status code: {pageCount.StatusCode})", ConsoleColor.Red);
                    return;
                }
                
                var response = await HttpClient.GetAsync($"{ApiBaseUri}plugin?limit={totalPlugins}");

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Raw($"[PLUGIN MANAGER] Failed to refresh plugins list! (Status code: {response.StatusCode})", ConsoleColor.Red);
                    return;
                }

                var data = JsonSerializer.Deserialize<PluginResponse>(await response.Content.ReadAsStringAsync());

                /*if (data == default)
                {
                    Logger.Raw($"[PLUGIN MANAGER] Failed to refresh plugins list! (deserialization error)", ConsoleColor.Red);
                    return;
                }*/

                Logger.Raw("[PLUGIN MANAGER] Reading LocalAdmin config file...", ConsoleColor.Blue);
                await Plugin.Instance!.LoadInternalJson();

                Plugin.DataJson!.AvailablePlugins = data.Data.Data;
                Plugin.DataJson.LastPluginAliasesRefresh = DateTime.UtcNow;

                Logger.Raw("[PLUGIN MANAGER] Writing LocalAdmin config file...", ConsoleColor.Blue);
                await Plugin.Instance.SaveInternalJson();

                Logger.Raw("[PLUGIN MANAGER] Plugins list has been refreshed!", ConsoleColor.DarkGreen);
            }
            catch (Exception e)
            {
                Logger.Raw($"[PLUGIN MANAGER] Failed to refresh plugins list! Exception: {e.Message}", ConsoleColor.Red);
            }
        }


        /*internal static string ResolvePluginAlias(string alias, PluginAliasFlags requiredFlags)
        {
            if (Plugin.DataJson == null || Plugin.DataJson.PluginAliases == null ||
                !Plugin.DataJson.PluginAliases.ContainsKey(alias))
                return alias;

            var pluginAlias = Plugin.DataJson.PluginAliases[alias];

            if (((PluginAliasFlags)pluginAlias.Flags & requiredFlags) == 0)
                return alias;

            Logger.Raw($"[PLUGIN MANAGER] Plugin name {alias} has been resolved to {pluginAlias.Repository}!", ConsoleColor.Gray);

            return pluginAlias.Repository;
        }*/
    
}