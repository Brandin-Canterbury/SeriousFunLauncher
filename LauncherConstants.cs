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

        public const string UserAgent = "SeriousFunLauncher/1.0";
    }
}
