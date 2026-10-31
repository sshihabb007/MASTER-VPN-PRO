using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ShihabShieldVpn.Core;

namespace ShihabShieldVpn.Services
{
    public sealed class ShihabPublicRelayService
    {
        private static readonly string[] ShihabVpnGateCsvUrls = new string[]
        {
            "http://www.vpngate.net/api/iphone/",
            "https://www.vpngate.net/api/iphone/"
        };

        private const string ShihabVpnGateHtmlUrl = "http://www.vpngate.net/en/";
        private const string ShihabGlobalMultiCountryPoolUrl = "https://raw.githubusercontent.com/GeorgeXie2333/vpngate-list-mirror/main/pool/servers.json";
        private static readonly string[] ShihabSstpMirrorUrls = new string[]
        {
            "https://raw.githubusercontent.com/mehdihoore/TestVpnGateServers/main/sstps.csv",
            "https://bpsub.194216.xyz/vpn"
        };

        private static readonly string[] ShihabPriorityCountryOrder = new string[]
        {
            "US", "GB", "CA", "DE", "FR", "SG", "IN", "AU", "NL", "CH", "JP", "KR", "AE", "BR", "BD", "IT", "ES", "SE", "FI", "RO", "HK", "TW"
        };

        private readonly string _cacheFilePath;
        private readonly object _relayLock = new object();
        private List<ShihabVpnServerNode> _cachedPublicRelays;

        public ShihabPublicRelayService(string dataDirectory)
        {
            string dir = string.IsNullOrWhiteSpace(dataDirectory)
                ? ShihabConfigService.ResolveShihabWritableDataDir()
                : dataDirectory;
            _cacheFilePath = Path.Combine(dir, "ShihabPublicRelays.v2.cache.json");
            _cachedPublicRelays = LoadCachedRelaysFromDisk();
        }

        public IReadOnlyList<ShihabVpnServerNode> GetCurrentPublicRelays()
        {
            lock (_relayLock)
            {
                return new List<ShihabVpnServerNode>(_cachedPublicRelays);
            }
        }

        public Task<ShihabResult<List<ShihabVpnServerNode>>> RefreshShihabPublicRelaysAsync(bool probeTopLatency)
        {
            return RefreshShihabPublicRelaysAsync(probeTopLatency, true);
        }

        public Task<ShihabResult<List<ShihabVpnServerNode>>> RefreshShihabPublicRelaysAsync(bool probeTopLatency, bool extendedCatalog)
        {
            return Task.Run(delegate
            {
                Stopwatch sw = Stopwatch.StartNew();
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                List<ShihabVpnServerNode> csvNodes = new List<ShihabVpnServerNode>();
                List<ShihabVpnServerNode> htmlNodes = new List<ShihabVpnServerNode>();
                List<ShihabVpnServerNode> poolNodes = new List<ShihabVpnServerNode>();
                List<ShihabVpnServerNode> mirrorNodes = new List<ShihabVpnServerNode>();
                string lastErr = "Unable to reach public relay directories.";

                Task tCsv = Task.Run(delegate
                {
                    for (int i = 0; i < ShihabVpnGateCsvUrls.Length; i++)
                    {
                        string err;
                        string raw = DownloadStringWithGzip(ShihabVpnGateCsvUrls[i], 9000, out err);
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            List<ShihabVpnServerNode> parsed = ShihabConfigParsers.ParseShihabVpnGateCsv(raw);
                            if (parsed.Count > 0)
                            {
                                csvNodes = parsed;
                                return;
                            }
                        }
                        else if (!string.IsNullOrEmpty(err))
                        {
                            lastErr = err;
                        }
                    }
                });

                Task tHtml = Task.Run(delegate
                {
                    string err;
                    string raw = DownloadStringWithGzip(ShihabVpnGateHtmlUrl, 9000, out err);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        htmlNodes = ShihabConfigParsers.ParseShihabVpnGateHtmlTable(raw);
                    }
                });

                Task tPool = Task.Run(delegate
                {
                    string err;
                    string raw = DownloadStringWithGzip(ShihabGlobalMultiCountryPoolUrl, 12000, out err);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        poolNodes = ShihabConfigParsers.ParseShihabGlobalPoolJson(raw);
                    }
                });

                Task tMirrors = Task.Run(delegate
                {
                    List<ShihabVpnServerNode> acc = new List<ShihabVpnServerNode>();
                    for (int i = 0; i < ShihabSstpMirrorUrls.Length; i++)
                    {
                        string err;
                        string raw = DownloadStringWithGzip(ShihabSstpMirrorUrls[i], 7000, out err);
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            List<ShihabVpnServerNode> sub = ShihabConfigParsers.ParseShihabSstpSubscriptionFeed(raw);
                            if (sub.Count > 0)
                            {
                                acc.AddRange(sub);
                            }
                        }
                    }
                    mirrorNodes = acc;
                });

                Task.WaitAll(new Task[] { tCsv, tHtml, tPool, tMirrors }, 14000);

                List<ShihabVpnServerNode> merged = MergeAndBalanceShihabGlobalPoolWithMode(
                    extendedCatalog,
                    htmlNodes,
                    csvNodes,
                    mirrorNodes,
                    poolNodes);

                if (merged.Count > 0)
                {
                    if (probeTopLatency)
                    {
                        ProbeShihabTcp443Batch(merged, 14, 1300);
                        SortShihabRelays(merged);
                    }

                    lock (_relayLock)
                    {
                        _cachedPublicRelays = merged;
                    }
                    SaveCachedRelaysToDisk(merged);
                    sw.Stop();
                    return ShihabResult<List<ShihabVpnServerNode>>.Ok(merged, sw.ElapsedMilliseconds);
                }

                sw.Stop();
                lock (_relayLock)
                {
                    if (_cachedPublicRelays != null && _cachedPublicRelays.Count > 0)
                    {
                        return ShihabResult<List<ShihabVpnServerNode>>.Ok(new List<ShihabVpnServerNode>(_cachedPublicRelays), sw.ElapsedMilliseconds);
                    }
                }

                return ShihabResult<List<ShihabVpnServerNode>>.Fail("DIRECTORY_FETCH_FAILED", lastErr, sw.ElapsedMilliseconds);
            });
        }

        private static string DownloadStringWithGzip(string url, int timeoutMs, out string errorOut)
        {
            errorOut = string.Empty;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.UserAgent = "ShihabShieldVPN/1.2 (Windows; OpenSource)";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream stream = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(stream, Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                errorOut = ex.Message;
                return string.Empty;
            }
        }

        public static List<ShihabVpnServerNode> MergeAndBalanceShihabGlobalPool(
            params List<ShihabVpnServerNode>[] sources)
        {
            return MergeAndBalanceShihabGlobalPoolWithMode(true, sources);
        }

        public static List<ShihabVpnServerNode> MergeAndBalanceShihabGlobalPoolWithMode(
            bool extendedCatalog,
            params List<ShihabVpnServerNode>[] sources)
        {
            Dictionary<string, ShihabVpnServerNode> byEndpoint = new Dictionary<string, ShihabVpnServerNode>(StringComparer.OrdinalIgnoreCase);

            if (sources != null)
            {
                for (int s = 0; s < sources.Length; s++)
                {
                    List<ShihabVpnServerNode> src = sources[s];
                    if (src == null) continue;

                    for (int i = 0; i < src.Count; i++)
                    {
                        ShihabVpnServerNode n = src[i];
                        if (n == null) continue;

                        string bare = n.BareSstpHost;
                        if (string.IsNullOrEmpty(bare)) continue;

                        int port = n.Port > 0 ? n.Port : 443;
                        string key = bare.ToLowerInvariant() + ":" + port;

                        ShihabVpnServerNode existing;
                        if (!byEndpoint.TryGetValue(key, out existing))
                        {
                            NormalizeSingleRelayMetadata(n);
                            byEndpoint[key] = n;
                        }
                        else
                        {
                            if (n.IsSstpVerified) existing.IsSstpVerified = true;
                            if (n.IsTcp443Reachable)
                            {
                                existing.IsTcp443Reachable = true;
                                existing.WasProbed = true;
                            }
                            if (n.SupportsL2tpIpsec) existing.SupportsL2tpIpsec = true;
                            if (string.IsNullOrEmpty(existing.OpenVpnConfigBase64) && !string.IsNullOrEmpty(n.OpenVpnConfigBase64))
                            {
                                existing.OpenVpnConfigBase64 = n.OpenVpnConfigBase64;
                            }
                            if ((string.IsNullOrEmpty(existing.IpAddress) || existing.IpAddress.IndexOf(".opengw.net", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                !string.IsNullOrEmpty(n.IpAddress) &&
                                n.IpAddress.IndexOf(".opengw.net", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                existing.IpAddress = n.IpAddress;
                            }
                        }
                    }
                }
            }

            Dictionary<string, List<ShihabVpnServerNode>> byCountry = new Dictionary<string, List<ShihabVpnServerNode>>(StringComparer.OrdinalIgnoreCase);
            foreach (ShihabVpnServerNode node in byEndpoint.Values)
            {
                string cc = string.IsNullOrWhiteSpace(node.CountryShort) ? "GL" : node.CountryShort.Trim().ToUpperInvariant();
                List<ShihabVpnServerNode> bucket;
                if (!byCountry.TryGetValue(cc, out bucket))
                {
                    bucket = new List<ShihabVpnServerNode>();
                    byCountry[cc] = bucket;
                }
                bucket.Add(node);
            }

            List<ShihabVpnServerNode> balanced = new List<ShihabVpnServerNode>();
            foreach (KeyValuePair<string, List<ShihabVpnServerNode>> kv in byCountry)
            {
                List<ShihabVpnServerNode> list = kv.Value;
                SortShihabRelays(list);

                bool isMegaHub = kv.Key == "JP" || kv.Key == "KR" || kv.Key == "RU";
                int cap;
                if (extendedCatalog)
                {
                    cap = isMegaHub ? 95 : 120;
                }
                else
                {
                    cap = isMegaHub ? 35 : 45;
                }

                int take = Math.Min(list.Count, cap);
                for (int i = 0; i < take; i++)
                {
                    balanced.Add(list[i]);
                }
            }

            SortShihabRelays(balanced);
            return balanced;
        }

        public static List<ShihabCountryHubSummary> BuildShihabCountrySummaries(IReadOnlyList<ShihabVpnServerNode> relays)
        {
            Dictionary<string, ShihabCountryHubSummary> map = new Dictionary<string, ShihabCountryHubSummary>(StringComparer.OrdinalIgnoreCase);
            if (relays != null)
            {
                for (int i = 0; i < relays.Count; i++)
                {
                    ShihabVpnServerNode n = relays[i];
                    if (n == null || string.IsNullOrWhiteSpace(n.CountryShort)) continue;

                    string cc = n.CountryShort.Trim().ToUpperInvariant();
                    if (cc == "UK") cc = "GB";

                    ShihabCountryHubSummary sum;
                    if (!map.TryGetValue(cc, out sum))
                    {
                        sum = new ShihabCountryHubSummary();
                        sum.CountryShort = cc;
                        sum.CountryLong = ShihabConfigParsers.NormalizeShihabCountryTitle(n.CountryLong, cc);
                        sum.CityName = ShihabConfigParsers.ResolveShihabCountryCity(cc);
                        sum.RegionGroup = ShihabConfigParsers.ResolveShihabCountryRegion(cc);
                        sum.TotalServers = 0;
                        sum.VerifiedTcpServers = 0;
                        sum.StreamingCapableCount = 0;
                        sum.OpenVpnProfileCount = 0;
                        sum.BestPingMs = 999;
                        sum.BestSpeedMbps = 0.0;
                        sum.BestNode = n;
                        map[cc] = sum;
                    }

                    sum.TotalServers++;
                    if (n.IsTcp443Reachable || n.IsSstpVerified)
                    {
                        sum.VerifiedTcpServers++;
                    }
                    if (n.SpeedMbps >= 20.0)
                    {
                        sum.StreamingCapableCount++;
                    }
                    if (!string.IsNullOrWhiteSpace(n.OpenVpnConfigBase64))
                    {
                        sum.OpenVpnProfileCount++;
                    }
                    if (n.PingMs > 0 && n.PingMs < sum.BestPingMs)
                    {
                        sum.BestPingMs = n.PingMs;
                    }
                    if (n.SpeedMbps > sum.BestSpeedMbps)
                    {
                        sum.BestSpeedMbps = n.SpeedMbps;
                    }
                    if (CompareShihabNodes(n, sum.BestNode) < 0)
                    {
                        sum.BestNode = n;
                    }
                }
            }

            List<ShihabCountryHubSummary> result = new List<ShihabCountryHubSummary>(map.Values);
            result.Sort(delegate(ShihabCountryHubSummary a, ShihabCountryHubSummary b)
            {
                int aPri = GetCountryPriorityIndex(a.CountryShort);
                int bPri = GetCountryPriorityIndex(b.CountryShort);
                if (aPri != bPri)
                {
                    return aPri.CompareTo(bPri);
                }

                int aVer = a.VerifiedTcpServers > 0 ? 1 : 0;
                int bVer = b.VerifiedTcpServers > 0 ? 1 : 0;
                if (aVer != bVer)
                {
                    return bVer.CompareTo(aVer);
                }

                return string.Compare(a.CountryLong, b.CountryLong, StringComparison.OrdinalIgnoreCase);
            });

            return result;
        }

        private static int GetCountryPriorityIndex(string cc)
        {
            for (int i = 0; i < ShihabPriorityCountryOrder.Length; i++)
            {
                if (string.Equals(ShihabPriorityCountryOrder[i], cc, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return 999;
        }

        public static void RecordShihabRecentHost(ShihabAppConfig cfg, string countryShort, string bareHost)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(bareHost))
            {
                return;
            }
            if (cfg.RecentHostsByCountry == null)
            {
                cfg.RecentHostsByCountry = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }

            string cc = string.IsNullOrWhiteSpace(countryShort) ? "AUTO" : countryShort.Trim().ToUpperInvariant();
            if (cc == "UK") cc = "GB";
            string key = bareHost.Trim().ToLowerInvariant();

            List<string> history;
            if (!cfg.RecentHostsByCountry.TryGetValue(cc, out history) || history == null)
            {
                history = new List<string>();
                cfg.RecentHostsByCountry[cc] = history;
            }

            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (string.Equals(history[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    history.RemoveAt(i);
                }
            }

            history.Add(key);
            while (history.Count > 12)
            {
                history.RemoveAt(0);
            }
        }

        public static List<ShihabVpnServerNode> OrderCandidatesForShihabRotation(
            IReadOnlyList<ShihabVpnServerNode> sortedCandidates,
            ShihabAppConfig cfg,
            string countryShort)
        {
            List<ShihabVpnServerNode> copy = new List<ShihabVpnServerNode>();
            if (sortedCandidates == null || sortedCandidates.Count == 0)
            {
                return copy;
            }

            for (int i = 0; i < sortedCandidates.Count; i++)
            {
                if (sortedCandidates[i] != null)
                {
                    copy.Add(sortedCandidates[i]);
                }
            }

            if (cfg == null || cfg.RecentHostsByCountry == null || copy.Count <= 1)
            {
                return copy;
            }

            string cc = string.IsNullOrWhiteSpace(countryShort) ? "AUTO" : countryShort.Trim().ToUpperInvariant();
            if (cc == "UK") cc = "GB";

            List<string> recent;
            if (!cfg.RecentHostsByCountry.TryGetValue(cc, out recent) || recent == null || recent.Count == 0)
            {
                return copy;
            }

            if (!cfg.DynamicIpRotation)
            {
                // Sticky IP mode: prefer the most recently verified host in this country
                string lastHost = recent[recent.Count - 1];
                for (int i = 0; i < copy.Count; i++)
                {
                    if (string.Equals(copy[i].BareSstpHost, lastHost, StringComparison.OrdinalIgnoreCase))
                    {
                        ShihabVpnServerNode pinned = copy[i];
                        copy.RemoveAt(i);
                        copy.Insert(0, pinned);
                        break;
                    }
                }
                return copy;
            }

            // Dynamic IP Rotation mode:
            // 1. Fresh reachable/verified gateways not in recent history
            // 2. Previously visited reachable/verified gateways ordered oldest-visited first (round-robin)
            // 3. Remaining unprobed gateways (fresh first, then visited)
            List<ShihabVpnServerNode> freshVerified = new List<ShihabVpnServerNode>();
            List<ShihabVpnServerNode> visitedVerified = new List<ShihabVpnServerNode>();
            List<ShihabVpnServerNode> freshOther = new List<ShihabVpnServerNode>();
            List<ShihabVpnServerNode> visitedOther = new List<ShihabVpnServerNode>();

            for (int i = 0; i < copy.Count; i++)
            {
                ShihabVpnServerNode n = copy[i];
                int recIdx = FindHostIndexIgnoreCase(recent, n.BareSstpHost);
                bool isReach = n.IsSstpVerified || n.IsTcp443Reachable;
                if (isReach)
                {
                    if (recIdx < 0) freshVerified.Add(n);
                    else visitedVerified.Add(n);
                }
                else
                {
                    if (recIdx < 0) freshOther.Add(n);
                    else visitedOther.Add(n);
                }
            }

            visitedVerified.Sort(delegate(ShihabVpnServerNode a, ShihabVpnServerNode b)
            {
                int ia = FindHostIndexIgnoreCase(recent, a.BareSstpHost);
                int ib = FindHostIndexIgnoreCase(recent, b.BareSstpHost);
                return ia.CompareTo(ib);
            });

            List<ShihabVpnServerNode> ordered = new List<ShihabVpnServerNode>(copy.Count);
            ordered.AddRange(freshVerified);
            ordered.AddRange(visitedVerified);
            ordered.AddRange(freshOther);
            ordered.AddRange(visitedOther);
            return ordered;
        }

        private static int FindHostIndexIgnoreCase(List<string> list, string host)
        {
            if (list == null || string.IsNullOrEmpty(host)) return -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], host, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        public static void ProbeShihabTcp443Batch(List<ShihabVpnServerNode> nodes, int maxCount, int timeoutMs)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            int count = Math.Min(nodes.Count, Math.Max(1, maxCount));
            using (SemaphoreSlim gate = new SemaphoreSlim(6))
            {
                List<Task> tasks = new List<Task>(count);
                for (int i = 0; i < count; i++)
                {
                    ShihabVpnServerNode item = nodes[i];
                    tasks.Add(Task.Run(delegate
                    {
                        gate.Wait();
                        try
                        {
                            int measuredMs;
                            string host = item.BareSstpHost;
                            int port = item.Port > 0 ? item.Port : 443;
                            bool open = ProbeSingleTcpPort(host, port, timeoutMs, out measuredMs);
                            item.WasProbed = true;
                            item.IsTcp443Reachable = open;
                            if (open && measuredMs > 0)
                            {
                                item.PingMs = measuredMs;
                            }
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }));
                }

                Task.WaitAll(tasks.ToArray(), timeoutMs * 3 + 1500);
            }
        }

        public static bool ProbeSingleTcpPort(string hostOrIp, int port, int timeoutMs, out int latencyMs)
        {
            latencyMs = 0;
            if (string.IsNullOrWhiteSpace(hostOrIp))
            {
                return false;
            }

            Stopwatch sw = Stopwatch.StartNew();
            TcpClient tcp = new TcpClient();
            try
            {
                IAsyncResult ar = tcp.BeginConnect(hostOrIp.Trim(), port, null, null);
                bool signaled = ar.AsyncWaitHandle.WaitOne(timeoutMs, false);
                if (signaled && tcp.Connected)
                {
                    tcp.EndConnect(ar);
                    sw.Stop();
                    latencyMs = Math.Max(1, (int)sw.ElapsedMilliseconds);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { tcp.Close(); } catch { }
            }
        }

        public ShihabVpnServerNode ResolveBestShihabTargetNode(
            ShihabAppConfig cfg,
            IReadOnlyList<ShihabVpnServerNode> publicRelays)
        {
            if (cfg != null && !string.IsNullOrWhiteSpace(cfg.SelectedServerId))
            {
                if (cfg.PrivateServers != null)
                {
                    for (int i = 0; i < cfg.PrivateServers.Count; i++)
                    {
                        if (string.Equals(cfg.PrivateServers[i].Id, cfg.SelectedServerId, StringComparison.OrdinalIgnoreCase))
                        {
                            return cfg.PrivateServers[i];
                        }
                    }
                }

                if (!cfg.AutoSelectFastest && publicRelays != null)
                {
                    for (int i = 0; i < publicRelays.Count; i++)
                    {
                        if (string.Equals(publicRelays[i].Id, cfg.SelectedServerId, StringComparison.OrdinalIgnoreCase))
                        {
                            return publicRelays[i];
                        }
                    }
                }
            }

            if (publicRelays == null || publicRelays.Count == 0)
            {
                if (cfg != null && cfg.PrivateServers != null && cfg.PrivateServers.Count > 0)
                {
                    return cfg.PrivateServers[0];
                }
                return null;
            }

            string wantedCountry = (cfg != null && !string.IsNullOrWhiteSpace(cfg.SelectedCountryCode))
                ? cfg.SelectedCountryCode.Trim().ToUpperInvariant()
                : "AUTO";
            if (wantedCountry == "UK") wantedCountry = "GB";

            bool fastOnly = cfg != null && cfg.StreamingHighSpeedOnly;

            List<ShihabVpnServerNode> candidates = new List<ShihabVpnServerNode>();
            for (int i = 0; i < publicRelays.Count; i++)
            {
                ShihabVpnServerNode n = publicRelays[i];
                if (n == null) continue;
                bool countryOk = wantedCountry == "AUTO" || string.Equals(n.CountryShort, wantedCountry, StringComparison.OrdinalIgnoreCase);
                if (!countryOk) continue;
                if (fastOnly && n.SpeedMbps < 20.0) continue;
                candidates.Add(n);
            }

            // If 4K Fast filter was too strict for a small country, fall back to all relays in that country
            if (candidates.Count == 0 && fastOnly)
            {
                for (int i = 0; i < publicRelays.Count; i++)
                {
                    ShihabVpnServerNode n = publicRelays[i];
                    if (n != null && (wantedCountry == "AUTO" || string.Equals(n.CountryShort, wantedCountry, StringComparison.OrdinalIgnoreCase)))
                    {
                        candidates.Add(n);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                candidates.AddRange(publicRelays);
            }

            SortShihabRelays(candidates);
            List<ShihabVpnServerNode> rotated = OrderCandidatesForShihabRotation(candidates, cfg, wantedCountry);
            return rotated.Count > 0 ? rotated[0] : candidates[0];
        }

        public static void SortShihabRelays(List<ShihabVpnServerNode> list)
        {
            if (list == null || list.Count <= 1)
            {
                return;
            }
            list.Sort(CompareShihabNodes);
        }

        private static int CompareShihabNodes(ShihabVpnServerNode a, ShihabVpnServerNode b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            int aReach = a.IsSstpVerified ? 3 : (a.WasProbed ? (a.IsTcp443Reachable ? 2 : 0) : 1);
            int bReach = b.IsSstpVerified ? 3 : (b.WasProbed ? (b.IsTcp443Reachable ? 2 : 0) : 1);
            if (aReach != bReach)
            {
                return bReach.CompareTo(aReach);
            }

            int aPing = (a.PingMs > 0 && a.PingMs < 2000) ? a.PingMs : 450;
            int bPing = (b.PingMs > 0 && b.PingMs < 2000) ? b.PingMs : 450;
            double aCappedSpeed = Math.Min(a.SpeedMbps, 200.0);
            double bCappedSpeed = Math.Min(b.SpeedMbps, 200.0);
            double aRank = (aCappedSpeed * 0.75) - (aPing * 1.25) - (a.LoadPercent * 0.35);
            double bRank = (bCappedSpeed * 0.75) - (bPing * 1.25) - (b.LoadPercent * 0.35);
            return bRank.CompareTo(aRank);
        }

        private static void NormalizeSingleRelayMetadata(ShihabVpnServerNode n)
        {
            if (n == null) return;
            string cc = string.IsNullOrWhiteSpace(n.CountryShort) ? "GL" : n.CountryShort.Trim().ToUpperInvariant();
            if (cc == "UK") cc = "GB";
            n.CountryShort = cc;
            if (string.IsNullOrWhiteSpace(n.CityName) || n.CityName == "Ashburn")
            {
                n.CityName = ShihabConfigParsers.ResolveShihabCountryCity(cc);
            }
            if (string.IsNullOrWhiteSpace(n.RegionGroup))
            {
                n.RegionGroup = ShihabConfigParsers.ResolveShihabCountryRegion(cc);
            }
        }

        private List<ShihabVpnServerNode> LoadCachedRelaysFromDisk()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    string json = File.ReadAllText(_cacheFilePath, Encoding.UTF8);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    ser.MaxJsonLength = 24 * 1024 * 1024;
                    List<ShihabVpnServerNode> items = ser.Deserialize<List<ShihabVpnServerNode>>(json);
                    if (items != null && items.Count > 0)
                    {
                        for (int i = 0; i < items.Count; i++)
                        {
                            NormalizeSingleRelayMetadata(items[i]);
                        }
                        return items;
                    }
                }
            }
            catch
            {
            }
            return new List<ShihabVpnServerNode>();
        }

        private void SaveCachedRelaysToDisk(List<ShihabVpnServerNode> relays)
        {
            try
            {
                string dir = Path.GetDirectoryName(_cacheFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 24 * 1024 * 1024;
                File.WriteAllText(_cacheFilePath, ser.Serialize(relays), new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }
}
