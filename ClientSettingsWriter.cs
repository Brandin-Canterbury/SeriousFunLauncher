using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace SeriousFunLauncher
{
    /// <summary>
    /// Writes the connection fields of TazUO's settings.json, keeping every other setting the
    /// player (or TazUO itself) has saved there.
    /// </summary>
    public static class ClientSettingsWriter
    {
        private const string SettingsFile = "settings.json";

        public static void Write(LauncherConfig config, string clientVersion)
        {
            string path = Path.Combine(config.TazUoDirectory, SettingsFile);
            var serializer = new JavaScriptSerializer();
            Dictionary<string, object> settings = null;

            if (File.Exists(path))
            {
                try
                {
                    settings = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                }
                catch
                {
                    // Unreadable settings are replaced with just the connection fields.
                }
            }

            settings = settings ?? new Dictionary<string, object>();
            settings["ip"] = config.ServerHost;
            settings["port"] = config.ServerPort;
            settings["ultimaonlinedirectory"] = config.UoDirectory;
            settings["clientversion"] = clientVersion;
            // ServUO rejects encrypted clients.
            settings["encryption"] = 0;

            File.WriteAllText(path, serializer.Serialize(settings));
        }
    }
}
