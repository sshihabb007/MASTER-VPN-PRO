// Shihab Shield VPN — Chrome & Brave Popup Controller
// Engineered by Mehedi Hasan Shihab (sshihabb007)

(function () {
  let shihabDashboard = {
    state: null,
    desktopBridge: null,
    desktopCatalog: null,
    relayCache: { publicProxies: [], ovpnRelays: [] }
  };

  let activeTab = 0;
  let srvMode = "countries"; // "countries" | "proxies" | "ovpn" | "private"
  let regionFilter = "ALL";
  let logCatFilter = "ALL";
  let currentTabDomain = "";
  let globe = null;

  function sendBg(type, extra) {
    if (typeof chrome !== "undefined" && chrome.runtime && typeof chrome.runtime.sendMessage === "function") {
      return new Promise((resolve) => {
        chrome.runtime.sendMessage(Object.assign({ type }, extra || {}), (resp) => {
          resolve(resp || { ok: false, error: chrome.runtime.lastError ? chrome.runtime.lastError.message : "No response" });
        });
      });
    }
    return standalonePreviewRpc(type, extra || {});
  }

  async function standalonePreviewRpc(type) {
    if (type !== "SHIHAB_GET_DASHBOARD") {
      return { ok: true };
    }
    let desktopBridge = null;
    let desktopCatalog = null;
    try {
      const r1 = await fetch("http://127.0.0.1:38195/api/shihab/status", {
        headers: { "X-Shihab-Client": "ShihabShieldBrowserExt" }
      });
      if (r1.ok) desktopBridge = await r1.json();
      const r2 = await fetch("http://127.0.0.1:38195/api/shihab/countries", {
        headers: { "X-Shihab-Client": "ShihabShieldBrowserExt" }
      });
      if (r2.ok) desktopCatalog = await r2.json();
    } catch (_e) {}

    return {
      ok: true,
      state: {
        enginePreference: "auto",
        activeMode: "none",
        connected: false,
        connecting: false,
        connectedSinceMs: 0,
        selectedCountryCode: "US",
        selectedNodeId: "",
        activeNode: null,
        dynamicIpRotation: true,
        webRtcShield: true,
        restoreDirectOnStartup: true,
        globeCameraMode: "3D",
        peakSpeedMbps: 48.4,
        lastPingMs: 22,
        lastJitterMs: 2,
        bypassDomains: ["localhost", "127.0.0.1", "*.local"],
        privateProxies: [],
        originGeo: {
          publicIp: "203.0.113.10",
          countryShort: "BD",
          countryLong: "Bangladesh",
          cityName: "Dhaka",
          ispName: "Direct ISP",
          utcOffset: "+06:00",
          lat: 23.81,
          lon: 90.41
        },
        currentGeo: {
          publicIp: "203.0.113.10",
          countryShort: "BD",
          countryLong: "Bangladesh",
          cityName: "Dhaka",
          ispName: "Direct ISP",
          utcOffset: "+06:00",
          lat: 23.81,
          lon: 90.41
        },
        logs: [
          { ts: "12:18:00", cat: "SYSTEM", msg: "MASTER VPN PRO Extension v1.3.0 ready (Chrome / Brave MV3)." }
        ]
      },
      desktopBridge,
      desktopCatalog,
      relayCache: {
        publicProxies: [
          { id: "pub-us-1", countryShort: "US", countryLong: "United States", cityName: "New York", regionGroup: "Americas", protocol: "HTTPS", host: "198.51.100.24", port: 8443, pingMs: 22, lat: 40.71, lon: -74.01 },
          { id: "pub-gb-1", countryShort: "GB", countryLong: "United Kingdom", cityName: "London", regionGroup: "EMEA", protocol: "SOCKS5", host: "203.0.113.45", port: 1080, pingMs: 38, lat: 51.51, lon: -0.13 },
          { id: "pub-jp-1", countryShort: "JP", countryLong: "Japan", cityName: "Tokyo", regionGroup: "Asia & Oceania", protocol: "HTTPS", host: "192.0.2.88", port: 443, pingMs: 41, lat: 35.68, lon: 139.69 }
        ],
        ovpnRelays: []
      }
    };
  }

  function isEffectivelyConnected() {
    const st = shihabDashboard.state;
    const db = shihabDashboard.desktopBridge;
    return !!(db && db.connected) || !!(st && st.connected);
  }

  function formatDuration(sec) {
    const s = Math.max(0, Math.floor(sec || 0));
    const m = Math.floor(s / 60);
    const r = s % 60;
    const h = Math.floor(m / 60);
    if (h > 0) {
      return String(h).padStart(2, "0") + ":" + String(m % 60).padStart(2, "0") + ":" + String(r).padStart(2, "0");
    }
    return String(m).padStart(2, "0") + ":" + String(r).padStart(2, "0");
  }

  async function detectActiveTabDomain() {
    try {
      const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
      if (tabs && tabs[0] && tabs[0].url) {
        const u = new URL(tabs[0].url);
        if (u.protocol === "http:" || u.protocol === "https:") {
          currentTabDomain = u.hostname.toLowerCase();
        }
      }
    } catch (_e) {}
  }

  function switchNavTab(idx) {
    activeTab = idx;
    document.querySelectorAll(".rail-btn").forEach((b) => {
      b.classList.toggle("active", Number(b.dataset.tab) === idx);
    });
    document.querySelectorAll(".tab-view").forEach((v, i) => {
      v.classList.toggle("active", i === idx);
    });
    if (idx === 1) renderServerList();
    if (idx === 2) renderLogs();
  }

  function buildCombinedCountryHubs() {
    const dbCat = shihabDashboard.desktopCatalog;
    if (dbCat && Array.isArray(dbCat.countries) && dbCat.countries.length > 0) {
      return dbCat.countries.map((c) => ({
        countryShort: c.countryShort,
        countryLong: c.countryLong,
        cityName: c.cityName || resolveShihabCountryMeta(c.countryShort).city,
        regionGroup: c.regionGroup || resolveShihabCountryMeta(c.countryShort).region,
        totalServers: c.totalServers || 1,
        bestPingMs: c.bestPingMs || 28,
        lat: c.latitude || resolveShihabCountryMeta(c.countryShort).lat,
        lon: c.longitude || resolveShihabCountryMeta(c.countryShort).lon,
        sourceLabel: "Desktop MS-SSTP"
      }));
    }

    const proxies = (shihabDashboard.relayCache && shihabDashboard.relayCache.publicProxies) || [];
    const byCc = new Map();
    for (let i = 0; i < proxies.length; i++) {
      const p = proxies[i];
      if (!byCc.has(p.countryShort)) {
        byCc.set(p.countryShort, {
          countryShort: p.countryShort,
          countryLong: p.countryLong,
          cityName: p.cityName,
          regionGroup: p.regionGroup,
          totalServers: 0,
          bestPingMs: p.pingMs || 65,
          lat: p.lat,
          lon: p.lon,
          sourceLabel: p.protocol
        });
      }
      const g = byCc.get(p.countryShort);
      g.totalServers++;
      if ((p.pingMs || 999) < g.bestPingMs) g.bestPingMs = p.pingMs;
    }
    return Array.from(byCc.values()).sort((a, b) => b.totalServers - a.totalServers);
  }

  function renderDashboard() {
    const st = shihabDashboard.state || {};
    const db = shihabDashboard.desktopBridge;
    const connected = isEffectivelyConnected();

    const pill = document.getElementById("topStatusPill");
    if (db && db.connected) {
      pill.textContent = "● Desktop Protected";
      pill.classList.add("protected");
    } else if (st.connected) {
      pill.textContent = "● Browser Protected";
      pill.classList.add("protected");
    } else if (db) {
      pill.textContent = "● Desktop Ready";
      pill.classList.remove("protected");
    } else {
      pill.textContent = "● Disconnected";
      pill.classList.remove("protected");
    }

    document.getElementById("engineBadgeLabel").textContent = db
      ? "Virtual location • Desktop"
      : "Virtual location • PAC";

    let cc = st.selectedCountryCode || "US";
    let title = cc === "AUTO" ? "Auto — Fastest Gateway" : resolveShihabCountryMeta(cc).name;
    let sub = db ? "70+ Countries via MASTER VPN PRO Desktop" : "Standalone Browser Proxy Shield";
    let ipShown = (st.currentGeo && st.currentGeo.ipAddress) || "—";
    let protoShown = db ? "MS-SSTP TLS" : "PAC Proxy";
    let pingShown = (st.lastPingMs || 24) + " ms";
    let targetLat = resolveShihabCountryMeta(cc === "AUTO" ? "US" : cc).lat;
    let targetLon = resolveShihabCountryMeta(cc === "AUTO" ? "US" : cc).lon;

    if (db && (db.countryShort || db.connected)) {
      cc = db.countryShort || cc;
      title = db.countryLong || title;
      sub = (db.cityName ? db.cityName + " • " : "") + (db.endpoint || "MS-SSTP TLS");
      ipShown = db.ipAddress || ipShown;
      protoShown = db.protocol || "MS-SSTP TLS";
      pingShown = (db.pingMs || 22) + " ms";
      targetLat = Number(db.latitude || targetLat);
      targetLon = Number(db.longitude || targetLon);
    } else if (st.activeNode) {
      cc = st.activeNode.countryShort || cc;
      title = st.activeNode.countryLong || title;
      sub = (st.activeNode.cityName ? st.activeNode.cityName + " • " : "") + st.activeNode.host + ":" + st.activeNode.port;
      ipShown = (st.connected && st.currentGeo && st.currentGeo.ipAddress) ? st.currentGeo.ipAddress : st.activeNode.host;
      protoShown = st.activeNode.protocol || "PROXY";
      pingShown = ((st.connected && st.lastPingMs) ? st.lastPingMs : (st.activeNode.pingMs || 38)) + " ms";
      targetLat = Number(st.activeNode.lat || targetLat);
      targetLon = Number(st.activeNode.lon || targetLon);
    }

    document.getElementById("dashCcBadge").textContent = cc === "AUTO" ? "GL" : cc.slice(0, 2);
    document.getElementById("dashLocTitle").textContent = title;
    document.getElementById("dashLocSub").textContent = sub;
    document.getElementById("dashProtoPill").textContent = protoShown;
    document.getElementById("dashIpVal").textContent = ipShown;
    document.getElementById("dashPingVal").textContent = pingShown;
    document.getElementById("dashSpeedVal").textContent = ((db && db.peakMbps) || st.peakSpeedMbps || 0).toFixed(1) + " Mbps";

    let upSec = 0;
    if (db && db.connected) upSec = db.uptimeSeconds || 0;
    else if (st.connected && st.connectedSinceMs > 0) upSec = (Date.now() - st.connectedSinceMs) / 1000;
    document.getElementById("dashTimerVal").textContent = formatDuration(upSec);

    const orb = document.getElementById("heroConnectBtn");
    orb.classList.toggle("connected", connected);
    document.getElementById("orbPowerSvg").innerHTML = connected
      ? '<rect x="7" y="7" width="10" height="10" rx="1.8" fill="#FFFFFF" stroke="none"/>'
      : '<path d="M12 3v8M7.05 5.6a7.5 7.5 0 1 0 9.9 0"/>';

    const orig = st.originGeo;
    if (globe) {
      globe.setMode(st.globeCameraMode || "3D");
      globe.setRoute(
        orig ? orig.lat : 23.81,
        orig ? orig.lon : 90.41,
        !!orig,
        targetLat,
        targetLon,
        connected
      );
    }

    document.querySelectorAll("[data-cam]").forEach((b) => {
      b.classList.toggle("active", b.dataset.cam === (st.globeCameraMode || "3D"));
    });

    // Current tab domain split-tunnel pill
    const siteLbl = document.getElementById("activeTabDomainLabel");
    const siteBtn = document.getElementById("btnBypassCurrentSite");
    if (currentTabDomain) {
      const bypassed = Array.isArray(st.bypassDomains) && st.bypassDomains.includes(currentTabDomain);
      siteLbl.textContent = "Tab: " + currentTabDomain;
      siteBtn.textContent = bypassed ? "Direct Bypass: ON" : "Direct Bypass: OFF";
      siteBtn.classList.toggle("active", bypassed);
    } else {
      siteLbl.textContent = "Split-Tunnel per-domain ready";
      siteBtn.textContent = "Configure";
    }

    // Diagnostics Audit values
    document.getElementById("diagMbpsVal").textContent = ((db && db.peakMbps) || st.peakSpeedMbps || 0).toFixed(1) + " Mbps";
    document.getElementById("diagPingJitterVal").textContent = (st.lastPingMs || 24) + " / " + (st.lastJitterMs || 2) + " ms";
    const geo = st.currentGeo;
    if (geo) {
      document.getElementById("diagIpVal").textContent = geo.ipAddress;
      document.getElementById("diagRouteVal").textContent = connected && orig
        ? (orig.countryShort + " → " + geo.city + ", " + geo.countryLong)
        : ("Direct • " + geo.city + ", " + geo.countryLong);
      document.getElementById("diagIspVal").textContent = (geo.asn ? geo.asn + " • " : "") + geo.isp;
      document.getElementById("diagClockVal").textContent = formatShihabCountryClock(geo.countryShort);
      const leakPill = document.getElementById("diagLeakPill");
      if (connected && geo.leakDetected) {
        leakPill.textContent = "IP LEAK WARNING";
        leakPill.style.color = "#FCA5A5";
      } else if (connected) {
        leakPill.textContent = "ZERO-LEAK (" + geo.countryShort + ")";
        leakPill.style.color = "#34D399";
      } else {
        leakPill.textContent = "DIRECT (" + geo.countryShort + ")";
        leakPill.style.color = "#9BA3B0";
      }
    }

    // Settings sync
    document.getElementById("selEnginePref").value = st.enginePreference || "auto";
    document.getElementById("chkDynRot").checked = st.dynamicIpRotation !== false;
    document.getElementById("chkWebRtc").checked = st.webRtcShield !== false;
    document.getElementById("chkRestoreStartup").checked = st.restoreDirectOnStartup !== false;
    document.getElementById("txtBypassDomains").value = (st.bypassDomains || []).join(", ");
  }

  function renderServerList() {
    const host = document.getElementById("serverRowsHost");
    host.innerHTML = "";
    const q = (document.getElementById("srvSearchInput").value || "").trim().toLowerCase();
    const st = shihabDashboard.state || {};
    const hubs = buildCombinedCountryHubs();
    const proxies = (shihabDashboard.relayCache && shihabDashboard.relayCache.publicProxies) || [];
    const ovpns = (shihabDashboard.relayCache && shihabDashboard.relayCache.ovpnRelays) || [];
    const privs = st.privateProxies || [];

    document.getElementById("srvCountSub").textContent =
      hubs.length + " Countries • " + proxies.length + " Open Proxies • " + ovpns.length + " .ovpn • " + privs.length + " Private";

    if (srvMode === "countries") {
      if (!q && regionFilter === "ALL") {
        host.appendChild(createRowElement("GL", "Auto — Fastest Global Gateway", "Dynamic IP Rotation across fastest verified gateways", "AUTO", st.selectedCountryCode === "AUTO" && !st.selectedNodeId, () => {
          triggerConnect({ countryCode: "AUTO" });
        }));
      }
      hubs.forEach((h) => {
        if (regionFilter !== "ALL" && h.regionGroup !== regionFilter) return;
        if (q && !(h.countryLong.toLowerCase().includes(q) || h.countryShort.toLowerCase().includes(q) || h.cityName.toLowerCase().includes(q))) return;
        const sub = h.cityName + " (" + h.regionGroup + ") • " + h.totalServers + " Relays • " + h.sourceLabel;
        host.appendChild(createRowElement(h.countryShort, h.countryLong, sub, h.bestPingMs + " ms", st.selectedCountryCode === h.countryShort && !st.selectedNodeId, () => {
          triggerConnect({ countryCode: h.countryShort });
        }));
      });
    } else if (srvMode === "proxies") {
      proxies.slice(0, 200).forEach((p) => {
        if (regionFilter !== "ALL" && p.regionGroup !== regionFilter) return;
        if (q && !(p.countryLong.toLowerCase().includes(q) || p.host.includes(q) || p.protocol.toLowerCase().includes(q))) return;
        const sub = p.protocol + " • " + p.host + ":" + p.port + " • " + p.cityName;
        host.appendChild(createRowElement(p.countryShort, p.countryLong + " Proxy", sub, p.pingMs + " ms", st.selectedNodeId === p.id, () => {
          triggerConnect({ nodeId: p.id });
        }));
      });
    } else if (srvMode === "ovpn") {
      ovpns.slice(0, 150).forEach((o) => {
        if (regionFilter !== "ALL" && o.regionGroup !== regionFilter) return;
        if (q && !(o.countryLong.toLowerCase().includes(q) || o.ipAddress.includes(q))) return;
        const sub = o.cityName + " • IP " + o.ipAddress + " • " + o.speedMbps + " Mbps";
        host.appendChild(createRowElement(o.countryShort, o.countryLong + " (" + o.hostName + ")", sub, "Export .ovpn", false, () => {
          downloadOvpnBase64(o);
        }, "Export"));
      });
    } else if (srvMode === "private") {
      if (privs.length === 0) {
        const empty = document.createElement("div");
        empty.className = "bento-card";
        empty.style.fontSize = "11px";
        empty.style.color = "#9BA3B0";
        empty.textContent = "No Private Proxies saved yet. Click '+ Private Proxy' above to add your own HTTP, HTTPS, or SOCKS5 gateway.";
        host.appendChild(empty);
      }
      privs.forEach((p) => {
        const row = createRowElement(p.countryShort, p.displayName, "Private " + p.protocol + " • " + p.host + ":" + p.port, p.protocol, st.selectedNodeId === p.id, () => {
          triggerConnect({ nodeId: p.id });
        });
        const del = document.createElement("button");
        del.className = "mini-pill";
        del.style.color = "#FCA5A5";
        del.textContent = "✕";
        del.addEventListener("click", async (e) => {
          e.stopPropagation();
          await sendBg("SHIHAB_DELETE_PRIVATE_PROXY", { id: p.id });
          await refreshDashboardData();
          renderServerList();
        });
        row.appendChild(del);
        host.appendChild(row);
      });
    }
  }

  function createRowElement(cc, title, sub, metric, selected, onConnect, btnText) {
    const row = document.createElement("div");
    row.className = "srv-row" + (selected ? " selected" : "");

    const badge = document.createElement("div");
    badge.className = "cc-badge";
    badge.style.width = "25px";
    badge.style.height = "25px";
    badge.style.fontSize = "9.5px";
    badge.textContent = (cc || "GL").slice(0, 2);

    const info = document.createElement("div");
    info.className = "srv-info";
    const t = document.createElement("div");
    t.className = "srv-title";
    t.textContent = title;
    const s = document.createElement("div");
    s.className = "srv-sub";
    s.textContent = sub;
    info.appendChild(t);
    info.appendChild(s);

    const tag = document.createElement("span");
    tag.className = "ping-tag";
    tag.textContent = metric;

    const btn = document.createElement("button");
    btn.className = "action-btn";
    btn.style.padding = "4px 8px";
    btn.style.fontSize = "10.5px";
    btn.textContent = btnText || "Connect";
    btn.addEventListener("click", (e) => {
      e.stopPropagation();
      onConnect();
    });

    row.appendChild(badge);
    row.appendChild(info);
    row.appendChild(tag);
    row.appendChild(btn);
    return row;
  }

  function downloadOvpnBase64(item) {
    try {
      const bin = atob(item.ovpnBase64);
      const blob = new Blob([bin], { type: "application/x-openvpn-profile" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = "MasterVpnPro_" + item.countryShort + "_" + item.ipAddress + ".ovpn";
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1500);
      setBanner("Exported " + a.download);
    } catch (err) {
      setBanner("Export failed: " + err.message);
    }
  }

  function renderLogs() {
    const box = document.getElementById("logsContainer");
    box.innerHTML = "";
    const logs = ((shihabDashboard.state && shihabDashboard.state.logs) || []).slice().reverse();
    logs.forEach((l) => {
      if (logCatFilter !== "ALL" && l.cat !== logCatFilter) return;
      const div = document.createElement("div");
      div.className = "log-item";
      const ts = document.createElement("span");
      ts.className = "log-ts";
      ts.textContent = l.ts;
      const cat = document.createElement("span");
      cat.className = "log-cat";
      cat.textContent = "[" + l.cat + "]";
      const msg = document.createElement("span");
      msg.textContent = l.msg;
      div.appendChild(ts);
      div.appendChild(cat);
      div.appendChild(msg);
      box.appendChild(div);
    });
  }

  function setBanner(text) {
    document.getElementById("bottomStatusText").textContent = text;
  }

  async function triggerConnect(target) {
    setBanner("Negotiating MASTER VPN PRO route...");
    const res = await sendBg("SHIHAB_CONNECT", { target: target || {} });
    if (!res.ok) {
      setBanner("Error: " + res.error);
      return;
    }
    await refreshDashboardData();
    switchNavTab(0);
    setBanner("Protected • Active shield session established");
  }

  async function refreshDashboardData() {
    const res = await sendBg("SHIHAB_GET_DASHBOARD");
    if (res && res.ok) {
      shihabDashboard = res;
      renderDashboard();
    }
  }

  document.addEventListener("DOMContentLoaded", async () => {
    globe = new window.ShihabGlobeRenderer(document.getElementById("shihabGlobeCanvas"));
    await detectActiveTabDomain();
    await refreshDashboardData();

    document.querySelectorAll(".rail-btn").forEach((btn) => {
      btn.addEventListener("click", () => switchNavTab(Number(btn.dataset.tab)));
    });

    document.getElementById("locSelectorRow").addEventListener("click", () => switchNavTab(1));

    document.querySelectorAll("[data-cam]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        await sendBg("SHIHAB_UPDATE_SETTINGS", { patch: { globeCameraMode: btn.dataset.cam } });
        await refreshDashboardData();
      });
    });

    document.getElementById("heroConnectBtn").addEventListener("click", async () => {
      if (isEffectivelyConnected()) {
        setBanner("Disconnecting...");
        await sendBg("SHIHAB_DISCONNECT");
        await refreshDashboardData();
        setBanner("Disconnected • Local network restored");
      } else {
        await triggerConnect({});
      }
    });

    document.getElementById("btnRotateIp").addEventListener("click", async () => {
      setBanner("Rotating gateway IP...");
      const r = await sendBg("SHIHAB_ROTATE_IP");
      await refreshDashboardData();
      setBanner(r.ok ? "Rotated gateway IP cleanly." : ("Rotate warning: " + r.error));
    });

    document.getElementById("btnTopRefresh").addEventListener("click", async () => {
      setBanner("Syncing global open proxies & .ovpn relays...");
      await sendBg("SHIHAB_REFRESH_CATALOG");
      await refreshDashboardData();
      if (activeTab === 1) renderServerList();
      setBanner("Global relay catalog updated.");
    });

    document.getElementById("btnBypassCurrentSite").addEventListener("click", async () => {
      if (!currentTabDomain) {
        switchNavTab(3);
        return;
      }
      await sendBg("SHIHAB_TOGGLE_BYPASS_DOMAIN", { domain: currentTabDomain });
      await refreshDashboardData();
    });

    document.getElementById("srvSearchInput").addEventListener("input", renderServerList);

    document.querySelectorAll("[data-srvmode]").forEach((b) => {
      b.addEventListener("click", () => {
        srvMode = b.dataset.srvmode;
        document.querySelectorAll("[data-srvmode]").forEach((x) => x.classList.toggle("active", x === b));
        renderServerList();
      });
    });

    document.querySelectorAll("[data-reg]").forEach((b) => {
      b.addEventListener("click", () => {
        regionFilter = b.dataset.reg;
        document.querySelectorAll("[data-reg]").forEach((x) => x.classList.toggle("active", x === b));
        renderServerList();
      });
    });

    document.getElementById("btnToggleAddPrivate").addEventListener("click", () => {
      const dr = document.getElementById("privCreatorDrawer");
      dr.style.display = dr.style.display === "none" ? "block" : "none";
    });

    document.getElementById("btnSavePrivateProxy").addEventListener("click", async () => {
      const r = await sendBg("SHIHAB_SAVE_PRIVATE_PROXY", {
        proxy: {
          title: document.getElementById("privTitleIn").value,
          scheme: document.getElementById("privSchemeIn").value,
          host: document.getElementById("privHostIn").value,
          port: document.getElementById("privPortIn").value,
          username: document.getElementById("privUserIn").value,
          password: document.getElementById("privPassIn").value
        }
      });
      if (!r.ok) {
        setBanner(r.error);
        return;
      }
      document.getElementById("privPassIn").value = "";
      document.getElementById("privCreatorDrawer").style.display = "none";
      srvMode = "private";
      document.querySelectorAll("[data-srvmode]").forEach((x) => x.classList.toggle("active", x.dataset.srvmode === "private"));
      await refreshDashboardData();
      renderServerList();
    });

    document.getElementById("btnDiagSubAudit").addEventListener("click", () => {
      document.getElementById("btnDiagSubAudit").classList.add("active");
      document.getElementById("btnDiagSubLogs").classList.remove("active");
      document.getElementById("diagAuditPane").style.display = "flex";
      document.getElementById("diagLogsPane").style.display = "none";
    });

    document.getElementById("btnDiagSubLogs").addEventListener("click", () => {
      document.getElementById("btnDiagSubLogs").classList.add("active");
      document.getElementById("btnDiagSubAudit").classList.remove("active");
      document.getElementById("diagAuditPane").style.display = "none";
      document.getElementById("diagLogsPane").style.display = "flex";
      renderLogs();
    });

    document.querySelectorAll("[data-logcat]").forEach((b) => {
      b.addEventListener("click", () => {
        logCatFilter = b.dataset.logcat;
        document.querySelectorAll("[data-logcat]").forEach((x) => x.classList.toggle("active", x === b));
        renderLogs();
      });
    });

    document.getElementById("btnClearLogs").addEventListener("click", async () => {
      await sendBg("SHIHAB_CLEAR_LOGS");
      await refreshDashboardData();
      renderLogs();
    });

    document.getElementById("btnRunSpeedProbe").addEventListener("click", async () => {
      setBanner("Running Cloudflare HTTPS throughput & RTT test...");
      await sendBg("SHIHAB_SPEED_PROBE");
      await refreshDashboardData();
      setBanner("Speed probe complete.");
    });

    document.getElementById("btnVerifyGeoNow").addEventListener("click", async () => {
      setBanner("Verifying internet-facing IPv4 & ISP...");
      await sendBg("SHIHAB_VERIFY_GEOIP");
      await refreshDashboardData();
      setBanner("GeoIP & leak audit updated.");
    });

    const saveSettingsNow = async () => {
      const domains = document.getElementById("txtBypassDomains").value
        .split(",")
        .map((s) => s.trim().toLowerCase())
        .filter(Boolean);
      await sendBg("SHIHAB_UPDATE_SETTINGS", {
        patch: {
          enginePreference: document.getElementById("selEnginePref").value,
          dynamicIpRotation: document.getElementById("chkDynRot").checked,
          webRtcShield: document.getElementById("chkWebRtc").checked,
          restoreDirectOnStartup: document.getElementById("chkRestoreStartup").checked,
          bypassDomains: domains
        }
      });
      await refreshDashboardData();
    };

    document.getElementById("selEnginePref").addEventListener("change", saveSettingsNow);
    document.getElementById("chkDynRot").addEventListener("change", saveSettingsNow);
    document.getElementById("chkWebRtc").addEventListener("change", saveSettingsNow);
    document.getElementById("chkRestoreStartup").addEventListener("change", saveSettingsNow);
    document.getElementById("txtBypassDomains").addEventListener("blur", saveSettingsNow);

    setInterval(refreshDashboardData, 2000);
  });
})();
