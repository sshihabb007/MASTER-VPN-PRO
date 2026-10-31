using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using ShihabShieldVpn.Core;
using ShihabShieldVpn.Services;
using ShihabShieldVpn.UI;

[assembly: AssemblyTitle("MASTER VPN PRO")]
[assembly: AssemblyDescription("MASTER VPN PRO — Portable Open-Source Multi-Protocol Windows VPN Client (Mehedi Hasan Shihab)")]
[assembly: AssemblyCompany("Mehedi Hasan Shihab")]
[assembly: AssemblyProduct("MASTER VPN PRO")]
[assembly: AssemblyCopyright("Copyright © 2026 Mehedi Hasan Shihab (sshihabb007)")]
[assembly: AssemblyVersion("1.3.1.0")]
[assembly: AssemblyFileVersion("1.3.1.0")]
[assembly: ComVisible(false)]

namespace ShihabShieldVpn
{
    public static class Program
    {
        private const string ShihabSingleInstanceMutex = @"Local\Shihab_ShieldVPN_SingleInstance_v1";
        private const string ShihabActivateEventName = @"Local\Shihab_ShieldVPN_ActivateEvent_v1";

        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                int? cliExit = TryHandleShihabCliMode(args);
                if (cliExit.HasValue)
                {
                    return cliExit.Value;
                }
            }

            bool startMinimized = HasShihabArg(args, "--minimized") || HasShihabArg(args, "-minimized");
            string captureDir = GetShihabArgValue(args, "--capture-ui");

            bool createdNew = false;
            string mutexName = string.IsNullOrWhiteSpace(captureDir)
                ? ShihabSingleInstanceMutex
                : (ShihabSingleInstanceMutex + "_Capture_" + Guid.NewGuid().ToString("N"));

            using (Mutex singleMutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    try
                    {
                        using (EventWaitHandle evt = EventWaitHandle.OpenExisting(ShihabActivateEventName))
                        {
                            evt.Set();
                        }
                    }
                    catch
                    {
                    }
                    return 0;
                }

                EventWaitHandle activateEvt = new EventWaitHandle(false, EventResetMode.AutoReset, ShihabActivateEventName);
                RegisteredWaitHandle waitReg = null;

                try
                {
                    Application app = new Application();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;

                    string tempCaptureProfileDir = null;
                    if (!string.IsNullOrWhiteSpace(captureDir))
                    {
                        tempCaptureProfileDir = Path.Combine(Path.GetTempPath(), "ShihabShieldVPN_Preview_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                        Directory.CreateDirectory(tempCaptureProfileDir);
                    }

                    ShihabConfigService cfgSvc = new ShihabConfigService(tempCaptureProfileDir);
                    ShihabPublicRelayService relaySvc = new ShihabPublicRelayService(cfgSvc.DataDirectory);
                    ShihabVpnEngine vpnEngine = new ShihabVpnEngine(cfgSvc.DataDirectory, cfgSvc);
                    ShihabSecurityShieldService shieldSvc = new ShihabSecurityShieldService(cfgSvc);
                    ShihabTelemetryAndSpeedService telemetrySvc = new ShihabTelemetryAndSpeedService();
                    ShihabTrayManager tray = new ShihabTrayManager();

                    ShihabMainWindow mainWindow = new ShihabMainWindow(cfgSvc, relaySvc, vpnEngine, shieldSvc, telemetrySvc, tray);
                    app.MainWindow = mainWindow;

                    if (string.IsNullOrWhiteSpace(captureDir))
                    {
                        waitReg = ThreadPool.RegisterWaitForSingleObject(
                            activateEvt,
                            delegate(object state, bool timedOut)
                            {
                                if (!timedOut)
                                {
                                    mainWindow.Dispatcher.BeginInvoke(new Action(mainWindow.RestoreAndActivateShihabWindow));
                                }
                            },
                            null,
                            -1,
                            false);
                    }

                    mainWindow.InitializeShihabRuntime(!string.IsNullOrWhiteSpace(captureDir));

                    if (!string.IsNullOrWhiteSpace(captureDir))
                    {
                        mainWindow.Show();
                        mainWindow.RunShihabUiPreviewCaptureAsync(captureDir);
                    }
                    else if (!startMinimized)
                    {
                        mainWindow.Show();
                    }

                    app.Run();

                    if (!string.IsNullOrEmpty(tempCaptureProfileDir) && Directory.Exists(tempCaptureProfileDir))
                    {
                        try { Directory.Delete(tempCaptureProfileDir, true); } catch { }
                    }
                    return 0;
                }
                finally
                {
                    if (waitReg != null)
                    {
                        waitReg.Unregister(null);
                    }
                    activateEvt.Dispose();
                }
            }
        }

        private static int? TryHandleShihabCliMode(string[] args)
        {
            string outFile = GetShihabArgValue(args, "--out");

            if (HasShihabArg(args, "--self-test"))
            {
                return RunShihabSelfTestSuite(outFile, HasShihabArg(args, "--skip-external-network"));
            }

            bool doStatus = HasShihabArg(args, "--status");
            bool doDisconnect = HasShihabArg(args, "--disconnect");
            bool doConnectAuto = HasShihabArg(args, "--connect-auto");

            if (!doStatus && !doDisconnect && !doConnectAuto)
            {
                return null;
            }

            ShihabConfigService cfgSvc = new ShihabConfigService();
            ShihabAppConfig cfg = cfgSvc.LoadShihabConfig();
            using (ShihabVpnEngine engine = new ShihabVpnEngine(cfgSvc.DataDirectory, cfgSvc))
            {
                if (doStatus)
                {
                    ShihabTelemetryAndSpeedService tel = new ShihabTelemetryAndSpeedService();
                    ShihabPhysicalNetworkInfo net = tel.DetectShihabPhysicalNetwork();
                    bool active = engine.IsShihabTunnelCurrentlyActive();
                    WriteShihabReport(
                        outFile,
                        string.Format("TunnelActive={0} | PhysicalAdapter={1} ({2}) | LocalIP={3}",
                            active, net.AdapterTitle, net.InterfaceDescription, net.LocalIpv4));
                    return 0;
                }

                if (doDisconnect)
                {
                    engine.DisconnectShihabTunnelSync();
                    engine.RecoverOrphanedShihabSystemProxy(cfg, false);
                    ShihabSecurityShieldService shield = new ShihabSecurityShieldService(cfgSvc);
                    shield.DisarmShihabFirewallKillSwitch(cfg);
                    cfgSvc.SaveShihabConfig(cfg);
                    WriteShihabReport(outFile, "OK: Disconnected MASTER VPN PRO tunnel & restored system proxy.");
                    return 0;
                }

                if (doConnectAuto)
                {
                    ShihabPublicRelayService relaySvc = new ShihabPublicRelayService(cfgSvc.DataDirectory);
                    ShihabResult<List<ShihabVpnServerNode>> listRes = relaySvc.RefreshShihabPublicRelaysAsync(true, cfg.ShowExtendedCatalog).GetAwaiter().GetResult();
                    ShihabVpnServerNode target = relaySvc.ResolveBestShihabTargetNode(cfg, listRes.Value);
                    if (target == null)
                    {
                        WriteShihabReport(outFile, "ERROR: No VPN servers available.");
                        return 2;
                    }
                    ShihabResult<ShihabVpnServerNode> connRes = engine.ConnectShihabTunnelAsync(
                        target,
                        listRes.Value,
                        cfg,
                        null,
                        CancellationToken.None).GetAwaiter().GetResult();
                    if (!connRes.IsSuccess)
                    {
                        WriteShihabReport(outFile, "ERROR: " + connRes.ErrorMessage);
                        return 1;
                    }
                    WriteShihabReport(outFile, string.Format("OK: Connected to {0} ({1})", connRes.Value.CountryLong, connRes.Value.IpAddress));
                    return 0;
                }
            }

            return null;
        }

        private static int RunShihabSelfTestSuite(string outFile, bool skipExternalNetwork)
        {
            StringBuilder log = new StringBuilder();
            int failures = 0;

            Action<string, bool> check = delegate(string name, bool ok)
            {
                if (ok)
                {
                    log.AppendLine("[PASS] " + name);
                }
                else
                {
                    failures++;
                    log.AppendLine("[FAIL] " + name);
                }
            };

            // 1. Public VPN Gate CSV Parser, Custom SoftEther TCP Port Extraction & Coordinate Mapping
            string usTcpOvpnB64 = Convert.ToBase64String(Encoding.ASCII.GetBytes("client\r\nproto tcp\r\nremote 50.39.171.59 1411\r\n"));
            string sampleCsv =
                "*vpn_servers\r\n" +
                "#HostName,IP,Score,Ping,Speed,CountryLong,CountryShort,NumVpnSessions,Uptime,TotalUsers,TotalTraffic,LogType,Operator,Message,OpenVPN_ConfigData_Base64\r\n" +
                "public-vpn-189,219.100.37.180,2651428,22,350123326,Japan,JP,91,589133,19784,120008,2weeks,Academic,,dGVzdA==\r\n" +
                "public-vpn-44,14.32.10.9,1900000,31,180000000,Korea Republic of,KR,48,120000,8000,90000,2weeks,Academic,,dGVzdA==\r\n" +
                "vpn181814903,50.39.171.59,1500000,19,9600000,United States,US,12,88000,2000,40000,2weeks,Volunteer,," + usTcpOvpnB64 + "\r\n";

            List<ShihabVpnServerNode> csvNodes = ShihabConfigParsers.ParseShihabVpnGateCsv(sampleCsv);
            check("VPN Gate CSV parser extracts 3 public nodes", csvNodes.Count == 3);
            check("Public node derives <HostName>.opengw.net MS-SSTP endpoint",
                csvNodes.Count == 3 && csvNodes[0].SstpEndpoint == "public-vpn-189.opengw.net" && csvNodes[0].PingMs == 22);
            check("US custom SoftEther TCP port (1411) extracted cleanly into EffectiveDialAddress without corrupting BareSstpHost",
                csvNodes.Count == 3 &&
                csvNodes[2].Port == 1411 &&
                csvNodes[2].BareSstpHost == "vpn181814903.opengw.net" &&
                csvNodes[2].EffectiveDialAddress == "vpn181814903.opengw.net:1411");
            check("Country title normalization (Korea Republic of -> South Korea)",
                csvNodes.Count == 3 && csvNodes[1].CountryLong == "South Korea" && Math.Abs(csvNodes[0].Latitude - 36.20) < 1.0);

            // 2. WireGuard .conf Parser + Malformed Input Rejection
            string validWg =
                "[Interface]\r\nPrivateKey = AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\r\nAddress = 10.66.66.2/32\r\n" +
                "[Peer]\r\nPublicKey = BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=\r\nEndpoint = wg.shihab.example.com:51820\r\nAllowedIPs = 0.0.0.0/0\r\n";
            ShihabResult<ShihabVpnServerNode> wgOk = ShihabConfigParsers.ParseShihabWireGuardConf(validWg, "TestWG");
            check("WireGuard .conf parser extracts Endpoint host & port 51820",
                wgOk.IsSuccess && wgOk.Value.HostName == "wg.shihab.example.com" && wgOk.Value.Port == 51820 && wgOk.Value.Protocol == ShihabVpnProtocol.WireGuard);

            ShihabResult<ShihabVpnServerNode> wgBad = ShihabConfigParsers.ParseShihabWireGuardConf("[Interface]\r\nAddress=10.0.0.2\r\n", "BadWG");
            check("WireGuard parser rejects incomplete config missing keys/endpoint", !wgBad.IsSuccess);

            // 3. OpenVPN .ovpn Parser + Malformed Input Rejection
            string validOvpn = "client\r\ndev tun\r\nproto tcp\r\nremote ovpn.shihab.example.org 443\r\n";
            ShihabResult<ShihabVpnServerNode> ovpnOk = ShihabConfigParsers.ParseShihabOpenVpnProfile(validOvpn, "TestOVPN");
            check("OpenVPN .ovpn parser extracts remote host & port 443",
                ovpnOk.IsSuccess && ovpnOk.Value.HostName == "ovpn.shihab.example.org" && ovpnOk.Value.Port == 443 && ovpnOk.Value.Protocol == ShihabVpnProtocol.OpenVpn);

            ShihabResult<ShihabVpnServerNode> ovpnBad = ShihabConfigParsers.ParseShihabOpenVpnProfile("client\r\ndev tun\r\n", "BadOVPN");
            check("OpenVPN parser rejects profile missing remote directive", !ovpnBad.IsSuccess);

            // 3b. OpenVPN Base64 Export Decoder, 113-Country Metadata, Custom HTTP/HTTPS System Proxy Parser & Dynamic IP Rotation
            string decodedUsOvpn;
            bool ovpnDecodedOk = csvNodes.Count == 3 &&
                                 csvNodes[2].HasExportableOpenVpnProfile &&
                                 ShihabConfigParsers.TryDecodeShihabOpenVpnConfig(csvNodes[2], out decodedUsOvpn) &&
                                 decodedUsOvpn.IndexOf("remote 50.39.171.59 1411", StringComparison.OrdinalIgnoreCase) >= 0;
            check("OpenVPN Base64 profile decoder extracts exportable .ovpn payload cleanly", ovpnDecodedOk);

            string bdCity = ShihabConfigParsers.ResolveShihabCountryCity("BD");
            string deReg = ShihabConfigParsers.ResolveShihabCountryRegion("DE");
            string jpClock = ShihabConfigParsers.FormatShihabCountryLocalClock("JP", new DateTime(2026, 10, 31, 0, 30, 0, DateTimeKind.Utc));
            check("113-Country metadata resolves gateway cities, regional groups, and local UTC clocks",
                bdCity == "Dhaka" && deReg == "EMEA" && jpClock.IndexOf("09:30", StringComparison.OrdinalIgnoreCase) >= 0);

            string proxyPlainPass;
            ShihabResult<ShihabVpnServerNode> proxyParsed = ShihabConfigParsers.ParseShihabCustomProxyProfile(
                "https://shihab_user:Secr3tPass@proxy.shihab.example.com:8443",
                "United Kingdom Private Proxy",
                string.Empty,
                string.Empty,
                out proxyPlainPass);
            check("Custom HTTP/HTTPS System Proxy parser extracts TLS scheme, host:port, country & credentials without plaintext disk storage",
                proxyParsed.IsSuccess &&
                proxyParsed.Value != null &&
                proxyParsed.Value.Protocol == ShihabVpnProtocol.HttpSystemProxy &&
                proxyParsed.Value.HostName == "proxy.shihab.example.com" &&
                proxyParsed.Value.Port == 8443 &&
                proxyParsed.Value.RawConfigProfile == "https" &&
                proxyParsed.Value.CountryShort == "GB" &&
                proxyParsed.Value.Username == "shihab_user" &&
                proxyPlainPass == "Secr3tPass");

            ShihabAppConfig rotCfg = new ShihabAppConfig();
            rotCfg.DynamicIpRotation = true;
            List<ShihabVpnServerNode> rotPool = new List<ShihabVpnServerNode>
            {
                new ShihabVpnServerNode { HostName = "us1", SstpEndpoint = "us1.opengw.net", CountryShort = "US", IsSstpVerified = true, PingMs = 20 },
                new ShihabVpnServerNode { HostName = "us2", SstpEndpoint = "us2.opengw.net", CountryShort = "US", IsSstpVerified = true, PingMs = 24 }
            };
            ShihabPublicRelayService.RecordShihabRecentHost(rotCfg, "US", "us1.opengw.net");
            List<ShihabVpnServerNode> dynOrdered = ShihabPublicRelayService.OrderCandidatesForShihabRotation(rotPool, rotCfg, "US");
            rotCfg.DynamicIpRotation = false;
            List<ShihabVpnServerNode> stickyOrdered = ShihabPublicRelayService.OrderCandidatesForShihabRotation(rotPool, rotCfg, "US");
            check("Dynamic IP Rotation selects fresh unvisited gateway when ACTIVE and pins last gateway when STICKY",
                dynOrdered.Count == 2 && dynOrdered[0].BareSstpHost == "us2.opengw.net" &&
                stickyOrdered.Count == 2 && stickyOrdered[0].BareSstpHost == "us1.opengw.net");

            // 3c. Low-Latency Ranking & Latency-Constrained Rotation Regressions
            List<ShihabVpnServerNode> latencyRankPool = new List<ShihabVpnServerNode>
            {
                // Unprobed remote-hinted home DSL node with Tsukuba 3ms hint and 5.5 Mbps
                new ShihabVpnServerNode { Id = "jp_slow_dsl", HostName = "vpn248153573", SstpEndpoint = "vpn248153573.opengw.net", CountryShort = "JP", WasProbed = true, IsTcp443Reachable = true, IsLocallyProbed = false, IsSstpVerified = false, PingMs = 3, SpeedBps = 5500000L },
                // Locally-probed 119ms 222 Mbps backbone fiber node
                new ShihabVpnServerNode { Id = "jp_fast_fiber", HostName = "public-vpn-90", SstpEndpoint = "public-vpn-90.opengw.net", CountryShort = "JP", WasProbed = true, IsTcp443Reachable = true, IsLocallyProbed = true, IsSstpVerified = true, PingMs = 119, SpeedBps = 222000000L }
            };
            ShihabPublicRelayService.SortShihabRelays(latencyRankPool);
            check("Locally-probed 1-RTT backbone gateway outranks unprobed remote-hinted 5.5 Mbps node",
                latencyRankPool[0].Id == "jp_fast_fiber");

            ShihabAppConfig slowGuardCfg = new ShihabAppConfig { DynamicIpRotation = true };
            List<ShihabVpnServerNode> slowGuardPool = new List<ShihabVpnServerNode>
            {
                new ShihabVpnServerNode { HostName = "sg_fast", SstpEndpoint = "sg_fast.opengw.net", CountryShort = "SG", IsLocallyProbed = true, IsSstpVerified = true, PingMs = 52, SpeedBps = 120000000L },
                new ShihabVpnServerNode { HostName = "sg_slow", SstpEndpoint = "sg_slow.opengw.net", CountryShort = "SG", IsLocallyProbed = true, IsSstpVerified = true, PingMs = 442, SpeedBps = 8000000L }
            };
            ShihabPublicRelayService.RecordShihabRecentHost(slowGuardCfg, "SG", "sg_fast.opengw.net");
            List<ShihabVpnServerNode> guardedOrder = ShihabPublicRelayService.OrderCandidatesForShihabRotation(slowGuardPool, slowGuardCfg, "SG");
            check("Latency-constrained Dynamic IP Rotation never jumps from a 52ms verified gateway to a 442ms slow node",
                guardedOrder.Count == 2 && guardedOrder[0].BareSstpHost == "sg_fast.opengw.net");

            int bridgePort = 0;
            bool bridgeRan = false;
            bool bridgeStopped = false;
            using (ShihabLocalAuthProxyBridge bridge = new ShihabLocalAuthProxyBridge("127.0.0.1", 8080, false, "shihab", "pw"))
            {
                bridge.StartShihabBridge();
                bridgePort = bridge.LocalListenPort;
                bridgeRan = bridge.IsRunning && bridgePort > 0;
                bridge.Dispose();
                bridgeStopped = !bridge.IsRunning;
            }
            check("Local Loopback Authenticated Proxy Bridge starts on ephemeral 127.0.0.1 port and disposes cleanly",
                bridgeRan && bridgeStopped);

            bool companionOk = false;
            ShihabBrowserCompanionBridgeCallbacks compCb = new ShihabBrowserCompanionBridgeCallbacks();
            compCb.GetStatusSnapshot = delegate
            {
                Dictionary<string, object> s = new Dictionary<string, object>();
                s["state"] = "Disconnected";
                return s;
            };
            using (ShihabBrowserCompanionService comp = new ShihabBrowserCompanionService(compCb, 0))
            {
                if (comp.TryStartShihabCompanion() && comp.ActivePort > 0)
                {
                    System.Net.HttpWebRequest okReq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("http://127.0.0.1:" + comp.ActivePort + "/api/shihab/status");
                    okReq.Headers["Origin"] = "chrome-extension://abcdefghijklmnopabcdefghijklmnop";
                    using (System.Net.HttpWebResponse okResp = (System.Net.HttpWebResponse)okReq.GetResponse())
                    {
                        companionOk = (int)okResp.StatusCode == 200;
                    }

                    bool blockedWebOrigin = false;
                    try
                    {
                        System.Net.HttpWebRequest badReq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("http://127.0.0.1:" + comp.ActivePort + "/api/shihab/status");
                        badReq.Headers["Origin"] = "https://evil.example.com";
                        using (badReq.GetResponse()) { }
                    }
                    catch (System.Net.WebException wex)
                    {
                        System.Net.HttpWebResponse badResp = wex.Response as System.Net.HttpWebResponse;
                        blockedWebOrigin = badResp != null && (int)badResp.StatusCode == 403;
                    }
                    companionOk = companionOk && blockedWebOrigin;
                }
            }
            check("Chrome/Brave Companion Bridge accepts chrome-extension:// origin and blocks web-page Origin (403)", companionOk);

            // 4. Windows DPAPI Secret Round-Trip Protection
            string sampleSecret = "Shihab_Test_P@ssw0rd_2026!";
            string encSecret = ShihabConfigService.ProtectShihabSecret(sampleSecret);
            string decSecret = ShihabConfigService.UnprotectShihabSecret(encSecret);
            check("Windows DPAPI secret round-trip encrypts & decrypts without plaintext exposure",
                !string.IsNullOrEmpty(encSecret) && encSecret != sampleSecret && decSecret == sampleSecret);

            // 5. Rolling 24-Hour Traffic Bucket Aggregation
            ShihabAppConfig testCfg = new ShihabAppConfig();
            DateTime fixedUtc = new DateTime(2026, 10, 31, 9, 15, 0, DateTimeKind.Utc);
            ShihabConfigService.RecordShihabTrafficDelta(testCfg, 135 * 1024L, 170 * 1024L, fixedUtc);
            long totDl, totUl;
            ShihabConfigService.ComputeShihab24HourTotals(testCfg, out totDl, out totUl);
            check("24-hour hourly traffic bucket maintains 24 slots and exact RX/TX totals",
                testCfg.HourlyTraffic.Count == 24 && totDl == 135 * 1024L && totUl == 170 * 1024L);

            // 6. Tunnel-Scoped Secure DNS RASIPADDR Encoding
            ShihabVpnEngine.RASIPADDR rasDns;
            bool dnsOk = ShihabVpnEngine.TryEncodeShihabIpv4ToRas("94.140.14.14", out rasDns);
            check("RASIPADDR octet encoding for AdGuard Secure DNS (94.140.14.14)",
                dnsOk && rasDns.a == 94 && rasDns.b == 140 && rasDns.c == 14 && rasDns.d == 14);

            // 7. Sandboxed Win32 RasSetEntryProperties Portable Phonebook (.pbk) Provisioning (SSTP + IKEv2 + Split-Tunnel)
            string sandboxDir = Path.Combine(Path.GetTempPath(), "ShihabVpnSelfTest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(sandboxDir);
            try
            {
                using (ShihabVpnEngine eng = new ShihabVpnEngine(sandboxDir))
                {
                    string testPbk = Path.Combine(sandboxDir, "test_shihab.pbk");
                    ShihabAppConfig fullCfg = new ShihabAppConfig
                    {
                        DnsPreset = ShihabDnsPreset.CloudflareSecure,
                        SplitTunnelingEnabled = false
                    };
                    ShihabResult<string> sstpProv = eng.ProvisionShihabPhonebookEntry(testPbk, "SstpTest", csvNodes[0], fullCfg);
                    ShihabResult<string> usPortProv = eng.ProvisionShihabPhonebookEntry(testPbk, "UsCustomPortTest", csvNodes[2], fullCfg);
                    string pbkText = File.Exists(testPbk) ? File.ReadAllText(testPbk) : string.Empty;
                    check("Win32 RasSetEntryProperties provisions portable MS-SSTP phonebook (VpnStrategy=5, DNS=1.1.1.1)",
                        sstpProv.IsSuccess &&
                        pbkText.IndexOf("VpnStrategy=5", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        pbkText.IndexOf("IpDnsAddress=1.1.1.1", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        pbkText.IndexOf("IpPrioritizeRemote=1", StringComparison.OrdinalIgnoreCase) >= 0);
                    check("Win32 RasSetEntryProperties writes host:port (vpn181814903.opengw.net:1411) for non-443 MS-SSTP gateways",
                        usPortProv.IsSuccess &&
                        pbkText.IndexOf("PhoneNumber=vpn181814903.opengw.net:1411", StringComparison.OrdinalIgnoreCase) >= 0);

                    ShihabAppConfig splitCfg = new ShihabAppConfig
                    {
                        DnsPreset = ShihabDnsPreset.AdGuardBlock,
                        SplitTunnelingEnabled = true
                    };
                    ShihabVpnServerNode ikeNode = new ShihabVpnServerNode
                    {
                        Protocol = ShihabVpnProtocol.Ikev2,
                        SstpEndpoint = "ikev2.shihab.example.com"
                    };
                    ShihabResult<string> ikeProv = eng.ProvisionShihabPhonebookEntry(testPbk, "Ikev2SplitTest", ikeNode, splitCfg);
                    string pbkSplitText = File.Exists(testPbk) ? File.ReadAllText(testPbk) : string.Empty;
                    check("Win32 RasSetEntryProperties provisions IKEv2 (VpnStrategy=7) + Split-Tunneling (IpPrioritizeRemote=0)",
                        ikeProv.IsSuccess &&
                        pbkSplitText.IndexOf("VpnStrategy=7", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        pbkSplitText.IndexOf("IpPrioritizeRemote=0", StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }
            finally
            {
                try { Directory.Delete(sandboxDir, true); } catch { }
            }

            // 8. Read-Only Live Hardware & Public Relay Verification
            log.AppendLine("--- LIVE READ-ONLY NETWORK & RELAY PROBE ---");
            ShihabTelemetryAndSpeedService liveTel = new ShihabTelemetryAndSpeedService();
            ShihabPhysicalNetworkInfo phys = liveTel.DetectShihabPhysicalNetwork();
            check("Physical network adapter detector completed cleanly", !string.IsNullOrEmpty(phys.AdapterTitle));
            log.AppendLine(string.Format("PhysicalAdapter='{0}' | Desc='{1}' | LocalIP='{2}'",
                phys.AdapterTitle, phys.InterfaceDescription, phys.LocalIpv4));

            if (skipExternalNetwork)
            {
                log.AppendLine("ExternalNetworkProbes='SKIPPED (--skip-external-network for deterministic CI)'");
                WriteShihabReport(outFile, log.ToString());
                return failures == 0 ? 0 : 1;
            }

            ShihabResult<ShihabGeoIpReport> liveGeo = liveTel.VerifyShihabGeoIpAsync(string.Empty, string.Empty).GetAwaiter().GetResult();
            check("Live GeoIP & ISP/ASN audit resolved current internet-facing IPv4",
                liveGeo.IsSuccess && liveGeo.Value != null && !string.IsNullOrEmpty(liveGeo.Value.IpAddress));
            if (liveGeo.IsSuccess && liveGeo.Value != null)
            {
                log.AppendLine(string.Format("LiveGeoIP='{0}' | Country='{1}' ({2}) | City='{3}' | ISP='{4}' | Clock='{5}'",
                    liveGeo.Value.IpAddress,
                    liveGeo.Value.CountryLong,
                    liveGeo.Value.CountryShort,
                    liveGeo.Value.City,
                    liveGeo.Value.Isp,
                    liveGeo.Value.LocalTimeFormatted));
            }

            string probeTempDir = Path.Combine(Path.GetTempPath(), "ShihabRelayProbe_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                ShihabPublicRelayService liveRelaySvc = new ShihabPublicRelayService(probeTempDir);
                ShihabResult<List<ShihabVpnServerNode>> liveRelays = liveRelaySvc.RefreshShihabPublicRelaysAsync(true, true).GetAwaiter().GetResult();
                if (liveRelays.IsSuccess && liveRelays.Value != null && liveRelays.Value.Count > 0)
                {
                    List<ShihabCountryHubSummary> countries = ShihabPublicRelayService.BuildShihabCountrySummaries(liveRelays.Value);
                    check("Global multi-source relay aggregator discovered 25+ countries", countries.Count >= 25);

                    ShihabVpnServerNode best = liveRelaySvc.ResolveBestShihabTargetNode(new ShihabAppConfig(), liveRelays.Value);
                    log.AppendLine(string.Format(
                        "LivePublicRelays={0} across {1} Countries | AutoBest='{2}' ({3}) | Endpoint='{4}' | Ping={5}ms | Speed={6:0.0}Mbps",
                        liveRelays.Value.Count,
                        countries.Count,
                        best.CountryLong,
                        best.IpAddress,
                        best.EffectiveDialAddress,
                        best.PingMs,
                        best.SpeedMbps));

                    // Verify live United States (US) MS-SSTP TLS + SSTP_DUPLEX_POST HTTP/1.1 200 OK handshake
                    bool usHandshakeVerified = false;
                    string usVerifiedEndpoint = string.Empty;
                    int usVerifiedRtt = 0;
                    int usTested = 0;
                    for (int i = 0; i < liveRelays.Value.Count && usTested < 10; i++)
                    {
                        ShihabVpnServerNode cand = liveRelays.Value[i];
                        if (!string.Equals(cand.CountryShort, "US", StringComparison.OrdinalIgnoreCase)) continue;
                        usTested++;
                        int vPort;
                        int vRtt;
                        if (ShihabVpnEngine.TryVerifyWorkingShihabSstpPort(cand, CancellationToken.None, out vPort, out vRtt))
                        {
                            usHandshakeVerified = true;
                            usVerifiedEndpoint = cand.BareSstpHost + ":" + vPort;
                            usVerifiedRtt = vRtt;
                            break;
                        }
                    }
                    check("Live United States (US) MS-SSTP TLS + SSTP_DUPLEX_POST 200 OK handshake verified", usHandshakeVerified);
                    if (usHandshakeVerified)
                    {
                        log.AppendLine(string.Format("VerifiedUSGateway='{0}' | HandshakeRTT={1}ms", usVerifiedEndpoint, usVerifiedRtt));
                    }
                }
                else
                {
                    log.AppendLine("LivePublicRelaysNote=" + liveRelays.ErrorMessage);
                }
            }
            finally
            {
                try { if (Directory.Exists(probeTempDir)) Directory.Delete(probeTempDir, true); } catch { }
            }

            WriteShihabReport(outFile, log.ToString());
            return failures == 0 ? 0 : 1;
        }

        private static void WriteShihabReport(string outFile, string text)
        {
            if (!string.IsNullOrWhiteSpace(outFile))
            {
                string dir = Path.GetDirectoryName(outFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(outFile, text, new UTF8Encoding(false));
            }
            else
            {
                try { Console.WriteLine(text); } catch { }
            }
        }

        private static bool HasShihabArg(string[] args, string flag)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string GetShihabArgValue(string[] args, string key)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}

