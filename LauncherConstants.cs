using System;
using System.Collections.Generic;

namespace SeriousFunLauncher
{
    internal static class LauncherConstants
    {
        public const string DefaultServerHost = "seriousfun.tplinkdns.com";
        public const int DefaultServerPort = 2593;

        public const string TazUoRepository = "Brandin-Canterbury/TazUO";
        public const string TazUoAssetName = "TazUO-win-x64.zip";
        public const string TazUoExecutable = "TazUO.exe";
        public const string TazUoVersionFile = "v.txt";

        // The download page is scraped for the current installer; this is the fallback.
        public const string UoDownloadPage = "https://uo.com/client-download/";
        public const string UoInstallerFallbackUrl =
            "https://downloads.eamythic.com/uo/installers/UOClassicSetup_7_0_24_0.exe";

        // EA's installer isn't code-signed, so a pinned checksum is what proves a download is genuine.
        // Only pinned installers are accepted when EA's server certificate has expired (see UoClientInstaller).
        public const string UoInstallerHost = "downloads.eamythic.com";
        public static readonly IReadOnlyDictionary<string, string> KnownInstallerSha256 =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["UOClassicSetup_7_0_24_0.exe"] = "C19F93F979B105B7E5E515369E87968CDAC9D950B8DE6B7A9442985BA1CED7A6",
            };

        public const string UserAgent = "SeriousFunLauncher/1.0";
    }
}
