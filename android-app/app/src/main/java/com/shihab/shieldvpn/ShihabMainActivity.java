package com.shihab.shieldvpn;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.net.VpnService;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.Editable;
import android.text.TextWatcher;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.CompoundButton;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import com.shihab.shieldvpn.ShihabModels.ShihabCountryHub;
import com.shihab.shieldvpn.ShihabModels.ShihabCountryInfo;
import com.shihab.shieldvpn.ShihabModels.ShihabGeoAudit;
import com.shihab.shieldvpn.ShihabModels.ShihabPreferencesStore;
import com.shihab.shieldvpn.ShihabModels.ShihabRelayNode;
import com.shihab.shieldvpn.ShihabModels.ShihabSpeedResult;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Shihab Shield VPN — Android Smartphone Main Activity (Dark Bento Theme).
 * Engineered by Mehedi Hasan Shihab (sshihabb007).
 */
public class ShihabMainActivity extends Activity {

    private static final int REQ_VPN_CONSENT = 901;
    private static final int REQ_IKEV2_PROVISION = 902;

    private final ExecutorService mBgExecutor = Executors.newCachedThreadPool();
    private final Handler mUiHandler = new Handler(Looper.getMainLooper());

    private ShihabPreferencesStore mPrefs;
    private List<ShihabRelayNode> mPublicRelays = new ArrayList<ShihabRelayNode>();
    private ShihabRelayNode mSelectedSpecificNode = null;
    private ShihabGeoAudit mOriginGeo = new ShihabGeoAudit();
    private ShihabGeoAudit mCurrentGeo = new ShihabGeoAudit();
    private double mPeakMbps = 0.0;
    private int mLastPingMs = 24;
    private int mLastJitterMs = 2;

    private int mActiveTab = 0;
    private String mServerViewMode = "COUNTRIES"; // COUNTRIES | PROXIES | OVPN | PRIVATE
    private String mRegionFilter = "ALL";
    private String mSearchQuery = "";

    // Top & Bottom Bar Views
    private TextView mTopStatusPill;
    private final TextView[] mBottomNavButtons = new TextView[5];
    private final View[] mTabContainers = new View[5];

    // Dashboard Views
    private TextView mDashCcBadge;
    private TextView mDashLocTitle;
    private TextView mDashLocSubtitle;
    private ShihabGlobeView mGlobeView;
    private TextView mDashModeTag;
    private TextView mDashIpVal;
    private TextView mDashPingVal;
    private TextView mDashSpeedVal;
    private TextView mDashTimerVal;
    private Button mConnectOrbButton;
    private TextView mBottomBannerText;

    // Servers Tab Views
    private TextView mServerSummarySub;
    private LinearLayout mServerRowsContainer;

    // Diagnostics Tab Views
    private TextView mDiagSpeedVal;
    private TextView mDiagPingVal;
    private TextView mDiagIpVal;
    private TextView mDiagRouteVal;
    private TextView mDiagIspVal;
    private TextView mDiagLeakBadge;
    private TextView mLogsBox;

    private final Runnable mTimerRunnable = new Runnable() {
        @Override
        public void run() {
            refreshDashboardUi();
            mUiHandler.postDelayed(this, 1000L);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        mPrefs = new ShihabPreferencesStore(this);

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Color.parseColor("#0F1115"));
        root.setLayoutParams(new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT));

        root.addView(buildTopHeaderBar());

        FrameLayout contentHost = new FrameLayout(this);
        LinearLayout.LayoutParams hostLp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
        contentHost.setLayoutParams(hostLp);

        mTabContainers[0] = buildDashboardTab();
        mTabContainers[1] = buildServersTab();
        mTabContainers[2] = buildDiagnosticsTab();
        mTabContainers[3] = buildShieldSettingsTab();
        mTabContainers[4] = buildAboutDeveloperTab();

        for (int i = 0; i < mTabContainers.length; i++) {
            contentHost.addView(mTabContainers[i]);
        }
        root.addView(contentHost);
        root.addView(buildBottomNavigationBar());

        setContentView(root);
        switchTab(0);

        triggerRelayCatalogSync();
        triggerGeoIpAudit();
    }

    @Override
    protected void onResume() {
        super.onResume();
        mUiHandler.removeCallbacks(mTimerRunnable);
        mUiHandler.post(mTimerRunnable);
    }

    @Override
    protected void onPause() {
        mUiHandler.removeCallbacks(mTimerRunnable);
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        mBgExecutor.shutdownNow();
        super.onDestroy();
    }

    // =========================================================================
    // UI BUILDERS (DARK BENTO DESIGN)
    // =========================================================================
    private View buildTopHeaderBar() {
        LinearLayout bar = new LinearLayout(this);
        bar.setOrientation(LinearLayout.HORIZONTAL);
        bar.setGravity(Gravity.CENTER_VERTICAL);
        bar.setPadding(dp(14), dp(12), dp(14), dp(10));
        bar.setBackgroundColor(Color.parseColor("#13161C"));

        mTopStatusPill = new TextView(this);
        mTopStatusPill.setText("● Disconnected");
        mTopStatusPill.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f);
        mTopStatusPill.setTypeface(Typeface.DEFAULT_BOLD);
        mTopStatusPill.setTextColor(Color.parseColor("#9AA3B2"));
        mTopStatusPill.setPadding(dp(10), dp(4), dp(10), dp(4));
        mTopStatusPill.setBackground(roundRect("#1B1F27", "#2B313D", 14));
        bar.addView(mTopStatusPill);

        TextView title = new TextView(this);
        title.setText("MASTER VPN PRO");
        title.setTextColor(Color.WHITE);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f);
        title.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams tLp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        bar.addView(title, tLp);

        TextView refreshBtn = makeSmallPillButton("↻", false);
        refreshBtn.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                Toast.makeText(ShihabMainActivity.this, "Syncing global public relays...", Toast.LENGTH_SHORT).show();
                triggerRelayCatalogSync();
                triggerGeoIpAudit();
            }
        });
        bar.addView(refreshBtn);

        View spacer = new View(this);
        bar.addView(spacer, new LinearLayout.LayoutParams(dp(6), dp(1)));

        TextView infoBtn = makeSmallPillButton("i", false);
        infoBtn.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                switchTab(4);
            }
        });
        bar.addView(infoBtn);

        return bar;
    }

    private View buildDashboardTab() {
        ScrollView sv = new ScrollView(this);
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        col.setPadding(dp(14), dp(12), dp(14), dp(18));

        // 1. Virtual Location + 3D Globe Card
        LinearLayout globeCard = makeBentoCard();
        LinearLayout hdrRow = new LinearLayout(this);
        hdrRow.setOrientation(LinearLayout.HORIZONTAL);
        hdrRow.setGravity(Gravity.CENTER_VERTICAL);

        TextView vLocLbl = new TextView(this);
        vLocLbl.setText("Virtual location");
        vLocLbl.setTextColor(Color.parseColor("#9AA3B2"));
        vLocLbl.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
        hdrRow.addView(vLocLbl, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        final String[] cams = new String[]{"3D", "Shield", "Orbit", "2D"};
        final List<TextView> camBtns = new ArrayList<TextView>();
        for (final String cm : cams) {
            final TextView b = makeSmallPillButton(cm, "3D".equals(cm));
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.leftMargin = dp(4);
            b.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    if (mGlobeView != null) mGlobeView.setCameraMode(cm);
                    for (TextView x : camBtns) stylePillButton(x, x == b);
                }
            });
            camBtns.add(b);
            hdrRow.addView(b, lp);
        }
        globeCard.addView(hdrRow);

        LinearLayout locRow = new LinearLayout(this);
        locRow.setOrientation(LinearLayout.HORIZONTAL);
        locRow.setGravity(Gravity.CENTER_VERTICAL);
        locRow.setPadding(0, dp(10), 0, dp(6));
        locRow.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                switchTab(1);
            }
        });

        mDashCcBadge = new TextView(this);
        mDashCcBadge.setText("US");
        mDashCcBadge.setTextColor(Color.WHITE);
        mDashCcBadge.setTypeface(Typeface.DEFAULT_BOLD);
        mDashCcBadge.setGravity(Gravity.CENTER);
        mDashCcBadge.setBackground(roundRect("#1DA1F2", "#38BDF8", 20));
        locRow.addView(mDashCcBadge, new LinearLayout.LayoutParams(dp(40), dp(40)));

        LinearLayout locTexts = new LinearLayout(this);
        locTexts.setOrientation(LinearLayout.VERTICAL);
        locTexts.setPadding(dp(10), 0, dp(8), 0);
        mDashLocTitle = new TextView(this);
        mDashLocTitle.setText("United States");
        mDashLocTitle.setTextColor(Color.WHITE);
        mDashLocTitle.setTypeface(Typeface.DEFAULT_BOLD);
        mDashLocTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15.5f);
        locTexts.addView(mDashLocTitle);

        mDashLocSubtitle = new TextView(this);
        mDashLocSubtitle.setText("Syncing 70+ country relays...");
        mDashLocSubtitle.setTextColor(Color.parseColor("#8A93A2"));
        mDashLocSubtitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f);
        locTexts.addView(mDashLocSubtitle);
        locRow.addView(locTexts, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        TextView chev = new TextView(this);
        chev.setText("›");
        chev.setTextColor(Color.parseColor("#8A93A2"));
        chev.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20f);
        locRow.addView(chev);
        globeCard.addView(locRow);

        mGlobeView = new ShihabGlobeView(this);
        globeCard.addView(mGlobeView, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(200)));
        col.addView(globeCard);

        col.addView(spaceV(10));

        // 2. Server Information Card
        LinearLayout infoCard = makeBentoCard();
        LinearLayout infoHdr = new LinearLayout(this);
        infoHdr.setOrientation(LinearLayout.HORIZONTAL);
        infoHdr.setGravity(Gravity.CENTER_VERTICAL);

        TextView sTitle = new TextView(this);
        sTitle.setText("Server Information");
        sTitle.setTextColor(Color.parseColor("#9AA3B2"));
        sTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
        infoHdr.addView(sTitle, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        TextView rotateBtn = makeSmallPillButton("↻ Rotate IP", false);
        rotateBtn.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                rotateShihabGatewayIp();
            }
        });
        infoHdr.addView(rotateBtn);

        View sp = new View(this);
        infoHdr.addView(sp, new LinearLayout.LayoutParams(dp(6), dp(1)));

        mDashModeTag = makeSmallPillButton("VpnService", true);
        infoHdr.addView(mDashModeTag);
        infoCard.addView(infoHdr);

        infoCard.addView(spaceV(8));
        mDashIpVal = addMetricLine(infoCard, "Active Gateway / IP", "—");
        mDashPingVal = addMetricLine(infoCard, "Latency", "24 ms");
        mDashSpeedVal = addMetricLine(infoCard, "Peak Speed", "0.0 Mbps");
        mDashTimerVal = addMetricLine(infoCard, "Connected Time", "00:00");
        col.addView(infoCard);

        col.addView(spaceV(14));

        // 3. Connect Power Orb
        LinearLayout orbWrap = new LinearLayout(this);
        orbWrap.setOrientation(LinearLayout.VERTICAL);
        orbWrap.setGravity(Gravity.CENTER);

        mConnectOrbButton = new Button(this);
        mConnectOrbButton.setText("CONNECT");
        mConnectOrbButton.setTextColor(Color.WHITE);
        mConnectOrbButton.setTypeface(Typeface.DEFAULT_BOLD);
        mConnectOrbButton.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f);
        mConnectOrbButton.setAllCaps(false);
        mConnectOrbButton.setBackground(roundRect("#1DA1F2", "#38BDF8", 48));
        mConnectOrbButton.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (ShihabShieldVpnService.isShihabTunnelConnected()) {
                    disconnectShihabVpn();
                } else {
                    initiateShihabVpnConnect();
                }
            }
        });
        orbWrap.addView(mConnectOrbButton, new LinearLayout.LayoutParams(dp(148), dp(52)));

        mBottomBannerText = new TextView(this);
        mBottomBannerText.setText("Tap Connect to activate Android VpnService + Proxy Shield");
        mBottomBannerText.setTextColor(Color.parseColor("#8A93A2"));
        mBottomBannerText.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f);
        mBottomBannerText.setGravity(Gravity.CENTER);
        mBottomBannerText.setPadding(0, dp(8), 0, 0);
        orbWrap.addView(mBottomBannerText);

        col.addView(orbWrap);
        sv.addView(col);
        return sv;
    }

    private View buildServersTab() {
        LinearLayout wrap = new LinearLayout(this);
        wrap.setOrientation(LinearLayout.VERTICAL);
        wrap.setPadding(dp(14), dp(12), dp(14), dp(8));

        LinearLayout topRow = new LinearLayout(this);
        topRow.setOrientation(LinearLayout.HORIZONTAL);
        topRow.setGravity(Gravity.CENTER_VERTICAL);

        LinearLayout titleStack = new LinearLayout(this);
        titleStack.setOrientation(LinearLayout.VERTICAL);
        TextView h = new TextView(this);
        h.setText("Global Countries & Private Servers");
        h.setTextColor(Color.WHITE);
        h.setTypeface(Typeface.DEFAULT_BOLD);
        h.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f);
        titleStack.addView(h);

        mServerSummarySub = new TextView(this);
        mServerSummarySub.setText("Fetching live public relays...");
        mServerSummarySub.setTextColor(Color.parseColor("#8A93A2"));
        mServerSummarySub.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f);
        titleStack.addView(mServerSummarySub);
        topRow.addView(titleStack, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        TextView addPrivBtn = makeSmallPillButton("+ Private", true);
        addPrivBtn.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                showAddPrivateServerDialog();
            }
        });
        topRow.addView(addPrivBtn);
        wrap.addView(topRow);

        wrap.addView(spaceV(8));

        EditText searchIn = new EditText(this);
        searchIn.setHint("Search United States, UK, Japan, IP...");
        searchIn.setHintTextColor(Color.parseColor("#6E7785"));
        searchIn.setTextColor(Color.WHITE);
        searchIn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
        searchIn.setSingleLine(true);
        searchIn.setPadding(dp(12), dp(8), dp(12), dp(8));
        searchIn.setBackground(roundRect("#171A21", "#2A2F3A", 8));
        searchIn.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence s, int start, int count, int after) {}
            @Override public void onTextChanged(CharSequence s, int start, int before, int count) {}
            @Override
            public void afterTextChanged(Editable s) {
                mSearchQuery = s.toString().trim().toLowerCase(Locale.US);
                renderServerRows();
            }
        });
        wrap.addView(searchIn);

        wrap.addView(spaceV(8));

        // Mode switcher pills
        LinearLayout modeBar = new LinearLayout(this);
        modeBar.setOrientation(LinearLayout.HORIZONTAL);
        final String[][] modes = new String[][]{
                {"COUNTRIES", "Countries"},
                {"PROXIES", "Proxies"},
                {"OVPN", ".ovpn Export"},
                {"PRIVATE", "Private"}
        };
        final List<TextView> modeViews = new ArrayList<TextView>();
        for (final String[] m : modes) {
            final TextView tv = makeSmallPillButton(m[1], "COUNTRIES".equals(m[0]));
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.rightMargin = dp(6);
            tv.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    mServerViewMode = m[0];
                    for (TextView x : modeViews) stylePillButton(x, x == tv);
                    renderServerRows();
                }
            });
            modeViews.add(tv);
            modeBar.addView(tv, lp);
        }
        wrap.addView(modeBar);

        wrap.addView(spaceV(6));

        // Region filter pills
        LinearLayout regBar = new LinearLayout(this);
        regBar.setOrientation(LinearLayout.HORIZONTAL);
        final String[] regs = new String[]{"ALL", "Americas", "EMEA", "Asia & Oceania"};
        final List<TextView> regViews = new ArrayList<TextView>();
        for (final String rg : regs) {
            final TextView tv = makeSmallPillButton("ALL".equals(rg) ? "All Regions" : rg, "ALL".equals(rg));
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            lp.rightMargin = dp(6);
            tv.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    mRegionFilter = rg;
                    for (TextView x : regViews) stylePillButton(x, x == tv);
                    renderServerRows();
                }
            });
            regViews.add(tv);
            regBar.addView(tv, lp);
        }
        wrap.addView(regBar);

        wrap.addView(spaceV(8));

        ScrollView listScroll = new ScrollView(this);
        mServerRowsContainer = new LinearLayout(this);
        mServerRowsContainer.setOrientation(LinearLayout.VERTICAL);
        listScroll.addView(mServerRowsContainer);
        wrap.addView(listScroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        return wrap;
    }

    private View buildDiagnosticsTab() {
        ScrollView sv = new ScrollView(this);
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        col.setPadding(dp(14), dp(12), dp(14), dp(18));

        LinearLayout speedCard = makeBentoCard();
        TextView stTitle = new TextView(this);
        stTitle.setText("Live Speed & RTT Benchmark");
        stTitle.setTextColor(Color.WHITE);
        stTitle.setTypeface(Typeface.DEFAULT_BOLD);
        stTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f);
        speedCard.addView(stTitle);
        speedCard.addView(spaceV(6));
        mDiagSpeedVal = addMetricLine(speedCard, "Cloudflare HTTPS Throughput", "0.0 Mbps");
        mDiagPingVal = addMetricLine(speedCard, "RTT / Jitter", "24 / 2 ms");

        Button runSpeedBtn = makeActionButton("Run Speed Test", "#1DA1F2");
        runSpeedBtn.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                Toast.makeText(ShihabMainActivity.this, "Running Cloudflare HTTPS benchmark...", Toast.LENGTH_SHORT).show();
                mBgExecutor.execute(new Runnable() {
                    @Override
                    public void run() {
                        final ShihabSpeedResult r = ShihabRelayService.runShihabSpeedProbe();
                        mPrefs.appendLog("SPEED", "Measured " + r.downloadMbps + " Mbps (RTT " + r.pingMs + " ms)");
                        mUiHandler.post(new Runnable() {
                            @Override
                            public void run() {
                                if (r.downloadMbps > mPeakMbps) mPeakMbps = r.downloadMbps;
                                if (r.pingMs > 0) mLastPingMs = r.pingMs;
                                mLastJitterMs = r.jitterMs;
                                mDiagSpeedVal.setText(String.format(Locale.US, "%.1f Mbps", r.downloadMbps));
                                mDiagPingVal.setText(r.pingMs + " / " + r.jitterMs + " ms");
                                refreshLogsView();
                            }
                        });
                    }
                });
            }
        });
        speedCard.addView(spaceV(8));
        speedCard.addView(runSpeedBtn);
        col.addView(speedCard);

        col.addView(spaceV(10));

        LinearLayout geoCard = makeBentoCard();
        LinearLayout gHdr = new LinearLayout(this);
        gHdr.setOrientation(LinearLayout.HORIZONTAL);
        gHdr.setGravity(Gravity.CENTER_VERTICAL);
        TextView gTitle = new TextView(this);
        gTitle.setText("Observed Egress IPv4 & Leak Audit");
        gTitle.setTextColor(Color.WHITE);
        gTitle.setTypeface(Typeface.DEFAULT_BOLD);
        gTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f);
        gHdr.addView(gTitle, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        mDiagLeakBadge = makeSmallPillButton("AUDITING", false);
        gHdr.addView(mDiagLeakBadge);
        geoCard.addView(gHdr);

        geoCard.addView(spaceV(6));
        mDiagIpVal = addMetricLine(geoCard, "Public IPv4", "Detecting...");
        mDiagRouteVal = addMetricLine(geoCard, "Location", "—");
        mDiagIspVal = addMetricLine(geoCard, "ISP / ASN", "—");

        Button verifyBtn = makeActionButton("Verify IP Now", "#252B36");
        verifyBtn.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                triggerGeoIpAudit();
            }
        });
        geoCard.addView(spaceV(8));
        geoCard.addView(verifyBtn);
        col.addView(geoCard);

        col.addView(spaceV(10));

        LinearLayout logCard = makeBentoCard();
        LinearLayout lHdr = new LinearLayout(this);
        lHdr.setOrientation(LinearLayout.HORIZONTAL);
        TextView lTitle = new TextView(this);
        lTitle.setText("Engine Activity Logs");
        lTitle.setTextColor(Color.WHITE);
        lTitle.setTypeface(Typeface.DEFAULT_BOLD);
        lTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f);
        lHdr.addView(lTitle, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        TextView clrBtn = makeSmallPillButton("Clear", false);
        clrBtn.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                mPrefs.clearLogs();
                refreshLogsView();
            }
        });
        lHdr.addView(clrBtn);
        logCard.addView(lHdr);
        logCard.addView(spaceV(6));

        mLogsBox = new TextView(this);
        mLogsBox.setTextColor(Color.parseColor("#B6C2D4"));
        mLogsBox.setTypeface(Typeface.MONOSPACE);
        mLogsBox.setTextSize(TypedValue.COMPLEX_UNIT_SP, 10.5f);
        mLogsBox.setPadding(dp(8), dp(8), dp(8), dp(8));
        mLogsBox.setBackground(roundRect("#111319", "#242934", 6));
        logCard.addView(mLogsBox);

        col.addView(logCard);
        sv.addView(col);
        return sv;
    }

    private View buildShieldSettingsTab() {
        ScrollView sv = new ScrollView(this);
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        col.setPadding(dp(14), dp(12), dp(14), dp(18));

        LinearLayout dnsCard = makeBentoCard();
        TextView dTitle = new TextView(this);
        dTitle.setText("Tunnel-Scoped Secure DNS Shield");
        dTitle.setTextColor(Color.WHITE);
        dTitle.setTypeface(Typeface.DEFAULT_BOLD);
        dTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f);
        dnsCard.addView(dTitle);
        dnsCard.addView(spaceV(6));

        final String[] dnsOpts = new String[]{
                "Cloudflare (1.1.1.1)",
                "AdGuard Adblock DNS (94.140.14.14)",
                "Quad9 Malware Block (9.9.9.9)",
                "Google Public DNS (8.8.8.8)"
        };
        Spinner dnsSpin = new Spinner(this);
        ArrayAdapter<String> ad = new ArrayAdapter<String>(
                this, android.R.layout.simple_spinner_dropdown_item, dnsOpts);
        dnsSpin.setAdapter(ad);
        String savedDns = mPrefs.getDnsProvider();
        for (int i = 0; i < dnsOpts.length; i++) {
            if (dnsOpts[i].equals(savedDns)) dnsSpin.setSelection(i);
        }
        dnsSpin.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override
            public void onItemSelected(AdapterView<?> parent, View view, int pos, long id) {
                mPrefs.setDnsProvider(dnsOpts[pos]);
            }
            @Override public void onNothingSelected(AdapterView<?> parent) {}
        });
        dnsCard.addView(dnsSpin);

        dnsCard.addView(spaceV(10));

        Switch swRot = new Switch(this);
        swRot.setText("Dynamic IP Rotation (Fresh gateway IP per connect)");
        swRot.setTextColor(Color.parseColor("#D5DCE6"));
        swRot.setChecked(mPrefs.isDynamicRotationEnabled());
        swRot.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override
            public void onCheckedChanged(CompoundButton buttonView, boolean isChecked) {
                mPrefs.setDynamicRotationEnabled(isChecked);
            }
        });
        dnsCard.addView(swRot);

        dnsCard.addView(spaceV(8));

        Switch swProxy = new Switch(this);
        swProxy.setText("Attach Country HTTP/HTTPS Proxy to VpnService (Android 10+)");
        swProxy.setTextColor(Color.parseColor("#D5DCE6"));
        swProxy.setChecked(mPrefs.isProxyAttachmentEnabled());
        swProxy.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
            @Override
            public void onCheckedChanged(CompoundButton buttonView, boolean isChecked) {
                mPrefs.setProxyAttachmentEnabled(isChecked);
            }
        });
        dnsCard.addView(swProxy);
        col.addView(dnsCard);

        col.addView(spaceV(10));

        LinearLayout splitCard = makeBentoCard();
        TextView spTitle = new TextView(this);
        spTitle.setText("Per-App Split-Tunneling (Bypass VPN)");
        spTitle.setTextColor(Color.WHITE);
        spTitle.setTypeface(Typeface.DEFAULT_BOLD);
        spTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f);
        splitCard.addView(spTitle);
        splitCard.addView(spaceV(6));

        final EditText bypassIn = new EditText(this);
        bypassIn.setText(mPrefs.getBypassPackagesCsv());
        bypassIn.setHint("e.g. com.whatsapp, com.google.android.youtube");
        bypassIn.setHintTextColor(Color.parseColor("#6E7785"));
        bypassIn.setTextColor(Color.WHITE);
        bypassIn.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
        bypassIn.setPadding(dp(10), dp(8), dp(10), dp(8));
        bypassIn.setBackground(roundRect("#151820", "#2A2F3A", 8));
        splitCard.addView(bypassIn);

        Button saveSplitBtn = makeActionButton("Save App Bypass List", "#1DA1F2");
        saveSplitBtn.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                mPrefs.setBypassPackagesCsv(bypassIn.getText().toString());
                Toast.makeText(ShihabMainActivity.this, "Saved per-app split-tunnel list.", Toast.LENGTH_SHORT).show();
            }
        });
        splitCard.addView(spaceV(8));
        splitCard.addView(saveSplitBtn);
        col.addView(splitCard);

        sv.addView(col);
        return sv;
    }

    private View buildAboutDeveloperTab() {
        ScrollView sv = new ScrollView(this);
        LinearLayout col = new LinearLayout(this);
        col.setOrientation(LinearLayout.VERTICAL);
        col.setPadding(dp(14), dp(12), dp(14), dp(18));

        LinearLayout card = makeBentoCard();
        TextView t1 = new TextView(this);
        t1.setText("MASTER VPN PRO — Android Edition v1.2.0");
        t1.setTextColor(Color.WHITE);
        t1.setTypeface(Typeface.DEFAULT_BOLD);
        t1.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15.5f);
        card.addView(t1);

        TextView t2 = new TextView(this);
        t2.setText("Engineered & Designed by Mehedi Hasan Shihab");
        t2.setTextColor(Color.parseColor("#38BDF8"));
        t2.setTypeface(Typeface.DEFAULT_BOLD);
        t2.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
        t2.setPadding(0, dp(4), 0, dp(8));
        card.addView(t2);

        TextView desc = new TextView(this);
        desc.setText("Open-source multi-country Android VPN & Proxy Shield powered by Android VpnService, "
                + "Android 10+ HTTP/HTTPS Proxy Tunneling, Android 11+ VpnManager IKEv2/IPsec provisioning, "
                + "AdGuard/Cloudflare Secure DNS, Per-App Split-Tunneling, 1-tap .ovpn export across 70+ countries, "
                + "and an interactive 3D Fibonacci Globe.");
        desc.setTextColor(Color.parseColor("#C6CFDC"));
        desc.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
        card.addView(desc);

        card.addView(spaceV(12));

        addDeveloperLinkButton(card, "Portfolio — sshihabb007.github.io", "https://sshihabb007.github.io/");
        addDeveloperLinkButton(card, "GitHub — github.com/sshihabb007", "https://github.com/sshihabb007");
        addDeveloperLinkButton(card, "LinkedIn — Mehedi Hasan Shihab", "https://www.linkedin.com/in/mehedi-hasan-shihab");
        addDeveloperLinkButton(card, "Email — sshihabb007@gmail.com", "mailto:sshihabb007@gmail.com");

        TextView foot = new TextView(this);
        foot.setText("Copyright © 2026 Mehedi Hasan Shihab • MIT Open-Source License");
        foot.setTextColor(Color.parseColor("#7A8494"));
        foot.setTextSize(TypedValue.COMPLEX_UNIT_SP, 10.5f);
        foot.setPadding(0, dp(10), 0, 0);
        card.addView(foot);

        col.addView(card);
        sv.addView(col);
        return sv;
    }

    private View buildBottomNavigationBar() {
        LinearLayout nav = new LinearLayout(this);
        nav.setOrientation(LinearLayout.HORIZONTAL);
        nav.setBackgroundColor(Color.parseColor("#13161C"));
        nav.setPadding(dp(6), dp(8), dp(6), dp(10));

        String[] labels = new String[]{"Home", "Servers", "Audit", "Shield", "About"};
        for (int i = 0; i < labels.length; i++) {
            final int idx = i;
            TextView tv = new TextView(this);
            tv.setText(labels[i]);
            tv.setGravity(Gravity.CENTER);
            tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
            tv.setTypeface(Typeface.DEFAULT_BOLD);
            tv.setPadding(dp(4), dp(8), dp(4), dp(8));
            tv.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    switchTab(idx);
                }
            });
            mBottomNavButtons[i] = tv;
            nav.addView(tv, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        }
        return nav;
    }

    private void switchTab(int idx) {
        mActiveTab = idx;
        for (int i = 0; i < mTabContainers.length; i++) {
            mTabContainers[i].setVisibility(i == idx ? View.VISIBLE : View.GONE);
            if (mBottomNavButtons[i] != null) {
                boolean active = (i == idx);
                mBottomNavButtons[i].setTextColor(active ? Color.parseColor("#38BDF8") : Color.parseColor("#8A93A2"));
                mBottomNavButtons[i].setBackground(active
                        ? roundRect("#1B2838", "#2B4666", 10)
                        : null);
            }
        }
        if (idx == 1) renderServerRows();
        if (idx == 2) refreshLogsView();
    }

    // =========================================================================
    // CONNECTION, RELAY SYNC & DIALOGS
    // =========================================================================
    private void triggerRelayCatalogSync() {
        mBgExecutor.execute(new Runnable() {
            @Override
            public void run() {
                final List<ShihabRelayNode> fetched = ShihabRelayService.fetchAllShihabPublicRelays();
                mPrefs.appendLog("SYSTEM", "Synced " + fetched.size() + " public relays & proxies.");
                mUiHandler.post(new Runnable() {
                    @Override
                    public void run() {
                        mPublicRelays = fetched;
                        refreshDashboardUi();
                        if (mActiveTab == 1) renderServerRows();
                    }
                });
            }
        });
    }

    private void triggerGeoIpAudit() {
        mBgExecutor.execute(new Runnable() {
            @Override
            public void run() {
                String expected = ShihabShieldVpnService.isShihabTunnelConnected()
                        ? ShihabShieldVpnService.getShihabActiveCountryCode()
                        : "";
                final ShihabGeoAudit audit = ShihabRelayService.verifyShihabGeoIp(expected);
                mUiHandler.post(new Runnable() {
                    @Override
                    public void run() {
                        mCurrentGeo = audit;
                        if (!ShihabShieldVpnService.isShihabTunnelConnected() && !"—".equals(audit.publicIp)) {
                            mOriginGeo = audit;
                        }
                        if (mDiagIpVal != null) {
                            mDiagIpVal.setText(audit.publicIp);
                            mDiagRouteVal.setText(audit.countryLong
                                    + (audit.cityName.isEmpty() ? "" : (" • " + audit.cityName)));
                            mDiagIspVal.setText(audit.ispName);
                            mDiagLeakBadge.setText(ShihabShieldVpnService.isShihabTunnelConnected()
                                    ? (audit.leakDetected ? "CHECK PROXY" : "PROTECTED")
                                    : "DIRECT ISP");
                        }
                        refreshDashboardUi();
                    }
                });
            }
        });
    }

    private void initiateShihabVpnConnect() {
        Intent prepareIntent = VpnService.prepare(this);
        if (prepareIntent != null) {
            startActivityForResult(prepareIntent, REQ_VPN_CONSENT);
            return;
        }
        startShihabVpnServiceNow();
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == REQ_VPN_CONSENT && resultCode == RESULT_OK) {
            startShihabVpnServiceNow();
        } else if (requestCode == REQ_IKEV2_PROVISION && resultCode == RESULT_OK) {
            Toast.makeText(this, "Android IKEv2 VPN profile provisioned! Activate from system VPN or connect.", Toast.LENGTH_LONG).show();
        }
    }

    private void startShihabVpnServiceNow() {
        List<ShihabRelayNode> all = new ArrayList<ShihabRelayNode>();
        all.addAll(mPrefs.getPrivateRelays());
        all.addAll(mPublicRelays);

        String targetCc = mPrefs.getSelectedCountry();
        ShihabRelayNode chosen = mSelectedSpecificNode;
        if (chosen == null) {
            chosen = ShihabRelayService.selectBestShihabRelay(
                    all,
                    targetCc,
                    true,
                    mPrefs.isDynamicRotationEnabled(),
                    mPrefs.getRecentHosts());
        }

        ShihabCountryInfo meta = ShihabModels.resolveShihabCountry(targetCc, "");
        String host = "";
        int port = 0;
        String cc = meta.code;
        String cLong = meta.name;

        if (chosen != null) {
            cc = chosen.countryShort;
            cLong = chosen.countryLong;
            if ("OPEN_PROXY".equals(chosen.sourceType) || "PRIVATE_PROXY".equals(chosen.sourceType)) {
                host = chosen.host;
                port = chosen.port;
                mPrefs.recordRecentHost(host);
            }
            mLastPingMs = chosen.pingMs;
        }

        Intent svc = new Intent(this, ShihabShieldVpnService.class);
        svc.setAction(ShihabShieldVpnService.ACTION_CONNECT);
        svc.putExtra(ShihabShieldVpnService.EXTRA_HOST, host);
        svc.putExtra(ShihabShieldVpnService.EXTRA_PORT, port);
        svc.putExtra(ShihabShieldVpnService.EXTRA_COUNTRY, cc);
        svc.putExtra(ShihabShieldVpnService.EXTRA_COUNTRY_LONG, cLong);
        svc.putExtra(ShihabShieldVpnService.EXTRA_DNS, mPrefs.getDnsProvider());
        svc.putExtra(ShihabShieldVpnService.EXTRA_ATTACH_PROXY, mPrefs.isProxyAttachmentEnabled());
        svc.putExtra(ShihabShieldVpnService.EXTRA_BYPASS_PKGS, mPrefs.getBypassPackagesCsv());

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startForegroundService(svc);
        } else {
            startService(svc);
        }

        mUiHandler.postDelayed(new Runnable() {
            @Override
            public void run() {
                refreshDashboardUi();
                triggerGeoIpAudit();
            }
        }, 700L);
    }

    private void disconnectShihabVpn() {
        Intent svc = new Intent(this, ShihabShieldVpnService.class);
        svc.setAction(ShihabShieldVpnService.ACTION_DISCONNECT);
        startService(svc);
        mUiHandler.postDelayed(new Runnable() {
            @Override
            public void run() {
                refreshDashboardUi();
                triggerGeoIpAudit();
            }
        }, 400L);
    }

    private void rotateShihabGatewayIp() {
        mSelectedSpecificNode = null;
        mPrefs.setDynamicRotationEnabled(true);
        Toast.makeText(this, "Rotating to a fresh gateway IP...", Toast.LENGTH_SHORT).show();
        initiateShihabVpnConnect();
    }

    private void refreshDashboardUi() {
        boolean connected = ShihabShieldVpnService.isShihabTunnelConnected();
        String cc = connected
                ? ShihabShieldVpnService.getShihabActiveCountryCode()
                : mPrefs.getSelectedCountry();
        ShihabCountryInfo meta = ShihabModels.resolveShihabCountry(cc, "");

        mTopStatusPill.setText(connected ? "● Protected" : "● Disconnected");
        mTopStatusPill.setTextColor(connected ? Color.parseColor("#10B981") : Color.parseColor("#9AA3B2"));
        mTopStatusPill.setBackground(connected
                ? roundRect("#102B23", "#10B981", 14)
                : roundRect("#1B1F27", "#2B313D", 14));

        mDashCcBadge.setText(meta.code);
        mDashLocTitle.setText(meta.name);
        mDashLocSubtitle.setText(connected
                ? ShihabShieldVpnService.getShihabActiveModeSummary()
                : (meta.city + " • " + ShihabModels.formatCountryUtcClock(meta.utcMinutes)
                + " • " + mPublicRelays.size() + " relays"));

        mDashModeTag.setText(connected ? "ACTIVE TUNNEL" : "READY");
        mDashIpVal.setText(connected
                ? ShihabShieldVpnService.getShihabActiveEndpoint()
                : mCurrentGeo.publicIp);
        mDashPingVal.setText(mLastPingMs + " ms");
        mDashSpeedVal.setText(String.format(Locale.US, "%.1f Mbps", mPeakMbps));

        if (connected && ShihabShieldVpnService.getShihabConnectedSinceMs() > 0) {
            long sec = Math.max(0L, (System.currentTimeMillis() - ShihabShieldVpnService.getShihabConnectedSinceMs()) / 1000L);
            mDashTimerVal.setText(String.format(Locale.US, "%02d:%02d", sec / 60L, sec % 60L));
        } else {
            mDashTimerVal.setText("00:00");
        }

        mConnectOrbButton.setText(connected ? "DISCONNECT" : "CONNECT");
        mConnectOrbButton.setBackground(connected
                ? roundRect("#10B981", "#34D399", 48)
                : roundRect("#1DA1F2", "#38BDF8", 48));

        mBottomBannerText.setText(connected
                ? ("Protected via " + meta.name + " (" + ShihabShieldVpnService.getShihabActiveEndpoint() + ")")
                : "Select any of 70+ countries or tap Connect");

        if (mGlobeView != null) {
            mGlobeView.setRouteEndpoints(mOriginGeo.lat, mOriginGeo.lon, meta.lat, meta.lon, connected);
        }
    }

    private void renderServerRows() {
        if (mServerRowsContainer == null) return;
        mServerRowsContainer.removeAllViews();

        List<ShihabCountryHub> hubs = ShihabModels.buildShihabCountryHubs(mPublicRelays);
        mServerSummarySub.setText(hubs.size() + " countries • " + mPublicRelays.size()
                + " public relays • " + mPrefs.getPrivateRelays().size() + " private");

        if ("COUNTRIES".equals(mServerViewMode)) {
            for (final ShihabCountryHub hub : hubs) {
                if (!"ALL".equals(mRegionFilter) && !mRegionFilter.equals(hub.regionGroup)) continue;
                if (!mSearchQuery.isEmpty()
                        && !hub.countryLong.toLowerCase(Locale.US).contains(mSearchQuery)
                        && !hub.countryShort.toLowerCase(Locale.US).contains(mSearchQuery)
                        && !hub.cityName.toLowerCase(Locale.US).contains(mSearchQuery)) {
                    continue;
                }
                String sub = hub.cityName + " • " + hub.proxyCount + " proxy / " + hub.ovpnCount + " .ovpn";
                mServerRowsContainer.addView(buildServerRowView(
                        hub.countryShort,
                        hub.countryLong,
                        sub,
                        hub.bestPingMs + " ms",
                        new View.OnClickListener() {
                            @Override
                            public void onClick(View v) {
                                mSelectedSpecificNode = null;
                                mPrefs.setSelectedCountry(hub.countryShort);
                                switchTab(0);
                                initiateShihabVpnConnect();
                            }
                        }));
            }
        } else if ("PROXIES".equals(mServerViewMode)) {
            int shown = 0;
            for (final ShihabRelayNode n : mPublicRelays) {
                if (!"OPEN_PROXY".equals(n.sourceType)) continue;
                if (!"ALL".equals(mRegionFilter) && !mRegionFilter.equals(n.regionGroup)) continue;
                if (!mSearchQuery.isEmpty()
                        && !n.countryLong.toLowerCase(Locale.US).contains(mSearchQuery)
                        && !n.host.toLowerCase(Locale.US).contains(mSearchQuery)) {
                    continue;
                }
                mServerRowsContainer.addView(buildServerRowView(
                        n.countryShort,
                        n.countryLong + " • " + n.cityName,
                        n.protocol + " • " + n.host + ":" + n.port,
                        n.pingMs + " ms",
                        new View.OnClickListener() {
                            @Override
                            public void onClick(View v) {
                                mSelectedSpecificNode = n;
                                mPrefs.setSelectedCountry(n.countryShort);
                                switchTab(0);
                                initiateShihabVpnConnect();
                            }
                        }));
                if (++shown >= 90) break;
            }
        } else if ("OVPN".equals(mServerViewMode)) {
            int shown = 0;
            for (final ShihabRelayNode n : mPublicRelays) {
                if (n.ovpnConfigBase64 == null || n.ovpnConfigBase64.isEmpty()) continue;
                if (!"ALL".equals(mRegionFilter) && !mRegionFilter.equals(n.regionGroup)) continue;
                if (!mSearchQuery.isEmpty()
                        && !n.countryLong.toLowerCase(Locale.US).contains(mSearchQuery)
                        && !n.host.toLowerCase(Locale.US).contains(mSearchQuery)) {
                    continue;
                }
                mServerRowsContainer.addView(buildServerRowView(
                        n.countryShort,
                        n.countryLong + " (.ovpn)",
                        n.host + " • " + n.speedMbps + " Mbps • Tap to Export/Open",
                        "EXPORT",
                        new View.OnClickListener() {
                            @Override
                            public void onClick(View v) {
                                shareOrCopyOvpnProfile(n);
                            }
                        }));
                if (++shown >= 90) break;
            }
        } else {
            final List<ShihabRelayNode> privs = mPrefs.getPrivateRelays();
            if (privs.isEmpty()) {
                TextView empty = new TextView(this);
                empty.setText("No private servers added yet. Tap '+ Private' above to add a Private Proxy or Android 11+ IKEv2 VPN.");
                empty.setTextColor(Color.parseColor("#8A93A2"));
                empty.setPadding(dp(10), dp(18), dp(10), dp(18));
                mServerRowsContainer.addView(empty);
            }
            for (final ShihabRelayNode p : privs) {
                mServerRowsContainer.addView(buildServerRowView(
                        p.countryShort,
                        p.title.isEmpty() ? (p.countryLong + " Private") : p.title,
                        p.protocol + " • " + p.host + ":" + p.port,
                        "CONNECT",
                        new View.OnClickListener() {
                            @Override
                            public void onClick(View v) {
                                if ("PRIVATE_IKEV2".equals(p.sourceType)) {
                                    try {
                                        Intent prov = ShihabShieldVpnService.provisionShihabPrivateIkev2(
                                                ShihabMainActivity.this,
                                                p.host,
                                                p.username.isEmpty() ? p.host : p.username,
                                                p.password);
                                        if (prov != null) {
                                            startActivityForResult(prov, REQ_IKEV2_PROVISION);
                                        } else {
                                            Toast.makeText(ShihabMainActivity.this,
                                                    "IKEv2 profile already provisioned in Android VPN settings.",
                                                    Toast.LENGTH_LONG).show();
                                        }
                                    } catch (Exception ex) {
                                        Toast.makeText(ShihabMainActivity.this, ex.getMessage(), Toast.LENGTH_LONG).show();
                                    }
                                } else {
                                    mSelectedSpecificNode = p;
                                    mPrefs.setSelectedCountry(p.countryShort);
                                    switchTab(0);
                                    initiateShihabVpnConnect();
                                }
                            }
                        }));
            }
        }
    }

    private void shareOrCopyOvpnProfile(ShihabRelayNode node) {
        String ovpn = ShihabRelayService.decodeShihabOvpnText(node);
        if (ovpn.isEmpty()) {
            Toast.makeText(this, "Unable to decode .ovpn payload.", Toast.LENGTH_SHORT).show();
            return;
        }
        ClipboardManager cm = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        if (cm != null) {
            cm.setPrimaryClip(ClipData.newPlainText("MasterVpnPro.ovpn", ovpn));
        }
        Intent send = new Intent(Intent.ACTION_SEND);
        send.setType("text/plain");
        send.putExtra(Intent.EXTRA_SUBJECT, "MasterVpnPro_" + node.countryShort + "_" + node.host + ".ovpn");
        send.putExtra(Intent.EXTRA_TEXT, ovpn);
        startActivity(Intent.createChooser(send, "Export .ovpn Profile (Copied to Clipboard)"));
    }

    private void showAddPrivateServerDialog() {
        LinearLayout form = new LinearLayout(this);
        form.setOrientation(LinearLayout.VERTICAL);
        form.setPadding(dp(18), dp(12), dp(18), dp(6));

        final Spinner typeSpin = new Spinner(this);
        final String[] types = new String[]{"Private HTTP/HTTPS Proxy", "Private IKEv2/IPsec PSK (Android 11+)"};
        typeSpin.setAdapter(new ArrayAdapter<String>(this, android.R.layout.simple_spinner_dropdown_item, types));
        form.addView(typeSpin);

        final EditText titleIn = new EditText(this);
        titleIn.setHint("Label (e.g. US Dedicated Proxy)");
        form.addView(titleIn);

        final EditText ccIn = new EditText(this);
        ccIn.setHint("2-Letter Country Code (e.g. US, GB, DE)");
        ccIn.setText("US");
        form.addView(ccIn);

        final EditText hostIn = new EditText(this);
        hostIn.setHint("Server Host / IP (e.g. vpn.example.com)");
        form.addView(hostIn);

        final EditText portIn = new EditText(this);
        portIn.setHint("Port (e.g. 8080 or 500)");
        portIn.setText("8080");
        form.addView(portIn);

        final EditText userIn = new EditText(this);
        userIn.setHint("IKEv2 Identity or Username (optional)");
        form.addView(userIn);

        final EditText passIn = new EditText(this);
        passIn.setHint("IKEv2 Pre-Shared Key (PSK) or Password");
        form.addView(passIn);

        new AlertDialog.Builder(this)
                .setTitle("Add Private VPN / Proxy Gateway")
                .setView(form)
                .setPositiveButton("Save", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        String host = hostIn.getText().toString().trim();
                        if (host.isEmpty()) {
                            Toast.makeText(ShihabMainActivity.this, "Host is required.", Toast.LENGTH_SHORT).show();
                            return;
                        }
                        int port = 8080;
                        try { port = Integer.parseInt(portIn.getText().toString().trim()); } catch (Exception ignored) {}
                        boolean isIkev2 = typeSpin.getSelectedItemPosition() == 1;
                        ShihabCountryInfo meta = ShihabModels.resolveShihabCountry(ccIn.getText().toString(), "");

                        ShihabRelayNode node = new ShihabRelayNode();
                        node.id = "priv-" + UUID.randomUUID().toString();
                        node.sourceType = isIkev2 ? "PRIVATE_IKEV2" : "PRIVATE_PROXY";
                        node.protocol = isIkev2 ? "IKEv2" : "HTTP";
                        node.title = titleIn.getText().toString().trim();
                        node.countryShort = meta.code;
                        node.countryLong = meta.name;
                        node.cityName = meta.city;
                        node.regionGroup = meta.region;
                        node.host = host;
                        node.port = port;
                        node.username = userIn.getText().toString().trim();
                        node.password = passIn.getText().toString();
                        node.lat = meta.lat;
                        node.lon = meta.lon;

                        List<ShihabRelayNode> list = mPrefs.getPrivateRelays();
                        list.add(0, node);
                        mPrefs.savePrivateRelays(list);
                        mServerViewMode = "PRIVATE";
                        renderServerRows();
                    }
                })
                .setNegativeButton("Cancel", null)
                .show();
    }

    private void refreshLogsView() {
        if (mLogsBox != null) {
            mLogsBox.setText(mPrefs.getLogsText());
        }
    }

    // =========================================================================
    // SMALL WIDGET HELPERS
    // =========================================================================
    private LinearLayout makeBentoCard() {
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dp(14), dp(12), dp(14), dp(12));
        card.setBackground(roundRect("#1A1D24", "#272B35", 12));
        return card;
    }

    private TextView addMetricLine(LinearLayout parent, String label, String initialVal) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setPadding(0, dp(3), 0, dp(3));

        TextView l = new TextView(this);
        l.setText(label);
        l.setTextColor(Color.parseColor("#8A93A2"));
        l.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
        row.addView(l, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        TextView v = new TextView(this);
        v.setText(initialVal);
        v.setTextColor(Color.WHITE);
        v.setTypeface(Typeface.DEFAULT_BOLD);
        v.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
        row.addView(v);

        parent.addView(row);
        return v;
    }

    private View buildServerRowView(String cc, String title, String sub, String rightBadge, View.OnClickListener onClick) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(10), dp(10), dp(10), dp(10));
        row.setBackground(roundRect("#181B22", "#252A34", 10));
        LinearLayout.LayoutParams rlp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        rlp.bottomMargin = dp(6);
        row.setLayoutParams(rlp);
        row.setOnClickListener(onClick);

        TextView badge = new TextView(this);
        badge.setText(cc);
        badge.setTextColor(Color.WHITE);
        badge.setTypeface(Typeface.DEFAULT_BOLD);
        badge.setGravity(Gravity.CENTER);
        badge.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f);
        badge.setBackground(roundRect("#22324A", "#38BDF8", 17));
        row.addView(badge, new LinearLayout.LayoutParams(dp(34), dp(34)));

        LinearLayout texts = new LinearLayout(this);
        texts.setOrientation(LinearLayout.VERTICAL);
        texts.setPadding(dp(10), 0, dp(8), 0);

        TextView t1 = new TextView(this);
        t1.setText(title);
        t1.setTextColor(Color.WHITE);
        t1.setTypeface(Typeface.DEFAULT_BOLD);
        t1.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f);
        texts.addView(t1);

        TextView t2 = new TextView(this);
        t2.setText(sub);
        t2.setTextColor(Color.parseColor("#8A93A2"));
        t2.setTextSize(TypedValue.COMPLEX_UNIT_SP, 10.8f);
        texts.addView(t2);
        row.addView(texts, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));

        TextView tag = makeSmallPillButton(rightBadge, false);
        row.addView(tag);
        return row;
    }

    private void addDeveloperLinkButton(LinearLayout parent, String label, final String url) {
        Button b = makeActionButton(label, "#1F2633");
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.bottomMargin = dp(6);
        b.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                try {
                    startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
                } catch (Exception ignored) {}
            }
        });
        parent.addView(b, lp);
    }

    private TextView makeSmallPillButton(String text, boolean active) {
        TextView tv = new TextView(this);
        tv.setText(text);
        tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f);
        tv.setTypeface(Typeface.DEFAULT_BOLD);
        tv.setPadding(dp(9), dp(4), dp(9), dp(4));
        stylePillButton(tv, active);
        return tv;
    }

    private void stylePillButton(TextView tv, boolean active) {
        tv.setTextColor(active ? Color.parseColor("#7DD3FC") : Color.parseColor("#A5AEBD"));
        tv.setBackground(active
                ? roundRect("#182A3E", "#1DA1F2", 8)
                : roundRect("#20242E", "#2E3442", 8));
    }

    private Button makeActionButton(String text, String bgHex) {
        Button b = new Button(this);
        b.setText(text);
        b.setAllCaps(false);
        b.setTextColor(Color.WHITE);
        b.setTypeface(Typeface.DEFAULT_BOLD);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
        b.setBackground(roundRect(bgHex, "#364153", 8));
        return b;
    }

    private GradientDrawable roundRect(String fillHex, String strokeHex, int radiusDp) {
        GradientDrawable gd = new GradientDrawable();
        gd.setColor(Color.parseColor(fillHex));
        gd.setCornerRadius(dp(radiusDp));
        gd.setStroke(dp(1), Color.parseColor(strokeHex));
        return gd;
    }

    private View spaceV(int heightDp) {
        View v = new View(this);
        v.setLayoutParams(new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(heightDp)));
        return v;
    }

    private int dp(int val) {
        return Math.round(val * getResources().getDisplayMetrics().density);
    }
}

