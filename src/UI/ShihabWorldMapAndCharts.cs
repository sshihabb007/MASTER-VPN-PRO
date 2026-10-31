using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ShihabShieldVpn.Core;
using ShihabShieldVpn.Services;

namespace ShihabShieldVpn.UI
{
    public sealed class ShihabWorldMapControl : FrameworkElement
    {
        private struct ShihabSphereDot
        {
            public double X;
            public double Y;
            public double Z;
            public bool IsLand;
        }

        // Simplified continent polygons in normalized (lon: -180..180, lat: -60..80) coordinates
        private static readonly double[][][] ContinentPolygons = new double[][][]
        {
            // North America
            new double[][]
            {
                new double[] { -166, 66 }, new double[] { -140, 70 }, new double[] { -110, 72 },
                new double[] { -82, 68 }, new double[] { -62, 54 }, new double[] { -55, 47 },
                new double[] { -70, 41 }, new double[] { -80, 26 }, new double[] { -90, 20 },
                new double[] { -83, 10 }, new double[] { -98, 16 }, new double[] { -112, 28 },
                new double[] { -124, 42 }, new double[] { -135, 56 }, new double[] { -162, 58 }
            },
            // Greenland
            new double[][]
            {
                new double[] { -54, 60 }, new double[] { -44, 60 }, new double[] { -22, 70 },
                new double[] { -26, 80 }, new double[] { -56, 78 }, new double[] { -58, 68 }
            },
            // South America
            new double[][]
            {
                new double[] { -80, 10 }, new double[] { -62, 10 }, new double[] { -50, 1 },
                new double[] { -35, -7 }, new double[] { -39, -18 }, new double[] { -48, -28 },
                new double[] { -58, -38 }, new double[] { -68, -54 }, new double[] { -74, -50 },
                new double[] { -72, -32 }, new double[] { -71, -18 }, new double[] { -81, -5 }
            },
            // Europe
            new double[][]
            {
                new double[] { -10, 37 }, new double[] { -9, 43 }, new double[] { -4, 48 },
                new double[] { 3, 52 }, new double[] { 8, 56 }, new double[] { 5, 62 },
                new double[] { 16, 69 }, new double[] { 30, 70 }, new double[] { 42, 64 },
                new double[] { 40, 46 }, new double[] { 28, 41 }, new double[] { 22, 37 },
                new double[] { 14, 38 }, new double[] { 4, 41 }
            },
            // UK / Ireland
            new double[][]
            {
                new double[] { -8, 51 }, new double[] { -6, 58 }, new double[] { 1, 57 },
                new double[] { 2, 51 }
            },
            // Africa
            new double[][]
            {
                new double[] { -17, 15 }, new double[] { -6, 35 }, new double[] { 11, 37 },
                new double[] { 32, 31 }, new double[] { 43, 12 }, new double[] { 51, 11 },
                new double[] { 41, -4 }, new double[] { 35, -24 }, new double[] { 20, -35 },
                new double[] { 14, -24 }, new double[] { 9, -2 }, new double[] { -8, 5 }
            },
            // Asia
            new double[][]
            {
                new double[] { 36, 42 }, new double[] { 42, 65 }, new double[] { 72, 72 },
                new double[] { 112, 73 }, new double[] { 142, 66 }, new double[] { 168, 64 },
                new double[] { 155, 52 }, new double[] { 138, 44 }, new double[] { 129, 35 },
                new double[] { 122, 24 }, new double[] { 108, 12 }, new double[] { 102, 2 },
                new double[] { 96, 16 }, new double[] { 88, 22 }, new double[] { 78, 8 },
                new double[] { 68, 23 }, new double[] { 55, 24 }, new double[] { 44, 13 },
                new double[] { 35, 30 }
            },
            // Japan Archipelago
            new double[][]
            {
                new double[] { 131, 32 }, new double[] { 136, 35 }, new double[] { 141, 41 },
                new double[] { 145, 44 }, new double[] { 142, 38 }, new double[] { 135, 33 }
            },
            // Indonesia / Maritime SE Asia
            new double[][]
            {
                new double[] { 96, 5 }, new double[] { 106, -6 }, new double[] { 118, -8 },
                new double[] { 126, -5 }, new double[] { 116, 3 }, new double[] { 103, 4 }
            },
            // Australia
            new double[][]
            {
                new double[] { 114, -22 }, new double[] { 130, -12 }, new double[] { 145, -15 },
                new double[] { 153, -27 }, new double[] { 147, -38 }, new double[] { 132, -33 },
                new double[] { 115, -34 }
            }
        };

        private static readonly ShihabSphereDot[] ShihabSphereDots = BuildFibonacciSphereDots(1120);

        // Pre-frozen brushes for zero-allocation 3D rendering
        private static readonly SolidColorBrush OceanDotBrush = CreateFrozenBrush(45, 78, 108, 148);
        private static readonly SolidColorBrush LandDepth0Brush = CreateFrozenBrush(115, 72, 92, 118);
        private static readonly SolidColorBrush LandDepth1Brush = CreateFrozenBrush(165, 88, 116, 148);
        private static readonly SolidColorBrush LandDepth2Brush = CreateFrozenBrush(215, 110, 152, 196);
        private static readonly SolidColorBrush LandConnBrush = CreateFrozenBrush(225, 56, 189, 248);

        private double _shihabLat = 38.9;
        private double _shihabLon = -97.0;
        private double _originLat = 23.68;
        private double _originLon = 90.35;
        private bool _hasOriginPin = false;
        private bool _isConnected = false;

        private ShihabGlobeCameraMode _cameraMode = ShihabGlobeCameraMode.Globe3D;
        private double _camLatDeg = 26.0;
        private double _camLonDeg = -40.0;
        private double _goalLatDeg = 38.9;
        private double _goalLonDeg = -97.0;
        private bool _userDragging;
        private Point _lastDragPt;
        private double _animPhase;
        private double _shieldAngleDeg;
        private readonly DispatcherTimer _animTimer;

        public ShihabWorldMapControl()
        {
            Height = 148;
            IsHitTestVisible = true;
            Cursor = Cursors.Hand;
            SnapsToDevicePixels = true;
            ClipToBounds = true;

            _animTimer = new DispatcherTimer(DispatcherPriority.Render);
            _animTimer.Interval = TimeSpan.FromMilliseconds(55);
            _animTimer.Tick += OnGlobeAnimationTick;

            IsVisibleChanged += delegate
            {
                UpdateAnimationTimerState();
            };
        }

        private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
        {
            SolidColorBrush br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        private static ShihabSphereDot[] BuildFibonacciSphereDots(int count)
        {
            ShihabSphereDot[] arr = new ShihabSphereDot[count];
            double goldenAngle = Math.PI * (3.0 - Math.Sqrt(5.0));
            for (int i = 0; i < count; i++)
            {
                double y = 1.0 - (i / (double)(count - 1)) * 2.0;
                double radiusAtY = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
                double theta = goldenAngle * i;

                double x = Math.Cos(theta) * radiusAtY;
                double z = Math.Sin(theta) * radiusAtY;

                double latDeg = Math.Asin(Math.Max(-1.0, Math.Min(1.0, y))) * (180.0 / Math.PI);
                double lonDeg = Math.Atan2(x, z) * (180.0 / Math.PI);

                ShihabSphereDot dot = new ShihabSphereDot();
                dot.X = x;
                dot.Y = y;
                dot.Z = z;
                dot.IsLand = IsPointOnContinent(latDeg, lonDeg);
                arr[i] = dot;
            }
            return arr;
        }

        private static bool IsPointOnContinent(double lat, double lon)
        {
            for (int p = 0; p < ContinentPolygons.Length; p++)
            {
                double[][] poly = ContinentPolygons[p];
                if (PointInPolygon(lon, lat, poly))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool PointInPolygon(double x, double y, double[][] poly)
        {
            bool inside = false;
            int n = poly.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly[i][0];
                double yi = poly[i][1];
                double xj = poly[j][0];
                double yj = poly[j][1];

                bool intersect = ((yi > y) != (yj > y)) &&
                                 (x < (xj - xi) * (y - yi) / ((yj - yi) + 1e-9) + xi);
                if (intersect)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        public void SetShihabCameraMode(ShihabGlobeCameraMode mode)
        {
            _cameraMode = mode;
            Cursor = mode == ShihabGlobeCameraMode.FlatMap2D ? Cursors.Arrow : Cursors.Hand;
            UpdateAnimationTimerState();
            InvalidateVisual();
        }

        public void SetShihabPin(double latitude, double longitude, bool connected)
        {
            SetShihabRoute(_originLat, _originLon, _hasOriginPin, latitude, longitude, connected);
        }

        public void SetShihabRoute(
            double originLat,
            double originLon,
            bool hasOrigin,
            double targetLat,
            double targetLon,
            bool connected)
        {
            _originLat = originLat;
            _originLon = originLon;
            _hasOriginPin = hasOrigin;

            bool targetChanged = Math.Abs(_shihabLat - targetLat) > 0.25 || Math.Abs(_shihabLon - targetLon) > 0.25;
            _shihabLat = targetLat;
            _shihabLon = targetLon;
            _isConnected = connected;

            if (targetChanged && !_userDragging)
            {
                _goalLatDeg = Math.Max(-52.0, Math.Min(58.0, targetLat * 0.78));
                _goalLonDeg = targetLon;
            }

            UpdateAnimationTimerState();
            InvalidateVisual();
        }

        private void UpdateAnimationTimerState()
        {
            if (!IsVisible)
            {
                if (_animTimer.IsEnabled) _animTimer.Stop();
                return;
            }

            double dLon = NormalizeDeltaAngle(_goalLonDeg - _camLonDeg);
            double dLat = _goalLatDeg - _camLatDeg;
            bool cameraMoving = _cameraMode != ShihabGlobeCameraMode.FlatMap2D &&
                                (Math.Abs(dLon) > 0.35 || Math.Abs(dLat) > 0.35);

            bool continuousAnim = _isConnected ||
                                  _cameraMode == ShihabGlobeCameraMode.AutoOrbit3D ||
                                  _cameraMode == ShihabGlobeCameraMode.Shield3D ||
                                  cameraMoving;

            if (continuousAnim && !_animTimer.IsEnabled)
            {
                _animTimer.Start();
            }
            else if (!continuousAnim && _animTimer.IsEnabled)
            {
                _animTimer.Stop();
            }
        }

        private void OnGlobeAnimationTick(object sender, EventArgs e)
        {
            if (!IsVisible)
            {
                _animTimer.Stop();
                return;
            }

            _animPhase = (_animPhase + 0.026) % 1.0;
            _shieldAngleDeg = (_shieldAngleDeg + 1.8) % 360.0;

            if (!_userDragging && _cameraMode != ShihabGlobeCameraMode.FlatMap2D)
            {
                if (_cameraMode == ShihabGlobeCameraMode.AutoOrbit3D)
                {
                    _camLonDeg = NormalizeLongitude(_camLonDeg + 1.15);
                    _goalLonDeg = _camLonDeg;
                    _camLatDeg += (18.0 - _camLatDeg) * 0.08;
                }
                else
                {
                    double dLon = NormalizeDeltaAngle(_goalLonDeg - _camLonDeg);
                    double dLat = _goalLatDeg - _camLatDeg;
                    if (Math.Abs(dLon) > 0.25 || Math.Abs(dLat) > 0.25)
                    {
                        _camLonDeg = NormalizeLongitude(_camLonDeg + dLon * 0.16);
                        _camLatDeg += dLat * 0.16;
                    }
                    else
                    {
                        _camLonDeg = _goalLonDeg;
                        _camLatDeg = _goalLatDeg;
                    }
                }
            }

            UpdateAnimationTimerState();
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_cameraMode == ShihabGlobeCameraMode.FlatMap2D)
            {
                return;
            }
            _userDragging = true;
            _lastDragPt = e.GetPosition(this);
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_userDragging)
            {
                return;
            }
            Point cur = e.GetPosition(this);
            double dx = cur.X - _lastDragPt.X;
            double dy = cur.Y - _lastDragPt.Y;
            _lastDragPt = cur;

            _camLonDeg = NormalizeLongitude(_camLonDeg - dx * 0.62);
            _camLatDeg = Math.Max(-65.0, Math.Min(65.0, _camLatDeg + dy * 0.52));
            _goalLonDeg = _camLonDeg;
            _goalLatDeg = _camLatDeg;
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_userDragging)
            {
                _userDragging = false;
                Cursor = _cameraMode == ShihabGlobeCameraMode.FlatMap2D ? Cursors.Arrow : Cursors.Hand;
                ReleaseMouseCapture();
                UpdateAnimationTimerState();
                e.Handled = true;
            }
        }

        private static double NormalizeLongitude(double deg)
        {
            while (deg > 180.0) deg -= 360.0;
            while (deg < -180.0) deg += 360.0;
            return deg;
        }

        private static double NormalizeDeltaAngle(double delta)
        {
            while (delta > 180.0) delta -= 360.0;
            while (delta < -180.0) delta += 360.0;
            return delta;
        }

        private static void LatLonToUnitVector(double latDeg, double lonDeg, out double x, out double y, out double z)
        {
            double latRad = latDeg * (Math.PI / 180.0);
            double lonRad = lonDeg * (Math.PI / 180.0);
            double cosLat = Math.Cos(latRad);
            x = cosLat * Math.Sin(lonRad);
            y = Math.Sin(latRad);
            z = cosLat * Math.Cos(lonRad);
        }

        private void RotateAndProject3D(
            double x0,
            double y0,
            double z0,
            double cx,
            double cy,
            double radius,
            double cosLon,
            double sinLon,
            double cosLat,
            double sinLat,
            out Point screenPt,
            out double zDepth)
        {
            double x1 = x0 * cosLon - z0 * sinLon;
            double y1 = y0;
            double z1 = x0 * sinLon + z0 * cosLon;

            double x2 = x1;
            double y2 = y1 * cosLat - z1 * sinLat;
            zDepth = y1 * sinLat + z1 * cosLat;

            screenPt = new Point(cx + x2 * radius, cy - y2 * radius);
        }

        private static Point ProjectLatLon2D(double lat, double lon, double width, double height)
        {
            double clampedLon = Math.Max(-175.0, Math.Min(175.0, lon));
            double clampedLat = Math.Max(-58.0, Math.Min(78.0, lat));

            double nx = (clampedLon + 175.0) / 350.0;
            double ny = (78.0 - clampedLat) / 136.0;

            double padX = 8.0;
            double padY = 6.0;
            double x = padX + nx * Math.Max(10.0, width - padX * 2.0);
            double y = padY + ny * Math.Max(10.0, height - padY * 2.0);
            return new Point(x, y);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 20 || h < 20)
            {
                return;
            }

            // Hit-test surface for interactive globe drag
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

            if (_cameraMode == ShihabGlobeCameraMode.FlatMap2D)
            {
                RenderFlatMap2D(dc, w, h);
            }
            else
            {
                RenderInteractiveGlobe3D(dc, w, h);
            }
        }

        private void RenderInteractiveGlobe3D(DrawingContext dc, double w, double h)
        {
            double cx = w * 0.5;
            double cy = h * 0.50;
            double r = Math.Min(w * 0.44, h * 0.46);

            // Subtle deep-space sphere core + cyan/emerald atmosphere halo
            RadialGradientBrush sphereBg = new RadialGradientBrush(
                Color.FromRgb(29, 39, 54),
                Color.FromRgb(16, 20, 28));
            sphereBg.GradientOrigin = new Point(0.36, 0.32);
            sphereBg.Freeze();

            Color rimColor = _isConnected ? Color.FromArgb(120, 16, 185, 129) : Color.FromArgb(95, 56, 189, 248);
            Pen rimPen = new Pen(new SolidColorBrush(rimColor), 1.15);
            rimPen.Freeze();

            SolidColorBrush outerAtmos = new SolidColorBrush(
                _isConnected ? Color.FromArgb(22, 16, 185, 129) : Color.FromArgb(18, 29, 161, 242));
            outerAtmos.Freeze();

            dc.DrawEllipse(outerAtmos, null, new Point(cx, cy), r + 6.5, r + 6.5);
            dc.DrawEllipse(sphereBg, rimPen, new Point(cx, cy), r, r);

            double lonRad = _camLonDeg * (Math.PI / 180.0);
            double latRad = _camLatDeg * (Math.PI / 180.0);
            double cosLon = Math.Cos(lonRad);
            double sinLon = Math.Sin(lonRad);
            double cosLat = Math.Cos(latRad);
            double sinLat = Math.Sin(latRad);

            // Back-half Shield Orbital Rings (behind globe)
            if (_cameraMode == ShihabGlobeCameraMode.Shield3D)
            {
                DrawShieldOrbitalRings(dc, cx, cy, r * 1.18, false);
            }

            // Fibonacci sphere land & ocean dots
            for (int i = 0; i < ShihabSphereDots.Length; i++)
            {
                ShihabSphereDot dot = ShihabSphereDots[i];
                Point pt;
                double z;
                RotateAndProject3D(dot.X, dot.Y, dot.Z, cx, cy, r, cosLon, sinLon, cosLat, sinLat, out pt, out z);
                if (z < -0.02)
                {
                    continue;
                }

                if (!dot.IsLand)
                {
                    if ((i % 3) == 0 && z > 0.15)
                    {
                        dc.DrawEllipse(OceanDotBrush, null, pt, 0.75, 0.75);
                    }
                    continue;
                }

                SolidColorBrush landBrush;
                double dotRadius;
                if (z > 0.68)
                {
                    landBrush = _isConnected ? LandConnBrush : LandDepth2Brush;
                    dotRadius = 1.55;
                }
                else if (z > 0.34)
                {
                    landBrush = LandDepth1Brush;
                    dotRadius = 1.30;
                }
                else
                {
                    landBrush = LandDepth0Brush;
                    dotRadius = 1.05;
                }
                dc.DrawEllipse(landBrush, null, pt, dotRadius, dotRadius);
            }

            // Origin -> Target 3D Elevated Route Arc & Traveling Packets
            bool distinctOrigin = _hasOriginPin &&
                                  (Math.Abs(_originLat - _shihabLat) > 1.2 || Math.Abs(_originLon - _shihabLon) > 1.2);
            if (distinctOrigin)
            {
                Draw3DRouteArc(dc, cx, cy, r, cosLon, sinLon, cosLat, sinLat);
            }

            // Origin ISP Pin (amber) if visible on front hemisphere
            if (distinctOrigin)
            {
                double ox, oy, oz;
                LatLonToUnitVector(_originLat, _originLon, out ox, out oy, out oz);
                Point originPt;
                double originZ;
                RotateAndProject3D(ox, oy, oz, cx, cy, r, cosLon, sinLon, cosLat, sinLat, out originPt, out originZ);
                if (originZ > -0.08)
                {
                    SolidColorBrush origGlow = CreateFrozenBrush(70, 245, 158, 11);
                    SolidColorBrush origCore = CreateFrozenBrush(235, 251, 191, 36);
                    dc.DrawEllipse(origGlow, null, originPt, 5.5, 5.5);
                    dc.DrawEllipse(origCore, null, originPt, 2.4, 2.4);
                }
            }

            // Active / Selected Target Beacon
            double tx, ty, tz;
            LatLonToUnitVector(_shihabLat, _shihabLon, out tx, out ty, out tz);
            Point targetPt;
            double targetZ;
            RotateAndProject3D(tx, ty, tz, cx, cy, r, cosLon, sinLon, cosLat, sinLat, out targetPt, out targetZ);
            if (targetZ > -0.15)
            {
                DrawTargetBeacon(dc, targetPt);
            }

            // Front-half Shield Orbital Rings (in front of globe)
            if (_cameraMode == ShihabGlobeCameraMode.Shield3D)
            {
                DrawShieldOrbitalRings(dc, cx, cy, r * 1.18, true);
            }
        }

        private void Draw3DRouteArc(
            DrawingContext dc,
            double cx,
            double cy,
            double r,
            double cosLon,
            double sinLon,
            double cosLat,
            double sinLat)
        {
            double ux0, uy0, uz0;
            double ux1, uy1, uz1;
            LatLonToUnitVector(_originLat, _originLon, out ux0, out uy0, out uz0);
            LatLonToUnitVector(_shihabLat, _shihabLon, out ux1, out uy1, out uz1);

            const int steps = 26;
            Point[] pts = new Point[steps + 1];
            bool[] vis = new bool[steps + 1];

            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                double ix = ux0 + (ux1 - ux0) * t;
                double iy = uy0 + (uy1 - uy0) * t;
                double iz = uz0 + (uz1 - uz0) * t;
                double len = Math.Sqrt(ix * ix + iy * iy + iz * iz);
                if (len < 1e-6) len = 1.0;

                double alt = 1.0 + 0.22 * Math.Sin(Math.PI * t);
                double ex = (ix / len) * alt;
                double ey = (iy / len) * alt;
                double ez = (iz / len) * alt;

                Point p;
                double z;
                RotateAndProject3D(ex, ey, ez, cx, cy, r, cosLon, sinLon, cosLat, sinLat, out p, out z);
                pts[i] = p;
                vis[i] = z > -0.18;
            }

            Color arcCol = _isConnected ? Color.FromArgb(190, 52, 211, 153) : Color.FromArgb(135, 56, 189, 248);
            Pen arcPen = new Pen(new SolidColorBrush(arcCol), _isConnected ? 1.65 : 1.2);
            if (!_isConnected)
            {
                arcPen.DashStyle = new DashStyle(new double[] { 2.5, 2.5 }, 0);
            }
            arcPen.Freeze();

            for (int i = 0; i < steps; i++)
            {
                if (vis[i] && vis[i + 1])
                {
                    dc.DrawLine(arcPen, pts[i], pts[i + 1]);
                }
            }

            if (_isConnected)
            {
                SolidColorBrush pktGlow = CreateFrozenBrush(95, 52, 211, 153);
                SolidColorBrush pktCore = CreateFrozenBrush(255, 236, 253, 245);
                for (int k = 0; k < 3; k++)
                {
                    double t = (_animPhase + k * 0.3333) % 1.0;
                    double idxF = t * steps;
                    int idx0 = Math.Min(steps - 1, (int)idxF);
                    double frac = idxF - idx0;
                    if (vis[idx0] && vis[idx0 + 1])
                    {
                        Point pp = new Point(
                            pts[idx0].X + (pts[idx0 + 1].X - pts[idx0].X) * frac,
                            pts[idx0].Y + (pts[idx0 + 1].Y - pts[idx0].Y) * frac);
                        dc.DrawEllipse(pktGlow, null, pp, 4.4, 4.4);
                        dc.DrawEllipse(pktCore, null, pp, 1.85, 1.85);
                    }
                }
            }
        }

        private void DrawShieldOrbitalRings(DrawingContext dc, double cx, double cy, double orbitR, bool frontHalf)
        {
            Color ringCol = _isConnected ? Color.FromArgb(frontHalf ? (byte)155 : (byte)55, 52, 211, 153)
                                         : Color.FromArgb(frontHalf ? (byte)135 : (byte)45, 56, 189, 248);
            Pen orbitPen = new Pen(new SolidColorBrush(ringCol), frontHalf ? 1.25 : 0.9);
            orbitPen.DashStyle = new DashStyle(new double[] { 3.0, 3.0 }, _shieldAngleDeg * 0.1);
            orbitPen.Freeze();

            double[] tilts = new double[] { 26.0, -32.0 };
            for (int rIdx = 0; rIdx < tilts.Length; rIdx++)
            {
                double tiltRad = tilts[rIdx] * (Math.PI / 180.0);
                double spinOffset = (_shieldAngleDeg + rIdx * 110.0) * (Math.PI / 180.0);

                Point prev = new Point();
                bool hasPrev = false;
                for (int s = 0; s <= 40; s++)
                {
                    double a = (s / 40.0) * Math.PI * 2.0 + spinOffset;
                    double x = Math.Cos(a);
                    double z = Math.Sin(a);
                    double y = z * Math.Sin(tiltRad) * 0.45;
                    bool isFront = z >= 0.0;

                    Point cur = new Point(
                        cx + (x * Math.Cos(tiltRad * 0.4) - y * Math.Sin(tiltRad * 0.4)) * orbitR,
                        cy - (x * Math.Sin(tiltRad * 0.4) + y * Math.Cos(tiltRad * 0.4)) * orbitR);

                    if (isFront == frontHalf)
                    {
                        if (hasPrev)
                        {
                            dc.DrawLine(orbitPen, prev, cur);
                        }
                        prev = cur;
                        hasPrev = true;
                    }
                    else
                    {
                        hasPrev = false;
                    }
                }
            }
        }

        private void RenderFlatMap2D(DrawingContext dc, double w, double h)
        {
            SolidColorBrush landFill = new SolidColorBrush(Color.FromRgb(43, 47, 55));
            Pen landStroke = new Pen(new SolidColorBrush(Color.FromRgb(54, 59, 69)), 0.7);
            landFill.Freeze();
            landStroke.Freeze();

            for (int p = 0; p < ContinentPolygons.Length; p++)
            {
                double[][] poly = ContinentPolygons[p];
                if (poly.Length < 3) continue;

                StreamGeometry geom = new StreamGeometry();
                using (StreamGeometryContext ctx = geom.Open())
                {
                    ctx.BeginFigure(ProjectLatLon2D(poly[0][1], poly[0][0], w, h), true, true);
                    for (int i = 1; i < poly.Length; i++)
                    {
                        ctx.LineTo(ProjectLatLon2D(poly[i][1], poly[i][0], w, h), true, true);
                    }
                }
                geom.Freeze();
                dc.DrawGeometry(landFill, landStroke, geom);
            }

            double[][] hubs = new double[][]
            {
                new double[] { 38.9, -97.0 },
                new double[] { 51.2, 10.4 },
                new double[] { 36.2, 138.2 },
                new double[] { 36.5, 127.8 },
                new double[] { 1.35, 103.8 }
            };
            SolidColorBrush dimHubBrush = new SolidColorBrush(Color.FromArgb(110, 90, 104, 124));
            dimHubBrush.Freeze();
            for (int i = 0; i < hubs.Length; i++)
            {
                Point hp = ProjectLatLon2D(hubs[i][0], hubs[i][1], w, h);
                dc.DrawEllipse(dimHubBrush, null, hp, 2.0, 2.0);
            }

            Point targetPt = ProjectLatLon2D(_shihabLat, _shihabLon, w, h);

            if (_hasOriginPin && (Math.Abs(_originLat - _shihabLat) > 1.2 || Math.Abs(_originLon - _shihabLon) > 1.2))
            {
                Point originPt = ProjectLatLon2D(_originLat, _originLon, w, h);
                Point ctrlPt = new Point(
                    (originPt.X + targetPt.X) * 0.5,
                    Math.Min(originPt.Y, targetPt.Y) - Math.Min(32.0, Math.Abs(targetPt.X - originPt.X) * 0.22));

                Color arcCol = _isConnected ? Color.FromArgb(185, 52, 211, 153) : Color.FromArgb(130, 56, 189, 248);
                Pen arcPen = new Pen(new SolidColorBrush(arcCol), 1.5);
                if (!_isConnected)
                {
                    arcPen.DashStyle = new DashStyle(new double[] { 3.0, 2.5 }, 0);
                }
                arcPen.Freeze();

                StreamGeometry arcGeom = new StreamGeometry();
                using (StreamGeometryContext ctx = arcGeom.Open())
                {
                    ctx.BeginFigure(originPt, false, false);
                    ctx.QuadraticBezierTo(ctrlPt, targetPt, true, true);
                }
                arcGeom.Freeze();
                dc.DrawGeometry(null, arcPen, arcGeom);

                SolidColorBrush origCore = CreateFrozenBrush(230, 251, 191, 36);
                dc.DrawEllipse(origCore, null, originPt, 2.5, 2.5);

                if (_isConnected)
                {
                    SolidColorBrush pktCore = CreateFrozenBrush(255, 236, 253, 245);
                    for (int k = 0; k < 2; k++)
                    {
                        double t = (_animPhase + k * 0.5) % 1.0;
                        double omt = 1.0 - t;
                        Point bp = new Point(
                            omt * omt * originPt.X + 2.0 * omt * t * ctrlPt.X + t * t * targetPt.X,
                            omt * omt * originPt.Y + 2.0 * omt * t * ctrlPt.Y + t * t * targetPt.Y);
                        dc.DrawEllipse(pktCore, null, bp, 2.0, 2.0);
                    }
                }
            }

            DrawTargetBeacon(dc, targetPt);
        }

        private void DrawTargetBeacon(DrawingContext dc, Point targetPt)
        {
            Color coreColor = _isConnected
                ? Color.FromRgb(16, 185, 129)
                : Color.FromRgb(56, 189, 248);

            double pulseBoost = _isConnected ? (Math.Sin(_animPhase * Math.PI * 2.0) * 2.2) : 0.0;
            SolidColorBrush outerGlow = new SolidColorBrush(Color.FromArgb(_isConnected ? (byte)64 : (byte)38, coreColor.R, coreColor.G, coreColor.B));
            SolidColorBrush midGlow = new SolidColorBrush(Color.FromArgb(_isConnected ? (byte)125 : (byte)80, coreColor.R, coreColor.G, coreColor.B));
            SolidColorBrush coreBrush = new SolidColorBrush(coreColor);
            Pen whiteRing = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 1.0);
            outerGlow.Freeze();
            midGlow.Freeze();
            coreBrush.Freeze();
            whiteRing.Freeze();

            double outerR = (_isConnected ? 13.0 : 9.0) + pulseBoost;
            dc.DrawEllipse(outerGlow, null, targetPt, outerR, outerR);
            dc.DrawEllipse(midGlow, null, targetPt, _isConnected ? 7.0 : 5.2, _isConnected ? 7.0 : 5.2);
            dc.DrawEllipse(coreBrush, whiteRing, targetPt, 3.5, 3.5);
        }
    }

    public sealed class ShihabDailyDataChartControl : FrameworkElement
    {
        private List<ShihabHourlyTrafficBucket> _buckets = new List<ShihabHourlyTrafficBucket>();

        public ShihabDailyDataChartControl()
        {
            Height = 108;
            SnapsToDevicePixels = true;
        }

        public void UpdateShihabBuckets(IReadOnlyList<ShihabHourlyTrafficBucket> buckets)
        {
            _buckets = new List<ShihabHourlyTrafficBucket>();
            if (buckets != null)
            {
                for (int i = 0; i < buckets.Count; i++)
                {
                    if (buckets[i] != null)
                    {
                        _buckets.Add(buckets[i]);
                    }
                }
            }
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth;
            double h = ActualHeight;
            if (w < 40 || h < 40)
            {
                return;
            }

            double rightLabelWidth = 34.0;
            double chartW = Math.Max(20.0, w - rightLabelWidth - 4.0);
            double topPad = 8.0;
            double botPad = 8.0;
            double chartH = Math.Max(20.0, h - topPad - botPad);

            long maxBytes = 0;
            for (int i = 0; i < _buckets.Count; i++)
            {
                long tot = Math.Max(_buckets[i].DownloadBytes, _buckets[i].UploadBytes);
                if (tot > maxBytes) maxBytes = tot;
            }

            double unitDivisor = 1024.0; // KB default
            double topScaleUnits = 400.0;
            string topText = "400";
            string midText = "200";

            double maxKb = maxBytes / 1024.0;
            if (maxKb > 400.0 && maxKb <= 2048.0)
            {
                topScaleUnits = 2000.0;
                topText = "2M";
                midText = "1M";
            }
            else if (maxKb > 2048.0)
            {
                unitDivisor = 1024.0 * 1024.0; // MB
                double maxMb = maxBytes / unitDivisor;
                if (maxMb <= 50.0)
                {
                    topScaleUnits = 50.0;
                    topText = "50M";
                    midText = "25M";
                }
                else if (maxMb <= 200.0)
                {
                    topScaleUnits = 200.0;
                    topText = "200M";
                    midText = "100M";
                }
                else
                {
                    topScaleUnits = Math.Ceiling(maxMb / 100.0) * 100.0;
                    topText = ((int)topScaleUnits) + "M";
                    midText = ((int)(topScaleUnits / 2.0)) + "M";
                }
            }

            Pen gridPen = new Pen(new SolidColorBrush(Color.FromRgb(42, 46, 54)), 1.0);
            SolidColorBrush axisTextBrush = new SolidColorBrush(Color.FromRgb(142, 150, 164));
            SolidColorBrush dlBarBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
            SolidColorBrush ulBarBrush = new SolidColorBrush(Color.FromRgb(29, 122, 252));
            gridPen.Freeze();
            axisTextBrush.Freeze();
            dlBarBrush.Freeze();
            ulBarBrush.Freeze();

            Typeface tf = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            string[] labels = new string[] { topText, midText, "0" };
            for (int g = 0; g < 3; g++)
            {
                double gy = Math.Round(topPad + (chartH * g / 2.0)) + 0.5;
                dc.DrawLine(gridPen, new Point(0, gy), new Point(chartW, gy));

                FormattedText ft = new FormattedText(
                    labels[g],
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    tf,
                    11.0,
                    axisTextBrush,
                    dpi);
                dc.DrawText(ft, new Point(chartW + 8.0, gy - ft.Height * 0.5));
            }

            int slots = _buckets.Count > 0 ? _buckets.Count : 24;
            double slotW = chartW / slots;
            double barW = Math.Max(2.2, Math.Min(4.5, slotW * 0.36));
            double baseY = topPad + chartH;

            for (int i = 0; i < _buckets.Count; i++)
            {
                ShihabHourlyTrafficBucket b = _buckets[i];
                double slotLeft = i * slotW;

                if (b.DownloadBytes > 0)
                {
                    double dlUnits = b.DownloadBytes / unitDivisor;
                    double dlRatio = Math.Min(1.0, dlUnits / topScaleUnits);
                    double bh = Math.Max(2.5, dlRatio * chartH);
                    dc.DrawRoundedRectangle(dlBarBrush, null, new Rect(slotLeft + 1.0, baseY - bh, barW, bh), 1.2, 1.2);
                }

                if (b.UploadBytes > 0)
                {
                    double ulUnits = b.UploadBytes / unitDivisor;
                    double ulRatio = Math.Min(1.0, ulUnits / topScaleUnits);
                    double bh = Math.Max(2.5, ulRatio * chartH);
                    dc.DrawRoundedRectangle(ulBarBrush, null, new Rect(slotLeft + 1.5 + barW, baseY - bh, barW, bh), 1.2, 1.2);
                }
            }
        }
    }

    public static class ShihabFlagBadgeFactory
    {
        public static Border CreateCircularFlagBadge(string countryShort, double diameter)
        {
            string code = string.IsNullOrWhiteSpace(countryShort) ? "US" : countryShort.Trim().ToUpperInvariant();
            if (code == "AUTO") code = "US";
            if (code == "UK") code = "GB";

            Color topCol;
            Color botCol;
            ResolveCountryBadgePalette(code, out topCol, out botCol);

            LinearGradientBrush grad = new LinearGradientBrush(topCol, botCol, 90.0);

            Border badge = new Border();
            badge.Width = diameter;
            badge.Height = diameter;
            badge.CornerRadius = new CornerRadius(diameter * 0.5);
            badge.Background = grad;
            badge.BorderBrush = new SolidColorBrush(Color.FromArgb(95, 255, 255, 255));
            badge.BorderThickness = new Thickness(1.0);

            TextBlock txt = new TextBlock();
            txt.Text = code.Length > 2 ? code.Substring(0, 2) : code;
            txt.FontSize = diameter * 0.39;
            txt.FontWeight = FontWeights.Bold;
            txt.Foreground = (code == "JP" || code == "KR" || code == "PL" || code == "SG" || code == "ID")
                ? new SolidColorBrush(Color.FromRgb(15, 23, 42))
                : Brushes.White;
            txt.HorizontalAlignment = HorizontalAlignment.Center;
            txt.VerticalAlignment = VerticalAlignment.Center;

            badge.Child = txt;
            return badge;
        }

        private static void ResolveCountryBadgePalette(string code, out Color topCol, out Color botCol)
        {
            switch (code)
            {
                case "US":
                    topCol = Color.FromRgb(37, 99, 235);
                    botCol = Color.FromRgb(220, 38, 38);
                    return;
                case "GB":
                    topCol = Color.FromRgb(30, 64, 175);
                    botCol = Color.FromRgb(220, 38, 38);
                    return;
                case "CA":
                case "AT":
                case "PE":
                case "DK":
                case "CH":
                case "HK":
                case "VN":
                case "TR":
                case "CN":
                    topCol = Color.FromRgb(220, 38, 38);
                    botCol = Color.FromRgb(153, 27, 27);
                    return;
                case "JP":
                    topCol = Color.FromRgb(248, 250, 252);
                    botCol = Color.FromRgb(225, 29, 72);
                    return;
                case "KR":
                    topCol = Color.FromRgb(241, 245, 249);
                    botCol = Color.FromRgb(37, 99, 235);
                    return;
                case "SG":
                case "ID":
                    topCol = Color.FromRgb(239, 68, 68);
                    botCol = Color.FromRgb(248, 250, 252);
                    return;
                case "PL":
                    topCol = Color.FromRgb(248, 250, 252);
                    botCol = Color.FromRgb(220, 38, 38);
                    return;
                case "DE":
                case "BE":
                    topCol = Color.FromRgb(30, 41, 59);
                    botCol = Color.FromRgb(234, 179, 8);
                    return;
                case "FR":
                case "NL":
                case "RU":
                case "TH":
                case "HR":
                case "CZ":
                case "NO":
                case "TW":
                case "CL":
                case "MY":
                case "PH":
                    topCol = Color.FromRgb(37, 99, 235);
                    botCol = Color.FromRgb(239, 68, 68);
                    return;
                case "AU":
                case "NZ":
                case "FI":
                case "GR":
                case "EE":
                case "IS":
                case "IL":
                    topCol = Color.FromRgb(2, 132, 199);
                    botCol = Color.FromRgb(30, 58, 138);
                    return;
                case "SE":
                case "UA":
                case "KZ":
                    topCol = Color.FromRgb(2, 132, 199);
                    botCol = Color.FromRgb(234, 179, 8);
                    return;
                case "ES":
                case "RO":
                case "CO":
                case "EC":
                case "VE":
                    topCol = Color.FromRgb(234, 179, 8);
                    botCol = Color.FromRgb(220, 38, 38);
                    return;
                case "IT":
                case "MX":
                case "HU":
                case "BG":
                case "IR":
                    topCol = Color.FromRgb(22, 163, 74);
                    botCol = Color.FromRgb(220, 38, 38);
                    return;
                case "IN":
                case "IE":
                    topCol = Color.FromRgb(249, 115, 22);
                    botCol = Color.FromRgb(22, 163, 74);
                    return;
                case "BD":
                case "PT":
                case "PK":
                    topCol = Color.FromRgb(21, 128, 61);
                    botCol = Color.FromRgb(220, 38, 38);
                    return;
                case "BR":
                    topCol = Color.FromRgb(22, 163, 74);
                    botCol = Color.FromRgb(234, 179, 8);
                    return;
                case "AR":
                case "UY":
                    topCol = Color.FromRgb(56, 189, 248);
                    botCol = Color.FromRgb(2, 132, 199);
                    return;
                case "AE":
                case "SA":
                case "ZA":
                case "NG":
                    topCol = Color.FromRgb(16, 185, 129);
                    botCol = Color.FromRgb(15, 118, 110);
                    return;
                case "WG":
                    topCol = Color.FromRgb(136, 23, 26);
                    botCol = Color.FromRgb(185, 28, 28);
                    return;
                case "OV":
                    topCol = Color.FromRgb(234, 88, 12);
                    botCol = Color.FromRgb(194, 65, 12);
                    return;
                case "PX":
                    topCol = Color.FromRgb(124, 58, 237);
                    botCol = Color.FromRgb(14, 165, 233);
                    return;
                case "PV":
                    topCol = Color.FromRgb(79, 70, 229);
                    botCol = Color.FromRgb(30, 64, 175);
                    return;
                default:
                    topCol = Color.FromRgb(29, 161, 242);
                    botCol = Color.FromRgb(14, 116, 210);
                    return;
            }
        }
    }
}
