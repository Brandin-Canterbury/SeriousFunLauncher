using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SeriousFunLauncher
{
    /// <summary>
    /// Keeps the shard's own map files (edited statics, e.g. trees added or removed) in
    /// TazUO\ShardData and writes the list that tells TazUO to load them instead of EA's.
    /// </summary>
    /// <remarks>
    /// The files are the assets of one GitHub release (tag <see cref="LauncherConstants.ShardDataTag"/>).
    /// They live outside the UO folder because EA's patcher would put its own files back there.
    /// No release means no shard map edits, and TazUO uses the stock files.
    /// </remarks>
    public sealed class ShardDataUpdater
    {
        private const string FolderName = "ShardData";
        private const string ManifestFile = "manifest.json";
        private const string OverridesFile = "overrides.txt";

        // Only map files can be overridden, whatever else ends up attached to the release.
        private static readonly Regex AllowedFile = new Regex(
            @"^((statics|staidx)\d+x?\.mul|map\d+x?(LegacyMUL\.uop|\.mul))$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly StatusReporter _reporter;

        public ShardDataUpdater(StatusReporter reporter)
        {
            _reporter = reporter;
        }

        /// <summary>
        /// Brings ShardData up to date and returns the overrides list to pass to TazUO,
        /// or null when the shard has no map edits.
        /// </summary>
        public async Task<string> EnsureLatestAsync(string tazUoDirectory, CancellationToken cancellationToken)
        {
            string folder = Path.Combine(tazUoDirectory, FolderName);
            string overridesPath = Path.Combine(folder, OverridesFile);

            _reporter.Status("Checking for shard map updates...");
            _reporter.Progress(null);

            List<AssetInfo> assets;
            try
            {
                assets = await GetAssetsAsync(cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // Offline or GitHub hiccup: keep whatever was downloaded last time.
                if (File.Exists(overridesPath))
                {
                    _reporter.Status($"Couldn't check for shard map updates ({ex.Message}); using the saved maps.");
                    return overridesPath;
                }

                _reporter.Status($"Couldn't check for shard map updates ({ex.Message}).");
                return null;
            }

            Dictionary<string, string> manifest = LoadManifest(folder);
            List<AssetInfo> stale = assets
                .Where(asset => !File.Exists(Path.Combine(folder, asset.Name))
                                || !manifest.TryGetValue(asset.Name, out string key)
                                || key != asset.Key)
                .ToList();
            List<string> removed = manifest.Keys
                .Where(name => assets.All(asset => !asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if ((stale.Count > 0 || removed.Count > 0) && UoClientInstaller.FindProcessesIn(tazUoDirectory).Any())
            {
                throw new InvalidOperationException("TazUO is running. Close the game, then press Play again.");
            }

            Directory.CreateDirectory(folder);

            for (int i = 0; i < stale.Count; i++)
            {
                AssetInfo asset = stale[i];
                _reporter.Status($"Downloading shard map {asset.Name} ({i + 1} of {stale.Count})...");
                await DownloadAsync(asset, folder, cancellationToken);
                manifest[asset.Name] = asset.Key;
                SaveManifest(folder, manifest);
            }

            foreach (string name in removed)
            {
                TryDelete(Path.Combine(folder, name));
                manifest.Remove(name);
            }

            SaveManifest(folder, manifest);

            if (assets.Count == 0)
            {
                TryDelete(overridesPath);
                _reporter.Status("The shard uses the standard maps.");
                return null;
            }

            var lines = new StringBuilder("# Written by SeriousFun Launcher. Shard map files TazUO loads instead of EA's.\r\n");
            foreach (AssetInfo asset in assets)
            {
                lines.Append(asset.Name.ToLowerInvariant()).Append('=').Append(Path.Combine(folder, asset.Name)).Append("\r\n");
            }

            File.WriteAllText(overridesPath, lines.ToString());
            _reporter.Status(stale.Count > 0 ? "Shard maps updated." : "Shard maps are up to date.");
            return overridesPath;
        }

        private async Task DownloadAsync(AssetInfo asset, string folder, CancellationToken cancellationToken)
        {
            string target = Path.Combine(folder, asset.Name);
            string partial = target + ".part";

            try
            {
                await Downloader.DownloadFileAsync(asset.DownloadUrl, partial, _reporter, cancellationToken);

                if (asset.Sha256 != null)
                {
                    string actual = await Task.Run(() => HashFile(partial), cancellationToken);
                    if (!actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"The download of {asset.Name} is corrupt. Press Play to try again.");
                    }
                }

                TryDelete(target);
                File.Move(partial, target);
            }
            finally
            {
                TryDelete(partial);
            }
        }

        /// <summary>The release's map files, or none when the release doesn't exist.</summary>
        private static async Task<List<AssetInfo>> GetAssetsAsync(CancellationToken cancellationToken)
        {
            string url = $"https://api.github.com/repos/{LauncherConstants.ShardDataRepository}/releases/tags/{LauncherConstants.ShardDataTag}";

            using (HttpResponseMessage response = await Downloader.Http.GetAsync(url, cancellationToken))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return new List<AssetInfo>();
                }

                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                var release = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);

                return ((IEnumerable)release["assets"])
                    .Cast<Dictionary<string, object>>()
                    .Where(item => AllowedFile.IsMatch((string)item["name"]))
                    .Select(item => new AssetInfo(item))
                    .ToList();
            }
        }

        private static Dictionary<string, string> LoadManifest(string folder)
        {
            string path = Path.Combine(folder, ManifestFile);
            try
            {
                if (File.Exists(path))
                {
                    return new Dictionary<string, string>(
                        new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(path)),
                        StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // A corrupt manifest just means everything downloads again.
            }

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static void SaveManifest(string folder, Dictionary<string, string> manifest) =>
            File.WriteAllText(Path.Combine(folder, ManifestFile), new JavaScriptSerializer().Serialize(manifest));

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // In use or gone; the next run tidies it.
            }
        }

        private sealed class AssetInfo
        {
            public AssetInfo(Dictionary<string, object> asset)
            {
                Name = (string)asset["name"];
                DownloadUrl = (string)asset["browser_download_url"];

                // Re-uploading a file gives the asset a new id, so the id says when to download again.
                Key = $"{asset["id"]}:{asset["size"]}:{(asset.TryGetValue("updated_at", out object updated) ? updated : "")}";

                if (asset.TryGetValue("digest", out object digest)
                    && digest is string text
                    && text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                {
                    Sha256 = text.Substring("sha256:".Length);
                }
            }

            public string Name { get; }
            public string DownloadUrl { get; }
            public string Key { get; }
            public string Sha256 { get; }
        }
    }
}
