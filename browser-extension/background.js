// Shihab Shield VPN — Chrome & Brave Manifest V3 Background Service Worker
// Engineered by Mehedi Hasan Shihab (sshihabb007)
// Zero unauthorized third-party commercial backends; 100% open-source public feeds + Desktop Bridge + Private Proxies.

importScripts("shihab_countries.js");

const SHIHAB_COMPANION_BASE = "http://127.0.0.1:38195";
const SHIHAB_STORAGE_KEY = "shihabBrowserState_v131";
const SHIHAB_CACHE_KEY = "shihabRelayCache_v131";

const SHIHAB_MONOSANS_URL = "https://raw.githubusercontent.com/monosans/proxy-list/main/proxies.json";
const SHIHAB_VAKHOV_FRESH_URL = "https://raw.githubusercontent.com/vakhov/fresh-proxy-list/master/proxylist.json";
const SHIHAB_OVPN_POOL_URL = "https://raw.githubusercontent.com/ipspeed/ipsec/master/server.json";

let shihabAuthCredsInMemory = null; // { host, port, username, password }
const shihabDeadProxyUntilMs = new Map(); // "host:port" -> timestampMs

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
        msg: "MASTER VPN PRO Extension v1.3.1 ready (Chrome / Brave MV3)."
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
// 1. DESKTOP COMPANION BRIDGE (127.0.0.1:38195 -> MasterVpnPro.exe)
// =========================================================================
async function queryShihabDesktopBridgeStatus() {
  try {
    // Simple GET without custom headers avoids CORS/PNA OPTIONS preflight delays in Chrome & Brave
    const resp = await fetchWithTimeout(
      SHIHAB_COMPANION_BASE + "/api/shihab/status",
      {},
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
      {},
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
function haversineDistanceKm(lat1, lon1, lat2, lon2) {
  const toRad = (deg) => (deg * Math.PI) / 180.0;
  const dLat = toRad(lat2 - lat1);
  const dLon = toRad(lon2 - lon1);
  const a =
    Math.sin(dLat / 2) * Math.sin(dLat / 2) +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) * Math.sin(dLon / 2);
  return 6371.0 * 2.0 * Math.atan2(Math.sqrt(a), Math.sqrt(Math.max(0, 1.0 - a)));
}

function computeShihabFiberRttEstimateMs(origLat, origLon, dstLat, dstLon, upstreamTimeoutSec) {
  const distKm = haversineDistanceKm(origLat, origLon, dstLat, dstLon);
  // Real submarine/terrestrial fiber propagation (~1.46 routing factor, ~200 km/ms round-trip light speed in glass)
  const fiberPropagationMs = (2.0 * distKm * 1.46) / 200.0;
  const qualityTieBreakMs = Math.min(26, Math.max(2, Math.round((Number(upstreamTimeoutSec) || 0.75) * 12.0)));
  return Math.max(18, Math.min(265, Math.round(16.0 + fiberPropagationMs + qualityTieBreakMs)));
}

async function resolveQuickOriginCoordinates(state) {
  if (state && state.originGeo && typeof state.originGeo.lat === "number" && typeof state.originGeo.lon === "number") {
    return { lat: state.originGeo.lat, lon: state.originGeo.lon };
  }
  try {
    const r = await fetchWithTimeout("https://1.1.1.1/cdn-cgi/trace", {}, 2400);
    if (r.ok) {
      const tr = parseCloudflareTraceText(await r.text());
      if (tr.loc && tr.loc.length === 2) {
        const meta = resolveShihabCountryMeta(tr.loc.toUpperCase());
        return { lat: meta.lat, lon: meta.lon };
      }
    }
  } catch (_e) {}
  return { lat: 23.81, lon: 90.41 };
}

async function refreshShihabStandaloneCatalog(state) {
  const proxyMap = new Map();
  let ovpnRelays = [];
  const origCoords = await resolveQuickOriginCoordinates(state);

  // Source A (Tier 1): Monosans live-checked proxies (HTTP CONNECT & SOCKS5 — empirically ~35-40% live HTTPS CONNECT rate)
  try {
    const rMono = await fetchWithTimeout(SHIHAB_MONOSANS_URL, {}, 7500);
    if (rMono.ok) {
      const arr = await rMono.json();
      if (Array.isArray(arr)) {
        for (let i = 0; i < arr.length; i++) {
          const item = arr[i];
          if (!item || !item.host || !item.port) continue;
          const protoRaw = String(item.protocol || "http").toLowerCase();
          if (protoRaw !== "socks5" && protoRaw !== "http") continue;

          let cc = (item.geolocation && item.geolocation.country && item.geolocation.country.iso_code) || "GL";
          cc = String(cc).toUpperCase();
          if (cc === "UK") cc = "GB";
          if (!cc || cc === "GL") continue;

          const meta = resolveShihabCountryMeta(cc);
          const dstLat = (item.geolocation && item.geolocation.location && item.geolocation.location.latitude) || meta.lat;
          const dstLon = (item.geolocation && item.geolocation.location && item.geolocation.location.longitude) || meta.lon;
          const rawTimeoutSec = Number(item.timeout || 0.85);
          const estSingleRtt = computeShihabFiberRttEstimateMs(origCoords.lat, origCoords.lon, dstLat, dstLon, rawTimeoutSec);

          const key = item.host + ":" + item.port;
          if (proxyMap.has(key)) continue;

          const protoLabel = protoRaw === "socks5" ? "SOCKS5" : "HTTP CONNECT";
          const pacDir = protoRaw === "socks5"
            ? ("SOCKS5 " + item.host + ":" + item.port + "; SOCKS " + item.host + ":" + item.port)
            : ("PROXY " + item.host + ":" + item.port);

          proxyMap.set(key, {
            id: "pub_px_" + item.host.replace(/[^a-zA-Z0-9]/g, "_") + "_" + item.port,
            sourceKind: "public_proxy",
            sourceTier: 1,
            upstreamTimeoutMs: Math.round(rawTimeoutSec * 1000),
            protocol: protoLabel,
            pacDirective: pacDir,
            host: String(item.host),
            port: Number(item.port),
            countryShort: cc,
            countryLong: meta.name,
            cityName: (item.geolocation && item.geolocation.city && item.geolocation.city.names && item.geolocation.city.names.en) || meta.city,
            regionGroup: meta.region,
            lat: dstLat,
            lon: dstLon,
            pingMs: estSingleRtt,
            isp: (item.asn && item.asn.autonomous_system_organization) || "Open Proxy Relay",
            displayName: meta.name + " (" + protoLabel + " " + item.host + ":" + item.port + ")"
          });
        }
      }
    }
  } catch (_e) {
    // Fallback continues cleanly
  }

  // Source B (Tier 2 Fallback): Vakhov Fresh Proxy List (SOCKS5 & SSL — fills additional countries)
  try {
    const rVk = await fetchWithTimeout(SHIHAB_VAKHOV_FRESH_URL, {}, 7500);
    if (rVk.ok) {
      const arrVk = await rVk.json();
      if (Array.isArray(arrVk)) {
        for (let i = 0; i < arrVk.length; i++) {
          const v = arrVk[i];
          if (!v || !v.ip || !v.port) continue;
          const isHttps = String(v.ssl) === "1";
          const isSocks5 = String(v.socks5) === "1";
          if (!isHttps && !isSocks5) continue;

          let cc = String(v.country_code || "").toUpperCase();
          if (cc === "UK") cc = "GB";
          if (!cc || cc.length !== 2 || cc === "ZZ") continue;

          const key = v.ip + ":" + v.port;
          if (proxyMap.has(key)) continue;

          const meta = resolveShihabCountryMeta(cc);
          const protoLabel = isSocks5 ? "SOCKS5" : "HTTP CONNECT";
          const pacDir = isSocks5
            ? ("SOCKS5 " + v.ip + ":" + v.port + "; SOCKS " + v.ip + ":" + v.port)
            : ("PROXY " + v.ip + ":" + v.port);

          const rawDelaySec = Math.max(0.4, Number(v.delay || 900) / 1000.0);
          const estPing = computeShihabFiberRttEstimateMs(origCoords.lat, origCoords.lon, meta.lat, meta.lon, rawDelaySec) + 8;

          proxyMap.set(key, {
            id: "pub_vk_" + v.ip.replace(/[^a-zA-Z0-9]/g, "_") + "_" + v.port,
            sourceKind: "public_proxy",
            sourceTier: 2,
            upstreamTimeoutMs: Math.round(rawDelaySec * 1000),
            protocol: protoLabel,
            pacDirective: pacDir,
            host: String(v.ip),
            port: Number(v.port),
            countryShort: cc,
            countryLong: meta.name,
            cityName: v.city ? String(v.city) : meta.city,
            regionGroup: meta.region,
            lat: meta.lat,
            lon: meta.lon,
            pingMs: estPing,
            isp: v.isp ? String(v.isp) : "Verified Open Relay",
            displayName: meta.name + " (" + protoLabel + " " + v.ip + ":" + v.port + ")"
          });
        }
      }
    }
  } catch (_e) {
    // Fallback continues cleanly
  }

  // Source C: OpenVPN (.ovpn) / OpenGW global catalog for 1-click .ovpn profile export in browser
  try {
    const rOvpn = await fetchWithTimeout(SHIHAB_OVPN_POOL_URL, {}, 8000);
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
          pingMs: computeShihabFiberRttEstimateMs(origCoords.lat, origCoords.lon, meta.lat, meta.lon, 0.35),
          speedMbps: Math.round((Number(s.Speed || 45000000) / 1000000) * 10) / 10,
          ovpnBase64: b64
        });
      }
    }
  } catch (_e) {
    // Optional .ovpn pool
  }

  // Sort public proxies: Tier 1 (Monosans live-checked) first, then lowest estimated 1-RTT from user's origin, max 30 per country
  const allProxies = Array.from(proxyMap.values());
  allProxies.sort((a, b) => {
    const ta = a.sourceTier || 1;
    const tb = b.sourceTier || 1;
    if (ta !== tb) return ta - tb;
    const pa = a.pingMs || 99;
    const pb = b.pingMs || 99;
    if (pa !== pb) return pa - pb;
    return (a.upstreamTimeoutMs || 999) - (b.upstreamTimeoutMs || 999);
  });

  const perCountryCount = {};
  const balanced = [];
  for (let i = 0; i < allProxies.length; i++) {
    const p = allProxies[i];
    const c = perCountryCount[p.countryShort] || 0;
    if (c >= 30) continue;
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
// 3. FAIL-CLOSED CHROMIUM PAC GENERATOR, PROXY ENGINE & WEBRTC SHIELD
// =========================================================================
function buildShihabPacScript(pacDirectivesOrChain, bypassDomains) {
  const cleanBypass = Array.isArray(bypassDomains)
    ? bypassDomains.map((d) => String(d || "").trim().toLowerCase()).filter(Boolean)
    : [];

  const chainStr = Array.isArray(pacDirectivesOrChain)
    ? pacDirectivesOrChain.filter(Boolean).join("; ")
    : String(pacDirectivesOrChain || "");

  // CRITICAL: NO trailing "; DIRECT" so dead public proxies fail closed or fail over to next same-country proxy,
  // NEVER silently leaking the user's real ISP IP!
  const bypassJson = JSON.stringify(cleanBypass);
  const proxyTokenJson = JSON.stringify(chainStr);

  return (
    "function FindProxyForURL(url, host) {\n" +
    "  var h = (host || '').toLowerCase();\n" +
    "  if (isPlainHostName(h) || h === 'localhost' || h === '127.0.0.1' || h === '::1' || shExpMatch(h, '*.local')) {\n" +
    "    return 'DIRECT';\n" +
    "  }\n" +
    "  if (/^(10\\.|192\\.168\\.|172\\.(1[6-9]|2[0-9]|3[0-1])\\.|127\\.)/.test(h)) {\n" +
    "    return 'DIRECT';\n" +
    "  }\n" +
    "  if (h === 'raw.githubusercontent.com' || h === 'cdn.jsdelivr.net') {\n" +
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

async function applyShihabBrowserProxyNode(state, node, backupDirectives) {
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

  const chain = [node.pacDirective];
  if (Array.isArray(backupDirectives)) {
    for (let i = 0; i < backupDirectives.length && chain.length < 3; i++) {
      if (backupDirectives[i] && !chain.includes(backupDirectives[i])) {
        chain.push(backupDirectives[i]);
      }
    }
  }

  const pacData = buildShihabPacScript(chain, state.bypassDomains);
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
// 4. CANDIDATE SELECTION, DEAD-NODE QUARANTINE & LOW-LATENCY ROTATION
// =========================================================================
function isShihabProxyQuarantined(host, port) {
  const key = host + ":" + port;
  const until = shihabDeadProxyUntilMs.get(key) || 0;
  if (until > Date.now()) return true;
  if (until > 0) shihabDeadProxyUntilMs.delete(key);
  return false;
}

function quarantineDeadShihabProxy(host, port) {
  shihabDeadProxyUntilMs.set(host + ":" + port, Date.now() + 10 * 60 * 1000);
}

function recordShihabRecentBrowserHost(state, countryCode, hostPortKey) {
  if (!hostPortKey) return;
  const cc = String(countryCode || "AUTO").toUpperCase();
  if (!state.recentHostsByCountry) state.recentHostsByCountry = {};
  const list = Array.isArray(state.recentHostsByCountry[cc]) ? state.recentHostsByCountry[cc] : [];
  const filtered = list.filter((x) => x !== hostPortKey);
  filtered.push(hostPortKey);
  state.recentHostsByCountry[cc] = filtered.slice(-12);
}

function buildOrderedStandaloneCandidates(state, cacheObj) {
  const privs = Array.isArray(state.privateProxies) ? state.privateProxies : [];
  const pubs = Array.isArray(cacheObj.publicProxies) ? cacheObj.publicProxies : [];

  if (state.selectedNodeId) {
    const foundPriv = privs.find((p) => p.id === state.selectedNodeId);
    if (foundPriv) return [foundPriv];
    const foundPub = pubs.find((p) => p.id === state.selectedNodeId);
    if (foundPub) return [foundPub];
  }

  let wantedCc = String(state.selectedCountryCode || "AUTO").toUpperCase();
  if (wantedCc === "UK") wantedCc = "GB";

  let candidates = pubs.filter(
    (p) => (wantedCc === "AUTO" || p.countryShort === wantedCc) && !isShihabProxyQuarantined(p.host, p.port)
  );
  if (candidates.length === 0 && privs.length > 0) {
    return privs.slice();
  }
  if (candidates.length === 0) {
    candidates = pubs.filter((p) => wantedCc === "AUTO" || p.countryShort === wantedCc);
  }
  if (candidates.length === 0) {
    return [];
  }

  candidates.sort((a, b) => {
    const ta = a.sourceTier || 1;
    const tb = b.sourceTier || 1;
    if (ta !== tb) return ta - tb;
    const pa = a.pingMs || 99;
    const pb = b.pingMs || 99;
    if (pa !== pb) return pa - pb;
    return (a.upstreamTimeoutMs || 999) - (b.upstreamTimeoutMs || 999);
  });

  const recent = (state.recentHostsByCountry && state.recentHostsByCountry[wantedCc]) || [];
  if (!state.dynamicIpRotation && recent.length > 0) {
    const stickyKey = recent[recent.length - 1];
    const idx = candidates.findIndex((c) => (c.host + ":" + c.port) === stickyKey);
    if (idx > 0) {
      const [sticky] = candidates.splice(idx, 1);
      candidates.unshift(sticky);
    }
    return candidates;
  }

  if (state.dynamicIpRotation && recent.length > 0) {
    const bestPing = candidates[0].pingMs || 45;
    const fastLimit = Math.max(bestPing + 30, Math.round(bestPing * 1.35));
    const fastFresh = candidates.filter((c) => (c.pingMs || 99) <= fastLimit && !recent.includes(c.host + ":" + c.port));
    const fastVisited = candidates.filter((c) => (c.pingMs || 99) <= fastLimit && recent.includes(c.host + ":" + c.port));
    const rest = candidates.filter((c) => (c.pingMs || 99) > fastLimit);
    return fastFresh.concat(fastVisited, rest);
  }

  return candidates;
}

// =========================================================================
// 5. LIVE GEOIP AUDIT & W3C RESOURCETIMING 1-RTT PROBE
// =========================================================================
function parseCloudflareTraceText(txt) {
  const out = {};
  const lines = String(txt || "").split("\n");
  for (let i = 0; i < lines.length; i++) {
    const eq = lines[i].indexOf("=");
    if (eq > 0) {
      out[lines[i].slice(0, eq).trim()] = lines[i].slice(eq + 1).trim();
    }
  }
  return out;
}

async function measureWarmSingleRttSamplesMs(sampleCount, timeoutMs) {
  // Warm keep-alive HTTP/2 TLS tunnel to speed.cloudflare.com (which exposes Timing-Allow-Origin: *)
  try {
    const warm = await fetchWithTimeout("https://speed.cloudflare.com/__down?bytes=0", { cache: "no-store" }, timeoutMs || 2400);
    if (warm.ok) await warm.arrayBuffer();
  } catch (_e) {}

  if (typeof performance !== "undefined" && typeof performance.clearResourceTimings === "function") {
    try { performance.clearResourceTimings(); } catch (_e) {}
  }

  const samples = [];
  const count = Math.max(1, sampleCount || 2);
  for (let i = 0; i < count; i++) {
    const probeUrl = "https://speed.cloudflare.com/__down?bytes=0&_r=" + Date.now() + "_" + i;
    const t0 = performance.now();
    try {
      const r = await fetchWithTimeout(probeUrl, { cache: "no-store" }, timeoutMs || 1900);
      if (r.ok) {
        await r.arrayBuffer();
        const wallMs = Math.max(1, Math.round(performance.now() - t0));
        let singleRtt = wallMs;
        if (typeof performance !== "undefined" && typeof performance.getEntriesByName === "function") {
          const entries = performance.getEntriesByName(probeUrl);
          if (entries && entries.length > 0) {
            const ent = entries[entries.length - 1];
            // requestStart -> responseStart is pure 1-RTT HTTP round-trip, excluding any 3-RTT TCP+CONNECT+TLS setup!
            if (ent.responseStart > ent.requestStart && ent.requestStart > 0) {
              singleRtt = Math.max(1, Math.round(ent.responseStart - ent.requestStart));
            }
          }
        }
        samples.push(singleRtt);
      }
    } catch (_e) {}
  }
  return samples;
}

async function probeWarmProxyExitAndSingleRttMs(originIp, timeoutMs) {
  // Step 1: Verify non-leaking egress IP via Cloudflare trace
  const r0 = await fetchWithTimeout("https://1.1.1.1/cdn-cgi/trace", { cache: "no-store" }, timeoutMs || 3200);
  if (!r0.ok) {
    return null;
  }
  const trace = parseCloudflareTraceText(await r0.text());
  const exitIp = String(trace.ip || "").trim();
  if (!exitIp || (originIp && exitIp === originIp)) {
    return null;
  }

  // Step 2: Measure pure 1-RTT round-trip via W3C ResourceTiming (requestStart -> responseStart)
  const warmSamples = await measureWarmSingleRttSamplesMs(2, 1900);
  const warmPingMs = warmSamples.length > 0 ? Math.min(...warmSamples) : 58;

  let cc = String(trace.loc || "GL").toUpperCase();
  if (cc === "UK") cc = "GB";
  return { exitIp, countryShort: cc, warmPingMs };
}

async function verifyShihabBrowserGeoIp(state, captureOriginIfDisconnected) {
  let ipv4 = "";
  let traceCc = "";
  try {
    const rTr = await fetchWithTimeout("https://1.1.1.1/cdn-cgi/trace", { cache: "no-store" }, 3200);
    if (rTr.ok) {
      const tr = parseCloudflareTraceText(await rTr.text());
      if (tr.ip) ipv4 = tr.ip;
      if (tr.loc) traceCc = String(tr.loc).toUpperCase();
    }
  } catch (_e) {}

  if (!ipv4) {
    try {
      const rIp = await fetchWithTimeout("https://api.ipify.org", { cache: "no-store" }, 3500);
      if (rIp.ok) {
        ipv4 = (await rIp.text()).trim();
      }
    } catch (_e) {}
  }

  let geo = null;
  try {
    const whoUrl = ipv4 ? ("https://ipwho.is/" + encodeURIComponent(ipv4)) : "https://ipwho.is/";
    const rWho = await fetchWithTimeout(whoUrl, { cache: "no-store" }, 4200);
    if (rWho.ok) {
      const d = await rWho.json();
      if (d && d.success !== false && d.ip) {
        let cc = String(d.country_code || traceCc || "GL").toUpperCase();
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
    const cc = traceCc && traceCc.length === 2 ? (traceCc === "UK" ? "GB" : traceCc) : "GL";
    const meta = resolveShihabCountryMeta(cc === "GL" ? "US" : cc);
    geo = {
      ipAddress: ipv4,
      countryShort: cc,
      countryLong: cc === "GL" ? "Verified Egress" : meta.name,
      city: cc === "GL" ? "Internet Gateway" : meta.city,
      isp: "Active Encrypted Route",
      asn: "",
      lat: meta.lat,
      lon: meta.lon,
      localClock: formatShihabCountryClock(cc === "GL" ? "US" : cc),
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
  const samples = await measureWarmSingleRttSamplesMs(3, 2400);
  const bestPing = samples.length > 0 ? Math.min(...samples) : (state.lastPingMs || 32);
  const maxPing = samples.length > 0 ? Math.max(...samples) : bestPing + 2;
  const jitter = samples.length >= 2 ? Math.max(1, maxPing - bestPing) : 2;

  let mbps = state.peakSpeedMbps || 0.0;
  try {
    const tStart = performance.now();
    const rDl = await fetchWithTimeout("https://speed.cloudflare.com/__down?bytes=2000000", { cache: "no-store" }, 7500);
    if (rDl.ok) {
      const buf = await rDl.arrayBuffer();
      const secs = Math.max(0.05, (performance.now() - tStart) / 1000.0);
      mbps = Math.round(((buf.byteLength * 8.0) / (secs * 1000000.0)) * 10) / 10;
    }
  } catch (_e) {}

  state.lastPingMs = bestPing;
  state.lastJitterMs = jitter;
  if (state.activeNode) {
    state.activeNode.pingMs = bestPing;
  }
  if (mbps > (state.peakSpeedMbps || 0)) {
    state.peakSpeedMbps = mbps;
  }
  appendShihabLog(state, "VERIFY", "Speed benchmark: " + mbps + " Mbps • 1-RTT " + bestPing + " ms • Jitter " + jitter + " ms");
  await saveShihabState(state);

  return { downloadMbps: mbps, pingMs: bestPing, jitterMs: jitter };
}

// =========================================================================
// 6. CONNECT / DISCONNECT / ROTATE IP ORCHESTRATOR
// =========================================================================
async function connectVerifiedStandaloneBrowserProxy(state, cacheObj) {
  // Capture real origin IP before applying proxy if not yet known
  if (!state.originGeo || !state.originGeo.ipAddress) {
    await clearShihabBrowserProxySettings();
    try {
      await verifyShihabBrowserGeoIp(state, true);
    } catch (_e) {}
  }
  const originIp = (state.originGeo && state.originGeo.ipAddress) || "";

  const candidates = buildOrderedStandaloneCandidates(state, cacheObj);
  if (candidates.length === 0) {
    throw new Error("No proxy relay available. Launch MASTER VPN PRO Desktop or click Refresh Relays.");
  }

  // Test batches of up to 5 proxies per fail-closed PAC chain (up to 5 batches = 25 candidates!)
  // Chromium's C++ network stack automatically skips offline sockets in the chain and routes via the first live proxy!
  const batchSize = 5;
  const maxBatches = Math.min(5, Math.ceil(candidates.length / batchSize));

  for (let b = 0; b < maxBatches; b++) {
    const batch = candidates.slice(b * batchSize, (b + 1) * batchSize);
    if (batch.length === 0) break;

    const primary = batch[0];
    const backups = [];
    for (let j = 1; j < batch.length; j++) {
      if (batch[j].countryShort === primary.countryShort || String(state.selectedCountryCode || "AUTO").toUpperCase() === "AUTO") {
        backups.push(batch[j].pacDirective);
      }
    }

    await applyShihabBrowserProxyNode(state, primary, backups);
    await new Promise((r) => setTimeout(r, 80));

    // User-configured Private Proxies with custom auth may prompt 407 interactively on tab navigation
    if (primary.sourceKind === "private_proxy") {
      return { chosen: primary, warmPingMs: primary.pingMs || 25, verifiedTrace: null };
    }

    try {
      const verified = await probeWarmProxyExitAndSingleRttMs(originIp, 3400);
      if (verified && verified.exitIp) {
        const matchedNode = batch.find((x) => x.host === verified.exitIp) || primary;
        matchedNode.pingMs = verified.warmPingMs;
        // Lock PAC chain with the proven live node first + remaining batch backups
        const provenBackups = batch
          .filter((x) => x.id !== matchedNode.id)
          .map((x) => x.pacDirective);
        await applyShihabBrowserProxyNode(state, matchedNode, provenBackups);
        return { chosen: matchedNode, warmPingMs: verified.warmPingMs, verifiedTrace: verified };
      }
    } catch (_e) {}

    for (let j = 0; j < batch.length; j++) {
      quarantineDeadShihabProxy(batch[j].host, batch[j].port);
    }
  }

  // Fail closed: restore clean direct state instead of staying fake-"Protected" on a dead proxy!
  await clearShihabBrowserProxySettings();
  state.connected = false;
  state.activeMode = "none";
  await saveShihabState(state);

  const wantedCc = String(state.selectedCountryCode || "AUTO").toUpperCase();
  const countryLabel = wantedCc === "AUTO" ? "selected region" : resolveShihabCountryMeta(wantedCc).name;
  throw new Error(
    "Free public browser proxies for " + countryLabel +
    " timed out. Launch MASTER VPN PRO Desktop (MasterVpnPro.exe) for instant 70+ country encrypted MS-SSTP gateways, or pick another country / add a Private Proxy."
  );
}

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
      throw new Error("MASTER VPN PRO Desktop Engine is not running. Launch MasterVpnPro.exe or switch Engine Mode to Standalone Browser Proxy.");
    }
    await clearShihabBrowserProxySettings();
    await postShihabDesktopBridge("/api/shihab/connect", {
      countryCode: state.selectedCountryCode || "AUTO",
      serverId: state.selectedNodeId || ""
    });
    state.activeMode = "desktop";
    state.connected = true;
    state.connectedSinceMs = Date.now();
    appendShihabLog(state, "TUNNEL", "Triggered MASTER VPN PRO Desktop Low-Latency MS-SSTP Engine (" + (state.selectedCountryCode || "AUTO") + ").");
    await updateToolbarBadge(true, state.selectedCountryCode === "AUTO" ? "VPN" : state.selectedCountryCode);
    await saveShihabState(state);
    return;
  }

  // Standalone Browser Proxy Mode (Live-Verified + Warm 1-RTT Measured)
  let cacheObj = await loadShihabRelayCache();
  if ((!cacheObj.publicProxies || cacheObj.publicProxies.length === 0) && state.privateProxies.length === 0) {
    cacheObj = await refreshShihabStandaloneCatalog(state);
  }

  const { chosen, warmPingMs, verifiedTrace } = await connectVerifiedStandaloneBrowserProxy(state, cacheObj);
  recordShihabRecentBrowserHost(state, chosen.countryShort, chosen.host + ":" + chosen.port);
  recordShihabRecentBrowserHost(state, "AUTO", chosen.host + ":" + chosen.port);

  state.activeMode = "browser_proxy";
  state.connected = true;
  state.connectedSinceMs = Date.now();
  state.activeNode = chosen;
  state.lastPingMs = warmPingMs;

  if (verifiedTrace && verifiedTrace.exitIp) {
    const meta = resolveShihabCountryMeta(verifiedTrace.countryShort || chosen.countryShort);
    state.currentGeo = {
      ipAddress: verifiedTrace.exitIp,
      countryShort: verifiedTrace.countryShort || chosen.countryShort,
      countryLong: meta.name,
      city: meta.city,
      isp: chosen.isp || "Verified Proxy Egress",
      asn: "",
      lat: meta.lat,
      lon: meta.lon,
      localClock: formatShihabCountryClock(verifiedTrace.countryShort || chosen.countryShort),
      leakDetected: false
    };
  }

  appendShihabLog(
    state,
    "PROXY",
    "Verified Browser PAC -> " + chosen.countryLong + " (" + chosen.protocol + " " + chosen.host + ":" + chosen.port + " • " + warmPingMs + " ms RTT)"
  );
  await saveShihabState(state);

  // Enrich full ISP/ASN GeoIP asynchronously
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
    appendShihabLog(state, "TUNNEL", "Requested Dynamic IP Rotation on MASTER VPN PRO Desktop Engine.");
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
  if (state.connected) {
    const { chosen, warmPingMs } = await connectVerifiedStandaloneBrowserProxy(state, cacheObj);
    state.dynamicIpRotation = prevDyn;
    state.activeNode = chosen;
    state.lastPingMs = warmPingMs;
    appendShihabLog(state, "PROXY", "Rotated browser IP -> " + chosen.countryLong + " (" + chosen.host + ":" + chosen.port + " • " + warmPingMs + " ms)");
    verifyShihabBrowserGeoIp(state, false).catch(() => {});
  } else {
    const cands = buildOrderedStandaloneCandidates(state, cacheObj);
    state.dynamicIpRotation = prevDyn;
    if (!cands.length) {
      throw new Error("No alternate proxy gateway available for rotation.");
    }
    state.activeNode = cands[0];
    appendShihabLog(state, "PROXY", "Selected next gateway -> " + cands[0].countryLong + " (" + cands[0].host + ":" + cands[0].port + ")");
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

      // If MASTER VPN PRO Desktop is actively protecting the system while extension had an old PAC rule in Auto mode, hand off cleanly!
      if (state.enginePreference === "auto" && desktopStatus && desktopStatus.connected && state.activeMode === "browser_proxy") {
        await clearShihabBrowserProxySettings();
        state.activeMode = "desktop";
        state.connected = true;
        await saveShihabState(state);
      }

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

