package com.shihab.shieldvpn;

import android.content.Context;
import android.content.SharedPreferences;

import org.json.JSONArray;
import org.json.JSONObject;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Calendar;
import java.util.Collections;
import java.util.Comparator;
import java.util.Date;
import java.util.HashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.TimeZone;

/**
 * Shihab Shield VPN — Android Core Domain Models, Country Metadata & Preferences Store.
 * Engineered by Mehedi Hasan Shihab (sshihabb007).
 */
public final class ShihabModels {

    private ShihabModels() {}

    public static final class ShihabRelayNode {
        public String id = "";
        public String sourceType = "OPEN_PROXY"; // OPEN_PROXY | VPNGATE_OVPN | IPSPEED_OVPN | PRIVATE_PROXY | PRIVATE_IKEV2
        public String title = "";
        public String countryShort = "US";
        public String countryLong = "United States";
        public String cityName = "New York";
        public String regionGroup = "Americas";
        public String protocol = "HTTPS"; // HTTP | HTTPS | OpenVPN | IKEv2
        public String host = "";
        public int port = 443;
        public String username = "";
        public String password = "";
        public int pingMs = 45;
        public double speedMbps = 25.0;
        public double lat = 40.71;
        public double lon = -74.01;
        public String ovpnConfigBase64 = "";

        public JSONObject toJson() {
            JSONObject o = new JSONObject();
            try {
                o.put("id", id);
                o.put("sourceType", sourceType);
                o.put("title", title);
                o.put("countryShort", countryShort);
                o.put("countryLong", countryLong);
                o.put("cityName", cityName);
                o.put("regionGroup", regionGroup);
                o.put("protocol", protocol);
                o.put("host", host);
                o.put("port", port);
                o.put("username", username);
                o.put("password", password);
                o.put("pingMs", pingMs);
                o.put("speedMbps", speedMbps);
                o.put("lat", lat);
                o.put("lon", lon);
                o.put("ovpnConfigBase64", ovpnConfigBase64);
            } catch (Exception ignored) {}
            return o;
        }

        public static ShihabRelayNode fromJson(JSONObject o) {
            ShihabRelayNode n = new ShihabRelayNode();
            if (o == null) return n;
            n.id = o.optString("id", "");
            n.sourceType = o.optString("sourceType", "PRIVATE_PROXY");
            n.title = o.optString("title", "");
            n.countryShort = o.optString("countryShort", "US");
            n.countryLong = o.optString("countryLong", "United States");
            n.cityName = o.optString("cityName", "Gateway");
            n.regionGroup = o.optString("regionGroup", "Americas");
            n.protocol = o.optString("protocol", "HTTP");
            n.host = o.optString("host", "");
            n.port = o.optInt("port", 8080);
            n.username = o.optString("username", "");
            n.password = o.optString("password", "");
            n.pingMs = o.optInt("pingMs", 30);
            n.speedMbps = o.optDouble("speedMbps", 50.0);
            n.lat = o.optDouble("lat", 40.71);
            n.lon = o.optDouble("lon", -74.01);
            n.ovpnConfigBase64 = o.optString("ovpnConfigBase64", "");
            return n;
        }
    }

    public static final class ShihabCountryHub {
        public String countryShort;
        public String countryLong;
        public String cityName;
        public String regionGroup;
        public int proxyCount;
        public int ovpnCount;
        public int bestPingMs;
        public double maxSpeedMbps;
        public double lat;
        public double lon;

        public int totalCount() {
            return proxyCount + ovpnCount;
        }
    }

    public static final class ShihabGeoAudit {
        public String publicIp = "—";
        public String countryShort = "--";
        public String countryLong = "Detecting...";
        public String cityName = "";
        public String ispName = "—";
        public String utcOffset = "+00:00";
        public double lat = 23.81;
        public double lon = 90.41;
        public boolean leakDetected = false;
    }

    public static final class ShihabSpeedResult {
        public double downloadMbps = 0.0;
        public int pingMs = 0;
        public int jitterMs = 0;
    }

    public static final class ShihabCountryInfo {
        public final String code;
        public final String name;
        public final String city;
        public final String region;
        public final double lat;
        public final double lon;
        public final int utcMinutes;

        public ShihabCountryInfo(String code, String name, String city, String region, double lat, double lon, int utcMinutes) {
            this.code = code;
            this.name = name;
            this.city = city;
            this.region = region;
            this.lat = lat;
            this.lon = lon;
            this.utcMinutes = utcMinutes;
        }
    }

    private static final Map<String, ShihabCountryInfo> COUNTRY_MAP = new HashMap<String, ShihabCountryInfo>();

    static {
        addCc("US", "United States", "New York", "Americas", 40.71, -74.01, -300);
        addCc("CA", "Canada", "Toronto", "Americas", 43.65, -79.38, -300);
        addCc("MX", "Mexico", "Mexico City", "Americas", 19.43, -99.13, -360);
        addCc("BR", "Brazil", "Sao Paulo", "Americas", -23.55, -46.63, -180);
        addCc("AR", "Argentina", "Buenos Aires", "Americas", -34.60, -58.38, -180);
        addCc("CL", "Chile", "Santiago", "Americas", -33.45, -70.67, -240);
        addCc("CO", "Colombia", "Bogota", "Americas", 4.71, -74.07, -300);
        addCc("PE", "Peru", "Lima", "Americas", -12.05, -77.04, -300);

        addCc("GB", "United Kingdom", "London", "EMEA", 51.51, -0.13, 0);
        addCc("DE", "Germany", "Frankfurt", "EMEA", 50.11, 8.68, 60);
        addCc("NL", "Netherlands", "Amsterdam", "EMEA", 52.37, 4.90, 60);
        addCc("FR", "France", "Paris", "EMEA", 48.86, 2.35, 60);
        addCc("CH", "Switzerland", "Zurich", "EMEA", 47.37, 8.54, 60);
        addCc("SE", "Sweden", "Stockholm", "EMEA", 59.33, 18.07, 60);
        addCc("NO", "Norway", "Oslo", "EMEA", 59.91, 10.75, 60);
        addCc("FI", "Finland", "Helsinki", "EMEA", 60.17, 24.94, 120);
        addCc("DK", "Denmark", "Copenhagen", "EMEA", 55.68, 12.57, 60);
        addCc("PL", "Poland", "Warsaw", "EMEA", 52.23, 21.01, 60);
        addCc("RO", "Romania", "Bucharest", "EMEA", 44.43, 26.10, 120);
        addCc("IT", "Italy", "Milan", "EMEA", 45.46, 9.19, 60);
        addCc("ES", "Spain", "Madrid", "EMEA", 40.42, -3.70, 60);
        addCc("PT", "Portugal", "Lisbon", "EMEA", 38.72, -9.14, 0);
        addCc("AT", "Austria", "Vienna", "EMEA", 48.21, 16.37, 60);
        addCc("BE", "Belgium", "Brussels", "EMEA", 50.85, 4.35, 60);
        addCc("IE", "Ireland", "Dublin", "EMEA", 53.35, -6.26, 0);
        addCc("CZ", "Czechia", "Prague", "EMEA", 50.08, 14.44, 60);
        addCc("UA", "Ukraine", "Kyiv", "EMEA", 50.45, 30.52, 120);
        addCc("BG", "Bulgaria", "Sofia", "EMEA", 42.70, 23.32, 120);
        addCc("TR", "Turkey", "Istanbul", "EMEA", 41.01, 28.98, 180);
        addCc("AE", "United Arab Emirates", "Dubai", "EMEA", 25.20, 55.27, 240);
        addCc("SA", "Saudi Arabia", "Riyadh", "EMEA", 24.71, 46.68, 180);
        addCc("ZA", "South Africa", "Johannesburg", "EMEA", -26.20, 28.05, 120);
        addCc("EG", "Egypt", "Cairo", "EMEA", 30.04, 31.24, 120);

        addCc("JP", "Japan", "Tokyo", "Asia & Oceania", 35.68, 139.69, 540);
        addCc("SG", "Singapore", "Singapore", "Asia & Oceania", 1.35, 103.82, 480);
        addCc("KR", "South Korea", "Seoul", "Asia & Oceania", 37.57, 126.98, 540);
        addCc("HK", "Hong Kong", "Hong Kong", "Asia & Oceania", 22.32, 114.17, 480);
        addCc("TW", "Taiwan", "Taipei", "Asia & Oceania", 25.03, 121.57, 480);
        addCc("IN", "India", "Mumbai", "Asia & Oceania", 19.08, 72.88, 330);
        addCc("BD", "Bangladesh", "Dhaka", "Asia & Oceania", 23.81, 90.41, 360);
        addCc("PK", "Pakistan", "Karachi", "Asia & Oceania", 24.86, 67.00, 300);
        addCc("ID", "Indonesia", "Jakarta", "Asia & Oceania", -6.21, 106.85, 420);
        addCc("MY", "Malaysia", "Kuala Lumpur", "Asia & Oceania", 3.14, 101.69, 480);
        addCc("TH", "Thailand", "Bangkok", "Asia & Oceania", 13.76, 100.50, 420);
        addCc("VN", "Vietnam", "Ho Chi Minh", "Asia & Oceania", 10.82, 106.63, 420);
        addCc("PH", "Philippines", "Manila", "Asia & Oceania", 14.60, 120.98, 480);
        addCc("AU", "Australia", "Sydney", "Asia & Oceania", -33.87, 151.21, 600);
        addCc("NZ", "New Zealand", "Auckland", "Asia & Oceania", -36.85, 174.76, 720);
    }

    private static void addCc(String code, String name, String city, String region, double lat, double lon, int utcMins) {
        COUNTRY_MAP.put(code, new ShihabCountryInfo(code, name, city, region, lat, lon, utcMins));
    }

    public static ShihabCountryInfo resolveShihabCountry(String code, String fallbackName) {
        String key = code == null ? "" : code.trim().toUpperCase(Locale.US);
        ShihabCountryInfo info = COUNTRY_MAP.get(key);
        if (info != null) {
            return info;
        }
        String cleanName = (fallbackName != null && !fallbackName.trim().isEmpty())
                ? fallbackName.trim()
                : (key.isEmpty() ? "Global Gateway" : key);
        return new ShihabCountryInfo(
                key.isEmpty() ? "GL" : key,
                cleanName,
                cleanName + " Hub",
                "EMEA",
                20.0,
                0.0,
                0
        );
    }

    public static String formatCountryUtcClock(int utcMinutes) {
        Calendar cal = Calendar.getInstance(TimeZone.getTimeZone("UTC"));
        cal.add(Calendar.MINUTE, utcMinutes);
        int hh = cal.get(Calendar.HOUR_OF_DAY);
        int mm = cal.get(Calendar.MINUTE);
        String sign = utcMinutes >= 0 ? "+" : "-";
        int abs = Math.abs(utcMinutes);
        return String.format(Locale.US, "%02d:%02d (UTC%s%02d:%02d)",
                hh, mm, sign, abs / 60, abs % 60);
    }

    public static List<ShihabCountryHub> buildShihabCountryHubs(List<ShihabRelayNode> nodes) {
        Map<String, ShihabCountryHub> byCc = new HashMap<String, ShihabCountryHub>();
        for (ShihabRelayNode n : nodes) {
            if (n.countryShort == null || n.countryShort.isEmpty()) continue;
            ShihabCountryHub hub = byCc.get(n.countryShort);
            if (hub == null) {
                ShihabCountryInfo meta = resolveShihabCountry(n.countryShort, n.countryLong);
                hub = new ShihabCountryHub();
                hub.countryShort = meta.code;
                hub.countryLong = meta.name;
                hub.cityName = meta.city;
                hub.regionGroup = meta.region;
                hub.bestPingMs = n.pingMs > 0 ? n.pingMs : 60;
                hub.maxSpeedMbps = n.speedMbps;
                hub.lat = meta.lat;
                hub.lon = meta.lon;
                byCc.put(n.countryShort, hub);
            }
            if ("OPEN_PROXY".equals(n.sourceType) || "PRIVATE_PROXY".equals(n.sourceType)) {
                hub.proxyCount++;
            } else {
                hub.ovpnCount++;
            }
            if (n.pingMs > 0 && n.pingMs < hub.bestPingMs) {
                hub.bestPingMs = n.pingMs;
            }
            if (n.speedMbps > hub.maxSpeedMbps) {
                hub.maxSpeedMbps = n.speedMbps;
            }
        }
        List<ShihabCountryHub> list = new ArrayList<ShihabCountryHub>(byCc.values());
        Collections.sort(list, new Comparator<ShihabCountryHub>() {
            @Override
            public int compare(ShihabCountryHub a, ShihabCountryHub b) {
                if ("US".equals(a.countryShort) && !"US".equals(b.countryShort)) return -1;
                if ("US".equals(b.countryShort) && !"US".equals(a.countryShort)) return 1;
                return Integer.compare(b.totalCount(), a.totalCount());
            }
        });
        return list;
    }

    public static final class ShihabPreferencesStore {
        private static final String PREF_NAME = "shihab_shield_prefs_v120";
        private final SharedPreferences prefs;

        public ShihabPreferencesStore(Context ctx) {
            this.prefs = ctx.getApplicationContext().getSharedPreferences(PREF_NAME, Context.MODE_PRIVATE);
        }

        public String getSelectedCountry() {
            return prefs.getString("selected_country", "US");
        }

        public void setSelectedCountry(String cc) {
            prefs.edit().putString("selected_country", cc == null ? "US" : cc).apply();
        }

        public String getDnsProvider() {
            return prefs.getString("dns_provider", "Cloudflare (1.1.1.1)");
        }

        public void setDnsProvider(String dns) {
            prefs.edit().putString("dns_provider", dns).apply();
        }

        public boolean isDynamicRotationEnabled() {
            return prefs.getBoolean("dynamic_rotation", true);
        }

        public void setDynamicRotationEnabled(boolean enabled) {
            prefs.edit().putBoolean("dynamic_rotation", enabled).apply();
        }

        public boolean isProxyAttachmentEnabled() {
            return prefs.getBoolean("attach_http_proxy", true);
        }

        public void setProxyAttachmentEnabled(boolean enabled) {
            prefs.edit().putBoolean("attach_http_proxy", enabled).apply();
        }

        public String getBypassPackagesCsv() {
            return prefs.getString("bypass_packages", "");
        }

        public void setBypassPackagesCsv(String csv) {
            prefs.edit().putString("bypass_packages", csv == null ? "" : csv.trim()).apply();
        }

        public Set<String> getRecentHosts() {
            String raw = prefs.getString("recent_hosts", "");
            Set<String> out = new LinkedHashSet<String>();
            if (raw != null && !raw.isEmpty()) {
                for (String p : raw.split(",")) {
                    if (!p.trim().isEmpty()) out.add(p.trim());
                }
            }
            return out;
        }

        public void recordRecentHost(String host) {
            if (host == null || host.isEmpty()) return;
            List<String> list = new ArrayList<String>(getRecentHosts());
            list.remove(host);
            list.add(host);
            while (list.size() > 16) list.remove(0);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < list.size(); i++) {
                if (i > 0) sb.append(',');
                sb.append(list.get(i));
            }
            prefs.edit().putString("recent_hosts", sb.toString()).apply();
        }

        public List<ShihabRelayNode> getPrivateRelays() {
            List<ShihabRelayNode> out = new ArrayList<ShihabRelayNode>();
            String raw = prefs.getString("private_relays_json", "[]");
            try {
                JSONArray arr = new JSONArray(raw);
                for (int i = 0; i < arr.length(); i++) {
                    out.add(ShihabRelayNode.fromJson(arr.getJSONObject(i)));
                }
            } catch (Exception ignored) {}
            return out;
        }

        public void savePrivateRelays(List<ShihabRelayNode> list) {
            JSONArray arr = new JSONArray();
            for (ShihabRelayNode n : list) {
                arr.put(n.toJson());
            }
            prefs.edit().putString("private_relays_json", arr.toString()).apply();
        }

        public void appendLog(String category, String message) {
            String ts = new SimpleDateFormat("HH:mm:ss", Locale.US).format(new Date());
            String line = "[" + ts + "] [" + category + "] " + message;
            String existing = prefs.getString("activity_logs", "");
            String combined = existing.isEmpty() ? line : (existing + "\n" + line);
            String[] lines = combined.split("\n");
            if (lines.length > 60) {
                StringBuilder sb = new StringBuilder();
                for (int i = lines.length - 60; i < lines.length; i++) {
                    if (sb.length() > 0) sb.append('\n');
                    sb.append(lines[i]);
                }
                combined = sb.toString();
            }
            prefs.edit().putString("activity_logs", combined).apply();
        }

        public String getLogsText() {
            return prefs.getString("activity_logs", "[SYSTEM] MASTER VPN PRO Android v1.3.0 initialized.");
        }

        public void clearLogs() {
            prefs.edit().putString("activity_logs", "").apply();
        }
    }
}

