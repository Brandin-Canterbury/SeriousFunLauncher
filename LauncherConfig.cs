using System;
using System.IO;
using System.Web.Script.Serialization;

namespace SeriousFunLauncher
{
    /// <summary>
    /// The launcher's own settings, stored per user in %AppData%\SeriousFunLauncher\launcher.json.
    /// </summary>
    public sealed class LauncherConfig
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SeriousFunLauncher",
            "launcher.json"
        );

        private static readonly string DefaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SeriousFun"
        );

        public string UoDirectory { get; set; } = Path.Combine(DefaultRoot, "UO Classic");
        public string TazUoDirectory { get; set; } = Path.Combine(DefaultRoot, "TazUO");
        public string ServerHost { get; set; } = LauncherConstants.DefaultServerHost;
        public int ServerPort { get; set; } = LauncherConstants.DefaultServerPort;
        public bool CheckClientUpdates { get; set; } = false;
        public bool CloseOnLaunch { get; set; } = true;

        public static LauncherConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    return new JavaScriptSerializer().Deserialize<LauncherConfig>(File.ReadAllText(ConfigPath));
                }
            }
            catch
            {
                // A corrupt config just falls back to defaults.
            }

            return new LauncherConfig();
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            File.WriteAllText(ConfigPath, new JavaScriptSerializer().Serialize(this));
        }
    }
}
