using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;
using Microsoft.Win32;
using ShihabShieldVpn.Core;
using ShihabShieldVpn.Services;

namespace ShihabShieldVpn.UI
{
    public sealed class ShihabMainWindow : Window
    {
        private readonly ShihabConfigService _shihabConfigService;
        private readonly ShihabPublicRelayService _shihabRelayService;
        private readonly ShihabVpnEngine _shihabVpnEngine;
        private readonly ShihabSecurityShieldService _shihabShieldService;
        private readonly ShihabTelemetryAndSpeedService _shihabTelemetryService;
        private readonly ShihabTrayManager _shihabTray;

        private ShihabAppConfig _shihabConfig;
        private List<ShihabVpnServerNode> _shihabPublicNodes = new List<ShihabVpnServerNode>();
        private ShihabVpnServerNode _shihabResolvedTarget;
        private ShihabConnectionState _shihabState = ShihabConnectionState.Disconnected;
        private DateTime _shihabConnectedSinceUtc = DateTime.MinValue;
        private CancellationTokenSource _shihabConnectCts;
        private CancellationTokenSource _shihabSpeedCts;
        private DispatcherTimer _shihabUiTimer;
        private int _shihabTickCounter;
        private bool _shihabExplicitExitRequested;
        private bool _shihabSpeedTestRunning;
        private bool _shihabGeoVerifyRunning;
        private string _shihabObservedPublicIp = string.Empty;
        private ShihabGeoIpReport _shihabOriginGeo;
        private ShihabGeoIpReport _shihabCurrentGeo;
        private readonly List<ShihabEngineLogEntry> _shihabEngineLogs = new List<ShihabEngineLogEntry>();
        private string _activeLogCategoryFilter = "ALL";
        private string _activeRegionFilter = "ALL";
        private ShihabBrowserCompanionService _shihabBrowserCompanion;

        // Navigation & Tab Views
        private int _activeNavIndex;
        private readonly List<Border> _navIndicatorBars = new List<Border>();
        private readonly List<ShapePath> _navIcons = new List<ShapePath>();
        private readonly List<UIElement> _tabViews = new List<UIElement>();

        // Home Dashboard Dynamic Controls
        private TextBlock _headerStatusPillText;
        private Border _headerStatusPillBorder;
        private Border _pinToolbarBtn;
        private Border _virtualFlagContainer;
        private TextBlock _virtualLocationTitleText;
        private TextBlock _virtualLocationSubText;
        private ShihabWorldMapControl _worldMapControl;
        private readonly List<Border> _cameraModePills = new List<Border>();
        private TextBlock _rotateIpActionText;

        private TextBlock _serverProtocolBadgeText;
        private TextBlock _serverModeBadgeText;
        private TextBlock _serverIpValueText;
        private TextBlock _serverLoadValueText;
        private TextBlock _serverLatencyValueText;
        private TextBlock _serverConnectedTimerText;

        private TextBlock _dailyDataTotalText;
        private ShihabDailyDataChartControl _dailyChartControl;
        private TextBlock _dailyDownloadText;
        private TextBlock _dailyUploadText;

        private TextBlock _peakSpeedValueText;
        private TextBlock _testSpeedActionText;
        private Border _peakSpeedBarFill;
        private Grid _peakSpeedBarTrackGrid;

        private TextBlock _networkTitleText;
        private TextBlock _networkSubText;
        private TextBlock _networkDnsBadgeText;

        private Border _heroConnectOrb;
        private Border _heroConnectOuterHalo;
        private Grid _heroConnectGlyphHost;
        private TextBlock _bottomBannerStatusText;

        // Servers View Controls
        private int _serverBrowserMode; // 0 = Countries Hub, 1 = All Individual Nodes, 2 = Private Servers
        private Button _btnModeCountries;
        private Button _btnModeNodes;
        private Button _btnModePrivate;
        private Button _btnRegAll;
        private Button _btnRegAmericas;
        private Button _btnRegEmea;
        private Button _btnRegApac;
        private Button _btnFilterFast4k;
        private Button _btnFilterExtended;
        private TextBox _serverSearchBox;
        private StackPanel _serverListStack;
        private TextBlock _serverCountSummaryText;
        private Border _privateEditorDrawer;
        private TextBox _privNameInput;
        private ComboBox _privProtoCombo;
        private TextBox _privHostInput;
        private TextBox _privUserInput;
        private PasswordBox _privPassInput;
        private PasswordBox _privPskInput;

        // Speed, GeoIP Audit & Logs View Controls
        private int _diagSubTabMode; // 0 = Speed & Live IP Audit, 1 = Engine Activity Logs
        private Button _btnDiagTabAudit;
        private Button _btnDiagTabLogs;
        private UIElement _diagAuditPanel;
        private UIElement _diagLogsPanel;
        private TextBlock _diagSpeedBigText;
        private TextBlock _diagPingBigText;
        private TextBlock _diagJitterBigText;
        private TextBlock _diagPublicIpBigText;
        private TextBlock _diagEndpointText;
        private Button _diagRunSpeedBtn;
        private Button _diagVerifyGeoBtn;
        private Border _geoAuditBadgeBorder;
        private TextBlock _geoAuditBadgeText;
        private TextBlock _geoRouteValueText;
        private TextBlock _geoIspAsnValueText;
        private TextBlock _geoClockValueText;
        private StackPanel _engineLogsStack;
        private readonly Dictionary<string, Button> _logFilterButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

        // Settings View Controls
        private CheckBox _chkDynamicIpRotation;
        private CheckBox _chkExtendedCatalog;
        private CheckBox _chkAutoReconnectKs;
        private CheckBox _chkStrictFirewallKs;
        private ComboBox _cmbDnsPreset;
        private ComboBox _cmbPublicProto;
        private CheckBox _chkSplitTunnel;
        private TextBox _txtSplitBypassApps;
        private CheckBox _chkMinimizeTray;
        private CheckBox _chkAutoConnectStartup;
        private CheckBox _chkDisconnectOnExit;

        public ShihabMainWindow(
            ShihabConfigService configService,
            ShihabPublicRelayService relayService,
            ShihabVpnEngine vpnEngine,
            ShihabSecurityShieldService shieldService,
            ShihabTelemetryAndSpeedService telemetryService,
            ShihabTrayManager trayManager)
        {
            _shihabConfigService = configService;
            _shihabRelayService = relayService;
            _shihabVpnEngine = vpnEngine;
            _shihabShieldService = shieldService;
            _shihabTelemetryService = telemetryService;
            _shihabTray = trayManager;

            _shihabConfig = _shihabConfigService.LoadShihabConfig();
            _shihabOriginGeo = _shihabConfig.OriginGeoCache;

            Title = "Shihab Shield VPN";
            Width = 852;
            Height = 592;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.CanMinimize;
            Background = Brushes.Transparent;
            Topmost = _shihabConfig.AlwaysOnTop;
            FontFamily = new FontFamily("Segoe UI");

            BuildShihabWindowLayout();
            BindShihabTrayEvents();
        }

        public void InitializeShihabRuntime(bool isPreviewCapture = false)
        {
            bool alreadyActive = _shihabVpnEngine.IsShihabTunnelCurrentlyActive();
            _shihabShieldService.RecoverOrphanedShihabKillSwitchRules(_shihabConfig, alreadyActive);
            _shihabVpnEngine.RecoverOrphanedShihabSystemProxy(_shihabConfig, alreadyActive);

            IReadOnlyList<ShihabVpnServerNode> cached = _shihabRelayService.GetCurrentPublicRelays();
            _shihabPublicNodes = new List<ShihabVpnServerNode>(cached);
            _shihabResolvedTarget = _shihabRelayService.ResolveBestShihabTargetNode(_shihabConfig, _shihabPublicNodes);

            if (_worldMapControl != null)
            {
                _worldMapControl.SetShihabCameraMode(_shihabConfig.GlobeCameraMode);
            }
            UpdateCameraModePillsVisual();

            if (alreadyActive)
            {
                _shihabState = ShihabConnectionState.Connected;
                _shihabConnectedSinceUtc = DateTime.UtcNow;
                AppendShihabEngineLog("SYSTEM", "Detected existing active Shihab Shield VPN session on startup.");
            }
            else
            {
                AppendShihabEngineLog(
                    "SYSTEM",
                    string.Format(
                        "Shihab Shield VPN v1.2.0 initialized • Dynamic IP Rotation={0} • Extended Pool={1}",
                        _shihabConfig.DynamicIpRotation ? "ACTIVE" : "STICKY",
                        _shihabConfig.ShowExtendedCatalog ? "ON" : "COMPACT"));
            }

            RefreshAllShihabViews();

            _shihabUiTimer = new DispatcherTimer();
            _shihabUiTimer.Interval = TimeSpan.FromSeconds(1.0);
            _shihabUiTimer.Tick += OnShihabSecondTick;
            _shihabUiTimer.Start();

            if (!isPreviewCapture)
            {
                StartShihabBrowserCompanionBridge();

                // Fetch live public relays & baseline GeoIP in background without blocking UI startup
                TriggerShihabRelayRefreshAsync(true);
                VerifyShihabGeoIpInBackgroundAsync(!alreadyActive);

                if (_shihabConfig.AutoConnectOnLaunch && !alreadyActive)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        ToggleShihabConnectionAsync();
                    }), DispatcherPriority.ApplicationIdle);
                }
            }
        }

        private void BuildShihabWindowLayout()
        {
            Border outerFrame = new Border();
            outerFrame.Margin = new Thickness(8);
            outerFrame.CornerRadius = new CornerRadius(10);
            outerFrame.Background = BrushHex("#131519");
            outerFrame.BorderBrush = BrushHex("#282D36");
            outerFrame.BorderThickness = new Thickness(1);
            outerFrame.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.58
            };

            Grid masterGrid = new Grid();
            masterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            masterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 1. Left Vertical Icon Rail (exact dark rail aesthetic from screenshot)
            Border leftRail = BuildShihabLeftNavigationRail();
            Grid.SetColumn(leftRail, 0);
            masterGrid.Children.Add(leftRail);

            // 2. Main Right Content Area (Top Header + View Switcher)
            Grid rightArea = new Grid();
            rightArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            rightArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border topHeader = BuildShihabTopHeaderBar();
            Grid.SetRow(topHeader, 0);
            rightArea.Children.Add(topHeader);

            Grid viewsHost = new Grid();
            UIElement homeView = BuildShihabHomeDashboardView();
            UIElement serversView = BuildShihabServersBrowserView();
            UIElement speedView = BuildShihabSpeedDiagnosticsView();
            UIElement settingsView = BuildShihabSecuritySettingsView();
            UIElement aboutView = BuildShihabAboutDeveloperView();

            _tabViews.Add(homeView);
            _tabViews.Add(serversView);
            _tabViews.Add(speedView);
            _tabViews.Add(settingsView);
            _tabViews.Add(aboutView);

            for (int i = 0; i < _tabViews.Count; i++)
            {
                _tabViews[i].Visibility = (i == 0) ? Visibility.Visible : Visibility.Collapsed;
                viewsHost.Children.Add(_tabViews[i]);
            }

            Grid.SetRow(viewsHost, 1);
            rightArea.Children.Add(viewsHost);

            Grid.SetColumn(rightArea, 1);
            masterGrid.Children.Add(rightArea);

            outerFrame.Child = masterGrid;
            Content = outerFrame;

            SelectShihabNavTab(0);
        }

        private Border BuildShihabLeftNavigationRail()
        {
            Border rail = new Border();
            rail.Background = BrushHex("#181B20");
            rail.BorderBrush = BrushHex("#242932");
            rail.BorderThickness = new Thickness(0, 0, 1, 0);
            rail.CornerRadius = new CornerRadius(10, 0, 0, 10);

            DockPanel dock = new DockPanel();
            dock.LastChildFill = false;

            // Top Brand Shield Mini-Emblem
            Border brandBox = new Border();
            brandBox.Height = 46;
            brandBox.Cursor = Cursors.Hand;
            brandBox.ToolTip = "Shihab Shield VPN — Open-Source Edition";
            brandBox.MouseLeftButtonDown += delegate { SelectShihabNavTab(0); };

            ShapePath shieldGlyph = new ShapePath();
            shieldGlyph.Data = Geometry.Parse("M 10,2 L 18,5 L 18,11 C 18,16 14.5,19.5 10,21 C 5.5,19.5 2,16 2,11 L 2,5 Z");
            shieldGlyph.Fill = BrushHex("#1DA1F2");
            shieldGlyph.Width = 20;
            shieldGlyph.Height = 22;
            shieldGlyph.Stretch = Stretch.Uniform;
            shieldGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            shieldGlyph.VerticalAlignment = VerticalAlignment.Center;
            brandBox.Child = shieldGlyph;
            DockPanel.SetDock(brandBox, Dock.Top);
            dock.Children.Add(brandBox);

            StackPanel navStack = new StackPanel();
            navStack.Margin = new Thickness(0, 6, 0, 0);

            // 0: Home
            navStack.Children.Add(CreateShihabRailButton(
                0,
                "Dashboard",
                "M 3,10.5 L 12,3 L 21,10.5 L 21,20 C 21,20.5 20.5,21 20,21 L 14.5,21 L 14.5,14.5 L 9.5,14.5 L 9.5,21 L 4,21 C 3.5,21 3,20.5 3,20 Z"));

            // 1: Servers (Public & Private)
            navStack.Children.Add(CreateShihabRailButton(
                1,
                "Public & Private Servers",
                "M 12,2 A 10,10 0 1,0 22,12 A 10,10 0 0,0 12,2 M 2.5,12 L 21.5,12 M 12,2.5 C 14.8,5.5 16,8.7 16,12 C 16,15.3 14.8,18.5 12,21.5 C 9.2,18.5 8,15.3 8,12 C 8,8.7 9.2,5.5 12,2.5"));

            // 2: Speed & Diagnostics
            navStack.Children.Add(CreateShihabRailButton(
                2,
                "Speed Test & Diagnostics",
                "M 12,4 A 9,9 0 0,0 3,13 C 3,15.8 4.2,18.2 6,19.8 L 18,19.8 C 19.8,18.2 21,15.8 21,13 A 9,9 0 0,0 12,4 M 12,13 L 16.5,8.5"));

            // 3: Security & Shield Settings
            navStack.Children.Add(CreateShihabRailButton(
                3,
                "Kill-Switch, DNS & Split-Tunneling",
                "M 12,3 L 20,7.5 L 20,16.5 L 12,21 L 4,16.5 L 4,7.5 Z M 12,9 A 3,3 0 1,0 15,12 A 3,3 0 0,0 12,9"));

            // 4: About Developer (i)
            navStack.Children.Add(CreateShihabRailButton(
                4,
                "About Developer (Mehedi Hasan Shihab)",
                "M 12,2 A 10,10 0 1,0 22,12 A 10,10 0 0,0 12,2 M 12,10.5 L 12,16.8 M 12,7.2 L 12,7.8"));

            DockPanel.SetDock(navStack, Dock.Top);
            dock.Children.Add(navStack);

            rail.Child = dock;
            return rail;
        }

        private Border CreateShihabRailButton(int tabIndex, string tooltip, string svgPath)
        {
            Border row = new Border();
            row.Height = 46;
            row.Background = Brushes.Transparent;
            row.Cursor = Cursors.Hand;
            row.ToolTip = tooltip;

            Grid g = new Grid();
            Border indicator = new Border();
            indicator.Width = 3;
            indicator.Height = 24;
            indicator.CornerRadius = new CornerRadius(0, 2, 2, 0);
            indicator.Background = Brushes.White;
            indicator.HorizontalAlignment = HorizontalAlignment.Left;
            indicator.Visibility = Visibility.Hidden;
            _navIndicatorBars.Add(indicator);

            ShapePath icon = new ShapePath();
            icon.Data = Geometry.Parse(svgPath);
            icon.Stroke = BrushHex("#8E96A4");
            icon.StrokeThickness = 1.75;
            icon.StrokeStartLineCap = PenLineCap.Round;
            icon.StrokeEndLineCap = PenLineCap.Round;
            icon.StrokeLineJoin = PenLineJoin.Round;
            icon.Width = 20;
            icon.Height = 20;
            icon.Stretch = Stretch.Uniform;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            _navIcons.Add(icon);

            g.Children.Add(indicator);
            g.Children.Add(icon);
            row.Child = g;

            row.MouseEnter += delegate
            {
                if (_activeNavIndex != tabIndex)
                {
                    row.Background = BrushHex("#20242B");
                }
            };
            row.MouseLeave += delegate
            {
                row.Background = Brushes.Transparent;
            };
            row.MouseLeftButtonDown += delegate
            {
                SelectShihabNavTab(tabIndex);
            };

            return row;
        }

        private void SelectShihabNavTab(int index)
        {
            _activeNavIndex = index;
            for (int i = 0; i < _tabViews.Count; i++)
            {
                bool active = (i == index);
                _tabViews[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                if (i < _navIndicatorBars.Count)
                {
                    _navIndicatorBars[i].Visibility = active ? Visibility.Visible : Visibility.Hidden;
                }
                if (i < _navIcons.Count)
                {
                    _navIcons[i].Stroke = active ? Brushes.White : BrushHex("#8E96A4");
                }
            }

            if (index == 1)
            {
                RebuildShihabServerRows();
            }
        }

        private Border BuildShihabTopHeaderBar()
        {
            Border bar = new Border();
            bar.Background = Brushes.Transparent;
            bar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };

            Grid g = new Grid();
            g.Margin = new Thickness(16, 0, 12, 0);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });

            // Left: Live Tunnel Status Pill
            _headerStatusPillBorder = new Border();
            _headerStatusPillBorder.HorizontalAlignment = HorizontalAlignment.Left;
            _headerStatusPillBorder.VerticalAlignment = VerticalAlignment.Center;
            _headerStatusPillBorder.CornerRadius = new CornerRadius(10);
            _headerStatusPillBorder.Padding = new Thickness(9, 3, 9, 3);
            _headerStatusPillBorder.Background = BrushHex("#1E222A");
            _headerStatusPillBorder.BorderBrush = BrushHex("#2D333E");
            _headerStatusPillBorder.BorderThickness = new Thickness(1);

            _headerStatusPillText = new TextBlock();
            _headerStatusPillText.Text = "● Ready";
            _headerStatusPillText.FontSize = 11.2;
            _headerStatusPillText.FontWeight = FontWeights.SemiBold;
            _headerStatusPillText.Foreground = BrushHex("#9BA3B0");
            _headerStatusPillBorder.Child = _headerStatusPillText;
            Grid.SetColumn(_headerStatusPillBorder, 0);
            g.Children.Add(_headerStatusPillBorder);

            // Center: Exact "Shihab Shield Open Pro" centered brand title matching screenshot
            StackPanel centerBrand = new StackPanel();
            centerBrand.Orientation = Orientation.Horizontal;
            centerBrand.HorizontalAlignment = HorizontalAlignment.Center;
            centerBrand.VerticalAlignment = VerticalAlignment.Center;

            TextBlock titleBold = new TextBlock();
            titleBold.Text = "Shihab Shield ";
            titleBold.FontSize = 15.5;
            titleBold.FontWeight = FontWeights.Bold;
            titleBold.Foreground = Brushes.White;

            TextBlock titleSub = new TextBlock();
            titleSub.Text = "Open Pro";
            titleSub.FontSize = 15.0;
            titleSub.FontWeight = FontWeights.Normal;
            titleSub.Foreground = BrushHex("#A0AAB8");

            centerBrand.Children.Add(titleBold);
            centerBrand.Children.Add(titleSub);
            Grid.SetColumn(centerBrand, 1);
            g.Children.Add(centerBrand);

            // Right: Pin + Small 'i' Developer Info Button + Minimize + Close
            StackPanel rightControls = new StackPanel();
            rightControls.Orientation = Orientation.Horizontal;
            rightControls.HorizontalAlignment = HorizontalAlignment.Right;
            rightControls.VerticalAlignment = VerticalAlignment.Center;

            _pinToolbarBtn = CreateHeaderIconButton("📌", "Pin Always on Top", false);
            _pinToolbarBtn.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _shihabConfig.AlwaysOnTop = !_shihabConfig.AlwaysOnTop;
                Topmost = _shihabConfig.AlwaysOnTop;
                _pinToolbarBtn.Background = _shihabConfig.AlwaysOnTop ? BrushHex("#1F3248") : Brushes.Transparent;
                _shihabConfigService.SaveShihabConfig(_shihabConfig);
            };

            Border refreshBtn = CreateHeaderIconButton("↻", "Refresh Public Relay Directory", false);
            refreshBtn.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                TriggerShihabRelayRefreshAsync(true);
            };

            // Delicate small circular 'i' developer info icon on top-right header
            Border smallAboutBtn = CreateSmallAboutInfoHeaderButton();
            smallAboutBtn.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                SelectShihabNavTab(_activeNavIndex == 4 ? 0 : 4);
            };

            Border minBtn = CreateHeaderIconButton("—", "Minimize", false);
            minBtn.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                WindowState = WindowState.Minimized;
            };

            Border closeBtn = CreateHeaderIconButton("✕", "Close to Tray / Exit", true);
            closeBtn.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (_shihabConfig.MinimizeToTrayOnClose)
                {
                    Hide();
                    _shihabTray.ShowShihabNotification("Shihab Shield VPN", "Running quietly in the System Tray.");
                }
                else
                {
                    RequestExplicitExit();
                }
            };

            rightControls.Children.Add(_pinToolbarBtn);
            rightControls.Children.Add(refreshBtn);
            rightControls.Children.Add(smallAboutBtn);
            rightControls.Children.Add(minBtn);
            rightControls.Children.Add(closeBtn);
            Grid.SetColumn(rightControls, 2);
            g.Children.Add(rightControls);

            bar.Child = g;
            return bar;
        }

        private static Border CreateSmallAboutInfoHeaderButton()
        {
            Border host = new Border();
            host.Width = 24;
            host.Height = 24;
            host.Margin = new Thickness(2, 0, 2, 0);
            host.CornerRadius = new CornerRadius(5);
            host.Background = Brushes.Transparent;
            host.Cursor = Cursors.Hand;
            host.ToolTip = "About Developer — Mehedi Hasan Shihab";

            Border circle = new Border();
            circle.Width = 14.5;
            circle.Height = 14.5;
            circle.CornerRadius = new CornerRadius(7.25);
            circle.BorderBrush = BrushHex("#9AA4B2");
            circle.BorderThickness = new Thickness(1.15);
            circle.HorizontalAlignment = HorizontalAlignment.Center;
            circle.VerticalAlignment = VerticalAlignment.Center;

            TextBlock txt = new TextBlock();
            txt.Text = "i";
            txt.FontSize = 9.3;
            txt.FontWeight = FontWeights.Bold;
            txt.Foreground = BrushHex("#D6DEE8");
            txt.HorizontalAlignment = HorizontalAlignment.Center;
            txt.VerticalAlignment = VerticalAlignment.Center;
            txt.Margin = new Thickness(0, -0.6, 0, 0);
            circle.Child = txt;
            host.Child = circle;

            host.MouseEnter += delegate
            {
                host.Background = BrushHex("#232A35");
                circle.BorderBrush = BrushHex("#38BDF8");
                txt.Foreground = Brushes.White;
            };
            host.MouseLeave += delegate
            {
                host.Background = Brushes.Transparent;
                circle.BorderBrush = BrushHex("#9AA4B2");
                txt.Foreground = BrushHex("#D6DEE8");
            };

            return host;
        }

        private static Border CreateHeaderIconButton(string symbol, string tooltip, bool dangerHover)
        {
            Border b = new Border();
            b.Width = 26;
            b.Height = 24;
            b.Margin = new Thickness(2, 0, 0, 0);
            b.CornerRadius = new CornerRadius(5);
            b.Background = Brushes.Transparent;
            b.Cursor = Cursors.Hand;
            b.ToolTip = tooltip;

            TextBlock t = new TextBlock();
            t.Text = symbol;
            t.FontSize = 11.5;
            t.Foreground = BrushHex("#9AA4B2");
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;

            b.MouseEnter += delegate
            {
                b.Background = dangerHover ? BrushHex("#B91C1C") : BrushHex("#232832");
                t.Foreground = Brushes.White;
            };
            b.MouseLeave += delegate
            {
                b.Background = Brushes.Transparent;
                t.Foreground = BrushHex("#9AA4B2");
            };
            return b;
        }

        // =====================================================================
        // TAB 0: HOME BENTO DASHBOARD (EXACT SCREENSHOT 5-CARD LAYOUT + ORB)
        // =====================================================================
        private UIElement BuildShihabHomeDashboardView()
        {
            Grid root = new Grid();
            root.Margin = new Thickness(24, 6, 24, 14);
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });

            Grid bentoCols = new Grid();
            bentoCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bentoCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            bentoCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // LEFT COLUMN: [Virtual location] + [Server Information]
            Grid leftCol = new Grid();
            leftCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.22, GridUnitType.Star) });
            leftCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            leftCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });

            Border cardVirtualLoc = BuildVirtualLocationCard();
            Grid.SetRow(cardVirtualLoc, 0);
            leftCol.Children.Add(cardVirtualLoc);

            Border cardServerInfo = BuildServerInformationCard();
            Grid.SetRow(cardServerInfo, 2);
            leftCol.Children.Add(cardServerInfo);

            Grid.SetColumn(leftCol, 0);
            bentoCols.Children.Add(leftCol);

            // RIGHT COLUMN: [Daily data] + [Peak speed] + [Network]
            Grid rightCol = new Grid();
            rightCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.38, GridUnitType.Star) });
            rightCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            rightCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.76, GridUnitType.Star) });
            rightCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            rightCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.72, GridUnitType.Star) });

            Border cardDailyData = BuildDailyDataCard();
            Grid.SetRow(cardDailyData, 0);
            rightCol.Children.Add(cardDailyData);

            Border cardPeakSpeed = BuildPeakSpeedCard();
            Grid.SetRow(cardPeakSpeed, 2);
            rightCol.Children.Add(cardPeakSpeed);

            Border cardNetwork = BuildNetworkAdapterCard();
            Grid.SetRow(cardNetwork, 4);
            rightCol.Children.Add(cardNetwork);

            Grid.SetColumn(rightCol, 2);
            bentoCols.Children.Add(rightCol);

            Grid.SetRow(bentoCols, 0);
            root.Children.Add(bentoCols);

            // Bottom subtle status line + developer quick link
            Grid bottomFooter = new Grid();
            bottomFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottomFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _bottomBannerStatusText = new TextBlock();
            _bottomBannerStatusText.Text = "Ready • Click center shield orb to connect";
            _bottomBannerStatusText.FontSize = 11.2;
            _bottomBannerStatusText.Foreground = BrushHex("#7F8896");
            _bottomBannerStatusText.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(_bottomBannerStatusText, 0);
            bottomFooter.Children.Add(_bottomBannerStatusText);

            TextBlock devFooterLink = new TextBlock();
            devFooterLink.Text = "By Mehedi Hasan Shihab";
            devFooterLink.FontSize = 11.0;
            devFooterLink.Foreground = BrushHex("#6E7785");
            devFooterLink.HorizontalAlignment = HorizontalAlignment.Right;
            devFooterLink.VerticalAlignment = VerticalAlignment.Bottom;
            devFooterLink.Cursor = Cursors.Hand;
            devFooterLink.MouseEnter += delegate { devFooterLink.Foreground = BrushHex("#38BDF8"); };
            devFooterLink.MouseLeave += delegate { devFooterLink.Foreground = BrushHex("#6E7785"); };
            devFooterLink.MouseLeftButtonDown += delegate { SelectShihabNavTab(4); };
            Grid.SetColumn(devFooterLink, 2);
            bottomFooter.Children.Add(devFooterLink);

            Grid.SetRow(bottomFooter, 1);
            root.Children.Add(bottomFooter);

            // Floating Center-Bottom Connect / Stop Circular Button (exact match to screenshot!)
            Grid floatingOrbContainer = BuildFloatingConnectOrb();
            Grid.SetRowSpan(floatingOrbContainer, 2);
            floatingOrbContainer.HorizontalAlignment = HorizontalAlignment.Center;
            floatingOrbContainer.VerticalAlignment = VerticalAlignment.Bottom;
            floatingOrbContainer.Margin = new Thickness(0, 0, 0, 2);
            root.Children.Add(floatingOrbContainer);

            return root;
        }

        private Border BuildVirtualLocationCard()
        {
            Border card = CreateBentoCard();
            DockPanel dock = new DockPanel();

            // Header row + 3D Globe / Shield / Orbit / 2D camera switcher pills
            DockPanel hdr = new DockPanel();
            TextBlock lbl = CreateCardHeaderTitle("Virtual location");
            DockPanel.SetDock(lbl, Dock.Left);
            hdr.Children.Add(lbl);

            StackPanel rightGlobeTools = new StackPanel();
            rightGlobeTools.Orientation = Orientation.Horizontal;
            rightGlobeTools.HorizontalAlignment = HorizontalAlignment.Right;
            rightGlobeTools.VerticalAlignment = VerticalAlignment.Center;

            _cameraModePills.Clear();
            string[] camTitles = new string[] { "3D", "Shield", "Orbit", "2D" };
            string[] camTips = new string[]
            {
                "Interactive 3D Globe (drag with mouse to rotate)",
                "3D Globe + Dual Shield Orbital Rings",
                "Continuous 3D Auto-Orbit Camera",
                "Flat 2D Continental Route Projection"
            };
            for (int c = 0; c < camTitles.Length; c++)
            {
                int capturedMode = c;
                Border pill = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5.5, 1.5, 5.5, 1.5),
                    Margin = new Thickness(0, 0, 3, 0),
                    Background = BrushHex("#242A35"),
                    BorderBrush = BrushHex("#2F3746"),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = camTips[c],
                    Child = new TextBlock
                    {
                        Text = camTitles[c],
                        FontSize = 9.6,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = BrushHex("#9BA3B0")
                    }
                };
                pill.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    _shihabConfig.GlobeCameraMode = (ShihabGlobeCameraMode)capturedMode;
                    if (_worldMapControl != null)
                    {
                        _worldMapControl.SetShihabCameraMode(_shihabConfig.GlobeCameraMode);
                    }
                    UpdateCameraModePillsVisual();
                    _shihabConfigService.SaveShihabConfig(_shihabConfig);
                };
                _cameraModePills.Add(pill);
                rightGlobeTools.Children.Add(pill);
            }

            Border infoBadge = CreateCardCornerInfoBadge("Drag the 3D globe to rotate, or click country row to browse 70+ countries & private servers");
            infoBadge.Margin = new Thickness(2, 0, 0, 0);
            rightGlobeTools.Children.Add(infoBadge);
            DockPanel.SetDock(rightGlobeTools, Dock.Right);
            hdr.Children.Add(rightGlobeTools);
            DockPanel.SetDock(hdr, Dock.Top);
            dock.Children.Add(hdr);

            // Interactive country selector pill
            Border selectorRow = new Border();
            selectorRow.Margin = new Thickness(0, 8, 0, 4);
            selectorRow.Padding = new Thickness(6, 4, 8, 4);
            selectorRow.CornerRadius = new CornerRadius(7);
            selectorRow.Background = Brushes.Transparent;
            selectorRow.Cursor = Cursors.Hand;
            selectorRow.MouseEnter += delegate { selectorRow.Background = BrushHex("#252932"); };
            selectorRow.MouseLeave += delegate { selectorRow.Background = Brushes.Transparent; };
            selectorRow.MouseLeftButtonDown += delegate { SelectShihabNavTab(1); };

            Grid selGrid = new Grid();
            selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _virtualFlagContainer = new Border();
            _virtualFlagContainer.Margin = new Thickness(0, 0, 10, 0);
            _virtualFlagContainer.VerticalAlignment = VerticalAlignment.Center;
            _virtualFlagContainer.Child = ShihabFlagBadgeFactory.CreateCircularFlagBadge("US", 24);
            Grid.SetColumn(_virtualFlagContainer, 0);
            selGrid.Children.Add(_virtualFlagContainer);

            StackPanel titleStack = new StackPanel();
            titleStack.VerticalAlignment = VerticalAlignment.Center;
            _virtualLocationTitleText = new TextBlock();
            _virtualLocationTitleText.Text = "Auto";
            _virtualLocationTitleText.FontSize = 14.5;
            _virtualLocationTitleText.FontWeight = FontWeights.Bold;
            _virtualLocationTitleText.Foreground = Brushes.White;

            _virtualLocationSubText = new TextBlock();
            _virtualLocationSubText.Text = "Fastest Open Relay";
            _virtualLocationSubText.FontSize = 11.0;
            _virtualLocationSubText.Foreground = BrushHex("#8A93A2");
            _virtualLocationSubText.TextTrimming = TextTrimming.CharacterEllipsis;
            titleStack.Children.Add(_virtualLocationTitleText);
            titleStack.Children.Add(_virtualLocationSubText);
            Grid.SetColumn(titleStack, 1);
            selGrid.Children.Add(titleStack);

            TextBlock chevron = new TextBlock();
            chevron.Text = "›";
            chevron.FontSize = 20;
            chevron.Foreground = BrushHex("#8E96A4");
            chevron.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(chevron, 2);
            selGrid.Children.Add(chevron);

            selectorRow.Child = selGrid;
            DockPanel.SetDock(selectorRow, Dock.Top);
            dock.Children.Add(selectorRow);

            // Interactive 3D Globe / 2D World Map Canvas
            _worldMapControl = new ShihabWorldMapControl();
            _worldMapControl.Margin = new Thickness(0, 2, 0, 0);
            dock.Children.Add(_worldMapControl);

            card.Child = dock;
            return card;
        }

        private void UpdateCameraModePillsVisual()
        {
            int activeIdx = (int)_shihabConfig.GlobeCameraMode;
            for (int i = 0; i < _cameraModePills.Count; i++)
            {
                bool sel = i == activeIdx;
                _cameraModePills[i].Background = BrushHex(sel ? "#1E3A5F" : "#242A35");
                _cameraModePills[i].BorderBrush = BrushHex(sel ? "#38BDF8" : "#2F3746");
                TextBlock tb = _cameraModePills[i].Child as TextBlock;
                if (tb != null)
                {
                    tb.Foreground = BrushHex(sel ? "#7DD3FC" : "#9BA3B0");
                }
            }
        }

        private Border BuildServerInformationCard()
        {
            Border card = CreateBentoCard();
            StackPanel stack = new StackPanel();

            DockPanel hdr = new DockPanel();
            TextBlock lbl = CreateCardHeaderTitle("Server Information");
            DockPanel.SetDock(lbl, Dock.Left);
            hdr.Children.Add(lbl);

            StackPanel rightServerTools = new StackPanel();
            rightServerTools.Orientation = Orientation.Horizontal;
            rightServerTools.HorizontalAlignment = HorizontalAlignment.Right;
            rightServerTools.VerticalAlignment = VerticalAlignment.Center;

            Border rotatePill = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Background = BrushHex("#1F2A38"),
                BorderBrush = BrushHex("#2C3B50"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "Dynamic IP Rotation: Switch to a fresh verified gateway in the selected region"
            };
            _rotateIpActionText = new TextBlock
            {
                Text = "↻ Rotate IP",
                FontSize = 10.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushHex("#7DD3FC")
            };
            rotatePill.Child = _rotateIpActionText;
            rotatePill.MouseEnter += delegate { rotatePill.BorderBrush = BrushHex("#38BDF8"); };
            rotatePill.MouseLeave += delegate { rotatePill.BorderBrush = BrushHex("#2C3B50"); };
            rotatePill.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                RotateShihabIpNowAsync();
            };
            rightServerTools.Children.Add(rotatePill);

            Border protoPill = new Border();
            protoPill.HorizontalAlignment = HorizontalAlignment.Right;
            protoPill.CornerRadius = new CornerRadius(5);
            protoPill.Padding = new Thickness(7, 2, 7, 2);
            protoPill.Background = BrushHex("#242A35");
            _serverProtocolBadgeText = new TextBlock();
            _serverProtocolBadgeText.Text = "MS-SSTP TLS";
            _serverProtocolBadgeText.FontSize = 10.2;
            _serverProtocolBadgeText.FontWeight = FontWeights.SemiBold;
            _serverProtocolBadgeText.Foreground = BrushHex("#38BDF8");
            protoPill.Child = _serverProtocolBadgeText;
            rightServerTools.Children.Add(protoPill);

            DockPanel.SetDock(rightServerTools, Dock.Right);
            hdr.Children.Add(rightServerTools);
            stack.Children.Add(hdr);

            // Row 1: IP + Open Pro pill + Value
            Grid rowIp = new Grid();
            rowIp.Margin = new Thickness(0, 14, 0, 0);
            rowIp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowIp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowIp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock ipLbl = CreateMetricLabel("IP");
            Grid.SetColumn(ipLbl, 0);
            rowIp.Children.Add(ipLbl);

            Border proPill = new Border();
            proPill.Margin = new Thickness(8, 0, 0, 0);
            proPill.Padding = new Thickness(7, 1.5, 7, 1.5);
            proPill.CornerRadius = new CornerRadius(8);
            proPill.Background = BrushHex("#2A303B");
            proPill.VerticalAlignment = VerticalAlignment.Center;
            _serverModeBadgeText = new TextBlock();
            _serverModeBadgeText.Text = "Open Pro";
            _serverModeBadgeText.FontSize = 10.2;
            _serverModeBadgeText.FontWeight = FontWeights.SemiBold;
            _serverModeBadgeText.Foreground = BrushHex("#38BDF8");
            proPill.Child = _serverModeBadgeText;
            Grid.SetColumn(proPill, 1);
            rowIp.Children.Add(proPill);

            _serverIpValueText = CreateMetricRightValue("—");
            Grid.SetColumn(_serverIpValueText, 2);
            rowIp.Children.Add(_serverIpValueText);
            stack.Children.Add(rowIp);

            // Row 2: Load, %
            stack.Children.Add(BuildMetricRow("Load, %", "12", out _serverLoadValueText));

            // Row 3: Latency, ms
            stack.Children.Add(BuildMetricRow("Latency, ms", "24", out _serverLatencyValueText));

            // Row 4: Connected
            stack.Children.Add(BuildMetricRow("Connected", "00:00", out _serverConnectedTimerText));

            card.Child = stack;
            return card;
        }

        private Border BuildDailyDataCard()
        {
            Border card = CreateBentoCard();
            DockPanel dock = new DockPanel();

            DockPanel hdr = new DockPanel();
            TextBlock lbl = CreateCardHeaderTitle("Daily data");
            DockPanel.SetDock(lbl, Dock.Left);
            hdr.Children.Add(lbl);

            Border infoBadge = CreateCardCornerInfoBadge("Tracks rolling 24-hour Download & Upload traffic with zero bandwidth cap");
            DockPanel.SetDock(infoBadge, Dock.Right);
            hdr.Children.Add(infoBadge);
            DockPanel.SetDock(hdr, Dock.Top);
            dock.Children.Add(hdr);

            _dailyDataTotalText = new TextBlock();
            _dailyDataTotalText.Text = "0 B";
            _dailyDataTotalText.FontSize = 15.5;
            _dailyDataTotalText.FontWeight = FontWeights.Bold;
            _dailyDataTotalText.Foreground = Brushes.White;
            _dailyDataTotalText.Margin = new Thickness(0, 8, 0, 6);
            DockPanel.SetDock(_dailyDataTotalText, Dock.Top);
            dock.Children.Add(_dailyDataTotalText);

            // Bottom Last 24h legend row
            Grid legendRow = new Grid();
            legendRow.Margin = new Thickness(0, 6, 0, 0);
            legendRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            legendRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock last24Lbl = new TextBlock();
            last24Lbl.Text = "Last 24h";
            last24Lbl.FontSize = 11.5;
            last24Lbl.Foreground = BrushHex("#8E96A4");
            Grid.SetColumn(last24Lbl, 0);
            legendRow.Children.Add(last24Lbl);

            StackPanel dlUlStack = new StackPanel();
            dlUlStack.Orientation = Orientation.Horizontal;
            dlUlStack.HorizontalAlignment = HorizontalAlignment.Right;

            Border dlDot = new Border { Width = 5, Height = 5, Background = BrushHex("#38BDF8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            _dailyDownloadText = new TextBlock { Text = "0 B ↓", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 14, 0) };

            Border ulDot = new Border { Width = 5, Height = 5, Background = BrushHex("#1D7AFC"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            _dailyUploadText = new TextBlock { Text = "0 B ↑", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };

            dlUlStack.Children.Add(dlDot);
            dlUlStack.Children.Add(_dailyDownloadText);
            dlUlStack.Children.Add(ulDot);
            dlUlStack.Children.Add(_dailyUploadText);
            Grid.SetColumn(dlUlStack, 1);
            legendRow.Children.Add(dlUlStack);

            DockPanel.SetDock(legendRow, Dock.Bottom);
            dock.Children.Add(legendRow);

            _dailyChartControl = new ShihabDailyDataChartControl();
            dock.Children.Add(_dailyChartControl);

            card.Child = dock;
            return card;
        }

        private Border BuildPeakSpeedCard()
        {
            Border card = CreateBentoCard();
            StackPanel stack = new StackPanel();

            DockPanel hdr = new DockPanel();
            TextBlock lbl = CreateCardHeaderTitle("Peak speed");
            DockPanel.SetDock(lbl, Dock.Left);
            hdr.Children.Add(lbl);

            Border infoBadge = CreateCardCornerInfoBadge("Live burst Mbps & on-demand HTTPS throughput benchmark");
            DockPanel.SetDock(infoBadge, Dock.Right);
            hdr.Children.Add(infoBadge);
            stack.Children.Add(hdr);

            Grid midRow = new Grid();
            midRow.Margin = new Thickness(0, 10, 0, 10);
            midRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            midRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _peakSpeedValueText = new TextBlock();
            _peakSpeedValueText.Text = "0.0 Mbps";
            _peakSpeedValueText.FontSize = 15.0;
            _peakSpeedValueText.FontWeight = FontWeights.Bold;
            _peakSpeedValueText.Foreground = Brushes.White;
            Grid.SetColumn(_peakSpeedValueText, 0);
            midRow.Children.Add(_peakSpeedValueText);

            _testSpeedActionText = new TextBlock();
            _testSpeedActionText.Text = "Test speed";
            _testSpeedActionText.FontSize = 13.0;
            _testSpeedActionText.Foreground = BrushHex("#1DA1F2");
            _testSpeedActionText.Cursor = Cursors.Hand;
            _testSpeedActionText.VerticalAlignment = VerticalAlignment.Center;
            _testSpeedActionText.MouseEnter += delegate { _testSpeedActionText.Foreground = BrushHex("#60C5FF"); };
            _testSpeedActionText.MouseLeave += delegate { _testSpeedActionText.Foreground = BrushHex("#1DA1F2"); };
            _testSpeedActionText.MouseLeftButtonDown += delegate { StartShihabSpeedBenchmarkAsync(); };
            Grid.SetColumn(_testSpeedActionText, 1);
            midRow.Children.Add(_testSpeedActionText);
            stack.Children.Add(midRow);

            // Progress bar track
            _peakSpeedBarTrackGrid = new Grid();
            _peakSpeedBarTrackGrid.Height = 4;
            Border trackBg = new Border();
            trackBg.CornerRadius = new CornerRadius(2);
            trackBg.Background = BrushHex("#2B303A");
            _peakSpeedBarTrackGrid.Children.Add(trackBg);

            _peakSpeedBarFill = new Border();
            _peakSpeedBarFill.CornerRadius = new CornerRadius(2);
            _peakSpeedBarFill.Background = BrushHex("#1DA1F2");
            _peakSpeedBarFill.HorizontalAlignment = HorizontalAlignment.Left;
            _peakSpeedBarFill.Width = 0;
            _peakSpeedBarTrackGrid.Children.Add(_peakSpeedBarFill);
            stack.Children.Add(_peakSpeedBarTrackGrid);

            Grid scaleRow = new Grid();
            scaleRow.Margin = new Thickness(0, 6, 0, 0);
            TextBlock s0 = new TextBlock { Text = "0", FontSize = 11.0, Foreground = BrushHex("#8E96A4"), HorizontalAlignment = HorizontalAlignment.Left };
            TextBlock s100 = new TextBlock { Text = "100+", FontSize = 11.0, Foreground = BrushHex("#8E96A4"), HorizontalAlignment = HorizontalAlignment.Right };
            scaleRow.Children.Add(s0);
            scaleRow.Children.Add(s100);
            stack.Children.Add(scaleRow);

            card.Child = stack;
            return card;
        }

        private Border BuildNetworkAdapterCard()
        {
            Border card = CreateBentoCard();
            StackPanel stack = new StackPanel();

            DockPanel hdr = new DockPanel();
            TextBlock lbl = CreateCardHeaderTitle("Network");
            DockPanel.SetDock(lbl, Dock.Left);
            hdr.Children.Add(lbl);

            Border dnsPill = new Border();
            dnsPill.HorizontalAlignment = HorizontalAlignment.Right;
            dnsPill.CornerRadius = new CornerRadius(5);
            dnsPill.Padding = new Thickness(7, 2, 7, 2);
            dnsPill.Background = BrushHex("#242A35");
            dnsPill.Cursor = Cursors.Hand;
            dnsPill.ToolTip = "Click to configure Secure DNS & Kill-Switch";
            dnsPill.MouseLeftButtonDown += delegate { SelectShihabNavTab(3); };

            _networkDnsBadgeText = new TextBlock();
            _networkDnsBadgeText.Text = "DNS: 1.1.1.1";
            _networkDnsBadgeText.FontSize = 10.2;
            _networkDnsBadgeText.Foreground = BrushHex("#9BA3B0");
            dnsPill.Child = _networkDnsBadgeText;
            DockPanel.SetDock(dnsPill, Dock.Right);
            hdr.Children.Add(dnsPill);
            stack.Children.Add(hdr);

            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 14, 0, 0);

            // Crisp Monitor/Ethernet vector icon matching screenshot
            ShapePath ethIcon = new ShapePath();
            ethIcon.Data = Geometry.Parse("M 3,4 L 15,4 L 15,12 L 3,12 Z M 9,12 L 9,16 M 5,16 L 13,16 M 16,9 L 21,9 L 21,17 L 15,17");
            ethIcon.Stroke = BrushHex("#9AA4B2");
            ethIcon.StrokeThickness = 1.65;
            ethIcon.Width = 22;
            ethIcon.Height = 20;
            ethIcon.Stretch = Stretch.Uniform;
            ethIcon.VerticalAlignment = VerticalAlignment.Center;
            ethIcon.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(ethIcon);

            StackPanel txtStack = new StackPanel();
            txtStack.VerticalAlignment = VerticalAlignment.Center;

            _networkTitleText = new TextBlock();
            _networkTitleText.Text = "Ethernet connection";
            _networkTitleText.FontSize = 14.0;
            _networkTitleText.FontWeight = FontWeights.Bold;
            _networkTitleText.Foreground = Brushes.White;

            _networkSubText = new TextBlock();
            _networkSubText.Text = "Active physical adapter";
            _networkSubText.FontSize = 11.0;
            _networkSubText.Foreground = BrushHex("#8A93A2");

            txtStack.Children.Add(_networkTitleText);
            txtStack.Children.Add(_networkSubText);
            row.Children.Add(txtStack);

            stack.Children.Add(row);
            card.Child = stack;
            return card;
        }

        private Grid BuildFloatingConnectOrb()
        {
            Grid wrap = new Grid();
            wrap.Width = 74;
            wrap.Height = 74;

            _heroConnectOuterHalo = new Border();
            _heroConnectOuterHalo.Width = 70;
            _heroConnectOuterHalo.Height = 70;
            _heroConnectOuterHalo.CornerRadius = new CornerRadius(35);
            _heroConnectOuterHalo.Background = new SolidColorBrush(Color.FromArgb(38, 29, 161, 242));
            _heroConnectOuterHalo.HorizontalAlignment = HorizontalAlignment.Center;
            _heroConnectOuterHalo.VerticalAlignment = VerticalAlignment.Center;
            wrap.Children.Add(_heroConnectOuterHalo);

            _heroConnectOrb = new Border();
            _heroConnectOrb.Width = 54;
            _heroConnectOrb.Height = 54;
            _heroConnectOrb.CornerRadius = new CornerRadius(27);
            _heroConnectOrb.Background = BrushHex("#1DA1F2");
            _heroConnectOrb.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
            _heroConnectOrb.BorderThickness = new Thickness(1.2);
            _heroConnectOrb.Cursor = Cursors.Hand;
            _heroConnectOrb.HorizontalAlignment = HorizontalAlignment.Center;
            _heroConnectOrb.VerticalAlignment = VerticalAlignment.Center;
            _heroConnectOrb.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(29, 161, 242),
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = 0.55
            };

            _heroConnectGlyphHost = new Grid();
            _heroConnectOrb.Child = _heroConnectGlyphHost;

            _heroConnectOrb.MouseEnter += delegate
            {
                _heroConnectOrb.Width = 56;
                _heroConnectOrb.Height = 56;
                _heroConnectOrb.CornerRadius = new CornerRadius(28);
            };
            _heroConnectOrb.MouseLeave += delegate
            {
                _heroConnectOrb.Width = 54;
                _heroConnectOrb.Height = 54;
                _heroConnectOrb.CornerRadius = new CornerRadius(27);
            };
            _heroConnectOrb.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                ToggleShihabConnectionAsync();
            };

            wrap.Children.Add(_heroConnectOrb);
            UpdateConnectOrbVisual();
            return wrap;
        }

        // =====================================================================
        // TAB 1: PUBLIC & PRIVATE SERVERS BROWSER + CUSTOM PROFILE IMPORTER
        // =====================================================================
        private UIElement BuildShihabServersBrowserView()
        {
            Grid root = new Grid();
            root.Margin = new Thickness(24, 6, 24, 16);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Top bar: Title + Actions (+ Private Server, Import .conf/.ovpn, Refresh)
            DockPanel topRow = new DockPanel();
            topRow.Margin = new Thickness(0, 0, 0, 8);

            StackPanel leftTitles = new StackPanel();
            TextBlock hTitle = new TextBlock
            {
                Text = "Virtual Locations — Public & Private Servers",
                FontSize = 16.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            _serverCountSummaryText = new TextBlock
            {
                Text = "Extended public MS-SSTP & OpenVPN catalog + custom WireGuard / OpenVPN / IKEv2 / SSTP / HTTP-Proxy servers",
                FontSize = 11.2,
                Foreground = BrushHex("#8A93A2"),
                Margin = new Thickness(0, 2, 0, 0)
            };
            leftTitles.Children.Add(hTitle);
            leftTitles.Children.Add(_serverCountSummaryText);
            DockPanel.SetDock(leftTitles, Dock.Left);
            topRow.Children.Add(leftTitles);

            StackPanel actionBtns = new StackPanel();
            actionBtns.Orientation = Orientation.Horizontal;
            actionBtns.HorizontalAlignment = HorizontalAlignment.Right;
            actionBtns.VerticalAlignment = VerticalAlignment.Center;

            Button btnAddPrivate = CreateActionPillButton("+ Add Private Server", "#1DA1F2", "#FFFFFF");
            btnAddPrivate.Margin = new Thickness(0, 0, 8, 0);
            btnAddPrivate.Click += delegate
            {
                _privateEditorDrawer.Visibility = _privateEditorDrawer.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            };

            Button btnImportConf = CreateActionPillButton("Import .conf / .ovpn", "#242A35", "#E2E8F0");
            btnImportConf.Click += delegate { ImportShihabProfileFromDisk(); };

            actionBtns.Children.Add(btnAddPrivate);
            actionBtns.Children.Add(btnImportConf);
            DockPanel.SetDock(actionBtns, Dock.Right);
            topRow.Children.Add(actionBtns);
            Grid.SetRow(topRow, 0);
            root.Children.Add(topRow);

            // Search box + Mode Filter Pills + Region & 4K Streaming Filter Bar + Collapsible Add Private Server Drawer
            StackPanel midStack = new StackPanel();
            midStack.Margin = new Thickness(0, 0, 0, 8);

            Grid filterBarGrid = new Grid();
            filterBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filterBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            filterBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border searchBorder = new Border();
            searchBorder.Background = BrushHex("#1C1F26");
            searchBorder.BorderBrush = BrushHex("#2B313C");
            searchBorder.BorderThickness = new Thickness(1);
            searchBorder.CornerRadius = new CornerRadius(7);
            searchBorder.Padding = new Thickness(10, 5, 10, 5);

            Grid searchInnerGrid = new Grid();
            TextBlock searchPlaceholder = new TextBlock
            {
                Text = "🔍  Search any country (United States, UK, Germany, Singapore...), city, host, or IP...",
                FontSize = 11.8,
                Foreground = BrushHex("#6E7787"),
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0)
            };

            _serverSearchBox = new TextBox();
            _serverSearchBox.Background = Brushes.Transparent;
            _serverSearchBox.BorderThickness = new Thickness(0);
            _serverSearchBox.Foreground = Brushes.White;
            _serverSearchBox.CaretBrush = Brushes.White;
            _serverSearchBox.FontSize = 12.2;
            _serverSearchBox.TextChanged += delegate
            {
                searchPlaceholder.Visibility = string.IsNullOrEmpty(_serverSearchBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                RebuildShihabServerRows();
            };

            searchInnerGrid.Children.Add(searchPlaceholder);
            searchInnerGrid.Children.Add(_serverSearchBox);
            searchBorder.Child = searchInnerGrid;
            Grid.SetColumn(searchBorder, 0);
            filterBarGrid.Children.Add(searchBorder);

            StackPanel modeTabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _btnModeCountries = CreateActionPillButton("All Countries", "#1DA1F2", "#FFFFFF");
            _btnModeCountries.Margin = new Thickness(0, 0, 6, 0);
            _btnModeCountries.Click += delegate { SetServerBrowserMode(0); };

            _btnModeNodes = CreateActionPillButton("All Servers", "#242A35", "#9BA3B0");
            _btnModeNodes.Margin = new Thickness(0, 0, 6, 0);
            _btnModeNodes.Click += delegate { SetServerBrowserMode(1); };

            _btnModePrivate = CreateActionPillButton("Private", "#242A35", "#9BA3B0");
            _btnModePrivate.Click += delegate { SetServerBrowserMode(2); };

            modeTabs.Children.Add(_btnModeCountries);
            modeTabs.Children.Add(_btnModeNodes);
            modeTabs.Children.Add(_btnModePrivate);
            Grid.SetColumn(modeTabs, 2);
            filterBarGrid.Children.Add(modeTabs);

            midStack.Children.Add(filterBarGrid);

            // Second compact filter row: Region pills + 4K Fast + Extended Pool
            DockPanel regionRow = new DockPanel();
            regionRow.Margin = new Thickness(0, 6, 0, 0);

            StackPanel regLeft = new StackPanel { Orientation = Orientation.Horizontal };
            _btnRegAll = CreateMiniFilterButton("All Regions", true);
            _btnRegAll.Click += delegate { SetRegionFilter("ALL"); };
            _btnRegAmericas = CreateMiniFilterButton("Americas", false);
            _btnRegAmericas.Click += delegate { SetRegionFilter("Americas"); };
            _btnRegEmea = CreateMiniFilterButton("EMEA", false);
            _btnRegEmea.Click += delegate { SetRegionFilter("EMEA"); };
            _btnRegApac = CreateMiniFilterButton("Asia & Oceania", false);
            _btnRegApac.Click += delegate { SetRegionFilter("Asia & Oceania"); };

            regLeft.Children.Add(_btnRegAll);
            regLeft.Children.Add(_btnRegAmericas);
            regLeft.Children.Add(_btnRegEmea);
            regLeft.Children.Add(_btnRegApac);
            DockPanel.SetDock(regLeft, Dock.Left);
            regionRow.Children.Add(regLeft);

            StackPanel regRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _btnFilterFast4k = CreateMiniFilterButton("⚡ 4K Fast (20+ Mbps)", _shihabConfig.StreamingHighSpeedOnly);
            _btnFilterFast4k.Click += delegate
            {
                _shihabConfig.StreamingHighSpeedOnly = !_shihabConfig.StreamingHighSpeedOnly;
                _shihabConfigService.SaveShihabConfig(_shihabConfig);
                UpdateRegionAndSpeedFilterVisuals();
                RebuildShihabServerRows();
            };

            _btnFilterExtended = CreateMiniFilterButton("🌐 Extended Pool", _shihabConfig.ShowExtendedCatalog);
            _btnFilterExtended.Margin = new Thickness(6, 0, 0, 0);
            _btnFilterExtended.Click += delegate
            {
                _shihabConfig.ShowExtendedCatalog = !_shihabConfig.ShowExtendedCatalog;
                if (_chkExtendedCatalog != null) _chkExtendedCatalog.IsChecked = _shihabConfig.ShowExtendedCatalog;
                _shihabConfigService.SaveShihabConfig(_shihabConfig);
                UpdateRegionAndSpeedFilterVisuals();
                TriggerShihabRelayRefreshAsync(false);
            };

            regRight.Children.Add(_btnFilterFast4k);
            regRight.Children.Add(_btnFilterExtended);
            DockPanel.SetDock(regRight, Dock.Right);
            regionRow.Children.Add(regRight);

            midStack.Children.Add(regionRow);

            _privateEditorDrawer = BuildPrivateServerCreatorCard();
            _privateEditorDrawer.Visibility = Visibility.Collapsed;
            midStack.Children.Add(_privateEditorDrawer);

            Grid.SetRow(midStack, 1);
            root.Children.Add(midStack);

            // Scrollable server list
            Border listContainer = CreateBentoCard();
            listContainer.Padding = new Thickness(8);
            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _serverListStack = new StackPanel();
            sv.Content = _serverListStack;
            listContainer.Child = sv;
            Grid.SetRow(listContainer, 2);
            root.Children.Add(listContainer);

            return root;
        }

        private static Button CreateMiniFilterButton(string text, bool active)
        {
            Button b = CreateActionPillButton(text, active ? "#1E3A5F" : "#1E222B", active ? "#7DD3FC" : "#94A3B8");
            b.FontSize = 10.8;
            b.Padding = new Thickness(9, 3.5, 9, 3.5);
            b.Margin = new Thickness(0, 0, 5, 0);
            return b;
        }

        private void SetRegionFilter(string region)
        {
            _activeRegionFilter = string.IsNullOrEmpty(region) ? "ALL" : region;
            UpdateRegionAndSpeedFilterVisuals();
            RebuildShihabServerRows();
        }

        private void UpdateRegionAndSpeedFilterVisuals()
        {
            ApplyMiniBtnState(_btnRegAll, _activeRegionFilter == "ALL");
            ApplyMiniBtnState(_btnRegAmericas, _activeRegionFilter == "Americas");
            ApplyMiniBtnState(_btnRegEmea, _activeRegionFilter == "EMEA");
            ApplyMiniBtnState(_btnRegApac, _activeRegionFilter == "Asia & Oceania");
            ApplyMiniBtnState(_btnFilterFast4k, _shihabConfig.StreamingHighSpeedOnly);
            ApplyMiniBtnState(_btnFilterExtended, _shihabConfig.ShowExtendedCatalog);
        }

        private static void ApplyMiniBtnState(Button btn, bool active)
        {
            if (btn == null) return;
            btn.Background = BrushHex(active ? "#1E3A5F" : "#1E222B");
            btn.Foreground = BrushHex(active ? "#7DD3FC" : "#94A3B8");
        }

        private Border BuildPrivateServerCreatorCard()
        {
            Border card = CreateBentoCard();
            card.Margin = new Thickness(0, 8, 0, 0);
            card.Padding = new Thickness(14, 12, 14, 12);
            card.BorderBrush = BrushHex("#1DA1F2");

            StackPanel st = new StackPanel();
            TextBlock hdr = new TextBlock
            {
                Text = "Add Private / Custom Server (SSTP, IKEv2, L2TP/IPsec, PPTP, or HTTP/HTTPS System Proxy)",
                FontSize = 12.8,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            st.Children.Add(hdr);

            Grid g1 = new Grid();
            g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
            g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
            g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });

            _privNameInput = CreateDarkTextBox("My US Private Gateway");
            _privHostInput = CreateDarkTextBox("vpn.example.com:443");

            _privProtoCombo = CreateDarkComboBox(28);
            _privProtoCombo.Items.Add("MS-SSTP (TLS 443 / :Port)");
            _privProtoCombo.Items.Add("IKEv2 / IPsec");
            _privProtoCombo.Items.Add("L2TP / IPsec (PSK)");
            _privProtoCombo.Items.Add("PPTP");
            _privProtoCombo.Items.Add("HTTP / HTTPS System Proxy (PAC / Auth)");
            _privProtoCombo.SelectedIndex = 0;

            g1.Children.Add(CreateFieldSlot(0, "Profile Name / Country", _privNameInput));
            g1.Children.Add(CreateFieldSlot(2, "Protocol", _privProtoCombo));
            g1.Children.Add(CreateFieldSlot(4, "Server Host:Port or http(s)://host:port", _privHostInput));
            st.Children.Add(g1);

            Grid g2 = new Grid();
            g2.Margin = new Thickness(0, 8, 0, 0);
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _privUserInput = CreateDarkTextBox("username");
            _privPassInput = new PasswordBox { Height = 28, Background = BrushHex("#14171D"), Foreground = Brushes.White, BorderBrush = BrushHex("#2E3542") };
            _privPskInput = new PasswordBox { Height = 28, Background = BrushHex("#14171D"), Foreground = Brushes.White, BorderBrush = BrushHex("#2E3542") };

            g2.Children.Add(CreateFieldSlot(0, "Username (Optional for Proxy)", _privUserInput));
            g2.Children.Add(CreateFieldSlot(2, "Password (DPAPI Encrypted)", _privPassInput));
            g2.Children.Add(CreateFieldSlot(4, "L2TP Pre-Shared Key (Optional)", _privPskInput));

            Button savePrivBtn = CreateActionPillButton("Save Server", "#10B981", "#FFFFFF");
            savePrivBtn.VerticalAlignment = VerticalAlignment.Bottom;
            savePrivBtn.Height = 28;
            savePrivBtn.Click += delegate { SaveNewPrivateServerFromForm(); };
            Grid.SetColumn(savePrivBtn, 6);
            g2.Children.Add(savePrivBtn);

            st.Children.Add(g2);
            card.Child = st;
            return card;
        }

        // =====================================================================
        // TAB 2: SPEED BENCHMARK, LIVE GEOIP/LEAK AUDIT & ENGINE ACTIVITY LOGS
        // =====================================================================
        private UIElement BuildShihabSpeedDiagnosticsView()
        {
            Grid root = new Grid();
            root.Margin = new Thickness(24, 6, 24, 16);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            DockPanel topBar = new DockPanel();
            topBar.Margin = new Thickness(0, 0, 0, 10);

            StackPanel topBtns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            _btnDiagTabAudit = CreateActionPillButton("Speed & Live IP Audit", "#1DA1F2", "#FFFFFF");
            _btnDiagTabAudit.Margin = new Thickness(0, 0, 6, 0);
            _btnDiagTabAudit.Click += delegate { SetDiagSubTab(0); };

            _btnDiagTabLogs = CreateActionPillButton("Engine Activity Logs", "#242A35", "#9BA3B0");
            _btnDiagTabLogs.Click += delegate { SetDiagSubTab(1); };

            topBtns.Children.Add(_btnDiagTabAudit);
            topBtns.Children.Add(_btnDiagTabLogs);
            DockPanel.SetDock(topBtns, Dock.Right);
            topBar.Children.Add(topBtns);

            StackPanel titles = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            titles.Children.Add(new TextBlock
            {
                Text = "Speed, Live GeoIP Leak Audit & Engine Logs",
                FontSize = 16.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            titles.Children.Add(new TextBlock
            {
                Text = "CDN throughput, ISP/ASN egress audit, country clock & categorized tunnel logs",
                FontSize = 11.2,
                Foreground = BrushHex("#8A93A2"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0)
            });
            topBar.Children.Add(titles);

            Grid.SetRow(topBar, 0);
            root.Children.Add(topBar);

            Grid contentSwitchHost = new Grid();
            _diagAuditPanel = BuildDiagAuditSubPanel();
            _diagLogsPanel = BuildDiagLogsSubPanel();
            _diagLogsPanel.Visibility = Visibility.Collapsed;

            contentSwitchHost.Children.Add(_diagAuditPanel);
            contentSwitchHost.Children.Add(_diagLogsPanel);
            Grid.SetRow(contentSwitchHost, 1);
            root.Children.Add(contentSwitchHost);

            return root;
        }

        private UIElement BuildDiagAuditSubPanel()
        {
            StackPanel sp = new StackPanel();

            Grid metricsGrid = new Grid();
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            metricsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border cSpeed = BuildDiagStatCard("Download Speed", "0.0 Mbps", "Cloudflare Edge Stream", out _diagSpeedBigText, out _diagEndpointText);
            TextBlock dummy1, dummy2, dummy3;
            Border cPing = BuildDiagStatCard("Tunnel / Gateway Ping", "24 ms", "TCP Handshake RTT", out _diagPingBigText, out dummy1);
            Border cJitter = BuildDiagStatCard("Connection Jitter", "2 ms", "Packet Delay Variance", out _diagJitterBigText, out dummy2);
            Border cPubIp = BuildDiagStatCard("Observed Egress IPv4", "Detecting...", "Live Internet-Facing Address", out _diagPublicIpBigText, out dummy3);

            Grid.SetRow(cSpeed, 0); Grid.SetColumn(cSpeed, 0);
            Grid.SetRow(cPing, 0); Grid.SetColumn(cPing, 2);
            Grid.SetRow(cJitter, 2); Grid.SetColumn(cJitter, 0);
            Grid.SetRow(cPubIp, 2); Grid.SetColumn(cPubIp, 2);

            metricsGrid.Children.Add(cSpeed);
            metricsGrid.Children.Add(cPing);
            metricsGrid.Children.Add(cJitter);
            metricsGrid.Children.Add(cPubIp);
            sp.Children.Add(metricsGrid);

            // Live GeoIP, ISP/ASN, Country Clock & Leak Verification Card
            Border geoCard = CreateBentoCard();
            geoCard.Margin = new Thickness(0, 10, 0, 0);
            geoCard.Padding = new Thickness(16, 12, 16, 12);

            StackPanel geoStack = new StackPanel();
            DockPanel geoHdr = new DockPanel();

            StackPanel geoLeftHdr = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            geoLeftHdr.Children.Add(CreateCardHeaderTitle("Live GeoIP, ISP/ASN & IP Leak Verification"));

            _geoAuditBadgeBorder = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(10, 0, 0, 0),
                Background = BrushHex("#1E222A"),
                BorderBrush = BrushHex("#2D333E"),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            _geoAuditBadgeText = new TextBlock
            {
                Text = "● AUDITING ROUTE...",
                FontSize = 10.0,
                FontWeight = FontWeights.Bold,
                Foreground = BrushHex("#9BA3B0")
            };
            _geoAuditBadgeBorder.Child = _geoAuditBadgeText;
            geoLeftHdr.Children.Add(_geoAuditBadgeBorder);
            DockPanel.SetDock(geoLeftHdr, Dock.Left);
            geoHdr.Children.Add(geoLeftHdr);

            StackPanel geoActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _diagRunSpeedBtn = CreateActionPillButton("Run Speed Test", "#1DA1F2", "#FFFFFF");
            _diagRunSpeedBtn.Padding = new Thickness(10, 4, 10, 4);
            _diagRunSpeedBtn.FontSize = 11.2;
            _diagRunSpeedBtn.Margin = new Thickness(0, 0, 6, 0);
            _diagRunSpeedBtn.Click += delegate { StartShihabSpeedBenchmarkAsync(); };

            _diagVerifyGeoBtn = CreateActionPillButton("Verify IP Now", "#242A35", "#E2E8F0");
            _diagVerifyGeoBtn.Padding = new Thickness(10, 4, 10, 4);
            _diagVerifyGeoBtn.FontSize = 11.2;
            _diagVerifyGeoBtn.Margin = new Thickness(0, 0, 6, 0);
            _diagVerifyGeoBtn.Click += delegate { VerifyShihabGeoIpInBackgroundAsync(false); };

            Button copyIpBtn = CreateActionPillButton("Copy IP", "#242A35", "#9BA3B0");
            copyIpBtn.Padding = new Thickness(9, 4, 9, 4);
            copyIpBtn.FontSize = 11.2;
            copyIpBtn.Click += delegate
            {
                if (!string.IsNullOrEmpty(_shihabObservedPublicIp))
                {
                    try
                    {
                        Clipboard.SetText(_shihabObservedPublicIp);
                        _bottomBannerStatusText.Text = "Copied egress IP " + _shihabObservedPublicIp + " to clipboard.";
                    }
                    catch
                    {
                    }
                }
            };

            geoActions.Children.Add(_diagRunSpeedBtn);
            geoActions.Children.Add(_diagVerifyGeoBtn);
            geoActions.Children.Add(copyIpBtn);
            DockPanel.SetDock(geoActions, Dock.Right);
            geoHdr.Children.Add(geoActions);
            geoStack.Children.Add(geoHdr);

            Grid geoRows = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            geoRows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            geoRows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            geoRows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            geoRows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            geoRows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });

            _geoRouteValueText = new TextBlock { Text = "Origin ISP -> Direct Local Route", FontSize = 12.0, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
            _geoIspAsnValueText = new TextBlock { Text = "Detecting ASN / Carrier...", FontSize = 12.0, FontWeight = FontWeights.SemiBold, Foreground = BrushHex("#7DD3FC"), TextTrimming = TextTrimming.CharacterEllipsis };
            _geoClockValueText = new TextBlock { Text = "--:-- (UTC)", FontSize = 12.0, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };

            geoRows.Children.Add(CreateFieldSlot(0, "Origin ISP  →  Active Egress Route", _geoRouteValueText));
            geoRows.Children.Add(CreateFieldSlot(2, "Observed Egress ISP / ASN", _geoIspAsnValueText));
            geoRows.Children.Add(CreateFieldSlot(4, "Egress Region Clock", _geoClockValueText));

            geoStack.Children.Add(geoRows);
            geoCard.Child = geoStack;
            sp.Children.Add(geoCard);

            return sp;
        }

        private UIElement BuildDiagLogsSubPanel()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            DockPanel filterRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            StackPanel catStack = new StackPanel { Orientation = Orientation.Horizontal };
            _logFilterButtons.Clear();
            string[] cats = new string[] { "ALL", "TUNNEL", "HANDSHAKE", "VERIFY", "SYSTEM" };
            for (int i = 0; i < cats.Length; i++)
            {
                string c = cats[i];
                Button b = CreateMiniFilterButton(c, c == "ALL");
                b.Click += delegate
                {
                    _activeLogCategoryFilter = c;
                    foreach (KeyValuePair<string, Button> kv in _logFilterButtons)
                    {
                        ApplyMiniBtnState(kv.Value, string.Equals(kv.Key, _activeLogCategoryFilter, StringComparison.OrdinalIgnoreCase));
                    }
                    RebuildEngineLogsVisual();
                };
                _logFilterButtons[c] = b;
                catStack.Children.Add(b);
            }
            DockPanel.SetDock(catStack, Dock.Left);
            filterRow.Children.Add(catStack);

            Button clearBtn = CreateMiniFilterButton("Clear Logs", false);
            clearBtn.HorizontalAlignment = HorizontalAlignment.Right;
            clearBtn.Margin = new Thickness(0);
            clearBtn.Click += delegate
            {
                _shihabEngineLogs.Clear();
                AppendShihabEngineLog("SYSTEM", "Activity log cleared by user.");
            };
            DockPanel.SetDock(clearBtn, Dock.Right);
            filterRow.Children.Add(clearBtn);

            Grid.SetRow(filterRow, 0);
            g.Children.Add(filterRow);

            Border logCard = CreateBentoCard();
            logCard.Padding = new Thickness(10, 8, 10, 8);
            logCard.Background = BrushHex("#15181E");

            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _engineLogsStack = new StackPanel();
            sv.Content = _engineLogsStack;
            logCard.Child = sv;

            Grid.SetRow(logCard, 1);
            g.Children.Add(logCard);
            return g;
        }

        private void SetDiagSubTab(int mode)
        {
            _diagSubTabMode = mode;
            if (_btnDiagTabAudit != null)
            {
                _btnDiagTabAudit.Background = BrushHex(mode == 0 ? "#1DA1F2" : "#242A35");
                _btnDiagTabAudit.Foreground = BrushHex(mode == 0 ? "#FFFFFF" : "#9BA3B0");
            }
            if (_btnDiagTabLogs != null)
            {
                _btnDiagTabLogs.Background = BrushHex(mode == 1 ? "#1DA1F2" : "#242A35");
                _btnDiagTabLogs.Foreground = BrushHex(mode == 1 ? "#FFFFFF" : "#9BA3B0");
            }
            if (_diagAuditPanel != null) _diagAuditPanel.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_diagLogsPanel != null) _diagLogsPanel.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (mode == 1)
            {
                RebuildEngineLogsVisual();
            }
        }

        private static Border BuildDiagStatCard(string title, string initialBig, string sub, out TextBlock bigValueOut, out TextBlock subOut)
        {
            Border card = CreateBentoCard();
            card.Padding = new Thickness(15, 11, 15, 11);
            StackPanel st = new StackPanel();
            st.Children.Add(CreateCardHeaderTitle(title));
            bigValueOut = new TextBlock
            {
                Text = initialBig,
                FontSize = 23,
                FontWeight = FontWeights.Bold,
                Foreground = BrushHex("#38BDF8"),
                Margin = new Thickness(0, 6, 0, 3)
            };
            st.Children.Add(bigValueOut);
            subOut = new TextBlock
            {
                Text = sub,
                FontSize = 11.2,
                Foreground = BrushHex("#8A93A2")
            };
            st.Children.Add(subOut);
            card.Child = st;
            return card;
        }

        // =====================================================================
        // TAB 3: SECURITY SHIELD SETTINGS (ROTATION, KILL-SWITCH, DNS, SPLIT)
        // =====================================================================
        private UIElement BuildShihabSecuritySettingsView()
        {
            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.Margin = new Thickness(24, 6, 24, 16);

            StackPanel root = new StackPanel();

            root.Children.Add(new TextBlock
            {
                Text = "Shield Security, Dynamic IP Rotation, DNS & Split-Tunneling",
                FontSize = 16.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Card 0: Dynamic IP Rotation & Extended Global Relay Catalog
            Border rotCard = CreateBentoCard();
            rotCard.Margin = new Thickness(0, 0, 0, 10);
            StackPanel rotStack = new StackPanel();
            rotStack.Children.Add(CreateCardHeaderTitle("Dynamic IP Rotation & Multi-Source Relay Pool"));

            _chkDynamicIpRotation = CreateStyledCheckBox(
                "Dynamic IP Rotation — ACTIVE (Automatically rotate to a fresh unvisited gateway IP on reconnect or 'Rotate IP')",
                _shihabConfig.DynamicIpRotation);
            _chkDynamicIpRotation.Checked += OnSettingsControlChanged;
            _chkDynamicIpRotation.Unchecked += OnSettingsControlChanged;
            rotStack.Children.Add(_chkDynamicIpRotation);

            _chkExtendedCatalog = CreateStyledCheckBox(
                "Extended Global Relay Catalog (Unlock up to 120 verified public relays per country across 70+ countries)",
                _shihabConfig.ShowExtendedCatalog);
            _chkExtendedCatalog.Checked += OnSettingsControlChanged;
            _chkExtendedCatalog.Unchecked += OnSettingsControlChanged;
            rotStack.Children.Add(_chkExtendedCatalog);

            rotCard.Child = rotStack;
            root.Children.Add(rotCard);

            // Card A: Kill-Switch Protection
            Border ksCard = CreateBentoCard();
            ksCard.Margin = new Thickness(0, 0, 0, 10);
            StackPanel ksStack = new StackPanel();
            ksStack.Children.Add(CreateCardHeaderTitle("Kill-Switch & Auto-Recovery"));

            _chkAutoReconnectKs = CreateStyledCheckBox(
                "Instant Reconnect Watchdog (Automatically redials immediately if VPN tunnel drops)",
                _shihabConfig.KillSwitchAutoReconnect);
            _chkAutoReconnectKs.Checked += OnSettingsControlChanged;
            _chkAutoReconnectKs.Unchecked += OnSettingsControlChanged;
            ksStack.Children.Add(_chkAutoReconnectKs);

            DockPanel fwRow = new DockPanel();
            fwRow.Margin = new Thickness(0, 6, 0, 0);

            Button btnResetFw = CreateActionPillButton("Reset Firewall Rules", "#272D38", "#E2E8F0");
            btnResetFw.HorizontalAlignment = HorizontalAlignment.Right;
            btnResetFw.VerticalAlignment = VerticalAlignment.Center;
            btnResetFw.Margin = new Thickness(12, 0, 0, 0);
            btnResetFw.Click += delegate
            {
                _shihabShieldService.DisarmShihabFirewallKillSwitch(_shihabConfig);
                AppendShihabEngineLog("SYSTEM", "Cleared Shihab Shield Windows Firewall lock rules.");
                _bottomBannerStatusText.Text = "Clean: No Shihab Shield firewall block rules active.";
            };
            DockPanel.SetDock(btnResetFw, Dock.Right);
            fwRow.Children.Add(btnResetFw);

            _chkStrictFirewallKs = CreateStyledCheckBox(
                "Strict Windows Firewall Endpoint Lock (Whitelists active VPN IP via Windows Firewall; auto-cleans on exit)",
                _shihabConfig.StrictFirewallKillSwitch);
            _chkStrictFirewallKs.Checked += OnSettingsControlChanged;
            _chkStrictFirewallKs.Unchecked += OnSettingsControlChanged;
            fwRow.Children.Add(_chkStrictFirewallKs);
            ksStack.Children.Add(fwRow);

            ksCard.Child = ksStack;
            root.Children.Add(ksCard);

            // Card B: Tunnel-Scoped Secure DNS & Protocol
            Border dnsCard = CreateBentoCard();
            dnsCard.Margin = new Thickness(0, 0, 0, 10);
            Grid dnsGrid = new Grid();
            dnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            dnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel leftDns = new StackPanel();
            leftDns.Children.Add(CreateCardHeaderTitle("Tunnel-Scoped Secure DNS"));
            _cmbDnsPreset = CreateDarkComboBox(32);
            _cmbDnsPreset.Margin = new Thickness(0, 8, 0, 5);
            _cmbDnsPreset.Items.Add("Cloudflare Privacy DNS (1.1.1.1 / 1.0.0.1)");
            _cmbDnsPreset.Items.Add("AdGuard Ad & Tracker Block (94.140.14.14)");
            _cmbDnsPreset.Items.Add("Quad9 Malware Shield (9.9.9.9)");
            _cmbDnsPreset.Items.Add("Google Public DNS (8.8.8.8)");
            _cmbDnsPreset.Items.Add("Custom DNS");
            _cmbDnsPreset.Items.Add("Automatic Tunnel DNS");
            _cmbDnsPreset.SelectedIndex = Math.Max(0, Math.Min(5, (int)_shihabConfig.DnsPreset));
            _cmbDnsPreset.SelectionChanged += OnSettingsControlChanged;
            leftDns.Children.Add(_cmbDnsPreset);
            leftDns.Children.Add(new TextBlock
            {
                Text = "Applied directly to the Windows RAS VPN interface (never alters physical NIC DNS).",
                FontSize = 10.8,
                Foreground = BrushHex("#8A93A2"),
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(leftDns, 0);
            dnsGrid.Children.Add(leftDns);

            StackPanel rightProto = new StackPanel();
            rightProto.Children.Add(CreateCardHeaderTitle("Public Relay Protocol"));
            _cmbPublicProto = CreateDarkComboBox(32);
            _cmbPublicProto.Margin = new Thickness(0, 8, 0, 5);
            _cmbPublicProto.Items.Add("MS-SSTP over TLS 443 (Built-in Windows)");
            _cmbPublicProto.Items.Add("OpenVPN (.ovpn via openvpn.exe)");
            _cmbPublicProto.SelectedIndex = _shihabConfig.PreferredPublicProtocol == ShihabVpnProtocol.OpenVpn ? 1 : 0;
            _cmbPublicProto.SelectionChanged += OnSettingsControlChanged;
            rightProto.Children.Add(_cmbPublicProto);
            rightProto.Children.Add(new TextBlock
            {
                Text = "MS-SSTP connects natively on Windows 10/11 over TCP port 443 with Let's Encrypt TLS.",
                FontSize = 10.8,
                Foreground = BrushHex("#8A93A2"),
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(rightProto, 2);
            dnsGrid.Children.Add(rightProto);

            dnsCard.Child = dnsGrid;
            root.Children.Add(dnsCard);

            // Card C: Split-Tunneling & App Behavior
            Border splitCard = CreateBentoCard();
            StackPanel splitStack = new StackPanel();
            splitStack.Children.Add(CreateCardHeaderTitle("Split-Tunneling & Application Lifecycle"));

            _chkSplitTunnel = CreateStyledCheckBox(
                "Enable Split-Tunneling Mode (Keep default LAN/Internet gateway direct; route only VPN subnets through tunnel)",
                _shihabConfig.SplitTunnelingEnabled);
            _chkSplitTunnel.Checked += OnSettingsControlChanged;
            _chkSplitTunnel.Unchecked += OnSettingsControlChanged;
            splitStack.Children.Add(_chkSplitTunnel);

            _txtSplitBypassApps = CreateDarkTextBox(string.Join(", ", _shihabConfig.SplitTunnelBypassApps));
            _txtSplitBypassApps.Margin = new Thickness(0, 6, 0, 8);
            _txtSplitBypassApps.LostFocus += OnSettingsControlChanged;
            splitStack.Children.Add(new TextBlock
            {
                Text = "Bypassed local apps / notes (comma-separated, e.g. chrome.exe, steam.exe):",
                FontSize = 11.0,
                Foreground = BrushHex("#8E96A4"),
                Margin = new Thickness(0, 4, 0, 0)
            });
            splitStack.Children.Add(_txtSplitBypassApps);

            WrapPanel appChecks = new WrapPanel();
            _chkMinimizeTray = CreateStyledCheckBox("Minimize to System Tray on Close (✕)", _shihabConfig.MinimizeToTrayOnClose);
            _chkMinimizeTray.Margin = new Thickness(0, 4, 24, 0);
            _chkMinimizeTray.Checked += OnSettingsControlChanged;
            _chkMinimizeTray.Unchecked += OnSettingsControlChanged;

            _chkAutoConnectStartup = CreateStyledCheckBox("Auto-Connect Fastest Server on App Launch", _shihabConfig.AutoConnectOnLaunch);
            _chkAutoConnectStartup.Margin = new Thickness(0, 4, 24, 0);
            _chkAutoConnectStartup.Checked += OnSettingsControlChanged;
            _chkAutoConnectStartup.Unchecked += OnSettingsControlChanged;

            _chkDisconnectOnExit = CreateStyledCheckBox("Auto-Cutoff Tunnel & Restore System Proxy on Exit", _shihabConfig.DisconnectTunnelOnExit);
            _chkDisconnectOnExit.Margin = new Thickness(0, 4, 0, 0);
            _chkDisconnectOnExit.Checked += OnSettingsControlChanged;
            _chkDisconnectOnExit.Unchecked += OnSettingsControlChanged;

            appChecks.Children.Add(_chkMinimizeTray);
            appChecks.Children.Add(_chkAutoConnectStartup);
            appChecks.Children.Add(_chkDisconnectOnExit);
            splitStack.Children.Add(appChecks);

            splitCard.Child = splitStack;
            root.Children.Add(splitCard);

            sv.Content = root;
            return sv;
        }

        // =====================================================================
        // TAB 4: ABOUT DEVELOPER (MEHEDI HASAN SHIHAB)
        // =====================================================================
        private UIElement BuildShihabAboutDeveloperView()
        {
            Grid host = new Grid();
            host.Margin = new Thickness(24, 8, 24, 16);

            Border card = CreateBentoCard();
            card.Padding = new Thickness(24, 20, 24, 20);

            StackPanel st = new StackPanel();

            DockPanel topRow = new DockPanel();
            StackPanel nameStack = new StackPanel();
            nameStack.Children.Add(new TextBlock
            {
                Text = "Shihab Shield VPN — Open-Source Edition v1.2.0",
                FontSize = 17.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            nameStack.Children.Add(new TextBlock
            {
                Text = "Developed & Engineered by Mehedi Hasan Shihab (Web & Software Developer)",
                FontSize = 13.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushHex("#38BDF8"),
                Margin = new Thickness(0, 4, 0, 0)
            });
            DockPanel.SetDock(nameStack, Dock.Left);
            topRow.Children.Add(nameStack);

            Border mitBadge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = BrushHex("#1F3349"),
                BorderBrush = BrushHex("#1DA1F2"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "AnyCPU • Open Pro • MIT",
                    FontSize = 11.0,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = BrushHex("#7DD3FC")
                }
            };
            DockPanel.SetDock(mitBadge, Dock.Right);
            topRow.Children.Add(mitBadge);
            st.Children.Add(topRow);

            st.Children.Add(new TextBlock
            {
                Text = "Focused on building fast, clean, user-centric desktop tools and full-stack web applications. Shihab Shield VPN v1.2.0 unifies an extended 70+ country open relay catalog, interactive 3D Fibonacci Globe & Shield route visualization, Dynamic IP Rotation, live GeoIP/ISP/ASN & IP-leak auditing, 1-click .ovpn profile export, and private MS-SSTP, IKEv2, L2TP/IPsec, PPTP, WireGuard (.conf), OpenVPN (.ovpn), and DPAPI-secured HTTP/HTTPS System Proxy support.",
                FontSize = 12.2,
                Foreground = BrushHex("#C4CCD8"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 16)
            });

            Border linksBox = new Border
            {
                Background = BrushHex("#16191F"),
                BorderBrush = BrushHex("#272C36"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12)
            };

            StackPanel rows = new StackPanel();
            rows.Children.Add(CreateDeveloperInfoRow("Developer:", "Mehedi Hasan Shihab (Web & Software Developer)", null));
            rows.Children.Add(CreateDeveloperInfoRow("Portfolio:", "https://sshihabb007.github.io/", "https://sshihabb007.github.io/"));
            rows.Children.Add(CreateDeveloperInfoRow("Email Me:", "sshihabb007@gmail.com", "mailto:sshihabb007@gmail.com?subject=Shihab%20Shield%20VPN"));
            rows.Children.Add(CreateDeveloperInfoRow("GitHub:", "github.com/sshihabb007 (@sshihabb007)", "https://github.com/sshihabb007"));
            rows.Children.Add(CreateDeveloperInfoRow("LinkedIn:", "linkedin.com/in/mehedi-hasan-shihab", "https://www.linkedin.com/in/mehedi-hasan-shihab"));
            linksBox.Child = rows;
            st.Children.Add(linksBox);

            StackPanel actionPills = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 16, 0, 12)
            };

            Button bPort = CreateActionPillButton("Visit Portfolio", "#1DA1F2", "#FFFFFF");
            bPort.Margin = new Thickness(0, 0, 10, 0);
            bPort.Click += delegate { OpenExternalUrlSafe("https://sshihabb007.github.io/"); };

            Button bMail = CreateActionPillButton("Email Me", "#252B36", "#E2E8F0");
            bMail.Margin = new Thickness(0, 0, 10, 0);
            bMail.Click += delegate { OpenExternalUrlSafe("mailto:sshihabb007@gmail.com?subject=Shihab%20Shield%20VPN"); };

            Button bGh = CreateActionPillButton("GitHub (@sshihabb007)", "#252B36", "#E2E8F0");
            bGh.Margin = new Thickness(0, 0, 10, 0);
            bGh.Click += delegate { OpenExternalUrlSafe("https://github.com/sshihabb007"); };

            Button bIn = CreateActionPillButton("LinkedIn", "#252B36", "#E2E8F0");
            bIn.Click += delegate { OpenExternalUrlSafe("https://www.linkedin.com/in/mehedi-hasan-shihab"); };

            actionPills.Children.Add(bPort);
            actionPills.Children.Add(bMail);
            actionPills.Children.Add(bGh);
            actionPills.Children.Add(bIn);
            st.Children.Add(actionPills);

            st.Children.Add(new TextBlock
            {
                Text = "Copyright © 2026 Mehedi Hasan Shihab (sshihabb007) • Open-Source MIT License",
                FontSize = 11.2,
                Foreground = BrushHex("#7A8494")
            });

            card.Child = st;
            host.Children.Add(card);
            return host;
        }

        private static Grid CreateDeveloperInfoRow(string label, string text, string url)
        {
            Grid g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lbl = new TextBlock
            {
                Text = label,
                FontSize = 12.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushHex("#8E96A4")
            };
            Grid.SetColumn(lbl, 0);
            g.Children.Add(lbl);

            TextBlock val = new TextBlock
            {
                Text = text,
                FontSize = 12.0,
                Foreground = string.IsNullOrEmpty(url) ? Brushes.White : BrushHex("#38BDF8"),
                Cursor = string.IsNullOrEmpty(url) ? Cursors.Arrow : Cursors.Hand
            };
            if (!string.IsNullOrEmpty(url))
            {
                val.MouseLeftButtonDown += delegate { OpenExternalUrlSafe(url); };
            }
            Grid.SetColumn(val, 1);
            g.Children.Add(val);
            return g;
        }

        // =====================================================================
        // RUNTIME ACTIONS: CONNECT, DISCONNECT, SPEED TEST, SERVER SELECTION
        // =====================================================================
        private void ToggleShihabConnectionAsync()
        {
            if (_shihabState == ShihabConnectionState.Connecting || _shihabState == ShihabConnectionState.Reconnecting)
            {
                if (_shihabConnectCts != null)
                {
                    _shihabConnectCts.Cancel();
                }
                return;
            }

            if (_shihabState == ShihabConnectionState.Connected)
            {
                DisconnectShihabAsync();
                return;
            }

            StartShihabConnectAsync(false);
        }

        private async void StartShihabConnectAsync(bool isAutoReconnect)
        {
            if (_shihabResolvedTarget == null)
            {
                _shihabResolvedTarget = _shihabRelayService.ResolveBestShihabTargetNode(_shihabConfig, _shihabPublicNodes);
            }
            if (_shihabResolvedTarget == null)
            {
                _bottomBannerStatusText.Text = "Fetching public VPN relays first...";
                AppendShihabEngineLog("SYSTEM", "Public relay cache empty — fetching live relay pool before connect...");
                await _shihabRelayService.RefreshShihabPublicRelaysAsync(true, _shihabConfig.ShowExtendedCatalog);
                _shihabPublicNodes = new List<ShihabVpnServerNode>(_shihabRelayService.GetCurrentPublicRelays());
                _shihabResolvedTarget = _shihabRelayService.ResolveBestShihabTargetNode(_shihabConfig, _shihabPublicNodes);
            }
            if (_shihabResolvedTarget == null)
            {
                _bottomBannerStatusText.Text = "No servers available. Add a Private Server or refresh Public Relays.";
                AppendShihabEngineLog("TUNNEL", "Connect aborted: No public or private gateway profiles available.");
                return;
            }

            if (_shihabConnectCts != null)
            {
                _shihabConnectCts.Cancel();
                _shihabConnectCts.Dispose();
            }
            _shihabConnectCts = new CancellationTokenSource();
            CancellationToken token = _shihabConnectCts.Token;

            _shihabState = isAutoReconnect ? ShihabConnectionState.Reconnecting : ShihabConnectionState.Connecting;
            _shihabShieldService.DisarmShihabFirewallKillSwitch(_shihabConfig);
            AppendShihabEngineLog(
                "TUNNEL",
                string.Format(
                    "{0} {1} ({2} • {3})...",
                    isAutoReconnect ? "Auto-reconnecting to" : "Initiating connection to",
                    _shihabResolvedTarget.CountryLong,
                    _shihabResolvedTarget.ProtocolBadgeText,
                    _shihabResolvedTarget.EffectiveDialAddress));
            RefreshAllShihabViews();

            ShihabResult<ShihabVpnServerNode> res = await _shihabVpnEngine.ConnectShihabTunnelAsync(
                _shihabResolvedTarget,
                _shihabPublicNodes,
                _shihabConfig,
                delegate(string msg)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _bottomBannerStatusText.Text = msg;
                        AppendShihabEngineLog("HANDSHAKE", msg);
                    }));
                },
                token);

            if (res.IsSuccess && res.Value != null)
            {
                _shihabResolvedTarget = res.Value;
                _shihabState = ShihabConnectionState.Connected;
                _shihabConnectedSinceUtc = DateTime.UtcNow;
                _shihabTelemetryService.ResetThroughputBaseline();
                _shihabConfigService.SaveShihabConfig(_shihabConfig);

                if (_shihabConfig.StrictFirewallKillSwitch &&
                    _shihabResolvedTarget.Protocol != ShihabVpnProtocol.HttpSystemProxy)
                {
                    _shihabShieldService.ArmShihabFirewallKillSwitch(_shihabConfig, _shihabResolvedTarget.IpAddress);
                    AppendShihabEngineLog("SYSTEM", "Armed Strict Windows Firewall Kill-Switch for gateway " + _shihabResolvedTarget.IpAddress);
                }

                string establishedMsg = string.Format(
                    "Encrypted session active • {0} ({1}) in {2} ms",
                    _shihabResolvedTarget.CountryLong,
                    _shihabResolvedTarget.EffectiveDialAddress,
                    res.ElapsedMs);

                _bottomBannerStatusText.Text = establishedMsg;
                AppendShihabEngineLog("TUNNEL", establishedMsg);

                _shihabTray.ShowShihabNotification(
                    "Shihab Shield Connected",
                    string.Format("{0} ({1})", _shihabResolvedTarget.CountryLong, _shihabResolvedTarget.IpAddress));

                VerifyShihabGeoIpInBackgroundAsync(false);
            }
            else
            {
                _shihabState = ShihabConnectionState.Disconnected;
                string errText = res.ErrorCode == "CANCELLED"
                    ? "Connection cancelled."
                    : ("Unable to connect: " + res.ErrorMessage);
                _bottomBannerStatusText.Text = errText;
                AppendShihabEngineLog("TUNNEL", errText);
            }

            RefreshAllShihabViews();
        }

        private async void DisconnectShihabAsync()
        {
            _shihabState = ShihabConnectionState.Disconnecting;
            _bottomBannerStatusText.Text = "Disconnecting VPN / System Proxy session...";
            AppendShihabEngineLog("TUNNEL", "Tearing down active tunnel and restoring local network state...");
            RefreshAllShihabViews();

            await _shihabVpnEngine.DisconnectShihabTunnelAsync();
            _shihabShieldService.DisarmShihabFirewallKillSwitch(_shihabConfig);
            _shihabConfigService.SaveShihabConfig(_shihabConfig);

            _shihabState = ShihabConnectionState.Disconnected;
            _shihabConnectedSinceUtc = DateTime.MinValue;
            _shihabTelemetryService.ResetThroughputBaseline();
            _bottomBannerStatusText.Text = "Disconnected • Local network & proxy state restored";
            AppendShihabEngineLog("TUNNEL", "Disconnected cleanly • Direct ISP route restored.");
            VerifyShihabGeoIpInBackgroundAsync(true);
            RefreshAllShihabViews();
        }

        private async void RotateShihabIpNowAsync()
        {
            if (_shihabState == ShihabConnectionState.Connecting ||
                _shihabState == ShihabConnectionState.Reconnecting ||
                _shihabState == ShihabConnectionState.Disconnecting)
            {
                return;
            }

            if (_shihabPublicNodes == null || _shihabPublicNodes.Count == 0)
            {
                _bottomBannerStatusText.Text = "Syncing relays before rotating IP...";
                await _shihabRelayService.RefreshShihabPublicRelaysAsync(true, _shihabConfig.ShowExtendedCatalog);
                _shihabPublicNodes = new List<ShihabVpnServerNode>(_shihabRelayService.GetCurrentPublicRelays());
            }

            string rotCountry = "AUTO";
            if (_shihabResolvedTarget != null &&
                _shihabResolvedTarget.SourceKind == ShihabServerSourceKind.PublicOpenRelay &&
                !string.IsNullOrWhiteSpace(_shihabResolvedTarget.CountryShort))
            {
                rotCountry = _shihabResolvedTarget.CountryShort;
                ShihabPublicRelayService.RecordShihabRecentHost(
                    _shihabConfig,
                    rotCountry,
                    _shihabResolvedTarget.BareSstpHost);
                ShihabPublicRelayService.RecordShihabRecentHost(
                    _shihabConfig,
                    "AUTO",
                    _shihabResolvedTarget.BareSstpHost);
            }
            else if (!string.IsNullOrWhiteSpace(_shihabConfig.SelectedCountryCode))
            {
                rotCountry = _shihabConfig.SelectedCountryCode;
            }

            bool prevDyn = _shihabConfig.DynamicIpRotation;
            _shihabConfig.DynamicIpRotation = true;
            _shihabConfig.AutoSelectFastest = true;
            _shihabConfig.SelectedServerId = string.Empty;
            _shihabConfig.SelectedCountryCode = rotCountry;

            ShihabVpnServerNode nextNode = _shihabRelayService.ResolveBestShihabTargetNode(_shihabConfig, _shihabPublicNodes);
            _shihabConfig.DynamicIpRotation = prevDyn;
            _shihabConfigService.SaveShihabConfig(_shihabConfig);

            if (nextNode == null)
            {
                _bottomBannerStatusText.Text = "No alternate public relay found in region.";
                return;
            }

            _shihabResolvedTarget = nextNode;
            AppendShihabEngineLog(
                "TUNNEL",
                string.Format(
                    "Dynamic IP Rotation selected next gateway: {0} ({1})",
                    nextNode.CountryLong,
                    nextNode.EffectiveDialAddress));

            RefreshAllShihabViews();
            RebuildShihabServerRows();

            if (_shihabState == ShihabConnectionState.Connected)
            {
                if (_rotateIpActionText != null) _rotateIpActionText.Text = "↻ Rotating...";
                await _shihabVpnEngine.DisconnectShihabTunnelAsync();
                _shihabShieldService.DisarmShihabFirewallKillSwitch(_shihabConfig);
                _shihabState = ShihabConnectionState.Disconnected;
                if (_rotateIpActionText != null) _rotateIpActionText.Text = "↻ Rotate IP";
                StartShihabConnectAsync(true);
            }
            else
            {
                _bottomBannerStatusText.Text = string.Format(
                    "Rotated target gateway -> {0} ({1})",
                    nextNode.CountryLong,
                    nextNode.EffectiveDialAddress);
            }
        }

        private async void TriggerShihabRelayRefreshAsync(bool probeTop)
        {
            _bottomBannerStatusText.Text = "Syncing global public VPN relays...";
            ShihabResult<List<ShihabVpnServerNode>> res = await _shihabRelayService.RefreshShihabPublicRelaysAsync(
                probeTop,
                _shihabConfig.ShowExtendedCatalog);

            if (res.IsSuccess && res.Value != null)
            {
                _shihabPublicNodes = res.Value;
                if (_shihabState != ShihabConnectionState.Connected)
                {
                    _shihabResolvedTarget = _shihabRelayService.ResolveBestShihabTargetNode(_shihabConfig, _shihabPublicNodes);
                }
                List<ShihabCountryHubSummary> countries = ShihabPublicRelayService.BuildShihabCountrySummaries(_shihabPublicNodes);
                string summaryMsg = string.Format(
                    "Loaded {0} global relays across {1} countries + {2} private profiles",
                    _shihabPublicNodes.Count,
                    countries.Count,
                    _shihabConfig.PrivateServers.Count);
                _bottomBannerStatusText.Text = summaryMsg;
                AppendShihabEngineLog("SYSTEM", summaryMsg);
                RefreshAllShihabViews();
                RebuildShihabServerRows();
            }
            else
            {
                _bottomBannerStatusText.Text = "Public relay sync warning: " + res.ErrorMessage;
                AppendShihabEngineLog("SYSTEM", "Relay sync warning: " + res.ErrorMessage);
            }
        }

        private async void StartShihabSpeedBenchmarkAsync()
        {
            if (_shihabSpeedTestRunning)
            {
                if (_shihabSpeedCts != null) _shihabSpeedCts.Cancel();
                return;
            }

            _shihabSpeedTestRunning = true;
            _testSpeedActionText.Text = "Testing...";
            if (_diagRunSpeedBtn != null) _diagRunSpeedBtn.Content = "Cancel Test";
            _bottomBannerStatusText.Text = "Running live HTTPS CDN speed benchmark...";
            AppendShihabEngineLog("VERIFY", "Started live HTTPS CDN download & jitter benchmark...");

            if (_shihabSpeedCts != null) _shihabSpeedCts.Dispose();
            _shihabSpeedCts = new CancellationTokenSource();

            string pingHost = _shihabResolvedTarget != null ? _shihabResolvedTarget.IpAddress : "1.1.1.1";
            ShihabResult<ShihabSpeedTestReport> res = await _shihabTelemetryService.RunShihabSpeedBenchmarkAsync(
                pingHost,
                delegate(double liveMbps)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (liveMbps > _shihabConfig.PeakSpeedMbps)
                        {
                            _shihabConfig.PeakSpeedMbps = liveMbps;
                        }
                        UpdateSpeedGaugeVisual(liveMbps);
                        if (_diagSpeedBigText != null)
                        {
                            _diagSpeedBigText.Text = liveMbps.ToString("0.0", CultureInfo.InvariantCulture) + " Mbps";
                        }
                    }));
                },
                _shihabSpeedCts.Token);

            _shihabSpeedTestRunning = false;
            _testSpeedActionText.Text = "Test speed";
            if (_diagRunSpeedBtn != null) _diagRunSpeedBtn.Content = "Run Speed Test";

            if (res.IsSuccess && res.Value != null)
            {
                if (res.Value.DownloadMbps > _shihabConfig.PeakSpeedMbps)
                {
                    _shihabConfig.PeakSpeedMbps = res.Value.DownloadMbps;
                }
                if (_shihabResolvedTarget != null && res.Value.LatencyMs > 0)
                {
                    _shihabResolvedTarget.PingMs = res.Value.LatencyMs;
                }
                _shihabConfigService.SaveShihabConfig(_shihabConfig);

                if (_diagSpeedBigText != null) _diagSpeedBigText.Text = res.Value.DownloadMbps.ToString("0.0", CultureInfo.InvariantCulture) + " Mbps";
                if (_diagPingBigText != null) _diagPingBigText.Text = res.Value.LatencyMs + " ms";
                if (_diagJitterBigText != null) _diagJitterBigText.Text = res.Value.JitterMs + " ms";
                if (_diagEndpointText != null) _diagEndpointText.Text = res.Value.EndpointLabel;

                string doneMsg = string.Format(
                    "Speed benchmark complete: {0:0.0} Mbps • Ping {1} ms • Jitter {2} ms",
                    res.Value.DownloadMbps,
                    res.Value.LatencyMs,
                    res.Value.JitterMs);
                _bottomBannerStatusText.Text = doneMsg;
                AppendShihabEngineLog("VERIFY", doneMsg);
            }
            else if (res.ErrorCode != "CANCELLED")
            {
                _bottomBannerStatusText.Text = "Speed benchmark error: " + res.ErrorMessage;
                AppendShihabEngineLog("VERIFY", "Speed benchmark warning: " + res.ErrorMessage);
            }

            RefreshAllShihabViews();
        }

        private async void VerifyShihabGeoIpInBackgroundAsync(bool captureAsOriginIfDisconnected)
        {
            if (_shihabGeoVerifyRunning)
            {
                return;
            }

            _shihabGeoVerifyRunning = true;
            if (_diagVerifyGeoBtn != null)
            {
                _diagVerifyGeoBtn.Content = "Verifying...";
            }

            try
            {
                bool connectedNow = _shihabState == ShihabConnectionState.Connected;
                string baselineIp = (_shihabOriginGeo != null && !string.IsNullOrEmpty(_shihabOriginGeo.IpAddress))
                    ? _shihabOriginGeo.IpAddress
                    : string.Empty;
                string expectedIso = (connectedNow && _shihabResolvedTarget != null)
                    ? _shihabResolvedTarget.CountryShort
                    : string.Empty;

                ShihabResult<ShihabGeoIpReport> geoRes = await _shihabTelemetryService.VerifyShihabGeoIpAsync(
                    baselineIp,
                    expectedIso);

                if (geoRes.IsSuccess && geoRes.Value != null)
                {
                    ShihabGeoIpReport rep = geoRes.Value;
                    _shihabCurrentGeo = rep;
                    _shihabObservedPublicIp = rep.IpAddress;

                    if (!connectedNow && (captureAsOriginIfDisconnected || _shihabOriginGeo == null || string.IsNullOrEmpty(_shihabOriginGeo.IpAddress)))
                    {
                        _shihabOriginGeo = rep;
                        _shihabConfig.OriginGeoCache = rep;
                        _shihabConfigService.SaveShihabConfig(_shihabConfig);
                    }

                    string logDetail = string.Format(
                        "Egress IP {0} • {1}, {2} • ISP: {3}{4}",
                        rep.IpAddress,
                        string.IsNullOrEmpty(rep.City) ? "Gateway" : rep.City,
                        rep.CountryLong,
                        string.IsNullOrEmpty(rep.Isp) ? "Carrier" : rep.Isp,
                        rep.LeakDetected ? " [WARNING: ORIGIN IP MATCH]" : string.Empty);
                    AppendShihabEngineLog("VERIFY", logDetail);
                }
            }
            finally
            {
                _shihabGeoVerifyRunning = false;
                if (_diagVerifyGeoBtn != null)
                {
                    _diagVerifyGeoBtn.Content = "Verify IP Now";
                }
                RefreshAllShihabViews();
            }
        }

        private void AppendShihabEngineLog(string category, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            ShihabEngineLogEntry entry = new ShihabEngineLogEntry();
            entry.TimestampText = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            entry.Category = string.IsNullOrWhiteSpace(category) ? "SYSTEM" : category.Trim().ToUpperInvariant();
            entry.Message = message.Trim();

            _shihabEngineLogs.Add(entry);
            while (_shihabEngineLogs.Count > 90)
            {
                _shihabEngineLogs.RemoveAt(0);
            }

            if (_diagSubTabMode == 1 && _engineLogsStack != null)
            {
                RebuildEngineLogsVisual();
            }
        }

        private void RebuildEngineLogsVisual()
        {
            if (_engineLogsStack == null)
            {
                return;
            }

            _engineLogsStack.Children.Clear();
            string wanted = string.IsNullOrWhiteSpace(_activeLogCategoryFilter) ? "ALL" : _activeLogCategoryFilter;
            int shown = 0;

            for (int i = _shihabEngineLogs.Count - 1; i >= 0; i--)
            {
                ShihabEngineLogEntry item = _shihabEngineLogs[i];
                if (wanted != "ALL" && !string.Equals(item.Category, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                shown++;
                string badgeBg = "#242A35";
                string badgeFg = "#94A3B8";
                if (item.Category == "TUNNEL") { badgeBg = "#132F28"; badgeFg = "#34D399"; }
                else if (item.Category == "HANDSHAKE") { badgeBg = "#1E2C3E"; badgeFg = "#38BDF8"; }
                else if (item.Category == "VERIFY") { badgeBg = "#2C223E"; badgeFg = "#C084FC"; }
                else if (item.Category == "SYSTEM") { badgeBg = "#2B2618"; badgeFg = "#FBBF24"; }

                Border row = new Border
                {
                    Margin = new Thickness(0, 0, 0, 4),
                    Padding = new Thickness(8, 5, 8, 5),
                    CornerRadius = new CornerRadius(5),
                    Background = BrushHex("#1A1E26"),
                    BorderBrush = BrushHex("#242934"),
                    BorderThickness = new Thickness(1)
                };

                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                TextBlock ts = new TextBlock
                {
                    Text = item.TimestampText,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11.0,
                    Foreground = BrushHex("#7A8494"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(ts, 0);
                g.Children.Add(ts);

                Border catPill = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = BrushHex(badgeBg),
                    Padding = new Thickness(6, 1.5, 6, 1.5),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = item.Category,
                        FontSize = 9.8,
                        FontWeight = FontWeights.Bold,
                        Foreground = BrushHex(badgeFg)
                    }
                };
                Grid.SetColumn(catPill, 1);
                g.Children.Add(catPill);

                TextBlock msg = new TextBlock
                {
                    Text = item.Message,
                    FontSize = 11.3,
                    Foreground = BrushHex("#E2E8F0"),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(msg, 2);
                g.Children.Add(msg);

                row.Child = g;
                _engineLogsStack.Children.Add(row);
            }

            if (shown == 0)
            {
                _engineLogsStack.Children.Add(new TextBlock
                {
                    Text = "No log entries in category '" + wanted + "' yet.",
                    FontSize = 11.5,
                    Foreground = BrushHex("#7A8494"),
                    Margin = new Thickness(6, 8, 6, 8)
                });
            }
        }

        private void OnShihabSecondTick(object sender, EventArgs e)
        {
            _shihabTickCounter++;

            // 1. Update Connected Timer & Kill-Switch Watchdog
            if (_shihabState == ShihabConnectionState.Connected)
            {
                TimeSpan up = DateTime.UtcNow - _shihabConnectedSinceUtc;
                _serverConnectedTimerText.Text = up.TotalHours >= 1.0
                    ? string.Format("{0:00}:{1:00}:{2:00}", (int)up.TotalHours, up.Minutes, up.Seconds)
                    : string.Format("{0:00}:{1:00}", up.Minutes, up.Seconds);

                if (_shihabTickCounter % 3 == 0 && !_shihabVpnEngine.IsShihabTunnelCurrentlyActive())
                {
                    if (_shihabConfig.KillSwitchAutoReconnect)
                    {
                        _bottomBannerStatusText.Text = "Kill-Switch Watchdog: Tunnel dropped — reconnecting immediately...";
                        AppendShihabEngineLog("SYSTEM", "Kill-Switch Watchdog detected tunnel drop — triggering instant redial.");
                        StartShihabConnectAsync(true);
                        return;
                    }
                    else
                    {
                        _shihabState = ShihabConnectionState.Disconnected;
                        _shihabShieldService.DisarmShihabFirewallKillSwitch(_shihabConfig);
                        RefreshAllShihabViews();
                    }
                }
            }
            else
            {
                _serverConnectedTimerText.Text = "00:00";
            }

            // 2. Sample real RX/TX bytes (including HTTP/HTTPS proxy bridge when active) & live Mbps every 2 seconds
            if (_shihabTickCounter % 2 == 0)
            {
                long proxyRx, proxyTx;
                bool hasProxyTotals = _shihabVpnEngine.TryGetActiveProxyBridgeTotals(out proxyRx, out proxyTx);

                long dRx, dTx;
                double liveMbps;
                _shihabTelemetryService.SampleShihabThroughputDelta(
                    _shihabState == ShihabConnectionState.Connected,
                    hasProxyTotals ? proxyRx : -1L,
                    hasProxyTotals ? proxyTx : -1L,
                    out dRx,
                    out dTx,
                    out liveMbps);

                if (dRx > 0 || dTx > 0)
                {
                    ShihabConfigService.RecordShihabTrafficDelta(_shihabConfig, dRx, dTx, DateTime.UtcNow);
                }
                if (liveMbps > _shihabConfig.PeakSpeedMbps)
                {
                    _shihabConfig.PeakSpeedMbps = liveMbps;
                }

                UpdateDailyDataVisuals();
                if (!_shihabSpeedTestRunning)
                {
                    double shownMbps = liveMbps > 0.05 ? liveMbps : _shihabConfig.PeakSpeedMbps;
                    UpdateSpeedGaugeVisual(shownMbps);
                }

                if (_shihabTickCounter % 15 == 0)
                {
                    UpdateGeoAuditLabelsVisual(_shihabState == ShihabConnectionState.Connected);
                }

                if (_shihabTickCounter % 20 == 0)
                {
                    _shihabConfigService.SaveShihabConfig(_shihabConfig);
                }
            }
        }

        private void RefreshAllShihabViews()
        {
            bool connected = _shihabState == ShihabConnectionState.Connected;
            bool busy = _shihabState == ShihabConnectionState.Connecting ||
                        _shihabState == ShihabConnectionState.Reconnecting ||
                        _shihabState == ShihabConnectionState.Disconnecting;

            // Header Status Pill
            if (connected)
            {
                _headerStatusPillBorder.Background = BrushHex("#132F28");
                _headerStatusPillBorder.BorderBrush = BrushHex("#10B981");
                _headerStatusPillText.Text = "● Protected";
                _headerStatusPillText.Foreground = BrushHex("#34D399");
            }
            else if (busy)
            {
                _headerStatusPillBorder.Background = BrushHex("#2F2713");
                _headerStatusPillBorder.BorderBrush = BrushHex("#F59E0B");
                _headerStatusPillText.Text = "● Negotiating...";
                _headerStatusPillText.Foreground = BrushHex("#FBBF24");
            }
            else
            {
                _headerStatusPillBorder.Background = BrushHex("#1E222A");
                _headerStatusPillBorder.BorderBrush = BrushHex("#2D333E");
                _headerStatusPillText.Text = "● Disconnected";
                _headerStatusPillText.Foreground = BrushHex("#9BA3B0");
            }

            bool hasOriginCoords = _shihabOriginGeo != null && !string.IsNullOrEmpty(_shihabOriginGeo.CountryShort);
            double origLat = hasOriginCoords ? _shihabOriginGeo.Latitude : 23.68;
            double origLon = hasOriginCoords ? _shihabOriginGeo.Longitude : 90.35;

            // Virtual Location & Server Information Cards
            if (_shihabResolvedTarget != null)
            {
                bool isGlobalAuto = _shihabConfig.AutoSelectFastest &&
                                    _shihabResolvedTarget.SourceKind == ShihabServerSourceKind.PublicOpenRelay &&
                                    (string.IsNullOrWhiteSpace(_shihabConfig.SelectedCountryCode) ||
                                     string.Equals(_shihabConfig.SelectedCountryCode, "AUTO", StringComparison.OrdinalIgnoreCase));

                bool isCountryAuto = _shihabConfig.AutoSelectFastest &&
                                     _shihabResolvedTarget.SourceKind == ShihabServerSourceKind.PublicOpenRelay &&
                                     !isGlobalAuto;

                string badgeCode = _shihabResolvedTarget.CountryShort;
                _virtualFlagContainer.Child = ShihabFlagBadgeFactory.CreateCircularFlagBadge(badgeCode, 24);

                string cityPart = !string.IsNullOrEmpty(_shihabResolvedTarget.CityName)
                    ? (_shihabResolvedTarget.CityName + " • ")
                    : string.Empty;

                if (connected && hasOriginCoords)
                {
                    _virtualLocationTitleText.Text = _shihabResolvedTarget.CountryLong;
                    _virtualLocationSubText.Text = string.Format(
                        "{0} → {1}{2}",
                        _shihabOriginGeo.CountryShort,
                        cityPart,
                        _shihabResolvedTarget.EffectiveDialAddress);
                }
                else if (isGlobalAuto)
                {
                    _virtualLocationTitleText.Text = "Auto";
                    _virtualLocationSubText.Text = string.Format("{0} ({1})", _shihabResolvedTarget.CountryLong, _shihabResolvedTarget.EffectiveDialAddress);
                }
                else if (isCountryAuto)
                {
                    _virtualLocationTitleText.Text = _shihabResolvedTarget.CountryLong;
                    _virtualLocationSubText.Text = string.Format("{0}Smart Rotation • {1}", cityPart, _shihabResolvedTarget.EffectiveDialAddress);
                }
                else if (_shihabResolvedTarget.SourceKind == ShihabServerSourceKind.PublicOpenRelay)
                {
                    _virtualLocationTitleText.Text = _shihabResolvedTarget.CountryLong;
                    _virtualLocationSubText.Text = string.Format("{0}{1} • {2}", cityPart, _shihabResolvedTarget.HostName, _shihabResolvedTarget.EffectiveDialAddress);
                }
                else
                {
                    _virtualLocationTitleText.Text = _shihabResolvedTarget.CountryLong;
                    _virtualLocationSubText.Text = _shihabResolvedTarget.DisplayName;
                }

                _worldMapControl.SetShihabRoute(
                    origLat,
                    origLon,
                    hasOriginCoords,
                    _shihabResolvedTarget.Latitude,
                    _shihabResolvedTarget.Longitude,
                    connected);

                _serverProtocolBadgeText.Text = _shihabResolvedTarget.ProtocolBadgeText;
                _serverModeBadgeText.Text = _shihabResolvedTarget.SourceKind == ShihabServerSourceKind.PrivateCustomServer ? "Private" : "Open Pro";
                _serverIpValueText.Text = connected && !string.IsNullOrEmpty(_shihabObservedPublicIp)
                    ? _shihabObservedPublicIp
                    : _shihabResolvedTarget.IpAddress;
                _serverLoadValueText.Text = _shihabResolvedTarget.LoadPercent.ToString(CultureInfo.InvariantCulture);
                _serverLatencyValueText.Text = (_shihabResolvedTarget.PingMs > 0 ? _shihabResolvedTarget.PingMs : 24).ToString(CultureInfo.InvariantCulture);

                if (_diagPingBigText != null)
                {
                    _diagPingBigText.Text = (_shihabResolvedTarget.PingMs > 0 ? _shihabResolvedTarget.PingMs : 24) + " ms";
                }
            }
            else
            {
                _virtualLocationTitleText.Text = "Auto";
                _virtualLocationSubText.Text = "Syncing relays...";
                _serverIpValueText.Text = !string.IsNullOrEmpty(_shihabObservedPublicIp) ? _shihabObservedPublicIp : "—";
            }

            if (_diagPublicIpBigText != null && !string.IsNullOrEmpty(_shihabObservedPublicIp))
            {
                _diagPublicIpBigText.Text = _shihabObservedPublicIp;
            }

            UpdateGeoAuditLabelsVisual(connected);
            UpdateRegionAndSpeedFilterVisuals();

            // Daily Data + Speed + Network Cards
            UpdateDailyDataVisuals();
            UpdateSpeedGaugeVisual(_shihabConfig.PeakSpeedMbps);

            ShihabPhysicalNetworkInfo net = _shihabTelemetryService.DetectShihabPhysicalNetwork();
            _networkTitleText.Text = net.AdapterTitle;
            _networkSubText.Text = !string.IsNullOrEmpty(net.LocalIpv4)
                ? (net.InterfaceDescription + " • " + net.LocalIpv4)
                : net.InterfaceDescription;

            string pDns, sDns, dnsBadge;
            ShihabConfigService.ResolveShihabDnsPair(_shihabConfig, out pDns, out sDns, out dnsBadge);
            _networkDnsBadgeText.Text = dnsBadge;

            UpdateConnectOrbVisual();

            string locName = _shihabResolvedTarget != null ? _shihabResolvedTarget.CountryLong : "Auto";
            string ipShown = _serverIpValueText.Text;
            _shihabTray.UpdateShihabTrayState(connected, locName, ipShown);
        }

        private void UpdateGeoAuditLabelsVisual(bool connected)
        {
            if (_geoRouteValueText == null || _geoIspAsnValueText == null || _geoClockValueText == null)
            {
                return;
            }

            string originLabel = (_shihabOriginGeo != null && !string.IsNullOrEmpty(_shihabOriginGeo.CountryLong))
                ? string.Format("{0} ({1})", _shihabOriginGeo.CountryLong, _shihabOriginGeo.IpAddress)
                : "Local ISP";

            if (_shihabCurrentGeo != null && !string.IsNullOrEmpty(_shihabCurrentGeo.IpAddress))
            {
                string exitCity = !string.IsNullOrEmpty(_shihabCurrentGeo.City) ? (_shihabCurrentGeo.City + ", ") : string.Empty;
                if (connected)
                {
                    _geoRouteValueText.Text = string.Format(
                        "{0}  →  {1}{2}",
                        originLabel,
                        exitCity,
                        _shihabCurrentGeo.CountryLong);
                }
                else
                {
                    _geoRouteValueText.Text = string.Format("Direct ISP • {0}{1}", exitCity, _shihabCurrentGeo.CountryLong);
                }

                string asnPart = !string.IsNullOrEmpty(_shihabCurrentGeo.Asn) ? (_shihabCurrentGeo.Asn + " • ") : string.Empty;
                string ispPart = !string.IsNullOrEmpty(_shihabCurrentGeo.Isp) ? _shihabCurrentGeo.Isp : "Verified Carrier";
                _geoIspAsnValueText.Text = asnPart + ispPart;

                string ccForClock = !string.IsNullOrEmpty(_shihabCurrentGeo.CountryShort)
                    ? _shihabCurrentGeo.CountryShort
                    : (_shihabResolvedTarget != null ? _shihabResolvedTarget.CountryShort : "US");
                _geoClockValueText.Text = ShihabConfigParsers.FormatShihabCountryLocalClock(ccForClock, DateTime.UtcNow);

                if (_geoAuditBadgeBorder != null && _geoAuditBadgeText != null)
                {
                    if (connected && _shihabCurrentGeo.LeakDetected)
                    {
                        _geoAuditBadgeBorder.Background = BrushHex("#3A181E");
                        _geoAuditBadgeBorder.BorderBrush = BrushHex("#EF4444");
                        _geoAuditBadgeText.Text = "● IP LEAK WARNING";
                        _geoAuditBadgeText.Foreground = BrushHex("#FCA5A5");
                    }
                    else if (connected)
                    {
                        _geoAuditBadgeBorder.Background = BrushHex("#132F28");
                        _geoAuditBadgeBorder.BorderBrush = BrushHex("#10B981");
                        _geoAuditBadgeText.Text = "● ZERO-LEAK MASKED (" + _shihabCurrentGeo.CountryShort + ")";
                        _geoAuditBadgeText.Foreground = BrushHex("#34D399");
                    }
                    else
                    {
                        _geoAuditBadgeBorder.Background = BrushHex("#1E222A");
                        _geoAuditBadgeBorder.BorderBrush = BrushHex("#2D333E");
                        _geoAuditBadgeText.Text = "● DIRECT ORIGIN (" + _shihabCurrentGeo.CountryShort + ")";
                        _geoAuditBadgeText.Foreground = BrushHex("#9BA3B0");
                    }
                }
            }
            else if (_shihabResolvedTarget != null)
            {
                _geoClockValueText.Text = ShihabConfigParsers.FormatShihabCountryLocalClock(_shihabResolvedTarget.CountryShort, DateTime.UtcNow);
            }
        }

        private void UpdateDailyDataVisuals()
        {
            long dlBytes, ulBytes;
            ShihabConfigService.ComputeShihab24HourTotals(_shihabConfig, out dlBytes, out ulBytes);
            _dailyDataTotalText.Text = ShihabConfigService.FormatShihabBytes(dlBytes + ulBytes);
            _dailyDownloadText.Text = ShihabConfigService.FormatShihabBytes(dlBytes) + " ↓";
            _dailyUploadText.Text = ShihabConfigService.FormatShihabBytes(ulBytes) + " ↑";
            _dailyChartControl.UpdateShihabBuckets(_shihabConfig.HourlyTraffic);
        }

        private void UpdateSpeedGaugeVisual(double mbps)
        {
            double clean = Math.Max(0.0, mbps);
            _peakSpeedValueText.Text = clean.ToString("0.0", CultureInfo.InvariantCulture) + " Mbps";
            double trackW = _peakSpeedBarTrackGrid != null && _peakSpeedBarTrackGrid.ActualWidth > 10
                ? _peakSpeedBarTrackGrid.ActualWidth
                : 320.0;
            double ratio = Math.Min(1.0, clean / 100.0);
            _peakSpeedBarFill.Width = Math.Max(clean > 0.0 ? 4.0 : 0.0, trackW * ratio);
        }

        private void UpdateConnectOrbVisual()
        {
            if (_heroConnectGlyphHost == null) return;
            _heroConnectGlyphHost.Children.Clear();

            if (_shihabState == ShihabConnectionState.Connected)
            {
                // Exact crisp white stop square inside #1DA1F2 orb matching screenshot!
                _heroConnectOrb.Background = BrushHex("#1DA1F2");
                _heroConnectOuterHalo.Background = new SolidColorBrush(Color.FromArgb(64, 29, 161, 242));
                Border stopSq = new Border
                {
                    Width = 15,
                    Height = 15,
                    CornerRadius = new CornerRadius(2),
                    Background = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _heroConnectGlyphHost.Children.Add(stopSq);
            }
            else if (_shihabState == ShihabConnectionState.Connecting || _shihabState == ShihabConnectionState.Reconnecting)
            {
                _heroConnectOrb.Background = BrushHex("#0284C7");
                _heroConnectOuterHalo.Background = new SolidColorBrush(Color.FromArgb(72, 245, 158, 11));
                TextBlock dots = new TextBlock
                {
                    Text = "•••",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _heroConnectGlyphHost.Children.Add(dots);
            }
            else
            {
                _heroConnectOrb.Background = BrushHex("#1DA1F2");
                _heroConnectOuterHalo.Background = new SolidColorBrush(Color.FromArgb(38, 29, 161, 242));
                ShapePath powerIcon = new ShapePath
                {
                    Data = Geometry.Parse("M 12,3 L 12,11 M 7.05,5.6 A 7.5,7.5 0 1,0 16.95,5.6"),
                    Stroke = Brushes.White,
                    StrokeThickness = 2.2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Width = 22,
                    Height = 22,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _heroConnectGlyphHost.Children.Add(powerIcon);
            }
        }

        private void SetServerBrowserMode(int mode)
        {
            _serverBrowserMode = mode;
            if (_btnModeCountries != null)
            {
                _btnModeCountries.Background = BrushHex(mode == 0 ? "#1DA1F2" : "#242A35");
                _btnModeCountries.Foreground = BrushHex(mode == 0 ? "#FFFFFF" : "#9BA3B0");
            }
            if (_btnModeNodes != null)
            {
                _btnModeNodes.Background = BrushHex(mode == 1 ? "#1DA1F2" : "#242A35");
                _btnModeNodes.Foreground = BrushHex(mode == 1 ? "#FFFFFF" : "#9BA3B0");
            }
            if (_btnModePrivate != null)
            {
                _btnModePrivate.Background = BrushHex(mode == 2 ? "#1DA1F2" : "#242A35");
                _btnModePrivate.Foreground = BrushHex(mode == 2 ? "#FFFFFF" : "#9BA3B0");
            }

            if (mode == 2 && (_shihabConfig.PrivateServers == null || _shihabConfig.PrivateServers.Count == 0) && _privateEditorDrawer != null)
            {
                _privateEditorDrawer.Visibility = Visibility.Visible;
            }

            RebuildShihabServerRows();
        }

        private void RebuildShihabServerRows()
        {
            if (_serverListStack == null) return;
            _serverListStack.Children.Clear();

            string filter = _serverSearchBox != null ? (_serverSearchBox.Text ?? string.Empty).Trim() : string.Empty;
            string regWanted = string.IsNullOrWhiteSpace(_activeRegionFilter) ? "ALL" : _activeRegionFilter;
            bool fastOnly = _shihabConfig != null && _shihabConfig.StreamingHighSpeedOnly;

            List<ShihabCountryHubSummary> countries = ShihabPublicRelayService.BuildShihabCountrySummaries(_shihabPublicNodes);

            bool globalAutoSelected = _shihabConfig.AutoSelectFastest &&
                                      string.IsNullOrEmpty(_shihabConfig.SelectedServerId) &&
                                      (string.IsNullOrWhiteSpace(_shihabConfig.SelectedCountryCode) ||
                                       string.Equals(_shihabConfig.SelectedCountryCode, "AUTO", StringComparison.OrdinalIgnoreCase));

            if (_serverBrowserMode != 2 && string.IsNullOrEmpty(filter) && regWanted == "ALL")
            {
                _serverListStack.Children.Add(CreateServerSelectionRow(
                    null,
                    "Auto — Fastest Global Relay (Dynamic IP Rotation)",
                    "Automatically verifies & rotates across lowest-latency MS-SSTP gateways worldwide",
                    "US",
                    "AUTO",
                    globalAutoSelected));
            }

            // Private Custom Servers Section (shown in Private tab, or at top of Countries/Servers when present)
            if (_shihabConfig.PrivateServers != null &&
                _shihabConfig.PrivateServers.Count > 0 &&
                (_serverBrowserMode == 2 || regWanted == "ALL"))
            {
                for (int i = 0; i < _shihabConfig.PrivateServers.Count; i++)
                {
                    ShihabVpnServerNode priv = _shihabConfig.PrivateServers[i];
                    if (!MatchesFilter(priv, filter)) continue;
                    bool sel = !_shihabConfig.AutoSelectFastest &&
                               string.Equals(_shihabConfig.SelectedServerId, priv.Id, StringComparison.OrdinalIgnoreCase);
                    _serverListStack.Children.Add(CreateServerSelectionRow(
                        priv,
                        priv.DisplayName,
                        string.Format("Private • {0} • {1}", priv.ProtocolBadgeText, priv.EffectiveDialAddress),
                        priv.CountryShort,
                        priv.ProtocolBadgeText,
                        sel));
                }
            }
            else if (_serverBrowserMode == 2)
            {
                Border emptyHint = new Border
                {
                    Margin = new Thickness(4, 6, 4, 6),
                    Padding = new Thickness(14, 12, 14, 12),
                    CornerRadius = new CornerRadius(7),
                    Background = BrushHex("#191C22"),
                    BorderBrush = BrushHex("#252A33"),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = "No Private / Custom VPN or Proxy profiles saved yet. Fill in the form above (MS-SSTP, IKEv2, L2TP/IPsec, PPTP, or HTTP/HTTPS System Proxy with optional DPAPI-encrypted auth) or click 'Import .conf / .ovpn' to add your own gateways.",
                        FontSize = 12.0,
                        Foreground = BrushHex("#9BA3B0"),
                        TextWrapping = TextWrapping.Wrap
                    }
                };
                _serverListStack.Children.Add(emptyHint);
            }

            if (_serverBrowserMode == 0)
            {
                // ALL COUNTRIES HUB — Filtered by Region & 4K Fast
                for (int i = 0; i < countries.Count; i++)
                {
                    ShihabCountryHubSummary hub = countries[i];
                    if (regWanted != "ALL" && !string.Equals(hub.RegionGroup, regWanted, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (fastOnly && hub.StreamingCapableCount <= 0 && hub.BestSpeedMbps < 20.0)
                    {
                        continue;
                    }
                    if (!MatchesCountryFilter(hub, filter))
                    {
                        continue;
                    }

                    bool countrySelected = (_shihabConfig.AutoSelectFastest &&
                                            string.Equals(_shihabConfig.SelectedCountryCode, hub.CountryShort, StringComparison.OrdinalIgnoreCase)) ||
                                           (!_shihabConfig.AutoSelectFastest &&
                                            _shihabResolvedTarget != null &&
                                            _shihabResolvedTarget.SourceKind == ShihabServerSourceKind.PublicOpenRelay &&
                                            string.Equals(_shihabResolvedTarget.CountryShort, hub.CountryShort, StringComparison.OrdinalIgnoreCase));

                    _serverListStack.Children.Add(CreateCountryHubSelectionRow(hub, countrySelected));
                }
            }
            else if (_serverBrowserMode == 1)
            {
                // ALL INDIVIDUAL SERVERS — Up to 450 nodes with city, custom :port, and .ovpn export button
                int shownPublic = 0;
                for (int i = 0; i < _shihabPublicNodes.Count; i++)
                {
                    ShihabVpnServerNode pub = _shihabPublicNodes[i];
                    if (regWanted != "ALL" && !string.Equals(pub.RegionGroup, regWanted, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (fastOnly && pub.SpeedMbps < 20.0)
                    {
                        continue;
                    }
                    if (!MatchesFilter(pub, filter))
                    {
                        continue;
                    }

                    shownPublic++;
                    if (shownPublic > 450) break;

                    bool sel = !_shihabConfig.AutoSelectFastest &&
                               string.Equals(_shihabConfig.SelectedServerId, pub.Id, StringComparison.OrdinalIgnoreCase);

                    string cityTag = !string.IsNullOrEmpty(pub.CityName) ? (pub.CityName + " • ") : string.Empty;
                    string sub = string.Format(
                        "{0}{1} • IP {2} • {3:0.0} Mbps • Load {4}%",
                        cityTag,
                        pub.EffectiveDialAddress,
                        pub.IpAddress,
                        pub.SpeedMbps,
                        pub.LoadPercent);

                    _serverListStack.Children.Add(CreateServerSelectionRow(
                        pub,
                        string.Format("{0} — {1}", pub.CountryLong, pub.HostName),
                        sub,
                        pub.CountryShort,
                        (pub.PingMs > 0 ? pub.PingMs : 28) + " ms",
                        sel));
                }
            }

            int ovpnExportCount = 0;
            for (int i = 0; i < _shihabPublicNodes.Count; i++)
            {
                if (_shihabPublicNodes[i].HasExportableOpenVpnProfile) ovpnExportCount++;
            }

            _serverCountSummaryText.Text = string.Format(
                "{0} Countries ({1} Public Relays • {2} .ovpn Profiles) + {3} Private Servers",
                countries.Count,
                _shihabPublicNodes.Count,
                ovpnExportCount,
                _shihabConfig.PrivateServers.Count);
        }

        private static bool MatchesCountryFilter(ShihabCountryHubSummary hub, string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return true;
            return (hub.CountryLong != null && hub.CountryLong.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (hub.CountryShort != null && hub.CountryShort.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (hub.CityName != null && hub.CityName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (hub.RegionGroup != null && hub.RegionGroup.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (hub.BestNode != null && MatchesFilter(hub.BestNode, q));
        }

        private static bool MatchesFilter(ShihabVpnServerNode node, string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return true;
            return (node.CountryLong != null && node.CountryLong.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (node.CountryShort != null && node.CountryShort.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (node.CityName != null && node.CityName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (node.RegionGroup != null && node.RegionGroup.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (node.DisplayName != null && node.DisplayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (node.HostName != null && node.HostName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (node.EffectiveDialAddress != null && node.EffectiveDialAddress.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (node.IpAddress != null && node.IpAddress.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (node.ProtocolBadgeText.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private Border CreateCountryHubSelectionRow(ShihabCountryHubSummary hub, bool isSelected)
        {
            Border row = new Border();
            row.Margin = new Thickness(2, 2, 2, 3);
            row.Padding = new Thickness(10, 8, 10, 8);
            row.CornerRadius = new CornerRadius(7);
            row.Background = isSelected ? BrushHex("#1E2C3E") : BrushHex("#191C22");
            row.BorderBrush = isSelected ? BrushHex("#1DA1F2") : BrushHex("#252A33");
            row.BorderThickness = new Thickness(1);
            row.Cursor = Cursors.Hand;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border flag = ShihabFlagBadgeFactory.CreateCircularFlagBadge(hub.CountryShort, 28);
            flag.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(flag, 0);
            g.Children.Add(flag);

            string relayWord = hub.TotalServers == 1 ? "1 Relay" : (hub.TotalServers + " Relays");
            string fastTag = hub.StreamingCapableCount > 0 ? (" • " + hub.StreamingCapableCount + " 4K Fast") : string.Empty;
            string ovpnTag = hub.OpenVpnProfileCount > 0 ? (" • " + hub.OpenVpnProfileCount + " .ovpn") : string.Empty;
            string bestEp = hub.BestNode != null ? hub.BestNode.EffectiveDialAddress : "MS-SSTP TLS";
            string sub = string.Format(
                "{0}{1}{2} • Best {3} • Up to {4:0.0} Mbps",
                relayWord,
                fastTag,
                ovpnTag,
                bestEp,
                Math.Max(1.0, hub.BestSpeedMbps));

            StackPanel titleLine = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleLine.Children.Add(new TextBlock
            {
                Text = hub.CountryLong,
                FontSize = 13.2,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            if (!string.IsNullOrEmpty(hub.CityName))
            {
                titleLine.Children.Add(new TextBlock
                {
                    Text = "  •  " + hub.CityName + " (" + hub.RegionGroup + ")",
                    FontSize = 11.0,
                    Foreground = BrushHex("#7DD3FC"),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            StackPanel st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(titleLine);
            st.Children.Add(new TextBlock { Text = sub, FontSize = 11.0, Foreground = BrushHex("#8A93A2") });
            Grid.SetColumn(st, 1);
            g.Children.Add(st);

            int pingShown = (hub.BestPingMs > 0 && hub.BestPingMs < 900) ? hub.BestPingMs : 35;
            Border pill = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = BrushHex("#242A35"),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock { Text = pingShown + " ms", FontSize = 11.0, FontWeight = FontWeights.SemiBold, Foreground = BrushHex("#38BDF8") }
            };
            Grid.SetColumn(pill, 2);
            g.Children.Add(pill);

            Button browseNodesBtn = CreateActionPillButton("Nodes ▸", "#242A35", "#CBD5E1");
            browseNodesBtn.Margin = new Thickness(6, 0, 0, 0);
            browseNodesBtn.Padding = new Thickness(8, 4, 8, 4);
            browseNodesBtn.FontSize = 11.0;
            browseNodesBtn.ToolTip = "Inspect individual relays & export .ovpn profiles in " + hub.CountryLong;
            browseNodesBtn.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                if (_serverSearchBox != null)
                {
                    _serverSearchBox.Text = hub.CountryLong;
                }
                SetServerBrowserMode(1);
            };
            Grid.SetColumn(browseNodesBtn, 3);
            g.Children.Add(browseNodesBtn);

            Button connectBtn = CreateActionPillButton("Connect", "#1DA1F2", "#FFFFFF");
            connectBtn.Margin = new Thickness(6, 0, 0, 0);
            connectBtn.Padding = new Thickness(10, 4, 10, 4);
            connectBtn.FontSize = 11.2;
            connectBtn.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                SelectCountryHubAndSwitch(hub, true);
            };
            Grid.SetColumn(connectBtn, 4);
            g.Children.Add(connectBtn);

            row.MouseLeftButtonDown += delegate
            {
                SelectCountryHubAndSwitch(hub, false);
            };

            row.Child = g;
            return row;
        }

        private void SelectCountryHubAndSwitch(ShihabCountryHubSummary hub, bool immediateConnect)
        {
            if (hub == null) return;
            _shihabConfig.AutoSelectFastest = true;
            _shihabConfig.SelectedCountryCode = hub.CountryShort;
            _shihabConfig.SelectedServerId = string.Empty;
            _shihabResolvedTarget = _shihabRelayService.ResolveBestShihabTargetNode(_shihabConfig, _shihabPublicNodes) ?? hub.BestNode;
            _shihabConfigService.SaveShihabConfig(_shihabConfig);
            RefreshAllShihabViews();
            SelectShihabNavTab(0);
            if (immediateConnect)
            {
                StartShihabConnectAsync(false);
            }
        }

        private Border CreateServerSelectionRow(
            ShihabVpnServerNode node,
            string title,
            string subtitle,
            string badgeCode,
            string rightMetric,
            bool isSelected)
        {
            Border row = new Border();
            row.Margin = new Thickness(2, 2, 2, 3);
            row.Padding = new Thickness(10, 8, 10, 8);
            row.CornerRadius = new CornerRadius(7);
            row.Background = isSelected ? BrushHex("#1E2C3E") : BrushHex("#191C22");
            row.BorderBrush = isSelected ? BrushHex("#1DA1F2") : BrushHex("#252A33");
            row.BorderThickness = new Thickness(1);
            row.Cursor = Cursors.Hand;

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border flag = ShihabFlagBadgeFactory.CreateCircularFlagBadge(badgeCode, 26);
            flag.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(flag, 0);
            g.Children.Add(flag);

            StackPanel st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(new TextBlock { Text = title, FontSize = 13.0, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            st.Children.Add(new TextBlock { Text = subtitle, FontSize = 11.0, Foreground = BrushHex("#8A93A2") });
            Grid.SetColumn(st, 1);
            g.Children.Add(st);

            Border pill = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = BrushHex("#242A35"),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock { Text = rightMetric, FontSize = 11.0, FontWeight = FontWeights.SemiBold, Foreground = BrushHex("#38BDF8") }
            };
            Grid.SetColumn(pill, 2);
            g.Children.Add(pill);

            if (node != null && node.HasExportableOpenVpnProfile)
            {
                Button ovpnBtn = CreateActionPillButton(".ovpn", "#1E293B", "#93C5FD");
                ovpnBtn.Margin = new Thickness(6, 0, 0, 0);
                ovpnBtn.Padding = new Thickness(7, 4, 7, 4);
                ovpnBtn.FontSize = 10.6;
                ovpnBtn.ToolTip = "Export genuine OpenVPN (.ovpn) profile for this server";
                ovpnBtn.Click += delegate(object s, RoutedEventArgs e)
                {
                    e.Handled = true;
                    ExportNodeOpenVpnProfile(node);
                };
                Grid.SetColumn(ovpnBtn, 3);
                g.Children.Add(ovpnBtn);
            }

            Button connectBtn = CreateActionPillButton("Connect", "#1DA1F2", "#FFFFFF");
            connectBtn.Margin = new Thickness(6, 0, 0, 0);
            connectBtn.Padding = new Thickness(10, 4, 10, 4);
            connectBtn.FontSize = 11.2;
            connectBtn.Click += delegate(object s, RoutedEventArgs e)
            {
                e.Handled = true;
                ApplySingleNodeSelection(node, true);
            };
            Grid.SetColumn(connectBtn, 4);
            g.Children.Add(connectBtn);

            if (node != null && node.SourceKind == ShihabServerSourceKind.PrivateCustomServer)
            {
                Button delBtn = CreateActionPillButton("✕", "#3A1E24", "#FCA5A5");
                delBtn.Margin = new Thickness(6, 0, 0, 0);
                delBtn.Padding = new Thickness(7, 3, 7, 3);
                delBtn.Click += delegate(object s, RoutedEventArgs e)
                {
                    e.Handled = true;
                    _shihabConfig.PrivateServers.Remove(node);
                    if (string.Equals(_shihabConfig.SelectedServerId, node.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        _shihabConfig.SelectedServerId = string.Empty;
                        _shihabConfig.AutoSelectFastest = true;
                    }
                    _shihabConfigService.SaveShihabConfig(_shihabConfig);
                    _shihabResolvedTarget = _shihabRelayService.ResolveBestShihabTargetNode(_shihabConfig, _shihabPublicNodes);
                    RebuildShihabServerRows();
                    RefreshAllShihabViews();
                };
                Grid.SetColumn(delBtn, 5);
                g.Children.Add(delBtn);
            }

            row.MouseLeftButtonDown += delegate
            {
                ApplySingleNodeSelection(node, false);
            };

            row.Child = g;
            return row;
        }

        private void ExportNodeOpenVpnProfile(ShihabVpnServerNode node)
        {
            string ovpnText;
            if (!ShihabConfigParsers.TryDecodeShihabOpenVpnConfig(node, out ovpnText))
            {
                MessageBox.Show(this, "This relay does not carry an exportable OpenVPN (.ovpn) payload.", "Export .ovpn", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Title = "Export OpenVPN (.ovpn) Profile";
            dlg.Filter = "OpenVPN Profile (*.ovpn)|*.ovpn|All Files (*.*)|*.*";
            string safeHost = (node.HostName ?? "relay").Replace(':', '_').Replace('/', '_');
            dlg.FileName = string.Format("ShihabShield_{0}_{1}.ovpn", node.CountryShort ?? "VPN", safeHost);

            if (dlg.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                File.WriteAllText(dlg.FileName, ovpnText);
                string msg = "Exported OpenVPN profile -> " + Path.GetFileName(dlg.FileName);
                _bottomBannerStatusText.Text = msg;
                AppendShihabEngineLog("SYSTEM", msg);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplySingleNodeSelection(ShihabVpnServerNode node, bool immediateConnect)
        {
            if (node == null)
            {
                _shihabConfig.AutoSelectFastest = true;
                _shihabConfig.SelectedCountryCode = "AUTO";
                _shihabConfig.SelectedServerId = string.Empty;
                _shihabResolvedTarget = _shihabRelayService.ResolveBestShihabTargetNode(_shihabConfig, _shihabPublicNodes);
            }
            else
            {
                _shihabConfig.AutoSelectFastest = false;
                _shihabConfig.SelectedServerId = node.Id;
                _shihabConfig.SelectedCountryCode = node.CountryShort;
                _shihabResolvedTarget = node;
            }
            _shihabConfigService.SaveShihabConfig(_shihabConfig);
            RefreshAllShihabViews();
            SelectShihabNavTab(0);
            if (immediateConnect)
            {
                StartShihabConnectAsync(false);
            }
        }

        private void SaveNewPrivateServerFromForm()
        {
            string rawInput = (_privHostInput.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(rawInput))
            {
                _bottomBannerStatusText.Text = "Please enter a valid Private Server Host or Proxy endpoint.";
                return;
            }

            string rawTitle = (_privNameInput.Text ?? string.Empty).Trim();
            string rawUser = (_privUserInput.Text ?? string.Empty).Trim();
            if (string.Equals(rawUser, "username", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(_privPassInput.Password))
            {
                rawUser = string.Empty;
            }

            // Option 4: Custom HTTP / HTTPS System Proxy (PAC / Auth Bridge)
            if (_privProtoCombo.SelectedIndex == 4)
            {
                string resolvedPlainPass;
                ShihabResult<ShihabVpnServerNode> proxyRes = ShihabConfigParsers.ParseShihabCustomProxyProfile(
                    rawInput,
                    rawTitle,
                    rawUser,
                    _privPassInput.Password,
                    out resolvedPlainPass);

                if (!proxyRes.IsSuccess || proxyRes.Value == null)
                {
                    _bottomBannerStatusText.Text = proxyRes.ErrorMessage;
                    return;
                }

                ShihabVpnServerNode proxyNode = proxyRes.Value;
                proxyNode.EncryptedPassword = ShihabConfigService.ProtectShihabSecret(resolvedPlainPass);

                _shihabConfig.PrivateServers.Insert(0, proxyNode);
                _shihabConfig.AutoSelectFastest = false;
                _shihabConfig.SelectedServerId = proxyNode.Id;
                _shihabResolvedTarget = proxyNode;
                _shihabConfigService.SaveShihabConfig(_shihabConfig);

                _privPassInput.Password = string.Empty;
                _privPskInput.Password = string.Empty;
                _privateEditorDrawer.Visibility = Visibility.Collapsed;

                AppendShihabEngineLog("SYSTEM", "Saved Private HTTP/HTTPS System Proxy profile: " + proxyNode.DisplayName);
                RebuildShihabServerRows();
                RefreshAllShihabViews();
                return;
            }

            string rawHost = rawInput;
            if (rawHost.StartsWith("sstp://", StringComparison.OrdinalIgnoreCase))
            {
                rawHost = rawHost.Substring(7).Trim().TrimEnd('/');
            }
            else if (rawHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                rawHost = rawHost.Substring(8).Trim().TrimEnd('/');
            }
            else if (rawHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                rawHost = rawHost.Substring(7).Trim().TrimEnd('/');
            }

            ShihabVpnProtocol selectedProto = ShihabVpnProtocol.AutoSstp;
            int defaultPort = 443;
            switch (_privProtoCombo.SelectedIndex)
            {
                case 1: selectedProto = ShihabVpnProtocol.Ikev2; defaultPort = 500; break;
                case 2: selectedProto = ShihabVpnProtocol.L2tpIpsec; defaultPort = 1701; break;
                case 3: selectedProto = ShihabVpnProtocol.Pptp; defaultPort = 1723; break;
                default: selectedProto = ShihabVpnProtocol.AutoSstp; defaultPort = 443; break;
            }

            string cleanHost;
            int parsedPort;
            ShihabConfigParsers.ParseHostAndPort(rawHost, defaultPort, out cleanHost, out parsedPort);
            if (string.IsNullOrEmpty(cleanHost))
            {
                cleanHost = rawHost;
                parsedPort = defaultPort;
            }

            string profileTitle = string.IsNullOrWhiteSpace(rawTitle) ? ("Private - " + cleanHost) : rawTitle;
            string inferredCc = ShihabConfigParsers.LookupShihabCountryCodeByName(profileTitle);
            if (inferredCc == "GL")
            {
                inferredCc = "PV";
            }
            double lat, lon;
            ShihabConfigParsers.ResolveShihabCountryCoordinates(inferredCc == "PV" ? "US" : inferredCc, out lat, out lon);

            ShihabVpnServerNode priv = new ShihabVpnServerNode();
            priv.Id = "priv_ras_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            priv.SourceKind = ShihabServerSourceKind.PrivateCustomServer;
            priv.DisplayName = profileTitle;
            priv.HostName = cleanHost;
            priv.SstpEndpoint = cleanHost;
            priv.IpAddress = cleanHost;
            priv.Protocol = selectedProto;
            priv.Port = parsedPort;
            priv.CountryLong = profileTitle;
            priv.CountryShort = inferredCc;
            priv.CityName = inferredCc == "PV" ? "Private Gateway" : ShihabConfigParsers.ResolveShihabCountryCity(inferredCc);
            priv.RegionGroup = inferredCc == "PV" ? "Private" : ShihabConfigParsers.ResolveShihabCountryRegion(inferredCc);
            priv.Latitude = lat;
            priv.Longitude = lon;
            priv.LoadPercent = 5;
            priv.PingMs = 20;
            priv.Username = (_privUserInput.Text ?? string.Empty).Trim();
            priv.EncryptedPassword = ShihabConfigService.ProtectShihabSecret(_privPassInput.Password);
            priv.EncryptedPreSharedKey = ShihabConfigService.ProtectShihabSecret(_privPskInput.Password);

            _shihabConfig.PrivateServers.Insert(0, priv);
            _shihabConfig.AutoSelectFastest = false;
            _shihabConfig.SelectedServerId = priv.Id;
            _shihabResolvedTarget = priv;
            _shihabConfigService.SaveShihabConfig(_shihabConfig);

            _privPassInput.Password = string.Empty;
            _privPskInput.Password = string.Empty;
            _privateEditorDrawer.Visibility = Visibility.Collapsed;

            AppendShihabEngineLog("SYSTEM", "Saved Private VPN profile: " + priv.DisplayName + " (" + priv.ProtocolBadgeText + ")");
            RebuildShihabServerRows();
            RefreshAllShihabViews();
        }

        private void ImportShihabProfileFromDisk()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "Import WireGuard (.conf) or OpenVPN (.ovpn) Profile";
            dlg.Filter = "VPN Profiles (*.conf;*.ovpn)|*.conf;*.ovpn|All Files (*.*)|*.*";
            if (dlg.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                string content = File.ReadAllText(dlg.FileName);
                string baseName = Path.GetFileNameWithoutExtension(dlg.FileName);
                string ext = Path.GetExtension(dlg.FileName) ?? string.Empty;

                ShihabResult<ShihabVpnServerNode> parsed = string.Equals(ext, ".conf", StringComparison.OrdinalIgnoreCase)
                    ? ShihabConfigParsers.ParseShihabWireGuardConf(content, baseName)
                    : ShihabConfigParsers.ParseShihabOpenVpnProfile(content, baseName);

                if (!parsed.IsSuccess || parsed.Value == null)
                {
                    MessageBox.Show(this, parsed.ErrorMessage, "Profile Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _shihabConfig.PrivateServers.Insert(0, parsed.Value);
                _shihabConfig.AutoSelectFastest = false;
                _shihabConfig.SelectedServerId = parsed.Value.Id;
                _shihabResolvedTarget = parsed.Value;
                _shihabConfigService.SaveShihabConfig(_shihabConfig);

                AppendShihabEngineLog("SYSTEM", "Imported profile: " + parsed.Value.DisplayName);
                RebuildShihabServerRows();
                RefreshAllShihabViews();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSettingsControlChanged(object sender, EventArgs e)
        {
            if (_shihabConfig == null) return;

            bool prevExtended = _shihabConfig.ShowExtendedCatalog;
            if (_chkDynamicIpRotation != null) _shihabConfig.DynamicIpRotation = _chkDynamicIpRotation.IsChecked == true;
            if (_chkExtendedCatalog != null) _shihabConfig.ShowExtendedCatalog = _chkExtendedCatalog.IsChecked == true;

            if (_chkAutoReconnectKs != null) _shihabConfig.KillSwitchAutoReconnect = _chkAutoReconnectKs.IsChecked == true;
            if (_chkStrictFirewallKs != null) _shihabConfig.StrictFirewallKillSwitch = _chkStrictFirewallKs.IsChecked == true;
            if (_cmbDnsPreset != null && _cmbDnsPreset.SelectedIndex >= 0)
            {
                _shihabConfig.DnsPreset = (ShihabDnsPreset)_cmbDnsPreset.SelectedIndex;
            }
            if (_cmbPublicProto != null)
            {
                _shihabConfig.PreferredPublicProtocol = _cmbPublicProto.SelectedIndex == 1
                    ? ShihabVpnProtocol.OpenVpn
                    : ShihabVpnProtocol.AutoSstp;
            }
            if (_chkSplitTunnel != null) _shihabConfig.SplitTunnelingEnabled = _chkSplitTunnel.IsChecked == true;
            if (_txtSplitBypassApps != null)
            {
                string[] parts = (_txtSplitBypassApps.Text ?? string.Empty).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                _shihabConfig.SplitTunnelBypassApps = new List<string>();
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Trim();
                    if (!string.IsNullOrEmpty(p)) _shihabConfig.SplitTunnelBypassApps.Add(p);
                }
            }
            if (_chkMinimizeTray != null) _shihabConfig.MinimizeToTrayOnClose = _chkMinimizeTray.IsChecked == true;
            if (_chkAutoConnectStartup != null) _shihabConfig.AutoConnectOnLaunch = _chkAutoConnectStartup.IsChecked == true;
            if (_chkDisconnectOnExit != null) _shihabConfig.DisconnectTunnelOnExit = _chkDisconnectOnExit.IsChecked == true;

            _shihabConfigService.SaveShihabConfig(_shihabConfig);
            UpdateRegionAndSpeedFilterVisuals();
            RefreshAllShihabViews();

            if (prevExtended != _shihabConfig.ShowExtendedCatalog)
            {
                TriggerShihabRelayRefreshAsync(false);
            }
        }

        private void BindShihabTrayEvents()
        {
            _shihabTray.OpenDashboardRequested += delegate { Dispatcher.BeginInvoke(new Action(RestoreAndActivateShihabWindow)); };
            _shihabTray.ToggleConnectRequested += delegate { Dispatcher.BeginInvoke(new Action(ToggleShihabConnectionAsync)); };
            _shihabTray.SpeedTestRequested += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    RestoreAndActivateShihabWindow();
                    StartShihabSpeedBenchmarkAsync();
                }));
            };
            _shihabTray.AboutDeveloperRequested += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    RestoreAndActivateShihabWindow();
                    SelectShihabNavTab(4);
                }));
            };
            _shihabTray.ExitAppRequested += delegate { Dispatcher.BeginInvoke(new Action(RequestExplicitExit)); };
        }

        public void RestoreAndActivateShihabWindow()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        public void RequestExplicitExit()
        {
            _shihabExplicitExitRequested = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_shihabExplicitExitRequested && _shihabConfig.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            if (_shihabUiTimer != null) _shihabUiTimer.Stop();
            if (_shihabBrowserCompanion != null)
            {
                _shihabBrowserCompanion.Dispose();
                _shihabBrowserCompanion = null;
            }
            if (_shihabConfig.DisconnectTunnelOnExit && _shihabVpnEngine.IsShihabTunnelCurrentlyActive())
            {
                _shihabVpnEngine.DisconnectShihabTunnelSync();
            }
            _shihabShieldService.DisarmShihabFirewallKillSwitch(_shihabConfig);
            _shihabConfigService.SaveShihabConfig(_shihabConfig);
            _shihabTray.Dispose();
            _shihabVpnEngine.Dispose();
            base.OnClosing(e);
        }

        private void StartShihabBrowserCompanionBridge()
        {
            ShihabBrowserCompanionBridgeCallbacks cb = new ShihabBrowserCompanionBridgeCallbacks();
            cb.GetStatusSnapshot = delegate
            {
                return (Dictionary<string, object>)Dispatcher.Invoke(new Func<Dictionary<string, object>>(BuildCompanionStatusOnUiThread));
            };
            cb.GetCatalogSnapshot = delegate
            {
                return (Dictionary<string, object>)Dispatcher.Invoke(new Func<Dictionary<string, object>>(BuildCompanionCatalogOnUiThread));
            };
            cb.RequestConnect = delegate(string cc, string sid)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    HandleCompanionConnectOnUiThread(cc, sid);
                }));
            };
            cb.RequestDisconnect = delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (_shihabState == ShihabConnectionState.Connected ||
                        _shihabState == ShihabConnectionState.Connecting ||
                        _shihabState == ShihabConnectionState.Reconnecting)
                    {
                        if (_shihabState == ShihabConnectionState.Connected)
                        {
                            DisconnectShihabAsync();
                        }
                        else if (_shihabConnectCts != null)
                        {
                            _shihabConnectCts.Cancel();
                        }
                    }
                }));
            };
            cb.RequestRotateIp = delegate
            {
                Dispatcher.BeginInvoke(new Action(RotateShihabIpNowAsync));
            };

            _shihabBrowserCompanion = new ShihabBrowserCompanionService(cb);
            if (_shihabBrowserCompanion.TryStartShihabCompanion())
            {
                AppendShihabEngineLog("SYSTEM", "Chrome/Brave Companion Bridge active on 127.0.0.1:" + _shihabBrowserCompanion.ActivePort);
            }
        }

        private Dictionary<string, object> BuildCompanionStatusOnUiThread()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["state"] = _shihabState.ToString();
            d["connected"] = _shihabState == ShihabConnectionState.Connected;
            d["dynamicIpRotation"] = _shihabConfig != null && _shihabConfig.DynamicIpRotation;
            d["publicRelayCount"] = _shihabPublicNodes != null ? _shihabPublicNodes.Count : 0;

            long dl24, ul24;
            ShihabConfigService.ComputeShihab24HourTotals(_shihabConfig, out dl24, out ul24);
            d["downloadBytes24h"] = dl24;
            d["uploadBytes24h"] = ul24;
            d["peakMbps"] = _shihabConfig != null ? _shihabConfig.PeakSpeedMbps : 0.0;

            int uptimeSec = 0;
            if (_shihabState == ShihabConnectionState.Connected && _shihabConnectedSinceUtc != DateTime.MinValue)
            {
                uptimeSec = Math.Max(0, (int)(DateTime.UtcNow - _shihabConnectedSinceUtc).TotalSeconds);
            }
            d["uptimeSeconds"] = uptimeSec;

            if (_shihabResolvedTarget != null)
            {
                d["countryShort"] = _shihabResolvedTarget.CountryShort;
                d["countryLong"] = _shihabResolvedTarget.CountryLong;
                d["cityName"] = _shihabResolvedTarget.CityName;
                d["regionGroup"] = _shihabResolvedTarget.RegionGroup;
                d["endpoint"] = _shihabResolvedTarget.EffectiveDialAddress;
                d["ipAddress"] = (_shihabState == ShihabConnectionState.Connected && !string.IsNullOrEmpty(_shihabObservedPublicIp))
                    ? _shihabObservedPublicIp
                    : _shihabResolvedTarget.IpAddress;
                d["protocol"] = _shihabResolvedTarget.ProtocolBadgeText;
                d["pingMs"] = _shihabResolvedTarget.PingMs > 0 ? _shihabResolvedTarget.PingMs : 24;
                d["loadPercent"] = _shihabResolvedTarget.LoadPercent;
                d["latitude"] = _shihabResolvedTarget.Latitude;
                d["longitude"] = _shihabResolvedTarget.Longitude;
            }

            if (_shihabOriginGeo != null)
            {
                d["originCountryShort"] = _shihabOriginGeo.CountryShort;
                d["originCountryLong"] = _shihabOriginGeo.CountryLong;
                d["originLat"] = _shihabOriginGeo.Latitude;
                d["originLon"] = _shihabOriginGeo.Longitude;
            }

            return d;
        }

        private Dictionary<string, object> BuildCompanionCatalogOnUiThread()
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            List<ShihabCountryHubSummary> hubs = ShihabPublicRelayService.BuildShihabCountrySummaries(_shihabPublicNodes);
            List<Dictionary<string, object>> countriesOut = new List<Dictionary<string, object>>(hubs.Count);

            for (int i = 0; i < hubs.Count; i++)
            {
                ShihabCountryHubSummary h = hubs[i];
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["countryShort"] = h.CountryShort;
                item["countryLong"] = h.CountryLong;
                item["cityName"] = h.CityName;
                item["regionGroup"] = h.RegionGroup;
                item["totalServers"] = h.TotalServers;
                item["streamingCapableCount"] = h.StreamingCapableCount;
                item["openVpnProfileCount"] = h.OpenVpnProfileCount;
                item["bestPingMs"] = h.BestPingMs;
                item["bestSpeedMbps"] = Math.Round(h.BestSpeedMbps, 1);
                if (h.BestNode != null)
                {
                    item["latitude"] = h.BestNode.Latitude;
                    item["longitude"] = h.BestNode.Longitude;
                    item["bestEndpoint"] = h.BestNode.EffectiveDialAddress;
                }
                countriesOut.Add(item);
            }

            List<Dictionary<string, object>> privOut = new List<Dictionary<string, object>>();
            if (_shihabConfig != null && _shihabConfig.PrivateServers != null)
            {
                for (int i = 0; i < _shihabConfig.PrivateServers.Count; i++)
                {
                    ShihabVpnServerNode p = _shihabConfig.PrivateServers[i];
                    Dictionary<string, object> pi = new Dictionary<string, object>();
                    pi["id"] = p.Id;
                    pi["displayName"] = p.DisplayName;
                    pi["countryShort"] = p.CountryShort;
                    pi["protocol"] = p.ProtocolBadgeText;
                    pi["endpoint"] = p.EffectiveDialAddress;
                    privOut.Add(pi);
                }
            }

            root["countries"] = countriesOut;
            root["privateServers"] = privOut;
            return root;
        }

        private async void HandleCompanionConnectOnUiThread(string countryCode, string serverId)
        {
            if (_shihabState == ShihabConnectionState.Connected)
            {
                await _shihabVpnEngine.DisconnectShihabTunnelAsync();
                _shihabShieldService.DisarmShihabFirewallKillSwitch(_shihabConfig);
                _shihabState = ShihabConnectionState.Disconnected;
            }

            if (!string.IsNullOrWhiteSpace(serverId))
            {
                _shihabConfig.AutoSelectFastest = false;
                _shihabConfig.SelectedServerId = serverId.Trim();
            }
            else
            {
                string cc = string.IsNullOrWhiteSpace(countryCode) ? "AUTO" : countryCode.Trim().ToUpperInvariant();
                if (cc == "UK") cc = "GB";
                _shihabConfig.AutoSelectFastest = true;
                _shihabConfig.SelectedServerId = string.Empty;
                _shihabConfig.SelectedCountryCode = cc;
            }

            _shihabResolvedTarget = _shihabRelayService.ResolveBestShihabTargetNode(_shihabConfig, _shihabPublicNodes);
            _shihabConfigService.SaveShihabConfig(_shihabConfig);
            AppendShihabEngineLog("SYSTEM", "Chrome/Brave Extension requested tunnel connection.");
            RefreshAllShihabViews();
            StartShihabConnectAsync(false);
        }

        public async void RunShihabUiPreviewCaptureAsync(string outDirectory)
        {
            bool seededPreviewTraffic = false;
            try
            {
                if (!Directory.Exists(outDirectory))
                {
                    Directory.CreateDirectory(outDirectory);
                }

                // Await live public relay directory & GeoIP audit so all snapshots show real populated data
                if (_shihabPublicNodes == null || _shihabPublicNodes.Count == 0)
                {
                    ShihabResult<List<ShihabVpnServerNode>> liveRes = await _shihabRelayService.RefreshShihabPublicRelaysAsync(
                        false,
                        _shihabConfig.ShowExtendedCatalog);
                    if (liveRes.IsSuccess && liveRes.Value != null && liveRes.Value.Count > 0)
                    {
                        _shihabPublicNodes = liveRes.Value;
                        _shihabResolvedTarget = _shihabRelayService.ResolveBestShihabTargetNode(_shihabConfig, _shihabPublicNodes);
                        List<ShihabCountryHubSummary> countries = ShihabPublicRelayService.BuildShihabCountrySummaries(_shihabPublicNodes);
                        string msg = string.Format(
                            "Loaded {0} global relays across {1} countries + {2} private profiles",
                            _shihabPublicNodes.Count,
                            countries.Count,
                            _shihabConfig.PrivateServers.Count);
                        _bottomBannerStatusText.Text = msg;
                        AppendShihabEngineLog("SYSTEM", msg);
                    }
                }

                if (_shihabResolvedTarget != null)
                {
                    ShihabGeoIpReport previewGeo = new ShihabGeoIpReport();
                    previewGeo.IpAddress = _shihabResolvedTarget.IpAddress;
                    previewGeo.CountryShort = _shihabResolvedTarget.CountryShort;
                    previewGeo.CountryLong = _shihabResolvedTarget.CountryLong;
                    previewGeo.City = _shihabResolvedTarget.CityName;
                    previewGeo.Isp = "Academic OpenGW Relay";
                    previewGeo.Asn = "AS2516";
                    previewGeo.Latitude = _shihabResolvedTarget.Latitude;
                    previewGeo.Longitude = _shihabResolvedTarget.Longitude;
                    previewGeo.LocalTimeFormatted = ShihabConfigParsers.FormatShihabCountryLocalClock(_shihabResolvedTarget.CountryShort, DateTime.UtcNow);
                    _shihabCurrentGeo = previewGeo;
                    _shihabObservedPublicIp = previewGeo.IpAddress;
                    AppendShihabEngineLog("VERIFY", "Preview GeoIP ready: " + previewGeo.CountryLong + " (" + previewGeo.IpAddress + ")");
                }

                // Seed temporary preview telemetry if hourly chart is completely empty so chart & bars render crisply
                long dlSum, ulSum;
                ShihabConfigService.ComputeShihab24HourTotals(_shihabConfig, out dlSum, out ulSum);
                if (dlSum == 0 && ulSum == 0 && _shihabConfig.HourlyTraffic.Count >= 4)
                {
                    seededPreviewTraffic = true;
                    int n = _shihabConfig.HourlyTraffic.Count;
                    _shihabConfig.HourlyTraffic[n - 3].DownloadBytes = 82 * 1024;
                    _shihabConfig.HourlyTraffic[n - 3].UploadBytes = 44 * 1024;
                    _shihabConfig.HourlyTraffic[n - 2].DownloadBytes = 148 * 1024;
                    _shihabConfig.HourlyTraffic[n - 2].UploadBytes = 96 * 1024;
                    _shihabConfig.HourlyTraffic[n - 1].DownloadBytes = 215 * 1024;
                    _shihabConfig.HourlyTraffic[n - 1].UploadBytes = 310 * 1024;
                }

                RefreshAllShihabViews();
                RebuildShihabServerRows();

                await Task.Delay(500);
                SelectShihabNavTab(0);
                await Task.Delay(350);
                SaveWindowSnapshotPng(Path.Combine(outDirectory, "shihab_vpn_dashboard.png"));

                SelectShihabNavTab(1);
                await Task.Delay(350);
                SaveWindowSnapshotPng(Path.Combine(outDirectory, "shihab_vpn_servers.png"));

                SelectShihabNavTab(2);
                SetDiagSubTab(0);
                await Task.Delay(350);
                SaveWindowSnapshotPng(Path.Combine(outDirectory, "shihab_vpn_diagnostics.png"));

                SelectShihabNavTab(3);
                await Task.Delay(350);
                SaveWindowSnapshotPng(Path.Combine(outDirectory, "shihab_vpn_security.png"));

                SelectShihabNavTab(4);
                await Task.Delay(350);
                SaveWindowSnapshotPng(Path.Combine(outDirectory, "shihab_vpn_about.png"));
            }
            finally
            {
                if (seededPreviewTraffic && _shihabConfig != null && _shihabConfig.HourlyTraffic != null)
                {
                    for (int i = 0; i < _shihabConfig.HourlyTraffic.Count; i++)
                    {
                        _shihabConfig.HourlyTraffic[i].DownloadBytes = 0;
                        _shihabConfig.HourlyTraffic[i].UploadBytes = 0;
                    }
                }
                _shihabExplicitExitRequested = true;
                Close();
            }
        }

        private void SaveWindowSnapshotPng(string filePath)
        {
            UpdateLayout();
            int w = Math.Max(400, (int)Math.Ceiling(ActualWidth * 1.25));
            int h = Math.Max(300, (int)Math.Ceiling(ActualHeight * 1.25));
            RenderTargetBitmap rtb = new RenderTargetBitmap(w, h, 120, 120, PixelFormats.Pbgra32);
            rtb.Render(this);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                enc.Save(fs);
            }
        }

        // =====================================================================
        // UI FACTORY HELPERS
        // =====================================================================
        private static Border CreateBentoCard()
        {
            return new Border
            {
                Background = BrushHex("#1D2026"),
                BorderBrush = BrushHex("#282D37"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 14, 16, 14)
            };
        }

        private static TextBlock CreateCardHeaderTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13.0,
                Foreground = BrushHex("#9BA3B2"),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Border CreateCardCornerInfoBadge(string tooltip)
        {
            Border b = new Border
            {
                Width = 17,
                Height = 17,
                CornerRadius = new CornerRadius(8.5),
                BorderBrush = BrushHex("#555E6E"),
                BorderThickness = new Thickness(1.1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = tooltip,
                Child = new TextBlock
                {
                    Text = "i",
                    FontSize = 10.0,
                    Foreground = BrushHex("#8A93A2"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -0.5, 0, 0)
                }
            };
            return b;
        }

        private static Grid BuildMetricRow(string labelText, string initialVal, out TextBlock valueOut)
        {
            Grid g = new Grid { Margin = new Thickness(0, 11, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock lbl = CreateMetricLabel(labelText);
            Grid.SetColumn(lbl, 0);
            g.Children.Add(lbl);

            valueOut = CreateMetricRightValue(initialVal);
            Grid.SetColumn(valueOut, 1);
            g.Children.Add(valueOut);
            return g;
        }

        private static TextBlock CreateMetricLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13.0,
                Foreground = BrushHex("#9AA3B2"),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static TextBlock CreateMetricRightValue(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Button CreateActionPillButton(string label, string bgHex, string fgHex)
        {
            Button btn = new Button();
            btn.Content = label;
            btn.Cursor = Cursors.Hand;
            btn.FontSize = 12.0;
            btn.FontWeight = FontWeights.SemiBold;
            btn.Foreground = BrushHex(fgHex);
            btn.Background = BrushHex(bgHex);
            btn.BorderThickness = new Thickness(0);
            btn.Padding = new Thickness(12, 6, 12, 6);

            ControlTemplate tpl = new ControlTemplate(typeof(Button));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            btn.Template = tpl;
            return btn;
        }

        private static Style _shihabDarkComboStyle;

        private static ComboBox CreateDarkComboBox(double height)
        {
            if (_shihabDarkComboStyle == null)
            {
                const string comboStyleXaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='ComboBox'>
  <Setter Property='Foreground' Value='#FFFFFF'/>
  <Setter Property='Background' Value='#14171D'/>
  <Setter Property='BorderBrush' Value='#2E3542'/>
  <Setter Property='BorderThickness' Value='1'/>
  <Setter Property='FontSize' Value='12'/>
  <Setter Property='SnapsToDevicePixels' Value='True'/>
  <Setter Property='ItemContainerStyle'>
    <Setter.Value>
      <Style TargetType='ComboBoxItem'>
        <Setter Property='Foreground' Value='#E2E8F0'/>
        <Setter Property='Background' Value='#181C24'/>
        <Setter Property='Padding' Value='10,6'/>
        <Setter Property='Cursor' Value='Hand'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='ComboBoxItem'>
              <Border x:Name='Bd' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
                <ContentPresenter VerticalAlignment='Center'/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property='IsHighlighted' Value='True'>
                  <Setter TargetName='Bd' Property='Background' Value='#233246'/>
                  <Setter Property='Foreground' Value='#FFFFFF'/>
                </Trigger>
                <Trigger Property='IsSelected' Value='True'>
                  <Setter TargetName='Bd' Property='Background' Value='#1E2C3E'/>
                  <Setter Property='Foreground' Value='#38BDF8'/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Style>
    </Setter.Value>
  </Setter>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ComboBox'>
        <Grid>
          <ToggleButton x:Name='DropDownToggle'
                        Focusable='False'
                        ClickMode='Press'
                        Cursor='Hand'
                        IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
            <ToggleButton.Template>
              <ControlTemplate TargetType='ToggleButton'>
                <Border x:Name='MainBd'
                        Background='#14171D'
                        BorderBrush='#2E3542'
                        BorderThickness='1'
                        CornerRadius='6'>
                  <Grid>
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width='*'/>
                      <ColumnDefinition Width='28'/>
                    </Grid.ColumnDefinitions>
                    <Path Grid.Column='1'
                          Data='M 0,0 L 4.5,4.5 L 9,0'
                          Stroke='#9BA3B0'
                          StrokeThickness='1.5'
                          HorizontalAlignment='Center'
                          VerticalAlignment='Center'/>
                  </Grid>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property='IsMouseOver' Value='True'>
                    <Setter TargetName='MainBd' Property='BorderBrush' Value='#3B82F6'/>
                  </Trigger>
                  <Trigger Property='IsChecked' Value='True'>
                    <Setter TargetName='MainBd' Property='BorderBrush' Value='#1DA1F2'/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </ToggleButton.Template>
          </ToggleButton>
          <ContentPresenter IsHitTestVisible='False'
                            Margin='10,0,30,0'
                            VerticalAlignment='Center'
                            HorizontalAlignment='Left'
                            Content='{TemplateBinding SelectionBoxItem}'
                            ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'
                            ContentStringFormat='{TemplateBinding SelectionBoxItemStringFormat}'/>
          <Popup x:Name='PART_Popup'
                 Placement='Bottom'
                 AllowsTransparency='True'
                 Focusable='False'
                 IsOpen='{TemplateBinding IsDropDownOpen}'
                 PopupAnimation='Slide'>
            <Border MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'
                    MaxHeight='{TemplateBinding MaxDropDownHeight}'
                    Margin='0,2,0,0'
                    Background='#181C24'
                    BorderBrush='#2E3542'
                    BorderThickness='1'
                    CornerRadius='6'>
              <ScrollViewer VerticalScrollBarVisibility='Auto'>
                <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Contained'/>
              </ScrollViewer>
            </Border>
          </Popup>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";
                _shihabDarkComboStyle = (Style)XamlReader.Parse(comboStyleXaml);
            }

            return new ComboBox
            {
                Height = height,
                Style = _shihabDarkComboStyle
            };
        }

        private static TextBox CreateDarkTextBox(string initialText)
        {
            return new TextBox
            {
                Text = initialText ?? string.Empty,
                Height = 28,
                Padding = new Thickness(7, 4, 7, 4),
                Background = BrushHex("#14171D"),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderBrush = BrushHex("#2E3542"),
                BorderThickness = new Thickness(1),
                FontSize = 12.0
            };
        }

        private static StackPanel WrapLabeledField(string label, UIElement input)
        {
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 10.8, Foreground = BrushHex("#8E96A4"), Margin = new Thickness(0, 0, 0, 3) });
            sp.Children.Add(input);
            return sp;
        }

        private static StackPanel CreateFieldSlot(int col, string label, UIElement input)
        {
            StackPanel sp = WrapLabeledField(label, input);
            Grid.SetColumn(sp, col);
            return sp;
        }

        private static CheckBox CreateStyledCheckBox(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = new TextBlock
                {
                    Text = text,
                    FontSize = 12.0,
                    Foreground = BrushHex("#DCE3EE"),
                    TextWrapping = TextWrapping.Wrap
                },
                IsChecked = isChecked,
                Margin = new Thickness(0, 8, 0, 2),
                Cursor = Cursors.Hand
            };
        }

        private static void OpenExternalUrlSafe(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        private static SolidColorBrush BrushHex(string hex)
        {
            Color c = (Color)ColorConverter.ConvertFromString(hex);
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
