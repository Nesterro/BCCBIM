using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BCCInstaller
{
    public class GitHubReleaseInfo
    {
        public string TagName { get; set; } = "v1.6.0";
        public string Name { get; set; } = "";
        public string Body { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public DateTime PublishedAt { get; set; }
    }

    public class UpdateService
    {
        private readonly InstallerConfig _config;

        public static readonly string[] SupportedRevitVersions = new string[] { "2021", "2022", "2023", "2024", "2025" };

        public UpdateService(InstallerConfig config)
        {
            _config = config;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        }

        public string GetSharedPluginDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "BIMBCC", "PlugIn");
        }

        public string GetRevitAddinFolder(string revitVersion)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Autodesk", "Revit", "Addins", revitVersion);
        }

        public string GetAddinManifestPath(string revitVersion)
        {
            return Path.Combine(GetRevitAddinFolder(revitVersion), "BCCPlugIn.addin");
        }

        public List<string> DetectInstalledRevitVersions()
        {
            List<string> detected = new List<string>();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string revitBase = Path.Combine(appData, "Autodesk", "Revit", "Addins");

            foreach (string ver in SupportedRevitVersions)
            {
                string path = Path.Combine(revitBase, ver);
                if (Directory.Exists(path) || File.Exists(GetAddinManifestPath(ver)))
                {
                    detected.Add(ver);
                }
            }

            if (detected.Count == 0)
            {
                detected.Add("2023");
                detected.Add("2024");
            }

            return detected;
        }

        public string GetInstalledVersion()
        {
            try
            {
                string pluginDir = GetSharedPluginDirectory();
                string versionFilePath = Path.Combine(pluginDir, "version.txt");

                if (File.Exists(versionFilePath))
                {
                    string vText = File.ReadAllText(versionFilePath, Encoding.UTF8).Trim();
                    if (!string.IsNullOrEmpty(vText))
                    {
                        return vText.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? vText : $"v{vText}";
                    }
                }

                string dllPath = Path.Combine(pluginDir, "BCCPlugIn.dll");
                if (File.Exists(dllPath))
                {
                    FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(dllPath);
                    string ver = fvi.ProductVersion ?? fvi.FileVersion;
                    if (!string.IsNullOrEmpty(ver) && ver != "0.0.0.0")
                    {
                        return ver.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? ver : $"v{ver}";
                    }
                }
            }
            catch { }
            return "Не установлен";
        }

        public List<string> GetActiveRevitManifests()
        {
            List<string> active = new List<string>();
            foreach (string ver in SupportedRevitVersions)
            {
                if (File.Exists(GetAddinManifestPath(ver)))
                {
                    active.Add(ver);
                }
            }
            return active;
        }

        public async Task<GitHubReleaseInfo> FetchLatestReleaseAsync()
        {
            string apiUrl = $"https://api.github.com/repos/{_config.GitHubOwner}/{_config.GitHubRepo}/releases/latest";

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "BIMBCC-Installer-App");
                HttpResponseMessage response = await client.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Не удалось получить информацию о релизах с GitHub (Код: {response.StatusCode}). Проверьте репозиторий: {_config.GitHubOwner}/{_config.GitHubRepo}");
                }

                string jsonStr = await response.Content.ReadAsStringAsync();

                GitHubReleaseInfo info = new GitHubReleaseInfo();

                var tagMatch = Regex.Match(jsonStr, @"""tag_name""\s*:\s*""([^""]+)""");
                if (tagMatch.Success) info.TagName = tagMatch.Groups[1].Value;

                var nameMatch = Regex.Match(jsonStr, @"""name""\s*:\s*""([^""]+)""");
                if (nameMatch.Success) info.Name = nameMatch.Groups[1].Value;

                var bodyMatch = Regex.Match(jsonStr, @"""body""\s*:\s*""((?:[^""\\]|\\.)*)""");
                if (bodyMatch.Success)
                {
                    info.Body = Regex.Unescape(bodyMatch.Groups[1].Value);
                }

                var urlMatch = Regex.Matches(jsonStr, @"""browser_download_url""\s*:\s*""([^""]+)""");
                foreach (Match m in urlMatch)
                {
                    string url = m.Groups[1].Value;
                    if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        info.DownloadUrl = url;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(info.DownloadUrl) && urlMatch.Count > 0)
                {
                    info.DownloadUrl = urlMatch[0].Groups[1].Value;
                }

                return info;
            }
        }

        public async Task InstallOrUpdateAsync(GitHubReleaseInfo releaseInfo, List<string> targetRevitVersions, Action<int, string> progressCallback)
        {
            if (string.IsNullOrEmpty(releaseInfo.DownloadUrl))
            {
                throw new Exception("В выбранном релизе GitHub не найден файл пакета (.zip).");
            }

            if (targetRevitVersions == null || targetRevitVersions.Count == 0)
            {
                throw new Exception("Пожалуйста, выберите хотя бы одну версию Revit для установки.");
            }

            string pluginDir = GetSharedPluginDirectory();
            string tempZip = Path.Combine(Path.GetTempPath(), $"BCCPlugIn_{Guid.NewGuid()}.zip");
            string tempExtract = Path.Combine(Path.GetTempPath(), $"BCCPlugIn_Extracted_{Guid.NewGuid()}");

            try
            {
                progressCallback?.Invoke(15, "Скачивание обновления с GitHub...");

                using (WebClient webClient = new WebClient())
                {
                    webClient.Headers.Add("User-Agent", "BIMBCC-Installer-App");
                    await webClient.DownloadFileTaskAsync(new Uri(releaseInfo.DownloadUrl), tempZip);
                }

                progressCallback?.Invoke(50, "Распаковка пакета установки...");
                if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
                ZipFile.ExtractToDirectory(tempZip, tempExtract);

                progressCallback?.Invoke(70, "Установка файлов плагина в %APPDATA%\\BIMBCC\\PlugIn...");

                if (Directory.Exists(pluginDir))
                {
                    try { Directory.Delete(pluginDir, true); } catch { }
                }
                Directory.CreateDirectory(pluginDir);

                string sourceDir = tempExtract;
                string[] subDirs = Directory.GetDirectories(tempExtract);
                if (subDirs.Length == 1 && File.Exists(Path.Combine(subDirs[0], "BCCPlugIn.dll")))
                {
                    sourceDir = subDirs[0];
                }

                CopyDirectory(sourceDir, pluginDir);

                // Save Installed Version File
                string versionFilePath = Path.Combine(pluginDir, "version.txt");
                File.WriteAllText(versionFilePath, releaseInfo.TagName, Encoding.UTF8);

                progressCallback?.Invoke(90, $"Регистрация манифеста для Revit ({string.Join(", ", targetRevitVersions)})...");

                string dllDestination = Path.Combine(pluginDir, "BCCPlugIn.dll");
                string addinContent = BuildAddinManifest(dllDestination);

                foreach (string ver in targetRevitVersions)
                {
                    string addinFolder = GetRevitAddinFolder(ver);
                    if (!Directory.Exists(addinFolder))
                    {
                        Directory.CreateDirectory(addinFolder);
                    }

                    string manifestPath = GetAddinManifestPath(ver);
                    File.WriteAllText(manifestPath, addinContent, Encoding.UTF8);
                }

                progressCallback?.Invoke(100, "Установка успешно завершена!");
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                try { if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true); } catch { }
            }
        }

        public void UninstallPlugin(List<string> targetRevitVersions)
        {
            foreach (string ver in targetRevitVersions)
            {
                string manifestPath = GetAddinManifestPath(ver);
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                }
            }

            if (GetActiveRevitManifests().Count == 0)
            {
                string pluginDir = GetSharedPluginDirectory();
                if (Directory.Exists(pluginDir))
                {
                    try { Directory.Delete(pluginDir, true); } catch { }
                }
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string dest = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string dest = Path.Combine(destinationDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, dest);
            }
        }

        private string BuildAddinManifest(string dllPath)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<RevitAddIns>
  <AddIn Type=""Application"">
    <Name>BIMBCC PlugIn</Name>
    <Assembly>{dllPath}</Assembly>
    <FullClassName>BCCPlugIn.App</FullClassName>
    <ClientId>c7b39a82-411a-4d2b-923f-e192f8019a12</ClientId>
    <VendorId>BIMBCC</VendorId>
    <VendorDescription>BIMBCC Construction &amp; Modeling Tools</VendorDescription>
  </AddIn>
</RevitAddIns>";
        }
    }
}
