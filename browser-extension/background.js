// Shihab Shield VPN — Chrome & Brave Manifest V3 Background Service Worker
// Engineered by Mehedi Hasan Shihab (sshihabb007)
// Zero unauthorized third-party commercial backends; 100% open-source public feeds + Desktop Bridge + Private Proxies.

importScripts("shihab_countries.js");

const SHIHAB_COMPANION_BASE = "http://127.0.0.1:38195";
const SHIHAB_STORAGE_KEY = "shihabBrowserState_v120";
const SHIHAB_CACHE_KEY = "shihabRelayCache_v120";

const SHIHAB_MONOSANS_URL = "https://raw.githubusercontent.com/monosans/proxy-list/main/proxies.json";
const SHIHAB_PROXIFLY_HTTPS_URL = "https://cdn.jsdelivr.net/gh/proxifly/free-proxy-list@main/proxies/protocols/https/data.json";
const SHIHAB_OVPN_POOL_URL = "https://raw.githubusercontent.com/ipspeed/ipsec/master/server.json";

let shihabAuthCredsInMemory = null; // { host, port, username, password }

function createDefaultShihabState() {
  return {
    enginePreference: "auto", // "auto" | "desktop" | "browser_proxy"
    activeMode: "none",       // "none" | "desktop" | "browser_proxy"
    connected: false,
    connecting: false,
    connectedSinceMs: 0,
    selectedCountryCode: "AUTO",
    selectedNodeId: "",
    activeNode: null,
    dynamicIpRotation: true,
    webRtcShield: true,
    restoreDirectOnStartup: true,
    globeCameraMode: "3D",
    peakSpeedMbps: 0.0,
    lastPingMs: 24,
    lastJitterMs: 2,
    bypassDomains: ["localhost", "127.0.0.1", "*.local"],
    privateProxies: [],
    recentHostsByCountry: {},
    originGeo: null,
    currentGeo: null,
    logs: [
      {
        ts: formatNowClock(),
        cat: "SYSTEM",
        msg: "Shihab Shield VPN Extension v1.2.0 ready (Chrome / Brave MV3)."
      }
    ]
  };
}

function formatNowClock() {
  const d = new Date();
  return [d.getHours(), d.getMinutes(), d.getSeconds()]
    .map((v) => String(v).padStart(2, "0"))
    .join(":");
}

async function loadShihabState() {
  const res = await chrome.storage.local.get([SHIHAB_STORAGE_KEY]);
  const def = createDefaultShihabState();
  const saved = res[SHIHAB_STORAGE_KEY];
  if (!saved || typeof saved !== "object") {
    return def;
  }
  return Object.assign(def, saved, {
    bypassDomains: Array.isArray(saved.bypassDomains) ? saved.bypassDomains : def.bypassDomains,
    privateProxies: Array.isArray(saved.privateProxies) ? saved.privateProxies : [],
    recentHostsByCountry: saved.recentHostsByCountry && typeof saved.recentHostsByCountry === "object" ? saved.recentHostsByCountry : {},
    logs: Array.isArray(saved.logs) ? saved.logs.slice(-80) : def.logs
  });
}

async function saveShihabState(state) {
  state.logs = Array.isArray(state.logs) ? state.logs.slice(-80) : [];
  await chrome.storage.local.set({ [SHIHAB_STORAGE_KEY]: state });
}

function appendShihabLog(state, cat, msg) {
  if (!msg) return;
  if (!Array.isArray(state.logs)) state.logs = [];
  state.logs.push({
    ts: formatNowClock(),
    cat: String(cat || "SYSTEM").toUpperCase(),
    msg: String(msg)
  });
  if (state.logs.length > 80) {
    state.logs = state.logs.slice(-80);
  }
}

async function loadShihabRelayCache() {
  const res = await chrome.storage.local.get([SHIHAB_CACHE_KEY]);
  const c = res[SHIHAB_CACHE_KEY];
  if (!c || typeof c !== "object") {
    return { publicProxies: [], ovpnRelays: [], updatedAtMs: 0 };
  }
  return {
    publicProxies: Array.isArray(c.publicProxies) ? c.publicProxies : [],
    ovpnRelays: Array.isArray(c.ovpnRelays) ? c.ovpnRelays : [],
    updatedAtMs: c.updatedAtMs || 0
  };
}

async function saveShihabRelayCache(cacheObj) {
  await chrome.storage.local.set({ [SHIHAB_CACHE_KEY]: cacheObj });
}

async function fetchWithTimeout(url, options, timeoutMs) {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), timeoutMs || 5000);
  try {
    const resp = await fetch(url, Object.assign({}, options || {}, { signal: ctrl.signal, cache: "no-store" }));
    return resp;
  } finally {
    clearTimeout(timer);
  }
}

// =========================================================================
// 1. DESKTOP COMPANION BRIDGE (127.0.0.1:38195 -> ShihabShieldVPN.exe)
// =========================================================================
async function queryShihabDesktopBridgeStatus() {
  try {
    const resp = await fetchWithTimeout(
      SHIHAB_COMPANION_BASE + "/api/shihab/status",
      { headers: { "X-Shihab-Client": "ShihabShieldBrowserExt" } },
      850
    );
    if (!resp.ok) return null;
    const data = await resp.json();
    return data && data.ok ? data : null;
  } catch (_e) {
    return null;
  }
}

async function queryShihabDesktopBridgeCatalog() {
  try {
    const resp = await fetchWithTimeout(
      SHIHAB_COMPANION_BASE + "/api/shihab/countries",
      { headers: { "X-Shihab-Client": "ShihabShieldBrowserExt" } },
      1200
    );
    if (!resp.ok) return null;
    const data = await resp.json();
    return data && data.ok ? data : null;
  } catch (_e) {
    return null;
  }
}

async function postShihabDesktopBridge(path, bodyObj) {
  const resp = await fetchWithTimeout(
    SHIHAB_COMPANION_BASE + path,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Shihab-Client": "ShihabShieldBrowserExt"
      },
      body: JSON.stringify(bodyObj || {})
    },
    3000
  );
  if (!resp.ok) {
    throw new Error("Desktop bridge returned HTTP " + resp.status);
  }
  return await resp.json();
}

// =========================================================================
// 2. OPEN-SOURCE PUBLIC PROXY & OVPN RELAY AGGREGATOR
// =========================================================================
async function refreshShihabStandaloneCatalog(state) {
  const proxyMap = new Map();
  let ovpnRelays = [];

  // Source A: Monosans verified geolocation proxies (SOCKS5 / SOCKS4 / HTTP with RTT)
  try {
    const rMono = await fetchWithTimeout(SHIHAB_MONOSANS_URL, {}, 8000);
    if (rMono.ok) {
      const arr = await rMono.json();
      if (Array.isArray(arr)) {
        for (let i = 0; i < arr.length; i++) {
          const item = arr[i];
          if (!item || !item.host || !item.port) continue;
          const protoRaw = String(item.protocol || "http").toLowerCase();
          if (protoRaw !== "socks5" && protoRaw !== "http" && protoRaw !== "socks4") continue;

          let cc = (item.geolocation && item.geolocation.country && item.geolocation.country.iso_code) || "GL";
          cc = String(cc).toUpperCase();
          if (cc === "UK") cc = "GB";
          if (!cc || cc === "GL") continue;

          const meta = resolveShihabCountryMeta(cc);
          const pingMs = item.timeout ? Math.max(8, Math.min(900, Math.round(Number(item.timeout) * 1000))) : 85;
          const key = protoRaw + "://" + item.host + ":" + item.port;
          if (proxyMap.has(key)) continue;

          const pacDir = protoRaw === "socks5"
            ? ("SOCKS5 " + item.host + ":" + item.port + "; SOCKS " + item.host + ":" + item.port)
            : (protoRaw === "socks4"
              ? ("SOCKS4 " + item.host + ":" + item.port + "; SOCKS " + item.host + ":" + item.port)
              : ("PROXY " + item.host + ":" + item.port));

          proxyMap.set(key, {
            id: "pub_px_" + item.host.replace(/[^a-zA-Z0-9]/g, "_") + "_" + item.port,
            sourceKind: "public_proxy",
            protocol: protoRaw.toUpperCase(),
            pacDirective: pacDir,
            host: String(item.host),
            port: Number(item.port),
            countryShort: cc,
            countryLong: meta.name,
            cityName: meta.city,
            regionGroup: meta.region,
            lat: (item.geolocation && item.geolocation.location && item.geolocation.location.latitude) || meta.lat,
            lon: (item.geolocation && item.geolocation.location && item.geolocation.location.longitude) || meta.lon,
            pingMs: pingMs,
            isp: (item.asn && item.asn.autonomous_system_organization) || "Open Proxy Relay",
            displayName: meta.name + " (" + protoRaw.toUpperCase() + " " + item.host + ":" + item.port + ")"
          });
        }
      }
    }
  } catch (_e) {
    // Fallback continues cleanly
  }

  // Source B: Proxifly HTTPS-CONNECT capable proxies
  try {
    const rPx = await fetchWithTimeout(SHIHAB_PROXIFLY_HTTPS_URL, {}, 8000);
    if (rPx.ok) {
      const arr2 = await rPx.json();
      if (Array.isArray(arr2)) {
        for (let i = 0; i < arr2.length; i++) {
          const it = arr2[i];
          if (!it || !it.ip || !it.port) continue;
          let cc = (it.geolocation && it.geolocation.country) || "GL";
          cc = String(cc).toUpperCase();
          if (cc === "UK") cc = "GB";
          if (!cc || cc === "GL" || cc === "ZZ") continue;

          const meta = resolveShihabCountryMeta(cc);
          const key = "http_connect://" + it.ip + ":" + it.port;
          if (proxyMap.has(key)) continue;

          proxyMap.set(key, {
            id: "pub_hc_" + it.ip.replace(/[^a-zA-Z0-9]/g, "_") + "_" + it.port,
            sourceKind: "public_proxy",
            protocol: "HTTP CONNECT",
            pacDirective: "PROXY " + it.ip + ":" + it.port,
            host: String(it.ip),
            port: Number(it.port),
            countryShort: cc,
            countryLong: meta.name,
            cityName: (it.geolocation && it.geolocation.city && it.geolocation.city !== "Unknown") ? it.geolocation.city : meta.city,
            regionGroup: meta.region,
            lat: meta.lat,
            lon: meta.lon,
            pingMs: 65,
            isp: "HTTPS-Capable Open Proxy",
            displayName: meta.name + " (CONNECT " + it.ip + ":" + it.port + ")"
          });
        }
      }
    }
  } catch (_e) {
    // Fallback continues cleanly
  }

  // Source C: OpenVPN (.ovpn) / OpenGW global catalog for 1-click .ovpn profile export in browser
  try {
    const rOvpn = await fetchWithTimeout(SHIHAB_OVPN_POOL_URL, {}, 8500);
    if (rOvpn.ok) {
      const pool = await rOvpn.json();
      const entries = pool && Array.isArray(pool.Servers) ? pool.Servers : (Array.isArray(pool) ? pool : []);
      for (let i = 0; i < entries.length && ovpnRelays.length < 250; i++) {
        const s = entries[i];
        const b64 = s && (s.OpenVPN_ConfigData_Base64 || s.ovpn);
        const hostName = s && (s.HostName || s.host);
        const ip = s && (s.IP || s.ip);
        let cc = String((s && (s.CountryShort || s.cc)) || "").toUpperCase();
        if (cc === "UK") cc = "GB";
        if (!cc || !ip || !b64) continue;
        const meta = resolveShihabCountryMeta(cc);
        ovpnRelays.push({
          id: "ovpn_" + ip + "_" + i,
          hostName: hostName || ip,
          ipAddress: ip,
          countryShort: cc,
          countryLong: meta.name,
          cityName: meta.city,
          regionGroup: meta.region,
          pingMs: Math.max(8, Number(s.Ping || 28)),
          speedMbps: Math.round((Number(s.Speed || 45000000) / 1000000) * 10) / 10,
          ovpnBase64: b64
        });
      }
    }
  } catch (_e) {
    // Optional .ovpn pool
  }

  // Balance and sort public proxies: prioritize SOCKS5 & HTTP CONNECT with lowest ping, max 25 per country
  const allProxies = Array.from(proxyMap.values());
  allProxies.sort((a, b) => {
    const pa = a.protocol === "SOCKS5" ? 0 : (a.protocol === "HTTP CONNECT" ? 1 : 2);
    const pb = b.protocol === "SOCKS5" ? 0 : (b.protocol === "HTTP CONNECT" ? 1 : 2);
    if (pa !== pb) return pa - pb;
    return (a.pingMs || 99) - (b.pingMs || 99);
  });

  const perCountryCount = {};
  const balanced = [];
  for (let i = 0; i < allProxies.length; i++) {
    const p = allProxies[i];
    const c = perCountryCount[p.countryShort] || 0;
    if (c >= 25) continue;
    perCountryCount[p.countryShort] = c + 1;
    balanced.push(p);
  }

  const cacheObj = {
    publicProxies: balanced,
    ovpnRelays: ovpnRelays,
    updatedAtMs: Date.now()
  };
  await saveShihabRelayCache(cacheObj);

  if (state) {
    const countrySet = new Set(balanced.map((x) => x.countryShort));
    appendShihabLog(
      state,
      "SYSTEM",
      "Synced " + balanced.length + " open browser proxies across " + countrySet.size + " countries + " + ovpnRelays.length + " .ovpn profiles."
    );
    await saveShihabState(state);
  }

  return cacheObj;
}

// =========================================================================
// 3. CHROMIUM PAC GENERATOR, PROXY ENGINE & WEBRTC SHIELD
// =========================================================================
function buildShihabPacScript(pacDirective, bypassDomains) {
  const cleanBypass = Array.isArray(bypassDomains)
    ? bypassDomains.map((d) => String(d || "").trim().toLowerCase()).filter(Boolean)
    : [];

  const bypassJson = JSON.stringify(cleanBypass);
  const proxyTokenJson = JSON.stringify(pacDirective + "; DIRECT");

  return (
    "function FindProxyForURL(url, host) {\n" +
    "  var h = (host || '').toLowerCase();\n" +
    "  if (isPlainHostName(h) || h === 'localhost' || h === '127.0.0.1' || h === '::1' || shExpMatch(h, '*.local')) {\n" +
    "    return 'DIRECT';\n" +
    "  }\n" +
    "  if (/^(10\\.|192\\.168\\.|172\\.(1[6-9]|2[0-9]|3[0-1])\\.|127\\.)/.test(h)) {\n" +
    "    return 'DIRECT';\n" +
    "  }\n" +
    "  var rules = " + bypassJson + ";\n" +
    "  for (var i = 0; i < rules.length; i++) {\n" +
    "    var pat = rules[i];\n" +
    "    if (!pat) continue;\n" +
    "    if (h === pat || dnsDomainIs(h, '.' + pat) || shExpMatch(h, pat)) {\n" +
    "      return 'DIRECT';\n" +
    "    }\n" +
    "  }\n" +
    "  return " + proxyTokenJson + ";\n" +
    "}\n"
  );
}

async function applyShihabBrowserProxyNode(state, node) {
  if (!node || !node.pacDirective) {
    throw new Error("Invalid proxy node configuration.");
  }

  if (node.username || node.password) {
    shihabAuthCredsInMemory = {
      host: node.host,
      port: Number(node.port),
      username: String(node.username || ""),
      password: String(node.password || "")
    };
  } else {
    shihabAuthCredsInMemory = null;
  }

  const pacData = buildShihabPacScript(node.pacDirective, state.bypassDomains);
  await chrome.proxy.settings.set({
    value: {
      mode: "pac_script",
      pacScript: { data: pacData }
    },
    scope: "regular"
  });

  if (state.webRtcShield && chrome.privacy && chrome.privacy.network && chrome.privacy.network.webRTCIPHandlingPolicy) {
    try {
      await chrome.privacy.network.webRTCIPHandlingPolicy.set({
        value: "disable_non_proxied_udp",
        scope: "regular"
      });
    } catch (_e) {}
  }

  await updateToolbarBadge(true, node.countryShort || "ON");
}

async function clearShihabBrowserProxySettings() {
  shihabAuthCredsInMemory = null;
  try {
    await chrome.proxy.settings.clear({ scope: "regular" });
  } catch (_e) {}

  if (chrome.privacy && chrome.privacy.network && chrome.privacy.network.webRTCIPHandlingPolicy) {
    try {
      await chrome.privacy.network.webRTCIPHandlingPolicy.clear({ scope: "regular" });
    } catch (_e) {}
  }

  await updateToolbarBadge(false, "");
}

async function updateToolbarBadge(connected, label) {
  try {
    if (connected) {
      await chrome.action.setBadgeText({ text: String(label || "ON").slice(0, 3).toUpperCase() });
      await chrome.action.setBadgeBackgroundColor({ color: "#10B981" });
    } else {
      await chrome.action.setBadgeText({ text: "" });
    }
  } catch (_e) {}
}

// Handle 407 Proxy Authentication for user-configured Private Proxies
if (chrome.webRequest && chrome.webRequest.onAuthRequired) {
  chrome.webRequest.onAuthRequired.addListener(
    (details, callback) => {
      if (details.isProxy && shihabAuthCredsInMemory && shihabAuthCredsInMemory.username) {
        callback({
          authCredentials: {
            username: shihabAuthCredsInMemory.username,
            password: shihabAuthCredsInMemory.password || ""
          }
        });
        return;
      }
      callback({});
    },
    { urls: ["<all_urls>"] },
    ["asyncBlocking"]
  );
}

// =========================================================================
// 4. CANDIDATE SELECTION & DYNAMIC IP ROTATION
// =========================================================================
function recordShihabRecentBrowserHost(state, countryCode, hostPortKey) {
  if (!hostPortKey) return;
  const cc = String(countryCode || "AUTO").toUpperCase();
  if (!state.recentHostsByCountry) state.recentHostsByCountry = {};
  const list = Array.isArray(state.recentHostsByCountry[cc]) ? state.recentHostsByCountry[cc] : [];
  const filtered = list.filter((x) => x !== hostPortKey);
  filtered.push(hostPortKey);
  state.recentHostsByCountry[cc] = filtered.slice(-12);
}

function selectBestStandaloneProxyNode(state, cacheObj) {
  const privs = Array.isArray(state.privateProxies) ? state.privateProxies : [];
  const pubs = Array.isArray(cacheObj.publicProxies) ? cacheObj.publicProxies : [];

  if (state.selectedNodeId) {
    const foundPriv = privs.find((p) => p.id === state.selectedNodeId);
    if (foundPriv) return foundPriv;
    const foundPub = pubs.find((p) => p.id === state.selectedNodeId);
    if (foundPub) return foundPub;
  }

  let wantedCc = String(state.selectedCountryCode || "AUTO").toUpperCase();
  if (wantedCc === "UK") wantedCc = "GB";

  let candidates = pubs.filter((p) => wantedCc === "AUTO" || p.countryShort === wantedCc);
  if (candidates.length === 0 && privs.length > 0) {
    return privs[0];
  }
  if (candidates.length === 0) {
    candidates = pubs.slice();
  }
  if (candidates.length === 0) {
    return null;
  }

  const recent = (state.recentHostsByCountry && state.recentHostsByCountry[wantedCc]) || [];
  if (!state.dynamicIpRotation && recent.length > 0) {
    const stickyKey = recent[recent.length - 1];
    const sticky = candidates.find((c) => (c.host + ":" + c.port) === stickyKey);
    if (sticky) return sticky;
  }

  if (state.dynamicIpRotation && recent.length > 0) {
    const fresh = candidates.filter((c) => !recent.includes(c.host + ":" + c.port));
    if (fresh.length > 0) {
      return fresh[0];
    }
  }

  return candidates[0];
}

// =========================================================================
// 5. LIVE GEOIP AUDIT & SPEED PROBE
// =========================================================================
async function verifyShihabBrowserGeoIp(state, captureOriginIfDisconnected) {
  let ipv4 = "";
  try {
    const rIp = await fetchWithTimeout("https://api.ipify.org", {}, 4500);
    if (rIp.ok) {
      ipv4 = (await rIp.text()).trim();
    }
  } catch (_e) {}

  let geo = null;
  try {
    const whoUrl = ipv4 ? ("https://ipwho.is/" + encodeURIComponent(ipv4)) : "https://ipwho.is/";
    const rWho = await fetchWithTimeout(whoUrl, {}, 4800);
    if (rWho.ok) {
      const d = await rWho.json();
      if (d && d.success !== false && d.ip) {
        let cc = String(d.country_code || "GL").toUpperCase();
        if (cc === "UK") cc = "GB";
        const meta = resolveShihabCountryMeta(cc);
        const conn = d.connection || {};
        geo = {
          ipAddress: ipv4 || String(d.ip),
          countryShort: cc,
          countryLong: d.country || meta.name,
          city: d.city || meta.city,
          isp: conn.isp || conn.org || "Verified Carrier",
          asn: conn.asn ? ("AS" + String(conn.asn).replace(/^AS/i, "")) : "",
          lat: Number(d.latitude || meta.lat),
          lon: Number(d.longitude || meta.lon),
          localClock: formatShihabCountryClock(cc),
          leakDetected: false
        };
      }
    }
  } catch (_e) {}

  if (!geo && ipv4) {
    geo = {
      ipAddress: ipv4,
      countryShort: "GL",
      countryLong: "Verified Egress",
      city: "Internet Gateway",
      isp: "Active Route",
      asn: "",
      lat: 38.9,
      lon: -77.0,
      localClock: formatShihabCountryClock("US"),
      leakDetected: false
    };
  }

  if (geo) {
    if (!state.connected && (captureOriginIfDisconnected || !state.originGeo)) {
      state.originGeo = geo;
    } else if (state.connected && state.originGeo && state.originGeo.ipAddress === geo.ipAddress) {
      geo.leakDetected = true;
    }
    state.currentGeo = geo;
    appendShihabLog(
      state,
      "VERIFY",
      "Observed Egress " + geo.ipAddress + " • " + geo.city + ", " + geo.countryLong + " (" + geo.isp + ")"
    );
    await saveShihabState(state);
  }

  return geo;
}

async function runShihabBrowserSpeedProbe(state) {
  const samples = [];
  for (let i = 0; i < 3; i++) {
    const t0 = performance.now();
    try {
      await fetchWithTimeout("https://1.1.1.1/cdn-cgi/trace", {}, 3500);
      samples.push(Math.max(1, Math.round(performance.now() - t0)));
    } catch (_e) {}
  }

  const avgPing = samples.length > 0
    ? Math.round(samples.reduce((a, b) => a + b, 0) / samples.length)
    : 28;
  const jitter = samples.length >= 2
    ? Math.max(1, Math.abs(samples[samples.length - 1] - samples[0]))
    : 2;

  let mbps = state.peakSpeedMbps || 12.5;
  try {
    const tStart = performance.now();
    const rDl = await fetchWithTimeout("https://speed.cloudflare.com/__down?bytes=2000000", {}, 7500);
    if (rDl.ok) {
      const buf = await rDl.arrayBuffer();
      const secs = Math.max(0.05, (performance.now() - tStart) / 1000.0);
      mbps = Math.round(((buf.byteLength * 8.0) / (secs * 1000000.0)) * 10) / 10;
    }
  } catch (_e) {}

  state.lastPingMs = avgPing;
  state.lastJitterMs = jitter;
  if (mbps > (state.peakSpeedMbps || 0)) {
    state.peakSpeedMbps = mbps;
  }
  appendShihabLog(state, "VERIFY", "Speed benchmark: " + mbps + " Mbps • Ping " + avgPing + " ms • Jitter " + jitter + " ms");
  await saveShihabState(state);

  return { downloadMbps: mbps, pingMs: avgPing, jitterMs: jitter };
}

// =========================================================================
// 6. CONNECT / DISCONNECT / ROTATE IP ORCHESTRATOR
// =========================================================================
async function handleShihabConnect(state, targetSpec) {
  if (targetSpec && typeof targetSpec === "object") {
    if (targetSpec.countryCode) {
      state.selectedCountryCode = String(targetSpec.countryCode).toUpperCase();
      state.selectedNodeId = "";
    }
    if (targetSpec.nodeId) {
      state.selectedNodeId = String(targetSpec.nodeId);
    }
  }

  const desktopStatus = await queryShihabDesktopBridgeStatus();
  const useDesktop =
    state.enginePreference === "desktop" ||
    (state.enginePreference === "auto" && desktopStatus !== null && !String(state.selectedNodeId || "").startsWith("priv_px_") && !String(state.selectedNodeId || "").startsWith("pub_"));

  if (useDesktop) {
    if (!desktopStatus) {
      throw new Error("ShihabShieldVPN.exe Desktop Engine is not running. Launch ShihabShieldVPN.exe or switch Engine Mode to Standalone Browser Proxy.");
    }
    await clearShihabBrowserProxySettings();
    await postShihabDesktopBridge("/api/shihab/connect", {
      countryCode: state.selectedCountryCode || "AUTO",
      serverId: state.selectedNodeId || ""
    });
    state.activeMode = "desktop";
    state.connected = true;
    state.connectedSinceMs = Date.now();
    appendShihabLog(state, "TUNNEL", "Triggered ShihabShieldVPN.exe Desktop MS-SSTP Tunnel (" + (state.selectedCountryCode || "AUTO") + ").");
    await updateToolbarBadge(true, state.selectedCountryCode === "AUTO" ? "VPN" : state.selectedCountryCode);
    await saveShihabState(state);
    return;
  }

  // Standalone Browser Proxy Mode
  let cacheObj = await loadShihabRelayCache();
  if ((!cacheObj.publicProxies || cacheObj.publicProxies.length === 0) && state.privateProxies.length === 0) {
    cacheObj = await refreshShihabStandaloneCatalog(state);
  }

  const chosen = selectBestStandaloneProxyNode(state, cacheObj);
  if (!chosen) {
    throw new Error("No proxy relay available. Click Refresh Relays or add a Private Proxy.");
  }

  await applyShihabBrowserProxyNode(state, chosen);
  recordShihabRecentBrowserHost(state, chosen.countryShort, chosen.host + ":" + chosen.port);
  recordShihabRecentBrowserHost(state, "AUTO", chosen.host + ":" + chosen.port);

  state.activeMode = "browser_proxy";
  state.connected = true;
  state.connectedSinceMs = Date.now();
  state.activeNode = chosen;
  appendShihabLog(
    state,
    "PROXY",
    "Browser PAC active -> " + chosen.countryLong + " (" + chosen.protocol + " " + chosen.host + ":" + chosen.port + ")"
  );
  await saveShihabState(state);

  // Verify GeoIP asynchronously
  verifyShihabBrowserGeoIp(state, false).catch(() => {});
}

async function handleShihabDisconnect(state) {
  if (state.activeMode === "desktop") {
    try {
      await postShihabDesktopBridge("/api/shihab/disconnect", {});
    } catch (_e) {}
  }
  await clearShihabBrowserProxySettings();
  state.activeMode = "none";
  state.connected = false;
  state.connectedSinceMs = 0;
  appendShihabLog(state, "TUNNEL", "Disconnected • Direct browser network & WebRTC defaults restored.");
  await saveShihabState(state);
  verifyShihabBrowserGeoIp(state, true).catch(() => {});
}

async function handleShihabRotateIp(state) {
  const desktopStatus = await queryShihabDesktopBridgeStatus();
  if (state.activeMode === "desktop" || (desktopStatus && desktopStatus.connected)) {
    await postShihabDesktopBridge("/api/shihab/rotate-ip", {});
    appendShihabLog(state, "TUNNEL", "Requested Dynamic IP Rotation on ShihabShieldVPN.exe Desktop Engine.");
    await saveShihabState(state);
    return;
  }

  const prevDyn = state.dynamicIpRotation;
  state.dynamicIpRotation = true;
  state.selectedNodeId = "";
  if (state.activeNode && state.activeNode.countryShort) {
    recordShihabRecentBrowserHost(state, state.activeNode.countryShort, state.activeNode.host + ":" + state.activeNode.port);
  }

  const cacheObj = await loadShihabRelayCache();
  const nextNode = selectBestStandaloneProxyNode(state, cacheObj);
  state.dynamicIpRotation = prevDyn;

  if (!nextNode) {
    throw new Error("No alternate proxy gateway available for rotation.");
  }

  state.activeNode = nextNode;
  if (state.connected) {
    await applyShihabBrowserProxyNode(state, nextNode);
    appendShihabLog(state, "PROXY", "Rotated browser IP -> " + nextNode.countryLong + " (" + nextNode.host + ":" + nextNode.port + ")");
    verifyShihabBrowserGeoIp(state, false).catch(() => {});
  } else {
    appendShihabLog(state, "PROXY", "Selected next gateway -> " + nextNode.countryLong + " (" + nextNode.host + ":" + nextNode.port + ")");
  }
  await saveShihabState(state);
}

// =========================================================================
// 7. LIFECYCLE & POPUP MESSAGE ROUTER
// =========================================================================
chrome.runtime.onInstalled.addListener(async () => {
  const st = await loadShihabState();
  await saveShihabState(st);
  refreshShihabStandaloneCatalog(st).catch(() => {});
  verifyShihabBrowserGeoIp(st, true).catch(() => {});
});

chrome.runtime.onStartup.addListener(async () => {
  const st = await loadShihabState();
  if (st.restoreDirectOnStartup) {
    await clearShihabBrowserProxySettings();
    st.connected = false;
    st.activeMode = "none";
    await saveShihabState(st);
  }
});

chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
  (async () => {
    const state = await loadShihabState();
    const type = msg && msg.type;

    if (type === "SHIHAB_GET_DASHBOARD") {
      const [desktopStatus, desktopCatalog, cacheObj] = await Promise.all([
        queryShihabDesktopBridgeStatus(),
        queryShihabDesktopBridgeCatalog(),
        loadShihabRelayCache()
      ]);

      if (desktopStatus && desktopStatus.connected) {
        await updateToolbarBadge(true, desktopStatus.countryShort || "VPN");
      } else if (!state.connected) {
        await updateToolbarBadge(false, "");
      }

      if ((!cacheObj.publicProxies || cacheObj.publicProxies.length === 0) && Date.now() - (cacheObj.updatedAtMs || 0) > 60000) {
        refreshShihabStandaloneCatalog(state).catch(() => {});
      }

      sendResponse({
        ok: true,
        state,
        desktopBridge: desktopStatus,
        desktopCatalog: desktopCatalog,
        relayCache: cacheObj
      });
      return;
    }

    if (type === "SHIHAB_REFRESH_CATALOG") {
      const cacheObj = await refreshShihabStandaloneCatalog(state);
      const desktopCatalog = await queryShihabDesktopBridgeCatalog();
      sendResponse({ ok: true, state, relayCache: cacheObj, desktopCatalog });
      return;
    }

    if (type === "SHIHAB_CONNECT") {
      await handleShihabConnect(state, msg.target || {});
      const desktopStatus = await queryShihabDesktopBridgeStatus();
      sendResponse({ ok: true, state, desktopBridge: desktopStatus });
      return;
    }

    if (type === "SHIHAB_DISCONNECT") {
      await handleShihabDisconnect(state);
      const desktopStatus = await queryShihabDesktopBridgeStatus();
      sendResponse({ ok: true, state, desktopBridge: desktopStatus });
      return;
    }

    if (type === "SHIHAB_ROTATE_IP") {
      await handleShihabRotateIp(state);
      const desktopStatus = await queryShihabDesktopBridgeStatus();
      sendResponse({ ok: true, state, desktopBridge: desktopStatus });
      return;
    }

    if (type === "SHIHAB_VERIFY_GEOIP") {
      const geo = await verifyShihabBrowserGeoIp(state, !state.connected);
      sendResponse({ ok: true, state, geo });
      return;
    }

    if (type === "SHIHAB_SPEED_PROBE") {
      const bench = await runShihabBrowserSpeedProbe(state);
      sendResponse({ ok: true, state, bench });
      return;
    }

    if (type === "SHIHAB_SAVE_PRIVATE_PROXY") {
      const p = msg.proxy || {};
      const host = String(p.host || "").trim();
      const port = Number(p.port || 8080);
      const scheme = String(p.scheme || "HTTP").toUpperCase();
      if (!host || !(port > 0 && port <= 65535)) {
        throw new Error("Enter a valid proxy host and port (1-65535).");
      }
      const cc = String(p.countryShort || "PV").toUpperCase();
      const meta = resolveShihabCountryMeta(cc === "PV" ? "US" : cc);
      let pacDir = "PROXY " + host + ":" + port;
      if (scheme === "HTTPS") pacDir = "HTTPS " + host + ":" + port;
      else if (scheme === "SOCKS5") pacDir = "SOCKS5 " + host + ":" + port + "; SOCKS " + host + ":" + port;
      else if (scheme === "SOCKS4") pacDir = "SOCKS4 " + host + ":" + port + "; SOCKS " + host + ":" + port;

      const node = {
        id: "priv_px_" + Date.now(),
        sourceKind: "private_proxy",
        protocol: scheme,
        pacDirective: pacDir,
        host: host,
        port: port,
        username: String(p.username || "").trim(),
        password: String(p.password || ""),
        countryShort: cc,
        countryLong: p.title ? String(p.title).trim() : ("Private " + scheme + " (" + host + ")"),
        cityName: cc === "PV" ? "Private Proxy" : meta.city,
        regionGroup: "Private",
        lat: meta.lat,
        lon: meta.lon,
        pingMs: 20,
        displayName: (p.title ? String(p.title).trim() : host) + " (" + scheme + " :" + port + ")"
      };

      state.privateProxies.unshift(node);
      state.selectedNodeId = node.id;
      appendShihabLog(state, "SYSTEM", "Saved Private Proxy profile: " + node.displayName);
      await saveShihabState(state);
      sendResponse({ ok: true, state });
      return;
    }

    if (type === "SHIHAB_DELETE_PRIVATE_PROXY") {
      state.privateProxies = state.privateProxies.filter((x) => x.id !== msg.id);
      if (state.selectedNodeId === msg.id) state.selectedNodeId = "";
      await saveShihabState(state);
      sendResponse({ ok: true, state });
      return;
    }

    if (type === "SHIHAB_UPDATE_SETTINGS") {
      const patch = msg.patch || {};
      if (typeof patch.enginePreference === "string") state.enginePreference = patch.enginePreference;
      if (typeof patch.dynamicIpRotation === "boolean") state.dynamicIpRotation = patch.dynamicIpRotation;
      if (typeof patch.webRtcShield === "boolean") state.webRtcShield = patch.webRtcShield;
      if (typeof patch.restoreDirectOnStartup === "boolean") state.restoreDirectOnStartup = patch.restoreDirectOnStartup;
      if (typeof patch.globeCameraMode === "string") state.globeCameraMode = patch.globeCameraMode;
      if (Array.isArray(patch.bypassDomains)) state.bypassDomains = patch.bypassDomains;
      if (typeof patch.selectedCountryCode === "string") {
        state.selectedCountryCode = patch.selectedCountryCode;
        state.selectedNodeId = "";
      }
      if (typeof patch.selectedNodeId === "string") {
        state.selectedNodeId = patch.selectedNodeId;
      }

      // Live-reload PAC if currently connected in browser_proxy mode
      if (state.connected && state.activeMode === "browser_proxy" && state.activeNode) {
        await applyShihabBrowserProxyNode(state, state.activeNode);
      }
      await saveShihabState(state);
      sendResponse({ ok: true, state });
      return;
    }

    if (type === "SHIHAB_TOGGLE_BYPASS_DOMAIN") {
      const dom = String(msg.domain || "").trim().toLowerCase();
      if (dom) {
        const idx = state.bypassDomains.indexOf(dom);
        if (idx >= 0) {
          state.bypassDomains.splice(idx, 1);
          appendShihabLog(state, "PROXY", "Removed '" + dom + "' from Split-Tunnel bypass list.");
        } else {
          state.bypassDomains.push(dom);
          appendShihabLog(state, "PROXY", "Added '" + dom + "' to Split-Tunnel direct bypass list.");
        }
        if (state.connected && state.activeMode === "browser_proxy" && state.activeNode) {
          await applyShihabBrowserProxyNode(state, state.activeNode);
        }
        await saveShihabState(state);
      }
      sendResponse({ ok: true, state });
      return;
    }

    if (type === "SHIHAB_CLEAR_LOGS") {
      state.logs = [{ ts: formatNowClock(), cat: "SYSTEM", msg: "Activity log cleared by user." }];
      await saveShihabState(state);
      sendResponse({ ok: true, state });
      return;
    }

    sendResponse({ ok: false, error: "Unknown command: " + type });
  })().catch((err) => {
    sendResponse({ ok: false, error: err && err.message ? err.message : String(err) });
  });
  return true;
});

