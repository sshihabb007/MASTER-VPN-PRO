# Shihab Shield VPN (`Open Pro` — v1.2.0)

An open-source, multi-platform **VPN & Proxy Shield Suite** across **Windows Desktop (`.exe`)**, **Chrome & Brave Browsers (`Manifest V3`)**, and **Android Smartphones (`.apk`)**, engineered with a unified Dark Bento dashboard, interactive **3D Fibonacci Globe**, **70+ Country Public Relays + Private Servers**, **Dynamic IP Rotation**, **Tunnel-Scoped Secure DNS**, **Split-Tunneling**, and **Live GeoIP Leak Auditing** by **Mehedi Hasan Shihab**.

---

## Platforms & Editions in v1.2.0

### 1. Windows Desktop (`Portable .exe` & `Setup .exe`)
- **Zero-Install Portable MS-SSTP Engine (`rasapi32.dll` + `rasdial.exe`):** Connects out-of-the-box over TLS 1.2/1.3 (`:443` and custom SoftEther `:port` gateways) across 70+ live countries without kernel driver installs.
- **Unified Public + Private Multi-Protocol Servers:** Supports public MS-SSTP & `.ovpn` export plus private **SSTP**, **IKEv2**, **L2TP/IPsec**, **PPTP**, **HTTP/HTTPS System Proxy** (with DPAPI-encrypted credentials & loopback auth bridge), **WireGuard (`.conf`)**, and **OpenVPN (`.ovpn`)**.
- **Local Loopback Companion Bridge (`127.0.0.1:38195`):** Allows the Chrome/Brave extension to control full system MS-SSTP tunnels while blocking web-page cross-origin requests (`403 Forbidden`).

### 2. Chrome & Brave Browser Extension (`browser-extension/`)
- **Dual-Engine Architecture:**
  1. **Desktop Companion Sync:** Automatically detects running `ShihabShieldVPN.exe` on `127.0.0.1:38195` to switch between 70+ MS-SSTP countries right from the browser toolbar.
  2. **Standalone Browser PAC Proxy Shield:** Works standalone on any OS via `chrome.proxy.settings` (`pac_script`) with verified open HTTPS/SOCKS5 proxies + private authenticated `HTTP`/`HTTPS`/`SOCKS5`/`SOCKS4` proxies.
- **Browser Privacy Controls:** Per-domain split-tunneling (`Direct Bypass`), WebRTC UDP leak protection (`disable_non_proxied_udp`), 3D Fibonacci Globe popup (`392x584`), Cloudflare speed probe, and `.ovpn` profile export.
- **How to Load in Chrome or Brave:**
  1. Open `chrome://extensions` or `brave://extensions`.
  2. Enable **Developer mode** (top-right toggle).
  3. Click **Load unpacked** and select `browser-extension/` (or extract `dist/Shihab-Shield-VPN-Browser-Extension-1.2.0.zip`).

### 3. Android Smartphone Application (`android-app/`, `com.shihab.shieldvpn`)
- **Native `android.net.VpnService` Engine (`minSdk 26`, `targetSdk 34`):**
  - Establishes a foreground `VpnService` interface with tunnel-scoped Secure DNS (**Cloudflare `1.1.1.1`**, **AdGuard AdBlock `94.140.14.14`**, **Quad9 `9.9.9.9`**, **Google `8.8.8.8`**).
  - Attaches country HTTP/HTTPS gateway proxies on Android 10+ via `VpnService.Builder.setHttpProxy(ProxyInfo.buildDirectProxy(...))`.
  - Supports **Per-App Split-Tunneling** (`addDisallowedApplication`), **Android 11+ `VpnManager` + `Ikev2VpnProfile`** provisioning for private IKEv2 PSK gateways, and 1-tap **`.ovpn` export/share** across 70+ countries.
  - Touch-rotatable `ShihabGlobeView` (`3D`, `Shield`, `Orbit`, `2D`) + Dark Bento 5-tab mobile UI.

---

## Build Commands

```powershell
# 1. Build Windows Portable + Setup Installer + run 24 self-tests
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 2. Package Chrome & Brave Extension (.zip + .crx)
powershell -ExecutionPolicy Bypass -File .\build-extension.ps1

# 3. Compile & Sign Android Smartphone APK (requires JDK 17+ & Android SDK 34)
powershell -ExecutionPolicy Bypass -File .\android-app\build-apk.ps1
```

---

## Developer

- **Mehedi Hasan Shihab** *(Web & Software Developer)*
- **Portfolio:** https://sshihabb007.github.io/
- **GitHub:** https://github.com/sshihabb007
- **LinkedIn:** https://www.linkedin.com/in/mehedi-hasan-shihab
- **Email:** sshihabb007@gmail.com
