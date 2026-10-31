package com.shihab.shieldvpn;

import android.util.Base64;

import com.shihab.shieldvpn.ShihabModels.ShihabCountryInfo;
import com.shihab.shieldvpn.ShihabModels.ShihabGeoAudit;
import com.shihab.shieldvpn.ShihabModels.ShihabRelayNode;
import com.shihab.shieldvpn.ShihabModels.ShihabSpeedResult;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

/**
 * Shihab Shield VPN — Live Public Relay Aggregator, GeoIP Leak Auditor & Speed Benchmark.
 * Uses only authentic open-source community & academic feeds + user-configured private servers.
 * Engineered by Mehedi Hasan Shihab (sshihabb007).
 */
public final class ShihabRelayService {

    private static final String PROXIFLY_HTTPS_URL =
            "https://cdn.jsdelivr.net/gh/proxifly/free-proxy-list@main/proxies/protocols/https/data.json";
    private static final String MONOSANS_JSON_URL =
            "https://raw.githubusercontent.com/monosans/proxy-list/main/proxies.json";
    private static final String IPSPEED_OVPN_URL =
            "https://raw.githubusercontent.com/ipspeed/ipsec/master/server.json";
    private static final String VPNGATE_CSV_URL =
            "https://www.vpngate.net/api/iphone/";

    private ShihabRelayService() {}

    public static String httpGetText(String urlString, int timeoutMs) throws Exception {
        HttpURLConnection conn = null;
        try {
            URL url = new URL(urlString);
            conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setConnectTimeout(timeoutMs);
            conn.setReadTimeout(timeoutMs);
            conn.setUseCaches(false);
            conn.setRequestProperty("User-Agent", "MasterVpnPro-Android/1.2.0");
            int status = conn.getResponseCode();
            if (status < 200 || status >= 300) {
                throw new Exception("HTTP " + status + " from " + urlString);
            }
            StringBuilder sb = new StringBuilder();
            BufferedReader reader = new BufferedReader(
                    new InputStreamReader(conn.getInputStream(), StandardCharsets.UTF_8));
            char[] buf = new char[8192];
            int read;
            while ((read = reader.read(buf)) != -1) {
                sb.append(buf, 0, read);
                if (sb.length() > 12_000_000) break;
            }
            reader.close();
            return sb.toString();
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    public static List<ShihabRelayNode> fetchAllShihabPublicRelays() {
        List<ShihabRelayNode> combined = new ArrayList<ShihabRelayNode>();
        Set<String> seenKeys = new HashSet<String>();

        // 1. Proxifly HTTPS-CONNECT Proxies (Android 10+ VpnService.Builder.setHttpProxy compatible)
        try {
            String raw = httpGetText(PROXIFLY_HTTPS_URL, 7000);
            JSONArray arr = new JSONArray(raw);
            for (int i = 0; i < arr.length(); i++) {
                JSONObject item = arr.optJSONObject(i);
                if (item == null) continue;
                String ip = item.optString("ip", "").trim();
                int port = item.optInt("port", 0);
                if (ip.isEmpty() || port <= 0 || port > 65535) continue;

                JSONObject geo = item.optJSONObject("geolocation");
                String cc = geo != null ? geo.optString("country", "").trim().toUpperCase(Locale.US) : "";
                if (cc.length() != 2 || "ZZ".equals(cc)) continue;

                String dedup = "PROXY:" + ip + ":" + port;
                if (!seenKeys.add(dedup)) continue;

                ShihabCountryInfo meta = ShihabModels.resolveShihabCountry(cc, "");
                ShihabRelayNode n = new ShihabRelayNode();
                n.id = dedup;
                n.sourceType = "OPEN_PROXY";
                n.countryShort = meta.code;
                n.countryLong = meta.name;
                n.cityName = (geo != null && !geo.optString("city", "").isEmpty())
                        ? geo.optString("city")
                        : meta.city;
                n.regionGroup = meta.region;
                n.protocol = "HTTPS";
                n.host = ip;
                n.port = port;
                n.pingMs = Math.max(18, Math.min(240, 100 - item.optInt("score", 50)));
                n.speedMbps = 28.0;
                n.lat = meta.lat;
                n.lon = meta.lon;
                n.title = meta.name + " (" + ip + ":" + port + ")";
                combined.add(n);
            }
        } catch (Exception ignored) {}

        // 2. Monosans HTTP/HTTPS Proxies with RTT + GeoIP
        try {
            String raw = httpGetText(MONOSANS_JSON_URL, 7000);
            JSONArray arr = new JSONArray(raw);
            for (int i = 0; i < arr.length(); i++) {
                JSONObject item = arr.optJSONObject(i);
                if (item == null) continue;
                String proto = item.optString("protocol", "").toLowerCase(Locale.US);
                if (!"http".equals(proto) && !"https".equals(proto)) continue;

                String ip = item.optString("host", "").trim();
                int port = item.optInt("port", 0);
                if (ip.isEmpty() || port <= 0 || port > 65535) continue;

                JSONObject geo = item.optJSONObject("geolocation");
                JSONObject cObj = geo != null ? geo.optJSONObject("country") : null;
                String cc = cObj != null ? cObj.optString("iso_code", "").trim().toUpperCase(Locale.US) : "";
                if (cc.length() != 2) continue;

                String dedup = "PROXY:" + ip + ":" + port;
                if (!seenKeys.add(dedup)) continue;

                ShihabCountryInfo meta = ShihabModels.resolveShihabCountry(cc, "");
                ShihabRelayNode n = new ShihabRelayNode();
                n.id = dedup;
                n.sourceType = "OPEN_PROXY";
                n.countryShort = meta.code;
                n.countryLong = meta.name;
                n.cityName = meta.city;
                n.regionGroup = meta.region;
                n.protocol = "https".equals(proto) ? "HTTPS" : "HTTP";
                n.host = ip;
                n.port = port;
                int timeoutSec = (int) Math.round(item.optDouble("timeout", 0.08) * 1000.0);
                n.pingMs = Math.max(12, Math.min(450, timeoutSec));
                n.speedMbps = 32.0;
                n.lat = meta.lat;
                n.lon = meta.lon;
                n.title = meta.name + " (" + ip + ":" + port + ")";
                combined.add(n);
            }
        } catch (Exception ignored) {}

        // 3. ipspeed/ipsec OpenVPN JSON Pool
        try {
            String raw = httpGetText(IPSPEED_OVPN_URL, 7500);
            JSONArray arr = new JSONArray(raw);
            for (int i = 0; i < arr.length() && i < 220; i++) {
                JSONObject item = arr.optJSONObject(i);
                if (item == null) continue;
                String cc = item.optString("countryShort", "").trim().toUpperCase(Locale.US);
                String ip = item.optString("ip", "").trim();
                String ovpnB64 = item.optString("ovpn", "").trim();
                if (cc.length() != 2 || ip.isEmpty() || ovpnB64.isEmpty()) continue;

                String dedup = "OVPN:" + ip;
                if (!seenKeys.add(dedup)) continue;

                ShihabCountryInfo meta = ShihabModels.resolveShihabCountry(
                        cc, item.optString("countryLong", ""));
                ShihabRelayNode n = new ShihabRelayNode();
                n.id = dedup;
                n.sourceType = "IPSPEED_OVPN";
                n.countryShort = meta.code;
                n.countryLong = meta.name;
                n.cityName = meta.city;
                n.regionGroup = meta.region;
                n.protocol = "OpenVPN";
                n.host = ip;
                n.port = item.optInt("port", 443);
                n.pingMs = Math.max(8, item.optInt("ping", 35));
                n.speedMbps = Math.round((item.optDouble("speed", 40_000_000.0) / 1_000_000.0) * 10.0) / 10.0;
                n.lat = meta.lat;
                n.lon = meta.lon;
                n.ovpnConfigBase64 = ovpnB64;
                n.title = meta.name + " OpenVPN (" + ip + ")";
                combined.add(n);
            }
        } catch (Exception ignored) {}

        // 4. VPN Gate Academic Relay CSV (70+ Countries)
        try {
            String csv = httpGetText(VPNGATE_CSV_URL, 8500);
            String[] lines = csv.split("\n");
            for (String rawLine : lines) {
                String line = rawLine.trim();
                if (line.isEmpty() || line.startsWith("*") || line.startsWith("#")) continue;
                String[] cols = line.split(",", -1);
                if (cols.length < 15) continue;
                String hostName = cols[0].trim();
                String ip = cols[1].trim();
                String cc = cols[6].trim().toUpperCase(Locale.US);
                String cLong = cols[5].trim();
                String ovpnB64 = cols[14].trim();
                if ("HostName".equalsIgnoreCase(hostName) || cc.length() != 2 || ip.isEmpty()) continue;

                String dedup = "OVPN:" + ip;
                if (!seenKeys.add(dedup)) continue;

                int ping = 45;
                try { ping = Integer.parseInt(cols[3].trim()); } catch (Exception ignored) {}
                double speedBps = 25_000_000.0;
                try { speedBps = Double.parseDouble(cols[4].trim()); } catch (Exception ignored) {}

                ShihabCountryInfo meta = ShihabModels.resolveShihabCountry(cc, cLong);
                ShihabRelayNode n = new ShihabRelayNode();
                n.id = dedup;
                n.sourceType = "VPNGATE_OVPN";
                n.countryShort = meta.code;
                n.countryLong = meta.name;
                n.cityName = meta.city;
                n.regionGroup = meta.region;
                n.protocol = "OpenVPN";
                n.host = ip;
                n.port = 443;
                n.pingMs = Math.max(5, ping);
                n.speedMbps = Math.round((speedBps / 1_000_000.0) * 10.0) / 10.0;
                n.lat = meta.lat;
                n.lon = meta.lon;
                n.ovpnConfigBase64 = ovpnB64;
                n.title = meta.name + " (" + hostName + ")";
                combined.add(n);
            }
        } catch (Exception ignored) {}

        Collections.sort(combined, new Comparator<ShihabRelayNode>() {
            @Override
            public int compare(ShihabRelayNode a, ShihabRelayNode b) {
                return Integer.compare(a.pingMs, b.pingMs);
            }
        });

        // Cap per-country entries to keep mobile memory nimble while preserving all countries
        Map<String, Integer> perCc = new HashMap<String, Integer>();
        List<ShihabRelayNode> balanced = new ArrayList<ShihabRelayNode>();
        for (ShihabRelayNode n : combined) {
            int c = perCc.containsKey(n.countryShort) ? perCc.get(n.countryShort) : 0;
            if (c >= 20) continue;
            perCc.put(n.countryShort, c + 1);
            balanced.add(n);
        }
        return balanced;
    }

    public static ShihabRelayNode selectBestShihabRelay(
            List<ShihabRelayNode> allNodes,
            String countryCode,
            boolean preferHttpProxy,
            boolean dynamicRotation,
            Set<String> recentHosts) {
        if (allNodes == null || allNodes.isEmpty()) return null;

        List<ShihabRelayNode> pool = new ArrayList<ShihabRelayNode>();
        for (ShihabRelayNode n : allNodes) {
            if (countryCode == null || countryCode.isEmpty() || "AUTO".equalsIgnoreCase(countryCode)
                    || countryCode.equalsIgnoreCase(n.countryShort)) {
                pool.add(n);
            }
        }
        if (pool.isEmpty()) {
            pool.addAll(allNodes);
        }

        if (preferHttpProxy) {
            List<ShihabRelayNode> proxyMatches = new ArrayList<ShihabRelayNode>();
            for (ShihabRelayNode n : pool) {
                if ("OPEN_PROXY".equals(n.sourceType) || "PRIVATE_PROXY".equals(n.sourceType)) {
                    proxyMatches.add(n);
                }
            }
            if (!proxyMatches.isEmpty()) {
                pool = proxyMatches;
            }
        }

        if (dynamicRotation && recentHosts != null && !recentHosts.isEmpty()) {
            for (ShihabRelayNode candidate : pool) {
                if (!recentHosts.contains(candidate.host)) {
                    return candidate;
                }
            }
        }
        return pool.get(0);
    }

    public static ShihabGeoAudit verifyShihabGeoIp(String expectedCountryCode) {
        ShihabGeoAudit audit = new ShihabGeoAudit();
        try {
            String ipv4 = "";
            try {
                String cand = httpGetText("https://api.ipify.org", 4500).trim();
                if (cand.matches("^\\d{1,3}(\\.\\d{1,3}){3}$")) {
                    ipv4 = cand;
                }
            } catch (Exception ignored) {}

            String lookupUrl = ipv4.isEmpty() ? "https://ipwho.is/" : ("https://ipwho.is/" + ipv4);
            JSONObject g = new JSONObject(httpGetText(lookupUrl, 5500));
            audit.publicIp = !ipv4.isEmpty() ? ipv4 : g.optString("ip", "—");
            audit.countryShort = g.optString("country_code", "--").toUpperCase(Locale.US);
            audit.countryLong = g.optString("country", "Unknown");
            audit.cityName = g.optString("city", "");
            JSONObject conn = g.optJSONObject("connection");
            audit.ispName = conn != null
                    ? conn.optString("isp", conn.optString("org", "Direct Network"))
                    : "Direct Network";
            JSONObject tz = g.optJSONObject("timezone");
            audit.utcOffset = tz != null ? tz.optString("utc", "+00:00") : "+00:00";
            audit.lat = g.optDouble("latitude", 23.81);
            audit.lon = g.optDouble("longitude", 90.41);

            if (expectedCountryCode != null
                    && !expectedCountryCode.isEmpty()
                    && !"AUTO".equalsIgnoreCase(expectedCountryCode)
                    && !"--".equals(audit.countryShort)) {
                audit.leakDetected = !expectedCountryCode.equalsIgnoreCase(audit.countryShort);
            }
        } catch (Exception ignored) {
            audit.countryLong = "Network Unreachable";
        }
        return audit;
    }

    public static ShihabSpeedResult runShihabSpeedProbe() {
        ShihabSpeedResult res = new ShihabSpeedResult();
        try {
            List<Long> samples = new ArrayList<Long>();
            for (int i = 0; i < 3; i++) {
                long t0 = System.currentTimeMillis();
                httpGetText("https://speed.cloudflare.com/__down?bytes=64", 4000);
                samples.add(Math.max(1L, System.currentTimeMillis() - t0));
            }
            long sum = 0;
            for (long v : samples) sum += v;
            res.pingMs = (int) Math.round(sum / (double) samples.size());
            if (samples.size() >= 2) {
                res.jitterMs = (int) Math.abs(samples.get(samples.size() - 1) - samples.get(0));
            }

            URL dlUrl = new URL("https://speed.cloudflare.com/__down?bytes=1500000");
            HttpURLConnection conn = (HttpURLConnection) dlUrl.openConnection();
            conn.setConnectTimeout(6000);
            conn.setReadTimeout(8000);
            conn.setUseCaches(false);
            long tStart = System.currentTimeMillis();
            InputStream in = conn.getInputStream();
            byte[] buf = new byte[16384];
            long totalBytes = 0;
            int n;
            while ((n = in.read(buf)) != -1) {
                totalBytes += n;
            }
            in.close();
            conn.disconnect();
            double elapsedSec = Math.max(0.08, (System.currentTimeMillis() - tStart) / 1000.0);
            res.downloadMbps = Math.round(((totalBytes * 8.0) / (elapsedSec * 1_000_000.0)) * 10.0) / 10.0;
        } catch (Exception ignored) {}
        return res;
    }

    public static String decodeShihabOvpnText(ShihabRelayNode node) {
        if (node == null || node.ovpnConfigBase64 == null || node.ovpnConfigBase64.isEmpty()) {
            return "";
        }
        try {
            byte[] decoded = Base64.decode(node.ovpnConfigBase64.trim(), Base64.DEFAULT);
            return new String(decoded, StandardCharsets.UTF_8);
        } catch (Exception e) {
            return "";
        }
    }
}

