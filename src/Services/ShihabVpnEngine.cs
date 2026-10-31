using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using ShihabShieldVpn.Core;

namespace ShihabShieldVpn.Services
{
    public sealed class ShihabVpnEngine : IDisposable
    {
        public const string ShihabTunnelEntryName = "ShihabShieldVPN_Tunnel";
        private const string WinInetSettingsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        private const int RAS_MaxEntryName = 256;
        private const int RAS_MaxPhoneNumber = 128;
        private const int RAS_MaxDeviceType = 16;
        private const int RAS_MaxDeviceName = 128;
        private const int RAS_MaxPadType = 32;
        private const int RAS_MaxX25Address = 200;
        private const int RAS_MaxFacilities = 200;
        private const int RAS_MaxUserData = 200;
        private const int MAX_PATH = 260;

        // RASENTRY dwfOptions flags (from Win32 ras.h)
        private const uint RASEO_SpecificIpAddr = 0x00000002;
        private const uint RASEO_SpecificNameServers = 0x00000004;
        private const uint RASEO_RemoteDefaultGateway = 0x00000010;
        private const uint RASEO_ModemLights = 0x00000400;
        private const uint RASEO_RequireMsEncryptedPw = 0x00001000;
        private const uint RASEO_RequireDataEncryption = 0x00002000;
        private const uint RASEO_PreviewUserPw = 0x01000000;
        private const uint RASEO_UsePreSharedKey = 0x00800000;

        private const uint RASNP_Ip = 0x00000004;
        private const uint RASNP_Ipv6 = 0x00000008;
        private const uint RASFP_Ppp = 0x00000001;
        private const uint RASET_Vpn = 2;
        private const uint ET_Require = 3;

        private const uint VS_PptpOnly = 1;
        private const uint VS_L2tpOnly = 3;
        private const uint VS_SstpOnly = 5;
        private const uint VS_Ikev2Only = 7;

        private const uint RASCM_PreSharedKey = 0x00000010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        public struct RASIPADDR
        {
            public byte a;
            public byte b;
            public byte c;
            public byte d;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RASENTRY
        {
            public uint dwSize;
            public uint dwfOptions;
            public uint dwCountryID;
            public uint dwCountryCode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)]
            public string szAreaCode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RAS_MaxPhoneNumber + 1)]
            public string szLocalPhoneNumber;
            public uint dwAlternateOffset;
            public RASIPADDR ipaddr;
            public RASIPADDR ipaddrDns;
            public RASIPADDR ipaddrDnsAlt;
            public RASIPADDR ipaddrWins;
            public RASIPADDR ipaddrWinsAlt;
            public uint dwFrameSize;
            public uint dwfNetProtocols;
            public uint dwFramingProtocol;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string szScript;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string szAutodialDll;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string szAutodialFunc;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RAS_MaxDeviceType + 1)]
            public string szDeviceType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RAS_MaxDeviceName + 1)]
            public string szDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RAS_MaxPadType + 1)]
            public string szX25PadType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RAS_MaxX25Address + 1)]
            public string szX25Address;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RAS_MaxFacilities + 1)]
            public string szX25Facilities;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RAS_MaxUserData + 1)]
            public string szX25UserData;
            public uint dwChannels;
            public uint dwReserved1;
            public uint dwReserved2;
            public uint dwSubEntries;
            public uint dwDialMode;
            public uint dwDialExtraPercent;
            public uint dwDialExtraSampleSeconds;
            public uint dwHangUpExtraPercent;
            public uint dwHangUpExtraSampleSeconds;
            public uint dwIdleDisconnectSeconds;
            public uint dwType;
            public uint dwEncryptionType;
            public uint dwCustomAuthKey;
            public Guid guidId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string szCustomDialDll;
            public uint dwVpnStrategy;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RASCREDENTIALS
        {
            public uint dwSize;
            public uint dwMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
            public string szUserName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
            public string szPassword;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string szDomain;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        private struct RASCONN
        {
            public int dwSize;
            public IntPtr hrasconn;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RAS_MaxEntryName + 1)]
            public string szEntryName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RAS_MaxDeviceType + 1)]
            public string szDeviceType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RAS_MaxDeviceName + 1)]
            public string szDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string szPhonebook;
            public int dwSubEntry;
            public Guid guidEntry;
            public uint dwFlags;
            public long luid;
            public Guid guidCorrelationId;
        }

        [DllImport("rasapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RasSetEntryProperties(
            string lpszPhonebook,
            string lpszEntry,
            ref RASENTRY lpRasEntry,
            uint dwEntryInfoSize,
            IntPtr lpbDeviceInfo,
            uint dwDeviceInfoSize);

        [DllImport("rasapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RasSetCredentials(
            string lpszPhonebook,
            string lpszEntry,
            ref RASCREDENTIALS lpCredentials,
            bool fClearCredentials);

        [DllImport("rasapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RasEnumConnections(
            [In, Out] RASCONN[] lpRasConn,
            ref int lpcb,
            ref int lpcConnections);

        [DllImport("rasapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RasHangUp(IntPtr hrasconn);

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(
            IntPtr hInternet,
            int dwOption,
            IntPtr lpBuffer,
            int dwBufferLength);

        private readonly string _shihabDataDir;
        private readonly string _shihabPbkPath;
        private readonly string _shihabProfilesDir;
        private readonly ShihabConfigService _shihabConfigService;
        private readonly object _procLock = new object();

        private Process _openVpnProcess;
        private bool _wireGuardTunnelInstalled;
        private string _wireGuardTunnelName;
        private ShihabLocalAuthProxyBridge _localProxyBridge;
        private ShihabAppConfig _activeProxyConfigRef;
        private bool _disposed;

        public string PortablePhonebookPath
        {
            get { return _shihabPbkPath; }
        }

        public ShihabVpnEngine(string dataDirectory)
            : this(dataDirectory, null)
        {
        }

        public ShihabVpnEngine(string dataDirectory, ShihabConfigService configService)
        {
            _shihabDataDir = string.IsNullOrWhiteSpace(dataDirectory)
                ? ShihabConfigService.ResolveShihabWritableDataDir()
                : dataDirectory;
            _shihabPbkPath = Path.Combine(_shihabDataDir, "ShihabShieldVPN.pbk");
            _shihabProfilesDir = Path.Combine(_shihabDataDir, "profiles");
            _shihabConfigService = configService;
        }

        public bool TryGetActiveProxyBridgeTotals(out long rxBytes, out long txBytes)
        {
            rxBytes = 0;
            txBytes = 0;
            lock (_procLock)
            {
                if (_localProxyBridge != null && _localProxyBridge.IsRunning)
                {
                    rxBytes = _localProxyBridge.TotalBytesReceived;
                    txBytes = _localProxyBridge.TotalBytesSent;
                    return true;
                }
            }
            return false;
        }

        public void RecoverOrphanedShihabSystemProxy(ShihabAppConfig cfg, bool tunnelCurrentlyActive)
        {
            if (cfg == null || !cfg.SystemProxyArmedOnDisk || tunnelCurrentlyActive)
            {
                return;
            }
            RestoreWinInetProxyFromConfig(cfg);
            if (_shihabConfigService != null)
            {
                _shihabConfigService.SaveShihabConfig(cfg);
            }
        }

        public static bool TryEncodeShihabIpv4ToRas(string ipv4Text, out RASIPADDR addr)
        {
            addr = new RASIPADDR();
            if (string.IsNullOrWhiteSpace(ipv4Text))
            {
                return false;
            }
            IPAddress parsed;
            if (!IPAddress.TryParse(ipv4Text.Trim(), out parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return false;
            }
            byte[] octets = parsed.GetAddressBytes();
            if (octets == null || octets.Length != 4)
            {
                return false;
            }
            addr.a = octets[0];
            addr.b = octets[1];
            addr.c = octets[2];
            addr.d = octets[3];
            return true;
        }

        public ShihabResult<string> ProvisionShihabPhonebookEntry(
            string targetPbkPath,
            string entryName,
            ShihabVpnServerNode node,
            ShihabAppConfig cfg)
        {
            Stopwatch sw = Stopwatch.StartNew();
            if (node == null)
            {
                return ShihabResult<string>.Fail("NULL_NODE", "No VPN server node specified.", 0);
            }

            string pbk = string.IsNullOrWhiteSpace(targetPbkPath) ? _shihabPbkPath : targetPbkPath;
            string dir = Path.GetDirectoryName(pbk);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            RASENTRY entry = new RASENTRY();
            entry.dwSize = (uint)Marshal.SizeOf(typeof(RASENTRY));

            uint opts = RASEO_ModemLights | RASEO_RequireDataEncryption;
            if (node.Protocol != ShihabVpnProtocol.Ikev2)
            {
                opts |= RASEO_RequireMsEncryptedPw;
            }

            bool splitTunnel = cfg != null && cfg.SplitTunnelingEnabled;
            if (!splitTunnel)
            {
                opts |= RASEO_RemoteDefaultGateway;
            }

            string priDns, secDns, dnsBadge;
            ShihabConfigService.ResolveShihabDnsPair(cfg, out priDns, out secDns, out dnsBadge);
            RASIPADDR dns1, dns2;
            if (TryEncodeShihabIpv4ToRas(priDns, out dns1))
            {
                opts |= RASEO_SpecificNameServers;
                entry.ipaddrDns = dns1;
                if (TryEncodeShihabIpv4ToRas(secDns, out dns2))
                {
                    entry.ipaddrDnsAlt = dns2;
                }
            }

            string pskPlain = ShihabConfigService.UnprotectShihabSecret(node.EncryptedPreSharedKey);
            if (node.Protocol == ShihabVpnProtocol.L2tpIpsec &&
                string.IsNullOrEmpty(pskPlain) &&
                node.SourceKind == ShihabServerSourceKind.PublicOpenRelay)
            {
                pskPlain = "vpn";
            }
            if (node.Protocol == ShihabVpnProtocol.L2tpIpsec && !string.IsNullOrEmpty(pskPlain))
            {
                opts |= RASEO_UsePreSharedKey;
            }

            entry.dwfOptions = opts;
            entry.szLocalPhoneNumber = node.EffectiveDialAddress;
            entry.dwfNetProtocols = RASNP_Ip | RASNP_Ipv6;
            entry.dwFramingProtocol = RASFP_Ppp;
            entry.szDeviceType = "vpn";
            entry.dwType = RASET_Vpn;
            entry.dwEncryptionType = ET_Require;

            switch (node.Protocol)
            {
                case ShihabVpnProtocol.Ikev2:
                    entry.dwVpnStrategy = VS_Ikev2Only;
                    entry.szDeviceName = "WAN Miniport (IKEv2)";
                    break;
                case ShihabVpnProtocol.L2tpIpsec:
                    entry.dwVpnStrategy = VS_L2tpOnly;
                    entry.szDeviceName = "WAN Miniport (L2TP)";
                    break;
                case ShihabVpnProtocol.Pptp:
                    entry.dwVpnStrategy = VS_PptpOnly;
                    entry.szDeviceName = "WAN Miniport (PPTP)";
                    break;
                default:
                    entry.dwVpnStrategy = VS_SstpOnly;
                    entry.szDeviceName = "WAN Miniport (SSTP)";
                    break;
            }

            uint rc = RasSetEntryProperties(pbk, entryName, ref entry, entry.dwSize, IntPtr.Zero, 0);
            if (rc != 0)
            {
                sw.Stop();
                return ShihabResult<string>.Fail("RAS_PROVISION_" + rc, "RasSetEntryProperties failed with Win32 error " + rc, sw.ElapsedMilliseconds);
            }

            if (node.Protocol == ShihabVpnProtocol.L2tpIpsec && !string.IsNullOrEmpty(pskPlain))
            {
                RASCREDENTIALS creds = new RASCREDENTIALS();
                creds.dwSize = (uint)Marshal.SizeOf(typeof(RASCREDENTIALS));
                creds.dwMask = RASCM_PreSharedKey;
                creds.szPassword = pskPlain;
                RasSetCredentials(pbk, entryName, ref creds, false);
            }

            sw.Stop();
            return ShihabResult<string>.Ok(pbk, sw.ElapsedMilliseconds);
        }

        public Task<ShihabResult<ShihabVpnServerNode>> ConnectShihabTunnelAsync(
            ShihabVpnServerNode targetNode,
            IReadOnlyList<ShihabVpnServerNode> fallbackCandidates,
            ShihabAppConfig cfg,
            Action<string> progressStatusCallback,
            CancellationToken ct)
        {
            return Task.Run(delegate
            {
                Stopwatch totalSw = Stopwatch.StartNew();
                if (targetNode == null)
                {
                    return ShihabResult<ShihabVpnServerNode>.Fail("NO_SERVER", "No VPN server available to connect.", 0);
                }

                // Tear down any existing Shihab tunnel or system proxy cleanly before switching countries or servers
                if (IsShihabTunnelCurrentlyActive())
                {
                    DisconnectShihabTunnelSync();
                }

                ShihabVpnProtocol effectiveProtocol = targetNode.Protocol;
                if (targetNode.SourceKind == ShihabServerSourceKind.PublicOpenRelay &&
                    cfg != null &&
                    cfg.PreferredPublicProtocol == ShihabVpnProtocol.OpenVpn &&
                    !string.IsNullOrEmpty(targetNode.OpenVpnConfigBase64))
                {
                    effectiveProtocol = ShihabVpnProtocol.OpenVpn;
                }

                if (effectiveProtocol == ShihabVpnProtocol.HttpSystemProxy)
                {
                    return ConnectShihabSystemProxyInternal(targetNode, cfg, progressStatusCallback, ct, totalSw);
                }

                if (effectiveProtocol == ShihabVpnProtocol.OpenVpn)
                {
                    ShihabResult<ShihabVpnServerNode> ovRes = ConnectShihabOpenVpnInternal(targetNode, progressStatusCallback, ct, totalSw);
                    if (ovRes.IsSuccess && ovRes.Value != null && ovRes.Value.SourceKind == ShihabServerSourceKind.PublicOpenRelay && cfg != null)
                    {
                        ShihabPublicRelayService.RecordShihabRecentHost(cfg, ovRes.Value.CountryShort, ovRes.Value.BareSstpHost);
                        if (_shihabConfigService != null) _shihabConfigService.SaveShihabConfig(cfg);
                    }
                    return ovRes;
                }

                if (effectiveProtocol == ShihabVpnProtocol.WireGuard)
                {
                    return ConnectShihabWireGuardInternal(targetNode, progressStatusCallback, ct, totalSw);
                }

                // Build candidate pool honoring Dynamic IP Rotation + same-country failover
                List<ShihabVpnServerNode> candidatePool = BuildShihabOrderedCandidates(targetNode, fallbackCandidates, cfg);

                bool explicitPinnedNode = cfg != null && !cfg.AutoSelectFastest && !string.IsNullOrWhiteSpace(cfg.SelectedServerId);
                if (!explicitPinnedNode && candidatePool.Count > 1)
                {
                    candidatePool = RaceFastestShihabSstpCandidates(candidatePool, cfg, progressStatusCallback, ct);
                }

                string lastDialErr = "No responsive MS-SSTP gateway found in selected region.";
                int dialAttemptsMade = 0;

                for (int idx = 0; idx < candidatePool.Count && dialAttemptsMade < 4; idx++)
                {
                    if (ct.IsCancellationRequested)
                    {
                        DisconnectShihabTunnelSync();
                        return ShihabResult<ShihabVpnServerNode>.Fail("CANCELLED", "Connection cancelled.", totalSw.ElapsedMilliseconds);
                    }

                    ShihabVpnServerNode candidate = candidatePool[idx];

                    // Fast Pre-Flight Real MS-SSTP Handshake Check (< 1.5s) for Public Relays so we never block 22s in rasdial on dead/UDP ports!
                    if (candidate.SourceKind == ShihabServerSourceKind.PublicOpenRelay &&
                        candidate.Protocol == ShihabVpnProtocol.AutoSstp &&
                        !(candidate.IsLocallyProbed && candidate.IsSstpVerified))
                    {
                        if (progressStatusCallback != null)
                        {
                            progressStatusCallback(string.Format(
                                "Verifying {0} gateway ({1}) [{2}/{3}]...",
                                candidate.CountryLong,
                                candidate.EffectiveDialAddress,
                                idx + 1,
                                candidatePool.Count));
                        }

                        int verifiedPort;
                        int singleIpRttMs;
                        if (!TryVerifyWorkingShihabSstpPort(candidate, ct, out verifiedPort, out singleIpRttMs))
                        {
                            if (!candidate.SupportsL2tpIpsec)
                            {
                                continue;
                            }
                            candidate = CloneNodeWithProtocol(candidate, ShihabVpnProtocol.L2tpIpsec, 1701);
                        }
                        else
                        {
                            candidate.Port = verifiedPort;
                            if (singleIpRttMs > 0)
                            {
                                candidate.PingMs = singleIpRttMs;
                            }
                            candidate.IsSstpVerified = true;
                            candidate.IsTcp443Reachable = true;
                            candidate.IsLocallyProbed = true;
                            candidate.WasProbed = true;
                        }
                    }

                    // Warm Windows DNS resolver cache for *.opengw.net outside latency timer so rasdial dials without cold DNS stall
                    WarmResolveHostForRasDial(candidate.BareSstpHost, 900);

                    dialAttemptsMade++;
                    if (progressStatusCallback != null)
                    {
                        progressStatusCallback(string.Format(
                            "Dialing fastest gateway {0} ({1} • {2} ms)...",
                            candidate.CountryLong,
                            candidate.EffectiveDialAddress,
                            candidate.PingMs > 0 ? candidate.PingMs : 25));
                    }

                    ShihabResult<string> prov = ProvisionShihabPhonebookEntry(_shihabPbkPath, ShihabTunnelEntryName, candidate, cfg);
                    if (!prov.IsSuccess)
                    {
                        lastDialErr = prov.ErrorMessage;
                        continue;
                    }

                    string user = string.IsNullOrWhiteSpace(candidate.Username)
                        ? (candidate.SourceKind == ShihabServerSourceKind.PublicOpenRelay ? "vpn" : string.Empty)
                        : candidate.Username.Trim();

                    string pass = candidate.SourceKind == ShihabServerSourceKind.PublicOpenRelay
                        ? "vpn"
                        : ShihabConfigService.UnprotectShihabSecret(candidate.EncryptedPassword);

                    StringBuilder args = new StringBuilder();
                    args.Append('"').Append(ShihabTunnelEntryName).Append('"');
                    if (!string.IsNullOrEmpty(user))
                    {
                        args.Append(' ').Append(QuoteCliArg(user));
                        args.Append(' ').Append(QuoteCliArg(pass ?? string.Empty));
                    }
                    args.Append(" /PHONEBOOK:").Append(QuoteCliArg(_shihabPbkPath));

                    string stdOut;
                    int exitCode = RunProcessBounded("rasdial.exe", args.ToString(), 18000, ct, out stdOut);
                    if (exitCode == 0 && IsShihabTunnelCurrentlyActive())
                    {
                        if (candidate.SourceKind == ShihabServerSourceKind.PublicOpenRelay && cfg != null)
                        {
                            ShihabPublicRelayService.RecordShihabRecentHost(cfg, candidate.CountryShort, candidate.BareSstpHost);
                            if (_shihabConfigService != null)
                            {
                                _shihabConfigService.SaveShihabConfig(cfg);
                            }
                        }
                        totalSw.Stop();
                        return ShihabResult<ShihabVpnServerNode>.Ok(candidate, totalSw.ElapsedMilliseconds);
                    }

                    lastDialErr = CleanRasDialErrorOutput(stdOut, exitCode);
                    DisconnectShihabRasOnly();
                }

                totalSw.Stop();
                return ShihabResult<ShihabVpnServerNode>.Fail("DIAL_FAILED", lastDialErr, totalSw.ElapsedMilliseconds);
            });
        }

        private ShihabResult<ShihabVpnServerNode> ConnectShihabSystemProxyInternal(
            ShihabVpnServerNode node,
            ShihabAppConfig cfg,
            Action<string> progressCallback,
            CancellationToken ct,
            Stopwatch sw)
        {
            string host = node.BareSstpHost;
            int port = node.Port > 0 ? node.Port : 8080;
            if (string.IsNullOrWhiteSpace(host))
            {
                return ShihabResult<ShihabVpnServerNode>.Fail("PROXY_HOST_EMPTY", "Proxy server hostname is empty.", sw.ElapsedMilliseconds);
            }

            if (progressCallback != null)
            {
                progressCallback(string.Format("Probing proxy gateway {0}:{1}...", host, port));
            }

            int rttMs;
            if (!ShihabPublicRelayService.ProbeSingleTcpPort(host, port, 4000, out rttMs))
            {
                return ShihabResult<ShihabVpnServerNode>.Fail(
                    "PROXY_UNREACHABLE",
                    string.Format("Custom proxy {0}:{1} did not accept TCP connection.", host, port),
                    sw.ElapsedMilliseconds);
            }

            if (ct.IsCancellationRequested)
            {
                return ShihabResult<ShihabVpnServerNode>.Fail("CANCELLED", "Proxy connection cancelled.", sw.ElapsedMilliseconds);
            }

            node.PingMs = rttMs > 0 ? rttMs : 20;
            bool useTlsUpstream = string.Equals(node.RawConfigProfile, "https", StringComparison.OrdinalIgnoreCase);
            string plainPass = ShihabConfigService.UnprotectShihabSecret(node.EncryptedPassword);

            StopShihabSystemProxyTunnel();

            ShihabLocalAuthProxyBridge bridge = new ShihabLocalAuthProxyBridge(
                host,
                port,
                useTlsUpstream,
                node.Username,
                plainPass);

            try
            {
                bridge.StartShihabBridge();
                ApplyWinInetSystemProxy("127.0.0.1:" + bridge.LocalListenPort.ToString(CultureInfo.InvariantCulture), cfg);

                lock (_procLock)
                {
                    _localProxyBridge = bridge;
                    _activeProxyConfigRef = cfg;
                }

                if (_shihabConfigService != null && cfg != null)
                {
                    _shihabConfigService.SaveShihabConfig(cfg);
                }

                sw.Stop();
                return ShihabResult<ShihabVpnServerNode>.Ok(node, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                bridge.Dispose();
                if (cfg != null)
                {
                    RestoreWinInetProxyFromConfig(cfg);
                }
                return ShihabResult<ShihabVpnServerNode>.Fail("PROXY_ARM_FAILED", ex.Message, sw.ElapsedMilliseconds);
            }
        }

        private static void ApplyWinInetSystemProxy(string loopbackEndpoint, ShihabAppConfig cfg)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(WinInetSettingsSubKey, true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Unable to open HKCU Internet Settings registry key.");
                }

                if (cfg != null && !cfg.SystemProxyArmedOnDisk)
                {
                    object origEnableObj = key.GetValue("ProxyEnable", 0);
                    int origEnable = 0;
                    if (origEnableObj is int)
                    {
                        origEnable = (int)origEnableObj;
                    }
                    string origServer = Convert.ToString(key.GetValue("ProxyServer", string.Empty), CultureInfo.InvariantCulture) ?? string.Empty;
                    string origOverride = Convert.ToString(key.GetValue("ProxyOverride", string.Empty), CultureInfo.InvariantCulture) ?? string.Empty;

                    cfg.SavedProxyEnable = origEnable;
                    cfg.SavedProxyServer = origServer;
                    cfg.SavedProxyOverride = origOverride;
                    cfg.SystemProxyArmedOnDisk = true;
                }

                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", loopbackEndpoint, RegistryValueKind.String);
                key.SetValue("ProxyOverride", "localhost;127.*;10.*;192.168.*;172.16.*;<local>", RegistryValueKind.String);
            }

            NotifyWinInetProxyChanged();
        }

        private static void RestoreWinInetProxyFromConfig(ShihabAppConfig cfg)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(WinInetSettingsSubKey, true))
                {
                    if (key != null)
                    {
                        int enable = (cfg != null && cfg.SystemProxyArmedOnDisk) ? cfg.SavedProxyEnable : 0;
                        string server = (cfg != null && cfg.SystemProxyArmedOnDisk) ? (cfg.SavedProxyServer ?? string.Empty) : string.Empty;
                        string over = (cfg != null && cfg.SystemProxyArmedOnDisk) ? (cfg.SavedProxyOverride ?? string.Empty) : string.Empty;

                        key.SetValue("ProxyEnable", enable, RegistryValueKind.DWord);
                        if (string.IsNullOrEmpty(server))
                        {
                            try { key.DeleteValue("ProxyServer", false); } catch { }
                        }
                        else
                        {
                            key.SetValue("ProxyServer", server, RegistryValueKind.String);
                        }

                        if (string.IsNullOrEmpty(over))
                        {
                            try { key.DeleteValue("ProxyOverride", false); } catch { }
                        }
                        else
                        {
                            key.SetValue("ProxyOverride", over, RegistryValueKind.String);
                        }
                    }
                }

                if (cfg != null)
                {
                    cfg.SystemProxyArmedOnDisk = false;
                }

                NotifyWinInetProxyChanged();
            }
            catch
            {
            }
        }

        private static void NotifyWinInetProxyChanged()
        {
            try
            {
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            }
            catch
            {
            }
        }

        private void StopShihabSystemProxyTunnel()
        {
            ShihabLocalAuthProxyBridge bridgeToStop = null;
            ShihabAppConfig cfgToRestore = null;

            lock (_procLock)
            {
                if (_localProxyBridge != null)
                {
                    bridgeToStop = _localProxyBridge;
                    _localProxyBridge = null;
                }
                if (_activeProxyConfigRef != null)
                {
                    cfgToRestore = _activeProxyConfigRef;
                    _activeProxyConfigRef = null;
                }
            }

            if (bridgeToStop != null)
            {
                try { bridgeToStop.Dispose(); } catch { }
            }

            if (cfgToRestore != null && cfgToRestore.SystemProxyArmedOnDisk)
            {
                RestoreWinInetProxyFromConfig(cfgToRestore);
                if (_shihabConfigService != null)
                {
                    _shihabConfigService.SaveShihabConfig(cfgToRestore);
                }
            }
        }

        private static void WarmResolveHostForRasDial(string fqdn, int maxWaitMs)
        {
            if (string.IsNullOrWhiteSpace(fqdn)) return;
            IPAddress dummy;
            if (IPAddress.TryParse(fqdn.Trim(), out dummy)) return;

            try
            {
                Task dnsTask = Task.Run(delegate
                {
                    try { Dns.GetHostAddresses(fqdn.Trim()); } catch { }
                });
                dnsTask.Wait(Math.Max(200, maxWaitMs));
            }
            catch
            {
            }
        }

        private static List<ShihabVpnServerNode> RaceFastestShihabSstpCandidates(
            List<ShihabVpnServerNode> pool,
            ShihabAppConfig cfg,
            Action<string> progressCallback,
            CancellationToken ct)
        {
            if (pool == null || pool.Count <= 1)
            {
                return pool;
            }

            int probeCap = Math.Min(pool.Count, 18);
            string regionLabel = pool[0] != null ? pool[0].CountryLong : "Selected Region";
            bool isGlobalAuto = cfg != null &&
                                cfg.AutoSelectFastest &&
                                (string.IsNullOrWhiteSpace(cfg.SelectedCountryCode) ||
                                 string.Equals(cfg.SelectedCountryCode, "AUTO", StringComparison.OrdinalIgnoreCase));
            if (isGlobalAuto)
            {
                regionLabel = "Global Low-Latency Hubs";
            }

            if (progressCallback != null)
            {
                progressCallback(string.Format("Racing {0} gateways for lowest latency ({1})...", probeCap, regionLabel));
            }

            object sync = new object();
            List<ShihabVpnServerNode> verifiedWinners = new List<ShihabVpnServerNode>();
            List<ShihabVpnServerNode> unverifiedFallbacks = new List<ShihabVpnServerNode>();

            using (SemaphoreSlim gate = new SemaphoreSlim(14))
            {
                List<Task> tasks = new List<Task>(probeCap);
                for (int i = 0; i < probeCap; i++)
                {
                    ShihabVpnServerNode item = pool[i];
                    if (item == null || item.SourceKind != ShihabServerSourceKind.PublicOpenRelay || item.Protocol != ShihabVpnProtocol.AutoSstp)
                    {
                        lock (sync)
                        {
                            if (item != null) verifiedWinners.Add(item);
                        }
                        continue;
                    }

                    tasks.Add(Task.Run(delegate
                    {
                        if (ct.IsCancellationRequested) return;
                        gate.Wait();
                        try
                        {
                            if (ct.IsCancellationRequested) return;
                            int vPort;
                            int singleIpRtt;
                            if (VerifyShihabSstpNodeDirectIp(item, 1350, ct, out vPort, out singleIpRtt))
                            {
                                item.Port = vPort;
                                if (singleIpRtt > 0)
                                {
                                    item.PingMs = singleIpRtt;
                                }
                                item.WasProbed = true;
                                item.IsLocallyProbed = true;
                                item.IsTcp443Reachable = true;
                                item.IsSstpVerified = true;
                                lock (sync)
                                {
                                    verifiedWinners.Add(item);
                                }
                            }
                            else
                            {
                                item.WasProbed = true;
                                item.IsLocallyProbed = true;
                                item.IsTcp443Reachable = false;
                                lock (sync)
                                {
                                    unverifiedFallbacks.Add(item);
                                }
                            }
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }));
                }

                try
                {
                    Task.WaitAll(tasks.ToArray(), 2900);
                }
                catch
                {
                }
            }

            if (verifiedWinners.Count > 0)
            {
                ShihabPublicRelayService.SortShihabRelays(verifiedWinners);
                string rotCountry = isGlobalAuto
                    ? "AUTO"
                    : (verifiedWinners[0].CountryShort ?? "AUTO");
                List<ShihabVpnServerNode> rotatedWinners = ShihabPublicRelayService.OrderCandidatesForShihabRotation(
                    verifiedWinners,
                    cfg,
                    rotCountry);

                // Cross-check exact kernel ICMP 1-RTT on the #1 winning server so local TCP socket scheduling jitter never inflates PingMs
                ShihabVpnServerNode topWinner = rotatedWinners[0];
                IPAddress winIp;
                if (topWinner != null && IPAddress.TryParse(topWinner.IpAddress ?? string.Empty, out winIp))
                {
                    int icmpMs = ShihabPublicRelayService.MeasureIcmpEchoRttMs(winIp, 550);
                    if (icmpMs > 0 && (topWinner.PingMs <= 0 || icmpMs < topWinner.PingMs))
                    {
                        topWinner.PingMs = icmpMs;
                    }
                }

                List<ShihabVpnServerNode> finalList = new List<ShihabVpnServerNode>(pool.Count);
                finalList.AddRange(rotatedWinners);
                for (int i = probeCap; i < pool.Count; i++)
                {
                    if (pool[i] != null && !ContainsNodeId(finalList, pool[i].Id))
                    {
                        finalList.Add(pool[i]);
                    }
                }
                for (int i = 0; i < unverifiedFallbacks.Count; i++)
                {
                    if (!ContainsNodeId(finalList, unverifiedFallbacks[i].Id))
                    {
                        finalList.Add(unverifiedFallbacks[i]);
                    }
                }
                return finalList;
            }

            return pool;
        }

        private static List<ShihabVpnServerNode> BuildShihabOrderedCandidates(
            ShihabVpnServerNode primary,
            IReadOnlyList<ShihabVpnServerNode> allRelays,
            ShihabAppConfig cfg)
        {
            List<ShihabVpnServerNode> result = new List<ShihabVpnServerNode>();

            if (primary.SourceKind != ShihabServerSourceKind.PublicOpenRelay || allRelays == null || allRelays.Count == 0)
            {
                result.Add(primary);
                return result;
            }

            bool explicitPinnedNode = cfg != null && !cfg.AutoSelectFastest && !string.IsNullOrWhiteSpace(cfg.SelectedServerId);
            if (explicitPinnedNode)
            {
                result.Add(primary);
            }

            bool isGlobalAuto = cfg != null &&
                                cfg.AutoSelectFastest &&
                                (string.IsNullOrWhiteSpace(cfg.SelectedCountryCode) ||
                                 string.Equals(cfg.SelectedCountryCode, "AUTO", StringComparison.OrdinalIgnoreCase));

            if (isGlobalAuto && !explicitPinnedNode)
            {
                List<ShihabVpnServerNode> globalSorted = new List<ShihabVpnServerNode>(allRelays.Count);
                for (int i = 0; i < allRelays.Count; i++)
                {
                    if (allRelays[i] != null) globalSorted.Add(allRelays[i]);
                }
                ShihabPublicRelayService.SortShihabRelays(globalSorted);

                Dictionary<string, int> perCountryCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < globalSorted.Count && result.Count < 20; i++)
                {
                    ShihabVpnServerNode cand = globalSorted[i];
                    string cc = cand.CountryShort ?? "GL";
                    int used;
                    if (!perCountryCount.TryGetValue(cc, out used)) used = 0;
                    if (used >= 3) continue;
                    if (!ContainsNodeId(result, cand.Id))
                    {
                        perCountryCount[cc] = used + 1;
                        result.Add(cand);
                    }
                }

                if (!ContainsNodeId(result, primary.Id))
                {
                    result.Insert(0, primary);
                }
                return result;
            }

            List<ShihabVpnServerNode> sameCountry = new List<ShihabVpnServerNode>();
            for (int i = 0; i < allRelays.Count; i++)
            {
                ShihabVpnServerNode cand = allRelays[i];
                if (cand == null) continue;
                if (explicitPinnedNode && string.Equals(cand.Id, primary.Id, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(cand.CountryShort, primary.CountryShort, StringComparison.OrdinalIgnoreCase))
                {
                    sameCountry.Add(cand);
                }
            }

            ShihabPublicRelayService.SortShihabRelays(sameCountry);
            List<ShihabVpnServerNode> rotatedSameCountry = ShihabPublicRelayService.OrderCandidatesForShihabRotation(
                sameCountry,
                cfg,
                primary.CountryShort);

            if (!explicitPinnedNode && rotatedSameCountry.Count == 0)
            {
                result.Add(primary);
            }

            for (int i = 0; i < rotatedSameCountry.Count && result.Count < 20; i++)
            {
                if (!ContainsNodeId(result, rotatedSameCountry[i].Id))
                {
                    result.Add(rotatedSameCountry[i]);
                }
            }

            if (!ContainsNodeId(result, primary.Id))
            {
                result.Insert(0, primary);
            }

            return result;
        }

        private static bool ContainsNodeId(List<ShihabVpnServerNode> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static ShihabVpnServerNode CloneNodeWithProtocol(ShihabVpnServerNode src, ShihabVpnProtocol proto, int port)
        {
            ShihabVpnServerNode c = new ShihabVpnServerNode();
            c.Id = src.Id;
            c.DisplayName = src.DisplayName;
            c.SourceKind = src.SourceKind;
            c.Protocol = proto;
            c.HostName = src.HostName;
            c.SstpEndpoint = src.BareSstpHost;
            c.IpAddress = src.IpAddress;
            c.Port = port;
            c.CountryLong = src.CountryLong;
            c.CountryShort = src.CountryShort;
            c.CityName = src.CityName;
            c.RegionGroup = src.RegionGroup;
            c.PingMs = src.PingMs;
            c.SpeedBps = src.SpeedBps;
            c.ActiveSessions = src.ActiveSessions;
            c.LoadPercent = src.LoadPercent;
            c.Latitude = src.Latitude;
            c.Longitude = src.Longitude;
            c.Username = src.Username;
            c.SupportsL2tpIpsec = src.SupportsL2tpIpsec;
            c.IsLocallyProbed = src.IsLocallyProbed;
            c.WasProbed = src.WasProbed;
            return c;
        }

        public static bool VerifyShihabSstpNodeDirectIp(
            ShihabVpnServerNode node,
            int primaryTimeoutMs,
            CancellationToken ct,
            out int verifiedPort,
            out int singleIpRttMs)
        {
            verifiedPort = node != null && node.Port > 0 ? node.Port : 443;
            singleIpRttMs = 0;
            if (node == null)
            {
                return false;
            }

            string fqdn = node.BareSstpHost;
            if (string.IsNullOrWhiteSpace(fqdn))
            {
                return false;
            }

            IPAddress ipAddr;
            if (!ShihabPublicRelayService.TryResolveEndpointIpOutsideTimer(node.IpAddress, fqdn, out ipAddr))
            {
                ipAddr = null;
            }
            else if (string.IsNullOrWhiteSpace(node.IpAddress) ||
                     node.IpAddress.IndexOf(".opengw.net", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                node.IpAddress = ipAddr.ToString();
            }

            int primaryPort = node.Port > 0 ? node.Port : 443;
            if (VerifySingleShihabSstpHandshakeWithIp(ipAddr, fqdn, primaryPort, primaryTimeoutMs, out singleIpRttMs))
            {
                verifiedPort = primaryPort;
                return true;
            }

            if (ct.IsCancellationRequested)
            {
                return false;
            }

            if (primaryPort != 443 && VerifySingleShihabSstpHandshakeWithIp(ipAddr, fqdn, 443, 1050, out singleIpRttMs))
            {
                verifiedPort = 443;
                return true;
            }

            return false;
        }

        public static bool TryVerifyWorkingShihabSstpPort(
            ShihabVpnServerNode node,
            CancellationToken ct,
            out int verifiedPort,
            out int rttMs)
        {
            verifiedPort = node != null && node.Port > 0 ? node.Port : 443;
            rttMs = 0;
            if (node == null)
            {
                return false;
            }

            string fqdn = node.BareSstpHost;
            if (string.IsNullOrWhiteSpace(fqdn))
            {
                return false;
            }

            IPAddress ipAddr;
            if (!ShihabPublicRelayService.TryResolveEndpointIpOutsideTimer(node.IpAddress, fqdn, out ipAddr))
            {
                ipAddr = null;
            }
            else if (string.IsNullOrWhiteSpace(node.IpAddress) ||
                     node.IpAddress.IndexOf(".opengw.net", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                node.IpAddress = ipAddr.ToString();
            }

            int primaryPort = node.Port > 0 ? node.Port : 443;
            if (VerifySingleShihabSstpHandshakeWithIp(ipAddr, fqdn, primaryPort, 1650, out rttMs))
            {
                if (ipAddr != null)
                {
                    int icmpMs = ShihabPublicRelayService.MeasureIcmpEchoRttMs(ipAddr, 550);
                    if (icmpMs > 0 && icmpMs < rttMs) rttMs = icmpMs;
                }
                verifiedPort = primaryPort;
                return true;
            }

            if (ct.IsCancellationRequested)
            {
                return false;
            }

            int[] fallbackPorts = new int[] { 443, 995, 5555 };
            for (int i = 0; i < fallbackPorts.Length; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    return false;
                }
                int fp = fallbackPorts[i];
                if (fp == primaryPort) continue;

                if (VerifySingleShihabSstpHandshakeWithIp(ipAddr, fqdn, fp, 1200, out rttMs))
                {
                    if (ipAddr != null)
                    {
                        int icmpMs = ShihabPublicRelayService.MeasureIcmpEchoRttMs(ipAddr, 550);
                        if (icmpMs > 0 && icmpMs < rttMs) rttMs = icmpMs;
                    }
                    verifiedPort = fp;
                    return true;
                }
            }

            return false;
        }

        public static bool VerifySingleShihabSstpHandshake(string fqdn, int port, int timeoutMs, out int rttMs)
        {
            IPAddress ipAddr;
            ShihabPublicRelayService.TryResolveEndpointIpOutsideTimer(null, fqdn, out ipAddr);
            return VerifySingleShihabSstpHandshakeWithIp(ipAddr, fqdn, port, timeoutMs, out rttMs);
        }

        public static bool VerifySingleShihabSstpHandshakeWithIp(
            IPAddress ipAddr,
            string fqdn,
            int port,
            int timeoutMs,
            out int rttMs)
        {
            rttMs = 0;
            if (string.IsNullOrWhiteSpace(fqdn) || port <= 0 || port > 65535)
            {
                return false;
            }

            TcpClient tcp = ipAddr != null ? new TcpClient(ipAddr.AddressFamily) : new TcpClient();
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                IAsyncResult ar = ipAddr != null
                    ? tcp.BeginConnect(ipAddr, port, null, null)
                    : tcp.BeginConnect(fqdn.Trim(), port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs, false) || !tcp.Connected)
                {
                    return false;
                }

                tcp.EndConnect(ar);
                sw.Stop();
                int tcpMs = Math.Max(1, (int)sw.ElapsedMilliseconds);
                tcp.ReceiveTimeout = timeoutMs;
                tcp.SendTimeout = timeoutMs;

                using (SslStream ssl = new SslStream(tcp.GetStream(), false))
                {
                    ssl.AuthenticateAsClient(fqdn.Trim());

                    string req = "SSTP_DUPLEX_POST /sra_{BA195980-CD49-458b-9E23-C84EE0ADCD75}/ HTTP/1.1\r\n" +
                                 "Host: " + fqdn.Trim() + "\r\n" +
                                 "Content-Length: 18446744073709551615\r\n" +
                                 "SSTPCORRELATIONID: {" + Guid.NewGuid().ToString() + "}\r\n\r\n";

                    byte[] reqBytes = Encoding.ASCII.GetBytes(req);
                    ssl.Write(reqBytes, 0, reqBytes.Length);
                    ssl.Flush();

                    byte[] buf = new byte[128];
                    int n = ssl.Read(buf, 0, buf.Length);
                    if (n <= 0)
                    {
                        return false;
                    }

                    string hdr = Encoding.ASCII.GetString(buf, 0, n);
                    int eol = hdr.IndexOf('\n');
                    string statusLine = eol > 0 ? hdr.Substring(0, eol) : hdr;
                    if (statusLine.IndexOf(" 200 ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        statusLine.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase))
                    {
                        rttMs = tcpMs;
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                try { tcp.Close(); } catch { }
            }

            return false;
        }

        public Task DisconnectShihabTunnelAsync()
        {
            return Task.Run(new Action(DisconnectShihabTunnelSync));
        }

        public void DisconnectShihabTunnelSync()
        {
            DisconnectShihabRasOnly();
            StopShihabOpenVpnProcess();
            StopShihabWireGuardTunnel();
            StopShihabSystemProxyTunnel();
        }

        public bool IsShihabTunnelCurrentlyActive()
        {
            if (IsShihabRasEntryConnected(ShihabTunnelEntryName))
            {
                return true;
            }

            lock (_procLock)
            {
                if (_openVpnProcess != null)
                {
                    try
                    {
                        if (!_openVpnProcess.HasExited)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
                if (_wireGuardTunnelInstalled)
                {
                    return true;
                }
                if (_localProxyBridge != null && _localProxyBridge.IsRunning)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsShihabRasEntryConnected(string entryName)
        {
            try
            {
                int structSize = Marshal.SizeOf(typeof(RASCONN));
                RASCONN[] arr = new RASCONN[16];
                for (int i = 0; i < arr.Length; i++)
                {
                    arr[i].dwSize = structSize;
                }
                int cb = structSize * arr.Length;
                int count = 0;
                uint rc = RasEnumConnections(arr, ref cb, ref count);
                if (rc == 0 && count > 0)
                {
                    int max = Math.Min(count, arr.Length);
                    for (int i = 0; i < max; i++)
                    {
                        if (string.Equals(arr[i].szEntryName, entryName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        private void DisconnectShihabRasOnly()
        {
            try
            {
                int structSize = Marshal.SizeOf(typeof(RASCONN));
                RASCONN[] arr = new RASCONN[16];
                for (int i = 0; i < arr.Length; i++)
                {
                    arr[i].dwSize = structSize;
                }
                int cb = structSize * arr.Length;
                int count = 0;
                if (RasEnumConnections(arr, ref cb, ref count) == 0 && count > 0)
                {
                    int max = Math.Min(count, arr.Length);
                    for (int i = 0; i < max; i++)
                    {
                        if (string.Equals(arr[i].szEntryName, ShihabTunnelEntryName, StringComparison.OrdinalIgnoreCase) &&
                            arr[i].hrasconn != IntPtr.Zero)
                        {
                            RasHangUp(arr[i].hrasconn);
                        }
                    }
                }
            }
            catch
            {
            }

            string ignored;
            RunProcessBounded("rasdial.exe", "\"" + ShihabTunnelEntryName + "\" /DISCONNECT", 5000, CancellationToken.None, out ignored);
        }

        private ShihabResult<ShihabVpnServerNode> ConnectShihabOpenVpnInternal(
            ShihabVpnServerNode node,
            Action<string> progressCallback,
            CancellationToken ct,
            Stopwatch sw)
        {
            string openVpnExe = LocateInstalledExecutable(
                "openvpn.exe",
                new string[]
                {
                    @"C:\Program Files\OpenVPN\bin\openvpn.exe",
                    @"C:\Program Files (x86)\OpenVPN\bin\openvpn.exe"
                });

            if (string.IsNullOrEmpty(openVpnExe))
            {
                return ShihabResult<ShihabVpnServerNode>.Fail(
                    "OPENVPN_NOT_FOUND",
                    "OpenVPN binary (openvpn.exe) is not installed. Switch protocol to MS-SSTP TLS (built-in Windows) or install OpenVPN.",
                    sw.ElapsedMilliseconds);
            }

            string ovpnText;
            if (!ShihabConfigParsers.TryDecodeShihabOpenVpnConfig(node, out ovpnText))
            {
                return ShihabResult<ShihabVpnServerNode>.Fail("OVPN_EMPTY", "Selected node has no valid OpenVPN configuration.", sw.ElapsedMilliseconds);
            }

            StopShihabOpenVpnProcess();
            if (!Directory.Exists(_shihabProfilesDir))
            {
                Directory.CreateDirectory(_shihabProfilesDir);
            }

            string ovpnFile = Path.Combine(_shihabProfilesDir, "active_shihab_session.ovpn");
            File.WriteAllText(ovpnFile, ovpnText, new UTF8Encoding(false));

            if (progressCallback != null)
            {
                progressCallback("Starting OpenVPN tunnel...");
            }

            ManualResetEventSlim connectedSignal = new ManualResetEventSlim(false);
            StringBuilder errLog = new StringBuilder();

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = openVpnExe;
            psi.Arguments = "--config " + QuoteCliArg(ovpnFile) + " --data-ciphers AES-256-GCM:AES-128-GCM:CHACHA20-POLY1305:AES-128-CBC";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            Process proc = new Process();
            proc.StartInfo = psi;
            proc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    if (e.Data.IndexOf("Initialization Sequence Completed", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        connectedSignal.Set();
                    }
                    else if (errLog.Length < 1024)
                    {
                        errLog.AppendLine(e.Data);
                    }
                }
            };
            proc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (!string.IsNullOrEmpty(e.Data) && errLog.Length < 1024)
                {
                    errLog.AppendLine(e.Data);
                }
            };

            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                lock (_procLock)
                {
                    _openVpnProcess = proc;
                }

                int waited = 0;
                while (waited < 20000)
                {
                    if (ct.IsCancellationRequested)
                    {
                        StopShihabOpenVpnProcess();
                        return ShihabResult<ShihabVpnServerNode>.Fail("CANCELLED", "OpenVPN connection cancelled.", sw.ElapsedMilliseconds);
                    }
                    if (connectedSignal.Wait(250))
                    {
                        sw.Stop();
                        return ShihabResult<ShihabVpnServerNode>.Ok(node, sw.ElapsedMilliseconds);
                    }
                    if (proc.HasExited)
                    {
                        break;
                    }
                    waited += 250;
                }

                StopShihabOpenVpnProcess();
                return ShihabResult<ShihabVpnServerNode>.Fail(
                    "OVPN_TIMEOUT",
                    "OpenVPN handshake did not complete. Ensure Administrator rights for TUN adapter creation.",
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                StopShihabOpenVpnProcess();
                return ShihabResult<ShihabVpnServerNode>.Fail("OVPN_START_ERR", ex.Message, sw.ElapsedMilliseconds);
            }
            finally
            {
                connectedSignal.Dispose();
            }
        }

        private ShihabResult<ShihabVpnServerNode> ConnectShihabWireGuardInternal(
            ShihabVpnServerNode node,
            Action<string> progressCallback,
            CancellationToken ct,
            Stopwatch sw)
        {
            string wgExe = LocateInstalledExecutable(
                "wireguard.exe",
                new string[]
                {
                    @"C:\Program Files\WireGuard\wireguard.exe",
                    @"C:\Program Files (x86)\WireGuard\wireguard.exe"
                });

            if (string.IsNullOrEmpty(wgExe))
            {
                return ShihabResult<ShihabVpnServerNode>.Fail(
                    "WIREGUARD_NOT_FOUND",
                    "WireGuard for Windows (wireguard.exe) was not found. Install WireGuard or use MS-SSTP / IKEv2.",
                    sw.ElapsedMilliseconds);
            }

            if (string.IsNullOrWhiteSpace(node.RawConfigProfile))
            {
                return ShihabResult<ShihabVpnServerNode>.Fail("WG_EMPTY", "WireGuard profile content is empty.", sw.ElapsedMilliseconds);
            }

            StopShihabWireGuardTunnel();
            if (!Directory.Exists(_shihabProfilesDir))
            {
                Directory.CreateDirectory(_shihabProfilesDir);
            }

            string tunnelName = "ShihabShieldWG";
            string confFile = Path.Combine(_shihabProfilesDir, tunnelName + ".conf");
            File.WriteAllText(confFile, node.RawConfigProfile, new UTF8Encoding(false));

            if (progressCallback != null)
            {
                progressCallback("Activating WireGuard tunnel...");
            }

            string output;
            int rc = RunProcessBounded(wgExe, "/installtunnelservice " + QuoteCliArg(confFile), 15000, ct, out output);
            if (rc == 0)
            {
                lock (_procLock)
                {
                    _wireGuardTunnelInstalled = true;
                    _wireGuardTunnelName = tunnelName;
                }
                sw.Stop();
                return ShihabResult<ShihabVpnServerNode>.Ok(node, sw.ElapsedMilliseconds);
            }

            return ShihabResult<ShihabVpnServerNode>.Fail(
                "WG_INSTALL_ERR",
                "WireGuard tunnel activation requires Administrator elevation (" + output.Trim() + ").",
                sw.ElapsedMilliseconds);
        }

        private void StopShihabOpenVpnProcess()
        {
            lock (_procLock)
            {
                if (_openVpnProcess != null)
                {
                    try
                    {
                        if (!_openVpnProcess.HasExited)
                        {
                            _openVpnProcess.Kill();
                            _openVpnProcess.WaitForExit(2000);
                        }
                    }
                    catch
                    {
                    }
                    try { _openVpnProcess.Dispose(); } catch { }
                    _openVpnProcess = null;
                }
            }
        }

        private void StopShihabWireGuardTunnel()
        {
            string tunnelToStop = null;
            lock (_procLock)
            {
                if (_wireGuardTunnelInstalled && !string.IsNullOrEmpty(_wireGuardTunnelName))
                {
                    tunnelToStop = _wireGuardTunnelName;
                    _wireGuardTunnelInstalled = false;
                    _wireGuardTunnelName = null;
                }
            }

            if (!string.IsNullOrEmpty(tunnelToStop))
            {
                string wgExe = LocateInstalledExecutable(
                    "wireguard.exe",
                    new string[]
                    {
                        @"C:\Program Files\WireGuard\wireguard.exe",
                        @"C:\Program Files (x86)\WireGuard\wireguard.exe"
                    });
                if (!string.IsNullOrEmpty(wgExe))
                {
                    string ignored;
                    RunProcessBounded(wgExe, "/uninstalltunnelservice " + QuoteCliArg(tunnelToStop), 8000, CancellationToken.None, out ignored);
                }
            }
        }

        private static string LocateInstalledExecutable(string exeName, string[] knownLocations)
        {
            if (knownLocations != null)
            {
                for (int i = 0; i < knownLocations.Length; i++)
                {
                    if (File.Exists(knownLocations[i]))
                    {
                        return knownLocations[i];
                    }
                }
            }

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] dirs = pathEnv.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < dirs.Length; i++)
            {
                try
                {
                    string candidate = Path.Combine(dirs[i].Trim(), exeName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        private static int RunProcessBounded(string fileName, string arguments, int timeoutMs, CancellationToken ct, out string combinedOutput)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) sb.AppendLine(e.Data);
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) sb.AppendLine(e.Data);
                    };

                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    int elapsed = 0;
                    while (!p.WaitForExit(200))
                    {
                        elapsed += 200;
                        if (ct.IsCancellationRequested || elapsed >= timeoutMs)
                        {
                            try { p.Kill(); } catch { }
                            combinedOutput = sb.ToString();
                            return -1;
                        }
                    }
                    p.WaitForExit();
                    combinedOutput = sb.ToString();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                combinedOutput = ex.Message;
                return -2;
            }
        }

        private static string CleanRasDialErrorOutput(string rawOutput, int exitCode)
        {
            if (string.IsNullOrWhiteSpace(rawOutput))
            {
                return "Remote VPN endpoint did not complete handshake (exit " + exitCode + ").";
            }
            string[] lines = rawOutput.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder concise = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (l.StartsWith("Connecting to", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("For more help", StringComparison.OrdinalIgnoreCase) ||
                    l.StartsWith("Command completed", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (concise.Length > 0) concise.Append(" ");
                concise.Append(l);
                if (concise.Length > 180) break;
            }
            string text = concise.ToString().Trim();
            return string.IsNullOrEmpty(text) ? ("VPN dial failed (code " + exitCode + ").") : text;
        }

        private static string QuoteCliArg(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                StopShihabOpenVpnProcess();
                StopShihabWireGuardTunnel();
                StopShihabSystemProxyTunnel();
            }
        }
    }
}
