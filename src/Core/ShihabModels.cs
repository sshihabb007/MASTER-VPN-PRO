using System;
using System.Collections.Generic;
using System.Globalization;

namespace ShihabShieldVpn.Core
{
    public enum ShihabServerSourceKind
    {
        PublicOpenRelay = 0,
        PrivateCustomServer = 1
    }

    public enum ShihabVpnProtocol
    {
        AutoSstp = 0,
        Ikev2 = 1,
        L2tpIpsec = 2,
        Pptp = 3,
        OpenVpn = 4,
        WireGuard = 5,
        HttpSystemProxy = 6
    }

    public enum ShihabDnsPreset
    {
        CloudflareSecure = 0,
        AdGuardBlock = 1,
        Quad9Security = 2,
        GooglePublic = 3,
        CustomDns = 4,
        AutomaticTunnel = 5
    }

    public enum ShihabGlobeCameraMode
    {
        Globe3D = 0,
        Shield3D = 1,
        AutoOrbit3D = 2,
        FlatMap2D = 3
    }

    public enum ShihabConnectionState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Disconnecting = 3,
        Reconnecting = 4,
        Error = 5
    }

    public sealed class ShihabResult<T>
    {
        public bool IsSuccess { get; private set; }
        public T Value { get; private set; }
        public string ErrorCode { get; private set; }
        public string ErrorMessage { get; private set; }
        public long ElapsedMs { get; private set; }

        private ShihabResult()
        {
        }

        public static ShihabResult<T> Ok(T shihabValue, long elapsedMs)
        {
            ShihabResult<T> r = new ShihabResult<T>();
            r.IsSuccess = true;
            r.Value = shihabValue;
            r.ErrorCode = string.Empty;
            r.ErrorMessage = string.Empty;
            r.ElapsedMs = elapsedMs;
            return r;
        }

        public static ShihabResult<T> Fail(string errorCode, string errorMessage, long elapsedMs)
        {
            ShihabResult<T> r = new ShihabResult<T>();
            r.IsSuccess = false;
            r.Value = default(T);
            r.ErrorCode = errorCode ?? "ERROR";
            r.ErrorMessage = errorMessage ?? "Unknown error";
            r.ElapsedMs = elapsedMs;
            return r;
        }
    }

    public sealed class ShihabVpnServerNode
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public ShihabServerSourceKind SourceKind { get; set; }
        public ShihabVpnProtocol Protocol { get; set; }
        public string HostName { get; set; }
        public string SstpEndpoint { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string CountryLong { get; set; }
        public string CountryShort { get; set; }
        public string CityName { get; set; }
        public string RegionGroup { get; set; }
        public int PingMs { get; set; }
        public long SpeedBps { get; set; }
        public int ActiveSessions { get; set; }
        public int LoadPercent { get; set; }
        public long Score { get; set; }
        public string OperatorName { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool IsTcp443Reachable { get; set; }
        public bool WasProbed { get; set; }
        public bool IsLocallyProbed { get; set; }
        public bool IsSstpVerified { get; set; }
        public bool SupportsL2tpIpsec { get; set; }

        // Public OpenVPN Base64 config (when provided by public academic directory)
        public string OpenVpnConfigBase64 { get; set; }

        // Private / Custom server credentials (DPAPI encrypted when persisted)
        public string Username { get; set; }
        public string EncryptedPassword { get; set; }
        public string EncryptedPreSharedKey { get; set; }
        public string RawConfigProfile { get; set; }

        public ShihabVpnServerNode()
        {
            Id = Guid.NewGuid().ToString("N");
            DisplayName = "Auto Server";
            SourceKind = ShihabServerSourceKind.PublicOpenRelay;
            Protocol = ShihabVpnProtocol.AutoSstp;
            HostName = string.Empty;
            SstpEndpoint = string.Empty;
            IpAddress = "0.0.0.0";
            Port = 443;
            CountryLong = "United States";
            CountryShort = "US";
            CityName = "Ashburn";
            RegionGroup = "Americas";
            PingMs = 0;
            SpeedBps = 0;
            ActiveSessions = 0;
            LoadPercent = 12;
            Score = 0;
            OperatorName = string.Empty;
            Latitude = 38.9;
            Longitude = -77.0;
            Username = "vpn";
            EncryptedPassword = string.Empty;
            EncryptedPreSharedKey = string.Empty;
            OpenVpnConfigBase64 = string.Empty;
            RawConfigProfile = string.Empty;
        }

        public double SpeedMbps
        {
            get { return SpeedBps > 0 ? Math.Round(SpeedBps / 1000000.0, 1) : 0.0; }
        }

        public bool HasExportableOpenVpnProfile
        {
            get
            {
                return !string.IsNullOrWhiteSpace(OpenVpnConfigBase64) ||
                       (Protocol == ShihabVpnProtocol.OpenVpn && !string.IsNullOrWhiteSpace(RawConfigProfile));
            }
        }

        public string BareSstpHost
        {
            get
            {
                string raw = !string.IsNullOrWhiteSpace(SstpEndpoint)
                    ? SstpEndpoint.Trim()
                    : (!string.IsNullOrWhiteSpace(HostName) ? HostName.Trim() : (IpAddress ?? string.Empty).Trim());

                int colon = raw.LastIndexOf(':');
                if (colon > 0 && raw.IndexOf(':') == colon)
                {
                    return raw.Substring(0, colon).Trim();
                }
                return raw;
            }
        }

        public string EffectiveDialAddress
        {
            get
            {
                string bare = BareSstpHost;
                if (string.IsNullOrEmpty(bare))
                {
                    return IpAddress ?? string.Empty;
                }
                if (Protocol == ShihabVpnProtocol.HttpSystemProxy)
                {
                    int p = Port > 0 ? Port : 8080;
                    return bare + ":" + p.ToString(CultureInfo.InvariantCulture);
                }
                if (Protocol == ShihabVpnProtocol.AutoSstp && Port > 0 && Port != 443)
                {
                    return bare + ":" + Port.ToString(CultureInfo.InvariantCulture);
                }
                return bare;
            }
        }

        public string ProtocolBadgeText
        {
            get
            {
                switch (Protocol)
                {
                    case ShihabVpnProtocol.AutoSstp: return "MS-SSTP TLS";
                    case ShihabVpnProtocol.Ikev2: return "IKEv2/IPsec";
                    case ShihabVpnProtocol.L2tpIpsec: return "L2TP/IPsec";
                    case ShihabVpnProtocol.Pptp: return "PPTP";
                    case ShihabVpnProtocol.OpenVpn: return "OpenVPN";
                    case ShihabVpnProtocol.WireGuard: return "WireGuard";
                    case ShihabVpnProtocol.HttpSystemProxy: return "HTTP/S Proxy";
                    default: return "VPN";
                }
            }
        }
    }

    public sealed class ShihabHourlyTrafficBucket
    {
        public string HourIsoKey { get; set; }
        public long DownloadBytes { get; set; }
        public long UploadBytes { get; set; }

        public ShihabHourlyTrafficBucket()
        {
            HourIsoKey = string.Empty;
            DownloadBytes = 0;
            UploadBytes = 0;
        }
    }

    public sealed class ShihabPhysicalNetworkInfo
    {
        public string AdapterTitle { get; set; }
        public string InterfaceDescription { get; set; }
        public string LocalIpv4 { get; set; }
        public string GatewayIpv4 { get; set; }
        public bool IsWireless { get; set; }
        public bool IsOnline { get; set; }

        public ShihabPhysicalNetworkInfo()
        {
            AdapterTitle = "Ethernet connection";
            InterfaceDescription = string.Empty;
            LocalIpv4 = string.Empty;
            GatewayIpv4 = string.Empty;
            IsWireless = false;
            IsOnline = true;
        }
    }

    public sealed class ShihabSpeedTestReport
    {
        public double DownloadMbps { get; set; }
        public int LatencyMs { get; set; }
        public int JitterMs { get; set; }
        public long BytesMeasured { get; set; }
        public string EndpointLabel { get; set; }
    }

    public sealed class ShihabGeoIpReport
    {
        public string IpAddress { get; set; }
        public string CountryShort { get; set; }
        public string CountryLong { get; set; }
        public string City { get; set; }
        public string Isp { get; set; }
        public string Asn { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string LocalTimeFormatted { get; set; }
        public bool LeakDetected { get; set; }
        public bool GeoMismatch { get; set; }
        public string SourceProvider { get; set; }
        public string VerifiedAtUtc { get; set; }

        public ShihabGeoIpReport()
        {
            IpAddress = string.Empty;
            CountryShort = string.Empty;
            CountryLong = string.Empty;
            City = string.Empty;
            Isp = string.Empty;
            Asn = string.Empty;
            Latitude = 38.9;
            Longitude = -97.0;
            LocalTimeFormatted = string.Empty;
            LeakDetected = false;
            GeoMismatch = false;
            SourceProvider = string.Empty;
            VerifiedAtUtc = string.Empty;
        }
    }

    public sealed class ShihabEngineLogEntry
    {
        public string TimestampText { get; set; }
        public string Category { get; set; }
        public string Message { get; set; }

        public ShihabEngineLogEntry()
        {
            TimestampText = string.Empty;
            Category = "SYSTEM";
            Message = string.Empty;
        }
    }

    public sealed class ShihabCountryHubSummary
    {
        public string CountryShort { get; set; }
        public string CountryLong { get; set; }
        public string CityName { get; set; }
        public string RegionGroup { get; set; }
        public int TotalServers { get; set; }
        public int VerifiedTcpServers { get; set; }
        public int StreamingCapableCount { get; set; }
        public int OpenVpnProfileCount { get; set; }
        public int BestPingMs { get; set; }
        public double BestSpeedMbps { get; set; }
        public ShihabVpnServerNode BestNode { get; set; }

        public ShihabCountryHubSummary()
        {
            CountryShort = string.Empty;
            CountryLong = string.Empty;
            CityName = string.Empty;
            RegionGroup = "Global";
            TotalServers = 0;
            VerifiedTcpServers = 0;
            StreamingCapableCount = 0;
            OpenVpnProfileCount = 0;
            BestPingMs = 999;
            BestSpeedMbps = 0.0;
            BestNode = null;
        }
    }

    public sealed class ShihabAppConfig
    {
        public bool AutoSelectFastest { get; set; }
        public string SelectedCountryCode { get; set; }
        public string SelectedServerId { get; set; }
        public ShihabVpnProtocol PreferredPublicProtocol { get; set; }
        public ShihabDnsPreset DnsPreset { get; set; }
        public string CustomPrimaryDns { get; set; }
        public string CustomSecondaryDns { get; set; }
        public bool KillSwitchAutoReconnect { get; set; }
        public bool StrictFirewallKillSwitch { get; set; }
        public bool FirewallKillSwitchArmedOnDisk { get; set; }
        public bool SplitTunnelingEnabled { get; set; }
        public List<string> SplitTunnelBypassApps { get; set; }
        public bool MinimizeToTrayOnClose { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool AutoConnectOnLaunch { get; set; }
        public bool DisconnectTunnelOnExit { get; set; }
        public bool DynamicIpRotation { get; set; }
        public bool ShowExtendedCatalog { get; set; }
        public bool StreamingHighSpeedOnly { get; set; }
        public ShihabGlobeCameraMode GlobeCameraMode { get; set; }
        public bool SystemProxyArmedOnDisk { get; set; }
        public int SavedProxyEnable { get; set; }
        public string SavedProxyServer { get; set; }
        public string SavedProxyOverride { get; set; }
        public ShihabGeoIpReport OriginGeoCache { get; set; }
        public Dictionary<string, List<string>> RecentHostsByCountry { get; set; }
        public double PeakSpeedMbps { get; set; }
        public List<ShihabVpnServerNode> PrivateServers { get; set; }
        public List<ShihabHourlyTrafficBucket> HourlyTraffic { get; set; }

        public ShihabAppConfig()
        {
            AutoSelectFastest = true;
            SelectedCountryCode = "AUTO";
            SelectedServerId = string.Empty;
            PreferredPublicProtocol = ShihabVpnProtocol.AutoSstp;
            DnsPreset = ShihabDnsPreset.CloudflareSecure;
            CustomPrimaryDns = "1.1.1.1";
            CustomSecondaryDns = "1.0.0.1";
            KillSwitchAutoReconnect = true;
            StrictFirewallKillSwitch = false;
            FirewallKillSwitchArmedOnDisk = false;
            SplitTunnelingEnabled = false;
            SplitTunnelBypassApps = new List<string>();
            MinimizeToTrayOnClose = true;
            AlwaysOnTop = false;
            AutoConnectOnLaunch = false;
            DisconnectTunnelOnExit = true;
            DynamicIpRotation = true;
            ShowExtendedCatalog = true;
            StreamingHighSpeedOnly = false;
            GlobeCameraMode = ShihabGlobeCameraMode.Globe3D;
            SystemProxyArmedOnDisk = false;
            SavedProxyEnable = 0;
            SavedProxyServer = string.Empty;
            SavedProxyOverride = string.Empty;
            OriginGeoCache = null;
            RecentHostsByCountry = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            PeakSpeedMbps = 0.0;
            PrivateServers = new List<ShihabVpnServerNode>();
            HourlyTraffic = new List<ShihabHourlyTrafficBucket>();
        }
    }
}
