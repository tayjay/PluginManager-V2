using System;
using System.IO;
using System.Threading.Tasks;
using CommandSystem;
using LabApi.Features;
using LabApi.Features.Console;
using LabApi.Loader.Features.Plugins;
using PluginManager_V2.JSON.Objects;
using PluginManager_V2.PluginManager;
using JsonSerializer = Utf8Json.JsonSerializer;

namespace PluginManager_V2
{
    public class Plugin : Plugin<Config>
    {
        internal static DataJson? DataJson;
        public static Plugin Instance { get; private set; }
        
        public override void Enable()
        {
            Instance = this;
            Logger.Info(PluginPaths.LabApi.Plugins);
            Logger.Info(PluginPaths.LabApi.Dependencies);
            Logger.Info(PluginPaths.Exiled.Plugins);
            Logger.Info(PluginPaths.Exiled.Dependencies);
        }

        public override void Disable()
        {
            
        }

        internal async Task LoadInternalJson()
        {
            try
            {
                if (!File.Exists(PluginPaths.InternalJsonDataPath))
                {
                    DataJson = new DataJson();
                    await SaveInternalJson();
                }
                else
                {
                    DataJson = JsonSerializer.Deserialize<DataJson>(File.ReadAllText(PluginPaths.InternalJsonDataPath));
                }
            } catch (Exception e)
            {
                Logger.Error($"[PLUGIN MANAGER] Failed to load internal JSON data: {e}");
            }
        }

        internal async Task SaveInternalJson()
        {
            try
            {
                if (DataJson == null)
                    DataJson = new DataJson();

                File.WriteAllText(PluginPaths.InternalJsonDataPath, JsonSerializer.ToJsonString(DataJson));
            }
            catch (Exception e)
            {
                Logger.Error($"[PLUGIN MANAGER] Failed to save internal JSON data: {e}");
            }
            
        }

        public override string Name { get; } = "PluginManager-V2";
        public override string Description { get; } = "Reimplementing LocalAdmin-V2's PluginManager for LabAPI an EXILED plugins.";
        public override string Author { get; } = "TayTay";
        public override Version Version { get; } = typeof(Plugin).Assembly.GetName().Version;
        public override Version RequiredApiVersion { get; } = new Version(LabApiProperties.CompiledVersion);
    }
}