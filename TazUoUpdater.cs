using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SeriousFunLauncher
{
    /// <summary>
    /// Keeps the TazUO folder on the latest GitHub release of the fork.
    /// </summary>
    /// <remarks>
    /// Updates are copied over the existing folder rather than replacing it, so the player's
    /// settings, profiles, macros and scripts (files the release doesn't ship) survive.
    /// </remarks>
    public sealed class TazUoUpdater
    {
        private readonly StatusReporter _reporter;

        public TazUoUpdater(StatusReporter reporter)
        {
            _reporter = reporter;
        }

        public static bool IsInstalled(string directory) =>
            File.Exists(Path.Combine(directory, LauncherConstants.TazUoExecutable));

        /// <summary>The installed build's version (from v.txt), or null when unknown.</summary>
        public static string GetInstalledVersion(string directory)
        {
            string path = Path.Combine(directory, LauncherConstants.TazUoVersionFile);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }

        public async Task EnsureLatestAsync(string directory, CancellationToken cancellationToken)
        {
            _reporter.Status("Checking for TazUO updates...");
            _reporter.Progress(null);

            ReleaseInfo release;
            try
            {
                release = await GetLatestReleaseAsync();
            }
            catch (Exception ex) when (IsInstalled(directory))
            {
                _reporter.Status($"Couldn't check for TazUO updates ({ex.Message}); using the installed version.");
                return;
            }

            string installed = GetInstalledVersion(directory);
            if (IsInstalled(directory) && installed == release.Version)
            {
                _reporter.Status($"TazUO {installed} is up to date.");
                return;
            }

            if (UoClientInstaller.FindProcessesIn(directory).Any())
            {
                throw new InvalidOperationException("TazUO is running. Close the game, then press Play again.");
            }

            await InstallAsync(directory, release, cancellationToken);
            _reporter.Status($"TazUO updated to {release.Version}.");
        }

        private async Task InstallAsync(string directory, ReleaseInfo release, CancellationToken cancellationToken)
        {
            string tempDirectory = Downloader.CreateTempDirectory();

            try
            {
                string zipPath = Path.Combine(tempDirectory, LauncherConstants.TazUoAssetName);
                string extracted = Path.Combine(tempDirectory, "extracted");

                _reporter.Status($"Downloading TazUO {release.Version}...");
                await Downloader.DownloadFileAsync(release.DownloadUrl, zipPath, _reporter, cancellationToken);

                _reporter.Status("Installing TazUO...");
                _reporter.Progress(null);
                await Task.Run(() =>
                {
                    ZipFile.ExtractToDirectory(zipPath, extracted);
                    CopyOver(extracted, directory);
                    File.WriteAllText(Path.Combine(directory, LauncherConstants.TazUoVersionFile), release.Version);
                }, cancellationToken);
            }
            finally
            {
                Downloader.TryDeleteDirectory(tempDirectory);
            }
        }

        private static void CopyOver(string source, string destination)
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(destination, file.Substring(source.Length).TrimStart('\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }

        private static async Task<ReleaseInfo> GetLatestReleaseAsync()
        {
            string url = $"https://api.github.com/repos/{LauncherConstants.TazUoRepository}/releases/latest";
            string json = await Downloader.Http.GetStringAsync(url);
            var release = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);

            string tag = (string)release["tag_name"];
            var asset = ((IEnumerable)release["assets"])
                .Cast<Dictionary<string, object>>()
                .FirstOrDefault(item => (string)item["name"] == LauncherConstants.TazUoAssetName);

            if (asset == null)
            {
                throw new InvalidOperationException($"Release {tag} has no {LauncherConstants.TazUoAssetName}.");
            }

            return new ReleaseInfo(tag.TrimStart('v'), (string)asset["browser_download_url"]);
        }

        private sealed class ReleaseInfo
        {
            public ReleaseInfo(string version, string downloadUrl)
            {
                Version = version;
                DownloadUrl = downloadUrl;
            }

            public string Version { get; }
            public string DownloadUrl { get; }
        }
    }
}
