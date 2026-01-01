using Exiled.API.Features;
using LabApi.Loader;
using LabApi.Loader.Features.Paths;


namespace PluginManager_V2.PluginManager
{
    public static class PluginPaths
    {

        public static string Staging => PathManager.SecretLab.FullName;

        public static string InternalJsonDataPath => LabApi.Plugins+"/internal_data.json";

        public static class LabApi
        {
            public static string Plugins => PathManager.Plugins.FullName;
            public static string Dependencies => PathManager.Dependencies.FullName;
            
            public static string Manifest => PathManager.SecretLab.FullName + "/plugin_manifest.json";
        }

        // Need to use reflection since Exiled may not be present
        public static class Exiled
        {
            public static string Plugins
            {
                get
                {
                    foreach (var dependency in PluginLoader.Dependencies)
                    {
                        if (dependency.FullName.Contains("Exiled.API"))
                        {
                            // Exiled is present
                            //Exiled.API.Features.Paths.Plugins
                            var type = dependency.GetType("Exiled.API.Features.Paths");
                            var property = type.GetProperty("Plugins");
                            if (property != null)
                            {
                                var path = property.GetValue(null) as string;
                                return path;
                            }
                        }
                    }

                    return null;
                }
            }

            public static string Dependencies
            {
                get
                {
                    foreach (var dependency in PluginLoader.Dependencies)
                    {
                        if (dependency.FullName.Contains("Exiled.API"))
                        {
                            // Exiled is present
                            //Exiled.API.Features.Paths.Dependencies
                            var type = dependency.GetType("Exiled.API.Features.Paths");
                            var property = type.GetProperty("Dependencies");
                            if (property != null)
                            {
                                var path = property.GetValue(null) as string;
                                return path;
                            }
                        }
                    }

                    return null;
                }
            }
            
            public static string Manifest
            {
                get
                {
                    if(Plugins==null) return null;
                    return Plugins + "/plugin_manifest.json";
                }
            }
        }
    }
}