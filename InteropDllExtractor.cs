using System;
using System.IO;
using System.Net;

namespace SMAInteropConverter
{
    public class InteropDllExtractor
    {

        public string Version { get; }
        private const string NugetUrl = "https://www.nuget.org/api/v2/package/SuperMemoAssistant.Interop/{0}";
        private string InteropDir { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Interops");

        public InteropDllExtractor(string version)
        {
            this.Version = version;
        }

        public string GetDll()
        {
            string stored = Path.Combine(InteropDir, Version + ".dll");
            if (File.Exists(stored))
            {
                return stored;
            }

            string nupkg = DownloadNupkg();
            if (string.IsNullOrWhiteSpace(nupkg))
                return null;

            string dll = ExtractDll(nupkg);
            if (File.Exists(dll))
                File.Move(dll, stored);
            else
                return null;

            return stored;
        }

        private string DownloadNupkg()
        {
            var fileName = Path.GetTempFileName();
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.DownloadFile(string.Format(NugetUrl, Version), fileName);
                    return fileName;
                }
            }
            catch (WebException e)
            {
                Console.WriteLine($"Failed to download interop package from nuget with exception {e}");
                return null;
            }
        }

        private string GetTemporaryDirectory()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        private string ExtractDll(string nupkgPath)
        {
            try
            {
                var tmp = GetTemporaryDirectory();
                System.IO.Compression.ZipFile.ExtractToDirectory(nupkgPath, tmp);
                var pathToDll = "lib/net472/SuperMemoAssistant.Interop.dll";
                return Path.Combine(tmp, pathToDll);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to extract dll from nupkg with exception {e}");
                return null;
            }
        }
    }
}
