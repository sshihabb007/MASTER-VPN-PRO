// Shihab Shield VPN — Interactive HTML5 Canvas 3D Fibonacci Globe & Route Renderer
// Engineered by Mehedi Hasan Shihab (sshihabb007)

(function () {
  const CONTINENT_POLYS = [
    [[-165, 68], [-130, 70], [-95, 66], [-76, 48], [-80, 26], [-97, 19], [-83, 9], [-105, 20], [-123, 38], [-140, 58]],
    [[-52, 60], [-44, 60], [-22, 70], [-25, 81], [-55, 78]],
    [[-80, 10], [-62, 10], [-35, -6], [-42, -23], [-58, -38], [-70, -53], [-75, -42], [-71, -18], [-80, 0]],
    [[-10, 36], [10, 38], [28, 37], [42, 46], [58, 56], [55, 68], [28, 71], [8, 62], [-6, 56], [-9, 44]],
    [[-17, 15], [-6, 35], [12, 33], [33, 31], [49, 11], [41, -4], [35, -25], [20, -35], [12, -18], [8, 4], [-10, 6]],
    [[35, 36], [56, 24], [73, 19], [78, 8], [90, 22], [102, 13], [109, 21], [122, 31], [132, 42], [142, 53], [168, 65], [140, 72], [95, 74], [62, 68], [48, 45]],
    [[130, 31], [139, 35], [142, 43], [136, 38]],
    [[96, 5], [106, -6], [118, -8], [126, -5], [116, 3], [103, 4]],
    [[114, -22], [130, -12], [145, -15], [153, -27], [147, -38], [132, -33], [115, -34]]
  ];

  function pointInPoly(lon, lat, poly) {
    let inside = false;
    for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
      const xi = poly[i][0], yi = poly[i][1];
      const xj = poly[j][0], yj = poly[j][1];
      const intersect = ((yi > lat) !== (yj > lat)) &&
        (lon < ((xj - xi) * (lat - yi)) / ((yj - yi) + 1e-9) + xi);
      if (intersect) inside = !inside;
    }
    return inside;
  }

  function isLand(lat, lon) {
    for (let i = 0; i < CONTINENT_POLYS.length; i++) {
      if (pointInPoly(lon, lat, CONTINENT_POLYS[i])) return true;
    }
    return false;
  }

  function buildShihabSphereDots(count) {
    const dots = [];
    const golden = Math.PI * (3 - Math.sqrt(5));
    for (let i = 0; i < count; i++) {
      const y = 1 - (i / (count - 1)) * 2;
      const r = Math.sqrt(Math.max(0, 1 - y * y));
      const th = golden * i;
      const x = Math.cos(th) * r;
      const z = Math.sin(th) * r;
      const lat = Math.asin(Math.max(-1, Math.min(1, y))) * (180 / Math.PI);
      const lon = Math.atan2(x, z) * (180 / Math.PI);
      dots.push({ x, y, z, land: isLand(lat, lon) });
    }
    return dots;
  }

  const SPHERE_DOTS = buildShihabSphereDots(860);

  class ShihabGlobeRenderer {
    constructor(canvas) {
      this.canvas = canvas;
      this.ctx = canvas.getContext("2d");
      this.mode = "3D"; // "3D" | "Shield" | "Orbit" | "2D"
      this.camLat = 24;
      this.camLon = -40;
      this.goalLat = 35;
      this.goalLon = -77;
      this.targetLat = 38.9;
      this.targetLon = -77.04;
      this.originLat = 23.81;
      this.originLon = 90.41;
      this.hasOrigin = false;
      this.connected = false;
      this.dragging = false;
      this.lastX = 0;
      this.lastY = 0;
      this.phase = 0;
      this.ringDeg = 0;

      this.bindDragEvents();
      this.startLoop();
    }

    bindDragEvents() {
      this.canvas.addEventListener("mousedown", (e) => {
        if (this.mode === "2D") return;
        this.dragging = true;
        this.lastX = e.clientX;
        this.lastY = e.clientY;
      });
      window.addEventListener("mousemove", (e) => {
        if (!this.dragging) return;
        const dx = e.clientX - this.lastX;
        const dy = e.clientY - this.lastY;
        this.lastX = e.clientX;
        this.lastY = e.clientY;
        this.camLon -= dx * 0.55;
        this.camLat = Math.max(-68, Math.min(68, this.camLat + dy * 0.45));
        this.goalLon = this.camLon;
        this.goalLat = this.camLat;
      });
      window.addEventListener("mouseup", () => {
        this.dragging = false;
      });
    }

    setMode(mode) {
      this.mode = mode || "3D";
    }

    setRoute(originLat, originLon, hasOrigin, targetLat, targetLon, connected) {
      const moved = Math.abs(this.targetLat - targetLat) > 0.3 || Math.abs(this.targetLon - targetLon) > 0.3;
      this.originLat = originLat;
      this.originLon = originLon;
      this.hasOrigin = !!hasOrigin;
      this.targetLat = targetLat;
      this.targetLon = targetLon;
      this.connected = !!connected;
      if (moved && !this.dragging) {
        this.goalLat = Math.max(-52, Math.min(56, targetLat * 0.78));
        this.goalLon = targetLon;
      }
    }

    startLoop() {
      const step = () => {
        this.phase = (this.phase + 0.032) % 1.0;
        this.ringDeg = (this.ringDeg + 1.5) % 360;
        if (!this.dragging) {
          if (this.mode === "Orbit") {
            this.camLon = ((this.camLon + 0.65 + 540) % 360) - 180;
            this.camLat += (18 - this.camLat) * 0.06;
          } else {
            let dLon = ((this.goalLon - this.camLon + 540) % 360) - 180;
            this.camLon += dLon * 0.14;
            this.camLat += (this.goalLat - this.camLat) * 0.14;
          }
        }
        this.render();
        requestAnimationFrame(step);
      };
      requestAnimationFrame(step);
    }

    project3D(x, y, z, cx, cy, R, cosYaw, sinYaw, cosPitch, sinPitch) {
      const x1 = x * cosYaw - z * sinYaw;
      const z1 = x * sinYaw + z * cosYaw;
      const y2 = y * cosPitch - z1 * sinPitch;
      const z2 = y * sinPitch + z1 * cosPitch;
      return { sx: cx + x1 * R, sy: cy - y2 * R, z: z2 };
    }

    latLonToUnit(lat, lon) {
      const la = (lat * Math.PI) / 180;
      const lo = (lon * Math.PI) / 180;
      const cl = Math.cos(la);
      return { x: cl * Math.sin(lo), y: Math.sin(la), z: cl * Math.cos(lo) };
    }

    render() {
      const dpr = window.devicePixelRatio || 1;
      const w = this.canvas.clientWidth || 300;
      const h = this.canvas.clientHeight || 128;
      if (this.canvas.width !== Math.round(w * dpr) || this.canvas.height !== Math.round(h * dpr)) {
        this.canvas.width = Math.round(w * dpr);
        this.canvas.height = Math.round(h * dpr);
      }
      const ctx = this.ctx;
      ctx.save();
      ctx.scale(dpr, dpr);
      ctx.clearRect(0, 0, w, h);

      if (this.mode === "2D") {
        this.renderFlat2D(ctx, w, h);
        ctx.restore();
        return;
      }

      const cx = w * 0.5;
      const cy = h * 0.5;
      const R = Math.min(w * 0.34, h * 0.44);

      const grad = ctx.createRadialGradient(cx - R * 0.25, cy - R * 0.25, R * 0.1, cx, cy, R * 1.15);
      grad.addColorStop(0, "#1D283A");
      grad.addColorStop(0.75, "#121822");
      grad.addColorStop(1, "rgba(18,24,34,0)");
      ctx.fillStyle = grad;
      ctx.beginPath();
      ctx.arc(cx, cy, R * 1.12, 0, Math.PI * 2);
      ctx.fill();

      ctx.strokeStyle = this.connected ? "rgba(16,185,129,0.55)" : "rgba(56,189,248,0.35)";
      ctx.lineWidth = 1.1;
      ctx.beginPath();
      ctx.arc(cx, cy, R, 0, Math.PI * 2);
      ctx.stroke();

      const yaw = (-this.camLon * Math.PI) / 180;
      const pitch = (this.camLat * Math.PI) / 180;
      const cyaw = Math.cos(yaw), syaw = Math.sin(yaw);
      const cpitch = Math.cos(pitch), spitch = Math.sin(pitch);

      for (let i = 0; i < SPHERE_DOTS.length; i++) {
        const d = SPHERE_DOTS[i];
        const p = this.project3D(d.x, d.y, d.z, cx, cy, R, cyaw, syaw, cpitch, spitch);
        if (p.z < -0.05) continue;
        if (!d.land) {
          if (p.z > 0.25 && i % 3 === 0) {
            ctx.fillStyle = "rgba(96,125,165,0.22)";
            ctx.fillRect(p.sx, p.sy, 1.1, 1.1);
          }
          continue;
        }
        ctx.fillStyle = this.connected
          ? (p.z > 0.55 ? "rgba(52,211,153,0.88)" : "rgba(56,189,248,0.55)")
          : (p.z > 0.55 ? "rgba(125,211,252,0.82)" : "rgba(100,132,172,0.48)");
        const rDot = p.z > 0.6 ? 1.55 : 1.15;
        ctx.beginPath();
        ctx.arc(p.sx, p.sy, rDot, 0, Math.PI * 2);
        ctx.fill();
      }

      if (this.mode === "Shield") {
        ctx.save();
        ctx.translate(cx, cy);
        ctx.rotate((this.ringDeg * Math.PI) / 180);
        ctx.strokeStyle = this.connected ? "rgba(52,211,153,0.45)" : "rgba(56,189,248,0.38)";
        ctx.lineWidth = 1.0;
        ctx.beginPath();
        ctx.ellipse(0, 0, R * 1.24, R * 0.36, 0.38, 0, Math.PI * 2);
        ctx.stroke();
        ctx.beginPath();
        ctx.ellipse(0, 0, R * 1.18, R * 0.32, -0.46, 0, Math.PI * 2);
        ctx.stroke();
        ctx.restore();
      }

      if (this.hasOrigin) {
        const uO = this.latLonToUnit(this.originLat, this.originLon);
        const uT = this.latLonToUnit(this.targetLat, this.targetLon);
        ctx.beginPath();
        let started = false;
        const pts = [];
        for (let s = 0; s <= 24; s++) {
          const t = s / 24;
          let ix = uO.x * (1 - t) + uT.x * t;
          let iy = uO.y * (1 - t) + uT.y * t;
          let iz = uO.z * (1 - t) + uT.z * t;
          const len = Math.hypot(ix, iy, iz) || 1;
          const lift = 1 + 0.14 * Math.sin(Math.PI * t);
          const pr = this.project3D((ix / len) * lift, (iy / len) * lift, (iz / len) * lift, cx, cy, R, cyaw, syaw, cpitch, spitch);
          pts.push(pr);
          if (pr.z > -0.15) {
            if (!started) {
              ctx.moveTo(pr.sx, pr.sy);
              started = true;
            } else {
              ctx.lineTo(pr.sx, pr.sy);
            }
          }
        }
        ctx.strokeStyle = this.connected ? "rgba(52,211,153,0.85)" : "rgba(56,189,248,0.55)";
        ctx.lineWidth = 1.5;
        ctx.stroke();

        if (this.connected && pts.length > 2) {
          const idx = Math.min(pts.length - 1, Math.floor(this.phase * pts.length));
          if (pts[idx].z > -0.1) {
            ctx.fillStyle = "#A7F3D0";
            ctx.beginPath();
            ctx.arc(pts[idx].sx, pts[idx].sy, 2.6, 0, Math.PI * 2);
            ctx.fill();
          }
        }
      }

      const uTarget = this.latLonToUnit(this.targetLat, this.targetLon);
      const pTarget = this.project3D(uTarget.x, uTarget.y, uTarget.z, cx, cy, R, cyaw, syaw, cpitch, spitch);
      if (pTarget.z >= -0.15) {
        this.drawBeacon(ctx, pTarget.sx, pTarget.sy);
      }

      ctx.restore();
    }

    renderFlat2D(ctx, w, h) {
      const mapX = (lon) => ((lon + 180) / 360) * (w - 16) + 8;
      const mapY = (lat) => ((84 - lat) / 146) * (h - 12) + 6;

      ctx.fillStyle = "#2A303B";
      ctx.strokeStyle = "#363E4C";
      ctx.lineWidth = 0.8;
      for (let i = 0; i < CONTINENT_POLYS.length; i++) {
        const poly = CONTINENT_POLYS[i];
        ctx.beginPath();
        for (let k = 0; k < poly.length; k++) {
          const px = mapX(poly[k][0]);
          const py = mapY(poly[k][1]);
          if (k === 0) ctx.moveTo(px, py);
          else ctx.lineTo(px, py);
        }
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
      }

      const tx = mapX(this.targetLon);
      const ty = mapY(this.targetLat);
      if (this.hasOrigin) {
        const ox = mapX(this.originLon);
        const oy = mapY(this.originLat);
        ctx.strokeStyle = this.connected ? "rgba(52,211,153,0.8)" : "rgba(56,189,248,0.5)";
        ctx.lineWidth = 1.4;
        ctx.beginPath();
        ctx.moveTo(ox, oy);
        ctx.quadraticCurveTo((ox + tx) * 0.5, Math.min(oy, ty) - 18, tx, ty);
        ctx.stroke();
      }
      this.drawBeacon(ctx, tx, ty);
    }

    drawBeacon(ctx, x, y) {
      const pulse = 7 + Math.sin(this.phase * Math.PI * 2) * 3;
      ctx.fillStyle = this.connected ? "rgba(16,185,129,0.28)" : "rgba(29,161,242,0.26)";
      ctx.beginPath();
      ctx.arc(x, y, pulse, 0, Math.PI * 2);
      ctx.fill();

      ctx.fillStyle = this.connected ? "#10B981" : "#38BDF8";
      ctx.strokeStyle = "#FFFFFF";
      ctx.lineWidth = 1.4;
      ctx.beginPath();
      ctx.arc(x, y, 4.0, 0, Math.PI * 2);
      ctx.fill();
      ctx.stroke();
    }
  }

  window.ShihabGlobeRenderer = ShihabGlobeRenderer;
})();

