using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SeriousFunLauncher
{
    internal static class Downloader
    {
        public static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(LauncherConstants.UserAgent);
            return client;
        }

        /// <summary>Downloads <paramref name="url"/> to <paramref name="destination"/>, reporting progress.</summary>
        public static async Task DownloadFileAsync(
            string url,
            string destination,
            StatusReporter reporter,
            CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await Http.GetAsync(
                       url,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                long? total = response.Content.Headers.ContentLength;

                using (Stream source = await response.Content.ReadAsStreamAsync())
                using (FileStream target = File.Create(destination))
                {
                    var buffer = new byte[81920];
                    long received = 0;
                    int read;

                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await target.WriteAsync(buffer, 0, read, cancellationToken);
                        received += read;
                        reporter.Progress(total > 0 ? (double)received / total.Value : (double?)null);
                    }
                }
            }
        }

        /// <summary>Creates a fresh, empty temp directory for one download or extraction.</summary>
        public static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "SeriousFunLauncher", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        public static void TryDeleteDirectory(string path)
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
                // Leftover temp files are harmless.
            }
        }
    }
}
