using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BCCInstaller
{
    public class InstallerConfig
    {
        public string GitHubOwner { get; set; } = "Nesterro";
        public string GitHubRepo { get; set; } = "BCCBIM";
        public string RevitVersion { get; set; } = "2024";

        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installer_config.json");

        public static InstallerConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    InstallerConfig cfg = new InstallerConfig();

                    var matchOwner = Regex.Match(json, @"""GitHubOwner""\s*:\s*""([^""]+)""");
                    if (matchOwner.Success) cfg.GitHubOwner = matchOwner.Groups[1].Value;

                    var matchRepo = Regex.Match(json, @"""GitHubRepo""\s*:\s*""([^""]+)""");
                    if (matchRepo.Success) cfg.GitHubRepo = matchRepo.Groups[1].Value;

                    var matchVer = Regex.Match(json, @"""RevitVersion""\s*:\s*""([^""]+)""");
                    if (matchVer.Success) cfg.RevitVersion = matchVer.Groups[1].Value;

                    return cfg;
                }
            }
            catch { }

            var defaultConfig = new InstallerConfig();
            Save(defaultConfig);
            return defaultConfig;
        }

        public static void Save(InstallerConfig config)
        {
            try
            {
                string json = "{\n" +
                    $"  \"GitHubOwner\": \"{config.GitHubOwner}\",\n" +
                    $"  \"GitHubRepo\": \"{config.GitHubRepo}\",\n" +
                    $"  \"RevitVersion\": \"{config.RevitVersion}\"\n" +
                    "}";
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
