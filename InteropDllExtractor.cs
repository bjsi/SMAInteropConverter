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

        public (string, string)? ExtractNugetPkgFiles()
        {
            string storedDll = Path.Combine(InteropDir, Version + ".dll");
            var storedDocs = Path.Combine(InteropDir, Version + ".xml");
            if (File.Exists(storedDll) && File.Exists(storedDocs))
            {
                return (storedDll, storedDocs);
            }

            string nupkg = DownloadNupkg();
            if (string.IsNullOrWhiteSpace(nupkg))
                return null;

            var (dll, docs) = ExtractDll(nupkg);
            if (File.Exists(dll) && File.Exists(docs))
            {
                File.Move(dll, storedDll);
                File.Move(docs, storedDocs);
            }

            return (storedDll, storedDocs);
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

        private (string, string) ExtractDll(string nupkgPath)
        {
            var tmp = GetTemporaryDirectory();
            System.IO.Compression.ZipFile.ExtractToDirectory(nupkgPath, tmp);
            var pathToDll = "lib/net472/SuperMemoAssistant.Interop.dll";
            var pathToXml = "lib/net472/SuperMemoAssistant.Interop.xml";
            return (Path.Combine(tmp, pathToDll), Path.Combine(tmp, pathToXml));
        }
    }
}
