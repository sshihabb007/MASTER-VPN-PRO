using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
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
            "SG", "IN", "HK", "TH", "MY", "VN", "JP", "KR", "TW", "BD", "AE",
            "DE", "NL", "GB", "FR", "CH", "SE", "FI", "RO", "IT", "ES", "PL",
            "US", "CA", "AU", "BR"
        };

        private readonly string _cacheFilePath;
        private readonly object _relayLock = new object();
        private List<ShihabVpnServerNode> _cachedPublicRelays;

        public ShihabPublicRelayService(string dataDirectory)
        {
            string dir = string.IsNullOrWhiteSpace(dataDirectory)
                ? ShihabConfigService.ResolveShihabWritableDataDir()
                : dataDirectory;
            _cacheFilePath = Path.Combine(dir, "ShihabPublicRelays.v3.cache.json");
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
                        ProbeShihabMultiCountryFastestBatch(merged, 4, 96, 800);
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
                    if (CompareShihabNodes(n, sum.BestNode) < 0)
                    {
                        sum.BestNode = n;
                    }
                    if (n.SpeedMbps > sum.BestSpeedMbps)
                    {
                        sum.BestSpeedMbps = n.SpeedMbps;
                    }
                }
            }

            foreach (ShihabCountryHubSummary hub in map.Values)
            {
                int localBest = 9999;
                int anyBest = 9999;
                if (relays != null)
                {
                    for (int i = 0; i < relays.Count; i++)
                    {
                        ShihabVpnServerNode r = relays[i];
                        if (r == null || !string.Equals(r.CountryShort, hub.CountryShort, StringComparison.OrdinalIgnoreCase)) continue;
                        if (r.PingMs > 0)
                        {
                            if (r.IsLocallyProbed && (r.IsTcp443Reachable || r.IsSstpVerified) && r.PingMs < localBest)
                            {
                                localBest = r.PingMs;
                            }
                            if ((r.IsTcp443Reachable || !r.IsLocallyProbed) && r.PingMs < anyBest)
                            {
                                anyBest = r.PingMs;
                            }
                        }
                    }
                }
                hub.BestPingMs = localBest < 9999 ? localBest : (anyBest < 9999 ? anyBest : 85);
            }

            List<ShihabCountryHubSummary> result = new List<ShihabCountryHubSummary>(map.Values);
            result.Sort(delegate(ShihabCountryHubSummary a, ShihabCountryHubSummary b)
            {
                bool aLocal = a.BestNode != null && a.BestNode.IsLocallyProbed && (a.BestNode.IsTcp443Reachable || a.BestNode.IsSstpVerified);
                bool bLocal = b.BestNode != null && b.BestNode.IsLocallyProbed && (b.BestNode.IsTcp443Reachable || b.BestNode.IsSstpVerified);
                if (aLocal != bLocal)
                {
                    return bLocal.CompareTo(aLocal);
                }
                if (aLocal && bLocal && a.BestPingMs != b.BestPingMs)
                {
                    return a.BestPingMs.CompareTo(b.BestPingMs);
                }

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

            // Dynamic IP Rotation mode (Latency-Constrained):
            // Only auto-rotate among top low-latency gateways (within max(+28ms, 1.30x) of fastest reachable node)
            // so reconnecting NEVER jumps from a 55ms/115ms fiber gateway to a 440ms slow residential node.
            int bestReachPing = 9999;
            for (int i = 0; i < copy.Count; i++)
            {
                ShihabVpnServerNode cand = copy[i];
                if ((cand.IsSstpVerified || cand.IsTcp443Reachable) && cand.PingMs > 0 && cand.PingMs < bestReachPing)
                {
                    bestReachPing = cand.PingMs;
                }
            }
            int fastBandLimitMs = bestReachPing < 9999
                ? Math.Max(bestReachPing + 28, (int)Math.Round(bestReachPing * 1.30))
                : 9999;

            List<ShihabVpnServerNode> fastFreshVerified = new List<ShihabVpnServerNode>();
            List<ShihabVpnServerNode> fastVisitedVerified = new List<ShihabVpnServerNode>();
            List<ShihabVpnServerNode> slowFreshVerified = new List<ShihabVpnServerNode>();
            List<ShihabVpnServerNode> slowVisitedVerified = new List<ShihabVpnServerNode>();
            List<ShihabVpnServerNode> freshOther = new List<ShihabVpnServerNode>();
            List<ShihabVpnServerNode> visitedOther = new List<ShihabVpnServerNode>();

            for (int i = 0; i < copy.Count; i++)
            {
                ShihabVpnServerNode n = copy[i];
                int recIdx = FindHostIndexIgnoreCase(recent, n.BareSstpHost);
                bool isReach = n.IsSstpVerified || n.IsTcp443Reachable;
                if (isReach)
                {
                    int np = n.PingMs > 0 ? n.PingMs : 450;
                    bool inFastBand = np <= fastBandLimitMs;
                    if (inFastBand)
                    {
                        if (recIdx < 0) fastFreshVerified.Add(n);
                        else fastVisitedVerified.Add(n);
                    }
                    else
                    {
                        if (recIdx < 0) slowFreshVerified.Add(n);
                        else slowVisitedVerified.Add(n);
                    }
                }
                else
                {
                    if (recIdx < 0) freshOther.Add(n);
                    else visitedOther.Add(n);
                }
            }

            Comparison<ShihabVpnServerNode> oldestVisitedFirst = delegate(ShihabVpnServerNode a, ShihabVpnServerNode b)
            {
                int ia = FindHostIndexIgnoreCase(recent, a.BareSstpHost);
                int ib = FindHostIndexIgnoreCase(recent, b.BareSstpHost);
                return ia.CompareTo(ib);
            };
            fastVisitedVerified.Sort(oldestVisitedFirst);
            slowVisitedVerified.Sort(oldestVisitedFirst);

            List<ShihabVpnServerNode> ordered = new List<ShihabVpnServerNode>(copy.Count);
            ordered.AddRange(fastFreshVerified);
            ordered.AddRange(fastVisitedVerified);
            ordered.AddRange(slowFreshVerified);
            ordered.AddRange(slowVisitedVerified);
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

        public static void ProbeShihabMultiCountryFastestBatch(
            List<ShihabVpnServerNode> nodes,
            int perCountryCap,
            int maxTotalProbes,
            int timeoutMs)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            Dictionary<string, List<ShihabVpnServerNode>> byCountry = new Dictionary<string, List<ShihabVpnServerNode>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < nodes.Count; i++)
            {
                ShihabVpnServerNode n = nodes[i];
                if (n == null || string.IsNullOrWhiteSpace(n.CountryShort)) continue;
                List<ShihabVpnServerNode> list;
                if (!byCountry.TryGetValue(n.CountryShort, out list))
                {
                    list = new List<ShihabVpnServerNode>();
                    byCountry[n.CountryShort] = list;
                }
                list.Add(n);
            }

            // Within each country, sort unprobed candidates by highest bandwidth + standard port 443 first
            foreach (List<ShihabVpnServerNode> bucket in byCountry.Values)
            {
                bucket.Sort(delegate(ShihabVpnServerNode a, ShihabVpnServerNode b)
                {
                    int aStd = (a.Port == 443 || a.Port == 0) ? 1 : 0;
                    int bStd = (b.Port == 443 || b.Port == 0) ? 1 : 0;
                    if (aStd != bStd) return bStd.CompareTo(aStd);
                    return b.SpeedBps.CompareTo(a.SpeedBps);
                });
            }

            List<ShihabVpnServerNode> selected = new List<ShihabVpnServerNode>();
            HashSet<string> seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Round-robin pick up to perCountryCap across priority countries first, then remaining countries
            for (int pass = 0; pass < Math.Max(1, perCountryCap) && selected.Count < maxTotalProbes; pass++)
            {
                for (int p = 0; p < ShihabPriorityCountryOrder.Length && selected.Count < maxTotalProbes; p++)
                {
                    List<ShihabVpnServerNode> bucket;
                    if (byCountry.TryGetValue(ShihabPriorityCountryOrder[p], out bucket) && pass < bucket.Count)
                    {
                        ShihabVpnServerNode cand = bucket[pass];
                        if (seenIds.Add(cand.Id))
                        {
                            selected.Add(cand);
                        }
                    }
                }

                foreach (KeyValuePair<string, List<ShihabVpnServerNode>> kv in byCountry)
                {
                    if (selected.Count >= maxTotalProbes) break;
                    if (pass < kv.Value.Count && seenIds.Add(kv.Value[pass].Id))
                    {
                        selected.Add(kv.Value[pass]);
                    }
                }
            }

            ProbeShihabTcp443Batch(selected, selected.Count, timeoutMs);
        }

        public static void ProbeShihabTcp443Batch(List<ShihabVpnServerNode> nodes, int maxCount, int timeoutMs)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            int count = Math.Min(nodes.Count, Math.Max(1, maxCount));
            using (SemaphoreSlim gate = new SemaphoreSlim(28))
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
                            bool open = MeasureTrueSingleRttMs(item, timeoutMs, out measuredMs);
                            item.WasProbed = true;
                            item.IsLocallyProbed = true;
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

                Task.WaitAll(tasks.ToArray(), Math.Max(3500, timeoutMs * 5));
            }
        }

        public static bool TryResolveEndpointIpOutsideTimer(string primaryIpOrHost, string fallbackHost, out IPAddress resolvedIp)
        {
            resolvedIp = null;
            if (!string.IsNullOrWhiteSpace(primaryIpOrHost))
            {
                string trimmed = primaryIpOrHost.Trim();
                if (IPAddress.TryParse(trimmed, out resolvedIp))
                {
                    return true;
                }
            }

            string dnsTarget = !string.IsNullOrWhiteSpace(fallbackHost)
                ? fallbackHost.Trim()
                : (primaryIpOrHost ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(dnsTarget))
            {
                return false;
            }

            if (IPAddress.TryParse(dnsTarget, out resolvedIp))
            {
                return true;
            }

            try
            {
                IPAddress[] addrs = Dns.GetHostAddresses(dnsTarget);
                if (addrs != null && addrs.Length > 0)
                {
                    for (int i = 0; i < addrs.Length; i++)
                    {
                        if (addrs[i].AddressFamily == AddressFamily.InterNetwork)
                        {
                            resolvedIp = addrs[i];
                            return true;
                        }
                    }
                    resolvedIp = addrs[0];
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public static int MeasureIcmpEchoRttMs(IPAddress ipAddr, int timeoutMs)
        {
            if (ipAddr == null) return -1;
            try
            {
                using (Ping pinger = new Ping())
                {
                    PingReply reply = pinger.Send(ipAddr, Math.Max(250, Math.Min(timeoutMs, 750)));
                    if (reply != null && reply.Status == IPStatus.Success)
                    {
                        return Math.Max(1, (int)reply.RoundtripTime);
                    }
                }
            }
            catch
            {
            }
            return -1;
        }

        public static bool MeasureTrueSingleRttMs(ShihabVpnServerNode node, int timeoutMs, out int singleRttMs)
        {
            singleRttMs = 0;
            if (node == null)
            {
                return false;
            }

            int port = node.Port > 0 ? node.Port : 443;
            IPAddress ipAddr;
            if (!TryResolveEndpointIpOutsideTimer(node.IpAddress, node.BareSstpHost, out ipAddr))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(node.IpAddress) ||
                node.IpAddress.IndexOf(".opengw.net", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                node.IpAddress = ipAddr.ToString();
            }

            return ProbeDirectIpTcpPort(ipAddr, port, timeoutMs, out singleRttMs);
        }

        public static bool ProbeDirectIpTcpPort(IPAddress ipAddr, int port, int timeoutMs, out int latencyMs)
        {
            latencyMs = 0;
            if (ipAddr == null || port <= 0 || port > 65535)
            {
                return false;
            }

            TcpClient tcp = new TcpClient(ipAddr.AddressFamily);
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                IAsyncResult ar = tcp.BeginConnect(ipAddr, port, null, null);
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

        public static bool ProbeSingleTcpPort(string hostOrIp, int port, int timeoutMs, out int latencyMs)
        {
            latencyMs = 0;
            if (string.IsNullOrWhiteSpace(hostOrIp))
            {
                return false;
            }

            // Resolve DNS BEFORE starting RTT Stopwatch so recursive DNS lookup never inflates network ping!
            IPAddress resolvedIp;
            if (TryResolveEndpointIpOutsideTimer(hostOrIp, null, out resolvedIp))
            {
                if (ProbeDirectIpTcpPort(resolvedIp, port, timeoutMs, out latencyMs))
                {
                    int icmpMs = MeasureIcmpEchoRttMs(resolvedIp, Math.Min(timeoutMs, 600));
                    if (icmpMs > 0 && icmpMs < latencyMs)
                    {
                        latencyMs = icmpMs;
                    }
                    return true;
                }
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

        private static int GetShihabNodeReachTier(ShihabVpnServerNode n)
        {
            if (n == null) return -1;
            if (n.IsLocallyProbed)
            {
                if (n.IsSstpVerified) return 4;
                if (n.IsTcp443Reachable) return 3;
                return 0;
            }
            if (n.IsSstpVerified) return 3;
            if (n.WasProbed)
            {
                return n.IsTcp443Reachable ? 2 : 0;
            }
            return 1;
        }

        private static int CompareShihabNodes(ShihabVpnServerNode a, ShihabVpnServerNode b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            int aReach = GetShihabNodeReachTier(a);
            int bReach = GetShihabNodeReachTier(b);
            if (aReach != bReach)
            {
                return bReach.CompareTo(aReach);
            }

            int aPing = (a.PingMs > 0 && a.PingMs < 2000) ? a.PingMs : 450;
            int bPing = (b.PingMs > 0 && b.PingMs < 2000) ? b.PingMs : 450;

            // Penalize ultra-low-bandwidth home DSL relays (< 10 Mbps) so high-speed backbone relays win
            int aLowBwPenalty = (a.SpeedMbps > 0.1 && a.SpeedMbps < 10.0) ? 35 : 0;
            int bLowBwPenalty = (b.SpeedMbps > 0.1 && b.SpeedMbps < 10.0) ? 35 : 0;

            double aCappedSpeed = Math.Min(a.SpeedMbps, 150.0);
            double bCappedSpeed = Math.Min(b.SpeedMbps, 150.0);

            // Strictly prioritize lowest true 1-RTT latency while rewarding clean >=20 Mbps throughput
            double aRank = -((aPing + aLowBwPenalty) * 3.2) + (aCappedSpeed * 0.18) - (a.LoadPercent * 0.15);
            double bRank = -((bPing + bLowBwPenalty) * 3.2) + (bCappedSpeed * 0.18) - (b.LoadPercent * 0.15);
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
