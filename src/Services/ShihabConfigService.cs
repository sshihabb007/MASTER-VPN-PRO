using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using ShihabShieldVpn.Core;

namespace ShihabShieldVpn.Services
{
    public sealed class ShihabConfigService
    {
        private static readonly byte[] ShihabEntropyBytes = Encoding.UTF8.GetBytes("ShihabShieldVPN_DPAPI_v1");

        private readonly string _shihabDataDir;
        private readonly string _shihabConfigPath;
        private readonly object _shihabSync = new object();

        public string DataDirectory
        {
            get { return _shihabDataDir; }
        }

        public string ConfigFilePath
        {
            get { return _shihabConfigPath; }
        }

        public ShihabConfigService()
            : this(null)
        {
        }

        public ShihabConfigService(string customDirectory)
        {
            _shihabDataDir = string.IsNullOrWhiteSpace(customDirectory)
                ? ResolveShihabWritableDataDir()
                : customDirectory;
            _shihabConfigPath = Path.Combine(_shihabDataDir, "ShihabShieldVPN.config.json");
        }

        public static string ResolveShihabWritableDataDir()
        {
            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                string exeDir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    bool inProtectedProgramFiles =
                        (!string.IsNullOrEmpty(programFiles) && exeDir.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(programFilesX86) && exeDir.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase));

                    if (!inProtectedProgramFiles)
                    {
                        string probeFile = Path.Combine(exeDir, ".shihab_write_probe_" + Guid.NewGuid().ToString("N") + ".tmp");
                        File.WriteAllText(probeFile, "ok");
                        File.Delete(probeFile);
                        return exeDir;
                    }
                }
            }
            catch
            {
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string fallback = Path.Combine(localAppData, "ShihabShieldVPN");
            if (!Directory.Exists(fallback))
            {
                Directory.CreateDirectory(fallback);
            }
            return fallback;
        }

        public ShihabAppConfig LoadShihabConfig()
        {
            lock (_shihabSync)
            {
                try
                {
                    if (File.Exists(_shihabConfigPath))
                    {
                        string json = File.ReadAllText(_shihabConfigPath, Encoding.UTF8);
                        JavaScriptSerializer ser = new JavaScriptSerializer();
                        ser.MaxJsonLength = 8 * 1024 * 1024;
                        ShihabAppConfig cfg = ser.Deserialize<ShihabAppConfig>(json);
                        if (cfg != null)
                        {
                            NormalizeShihabConfig(cfg);
                            return cfg;
                        }
                    }
                }
                catch
                {
                }

                ShihabAppConfig fresh = new ShihabAppConfig();
                NormalizeShihabConfig(fresh);
                return fresh;
            }
        }

        public void SaveShihabConfig(ShihabAppConfig shihabConfig)
        {
            if (shihabConfig == null)
            {
                return;
            }

            lock (_shihabSync)
            {
                try
                {
                    NormalizeShihabConfig(shihabConfig);
                    if (!Directory.Exists(_shihabDataDir))
                    {
                        Directory.CreateDirectory(_shihabDataDir);
                    }

                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    ser.MaxJsonLength = 8 * 1024 * 1024;
                    string json = ser.Serialize(shihabConfig);

                    string tmpPath = _shihabConfigPath + ".tmp";
                    File.WriteAllText(tmpPath, json, new UTF8Encoding(false));
                    if (File.Exists(_shihabConfigPath))
                    {
                        File.Delete(_shihabConfigPath);
                    }
                    File.Move(tmpPath, _shihabConfigPath);
                }
                catch
                {
                }
            }
        }

        public static void NormalizeShihabConfig(ShihabAppConfig cfg)
        {
            if (cfg.PrivateServers == null)
            {
                cfg.PrivateServers = new List<ShihabVpnServerNode>();
            }
            if (cfg.HourlyTraffic == null)
            {
                cfg.HourlyTraffic = new List<ShihabHourlyTrafficBucket>();
            }
            if (cfg.SplitTunnelBypassApps == null)
            {
                cfg.SplitTunnelBypassApps = new List<string>();
            }
            if (cfg.RecentHostsByCountry == null)
            {
                cfg.RecentHostsByCountry = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Dictionary<string, List<string>> ciMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, List<string>> kv in cfg.RecentHostsByCountry)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null)
                    {
                        ciMap[kv.Key.Trim().ToUpperInvariant()] = kv.Value;
                    }
                }
                cfg.RecentHostsByCountry = ciMap;
            }
            if (cfg.SavedProxyServer == null)
            {
                cfg.SavedProxyServer = string.Empty;
            }
            if (cfg.SavedProxyOverride == null)
            {
                cfg.SavedProxyOverride = string.Empty;
            }
            if (string.IsNullOrWhiteSpace(cfg.SelectedCountryCode))
            {
                cfg.SelectedCountryCode = "AUTO";
            }
            if (string.IsNullOrWhiteSpace(cfg.CustomPrimaryDns))
            {
                cfg.CustomPrimaryDns = "1.1.1.1";
            }
            if (string.IsNullOrWhiteSpace(cfg.CustomSecondaryDns))
            {
                cfg.CustomSecondaryDns = "1.0.0.1";
            }
            EnsureRolling24HourBuckets(cfg, DateTime.UtcNow);
        }

        public static void EnsureRolling24HourBuckets(ShihabAppConfig cfg, DateTime nowUtc)
        {
            DateTime currentHourUtc = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, 0, 0, DateTimeKind.Utc);
            Dictionary<string, ShihabHourlyTrafficBucket> map = new Dictionary<string, ShihabHourlyTrafficBucket>(StringComparer.Ordinal);
            if (cfg.HourlyTraffic != null)
            {
                for (int i = 0; i < cfg.HourlyTraffic.Count; i++)
                {
                    ShihabHourlyTrafficBucket b = cfg.HourlyTraffic[i];
                    if (b != null && !string.IsNullOrEmpty(b.HourIsoKey))
                    {
                        map[b.HourIsoKey] = b;
                    }
                }
            }

            List<ShihabHourlyTrafficBucket> normalized = new List<ShihabHourlyTrafficBucket>(24);
            for (int offset = 23; offset >= 0; offset--)
            {
                DateTime slot = currentHourUtc.AddHours(-offset);
                string key = slot.ToString("yyyy-MM-ddTHH", CultureInfo.InvariantCulture);
                ShihabHourlyTrafficBucket existing;
                if (map.TryGetValue(key, out existing))
                {
                    normalized.Add(existing);
                }
                else
                {
                    ShihabHourlyTrafficBucket empty = new ShihabHourlyTrafficBucket();
                    empty.HourIsoKey = key;
                    normalized.Add(empty);
                }
            }

            cfg.HourlyTraffic = normalized;
        }

        public static void RecordShihabTrafficDelta(ShihabAppConfig cfg, long rxDeltaBytes, long txDeltaBytes, DateTime nowUtc)
        {
            if (cfg == null)
            {
                return;
            }
            EnsureRolling24HourBuckets(cfg, nowUtc);
            if (cfg.HourlyTraffic.Count > 0)
            {
                ShihabHourlyTrafficBucket latest = cfg.HourlyTraffic[cfg.HourlyTraffic.Count - 1];
                if (rxDeltaBytes > 0)
                {
                    latest.DownloadBytes += rxDeltaBytes;
                }
                if (txDeltaBytes > 0)
                {
                    latest.UploadBytes += txDeltaBytes;
                }
            }
        }

        public static void ComputeShihab24HourTotals(ShihabAppConfig cfg, out long totalDownloadBytes, out long totalUploadBytes)
        {
            totalDownloadBytes = 0;
            totalUploadBytes = 0;
            if (cfg == null || cfg.HourlyTraffic == null)
            {
                return;
            }
            for (int i = 0; i < cfg.HourlyTraffic.Count; i++)
            {
                ShihabHourlyTrafficBucket b = cfg.HourlyTraffic[i];
                if (b != null)
                {
                    totalDownloadBytes += Math.Max(0L, b.DownloadBytes);
                    totalUploadBytes += Math.Max(0L, b.UploadBytes);
                }
            }
        }

        public static string ProtectShihabSecret(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return string.Empty;
            }
            try
            {
                byte[] raw = Encoding.UTF8.GetBytes(plainText);
                byte[] enc = ProtectedData.Protect(raw, ShihabEntropyBytes, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(enc);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string UnprotectShihabSecret(string encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64))
            {
                return string.Empty;
            }
            try
            {
                byte[] enc = Convert.FromBase64String(encryptedBase64);
                byte[] raw = ProtectedData.Unprotect(enc, ShihabEntropyBytes, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(raw);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void ResolveShihabDnsPair(ShihabAppConfig cfg, out string primaryDns, out string secondaryDns, out string badgeLabel)
        {
            ShihabDnsPreset preset = cfg != null ? cfg.DnsPreset : ShihabDnsPreset.CloudflareSecure;
            switch (preset)
            {
                case ShihabDnsPreset.CloudflareSecure:
                    primaryDns = "1.1.1.1";
                    secondaryDns = "1.0.0.1";
                    badgeLabel = "Cloudflare 1.1.1.1";
                    return;
                case ShihabDnsPreset.AdGuardBlock:
                    primaryDns = "94.140.14.14";
                    secondaryDns = "94.140.15.15";
                    badgeLabel = "AdGuard AdBlock DNS";
                    return;
                case ShihabDnsPreset.Quad9Security:
                    primaryDns = "9.9.9.9";
                    secondaryDns = "149.112.112.112";
                    badgeLabel = "Quad9 Malware Shield";
                    return;
                case ShihabDnsPreset.GooglePublic:
                    primaryDns = "8.8.8.8";
                    secondaryDns = "8.8.4.4";
                    badgeLabel = "Google 8.8.8.8";
                    return;
                case ShihabDnsPreset.CustomDns:
                    primaryDns = (cfg != null && !string.IsNullOrWhiteSpace(cfg.CustomPrimaryDns)) ? cfg.CustomPrimaryDns.Trim() : "1.1.1.1";
                    secondaryDns = (cfg != null && !string.IsNullOrWhiteSpace(cfg.CustomSecondaryDns)) ? cfg.CustomSecondaryDns.Trim() : "1.0.0.1";
                    badgeLabel = "Custom " + primaryDns;
                    return;
                default:
                    primaryDns = string.Empty;
                    secondaryDns = string.Empty;
                    badgeLabel = "Tunnel Auto DNS";
                    return;
            }
        }

        public static string FormatShihabBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < 1024L)
            {
                return bytes + " B";
            }
            double kb = bytes / 1024.0;
            if (kb < 1024.0)
            {
                return kb.ToString(kb >= 100.0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " KB";
            }
            double mb = kb / 1024.0;
            if (mb < 1024.0)
            {
                return mb.ToString(mb >= 100.0 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + " MB";
            }
            double gb = mb / 1024.0;
            return gb.ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }
    }
}

