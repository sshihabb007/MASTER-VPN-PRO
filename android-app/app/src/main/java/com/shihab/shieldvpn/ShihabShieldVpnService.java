package com.shihab.shieldvpn;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.net.Ikev2VpnProfile;
import android.net.ProxyInfo;
import android.net.VpnManager;
import android.net.VpnService;
import android.os.Build;
import android.os.ParcelFileDescriptor;

import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.List;

/**
 * Shihab Shield VPN — Android VpnService Engine.
 * Establishes Android TUN interface with Secure DNS Shield, Android 10+ System HTTP/HTTPS Proxy
 * Tunneling, Per-App Split-Tunneling, and Android 11+ VpnManager IKEv2/IPsec Provisioning.
 * Engineered by Mehedi Hasan Shihab (sshihabb007).
 */
public class ShihabShieldVpnService extends VpnService {

    public static final String ACTION_CONNECT = "com.shihab.shieldvpn.ACTION_CONNECT";
    public static final String ACTION_DISCONNECT = "com.shihab.shieldvpn.ACTION_DISCONNECT";

    public static final String EXTRA_HOST = "extra_host";
    public static final String EXTRA_PORT = "extra_port";
    public static final String EXTRA_COUNTRY = "extra_country";
    public static final String EXTRA_COUNTRY_LONG = "extra_country_long";
    public static final String EXTRA_PROTOCOL = "extra_protocol";
    public static final String EXTRA_DNS = "extra_dns";
    public static final String EXTRA_ATTACH_PROXY = "extra_attach_proxy";
    public static final String EXTRA_BYPASS_PKGS = "extra_bypass_pkgs";

    private static final String CHANNEL_ID = "shihab_shield_vpn_channel";
    private static final int NOTIFICATION_ID = 38195;

    private static volatile boolean sConnected = false;
    private static volatile long sConnectedSinceMs = 0L;
    private static volatile String sActiveEndpoint = "";
    private static volatile String sActiveCountryCode = "";
    private static volatile String sActiveCountryLong = "";
    private static volatile String sActiveModeSummary = "";

    private ParcelFileDescriptor mShihabTunInterface;

    public static boolean isShihabTunnelConnected() {
        return sConnected;
    }

    public static long getShihabConnectedSinceMs() {
        return sConnectedSinceMs;
    }

    public static String getShihabActiveEndpoint() {
        return sActiveEndpoint;
    }

    public static String getShihabActiveCountryCode() {
        return sActiveCountryCode;
    }

    public static String getShihabActiveCountryLong() {
        return sActiveCountryLong;
    }

    public static String getShihabActiveModeSummary() {
        return sActiveModeSummary;
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent == null || ACTION_DISCONNECT.equals(intent.getAction())) {
            stopShihabTunnel();
            return START_NOT_STICKY;
        }

        if (ACTION_CONNECT.equals(intent.getAction())) {
            String host = intent.getStringExtra(EXTRA_HOST);
            int port = intent.getIntExtra(EXTRA_PORT, 0);
            String cc = intent.getStringExtra(EXTRA_COUNTRY);
            String cLong = intent.getStringExtra(EXTRA_COUNTRY_LONG);
            String dnsLabel = intent.getStringExtra(EXTRA_DNS);
            boolean attachProxy = intent.getBooleanExtra(EXTRA_ATTACH_PROXY, true);
            String bypassCsv = intent.getStringExtra(EXTRA_BYPASS_PKGS);

            startShihabTunnel(host, port, cc, cLong, dnsLabel, attachProxy, bypassCsv);
            return START_STICKY;
        }
        return START_NOT_STICKY;
    }

    private synchronized void startShihabTunnel(
            String host,
            int port,
            String cc,
            String cLong,
            String dnsLabel,
            boolean attachProxy,
            String bypassCsv) {

        closeTunDescriptorQuietly();

        String safeCc = (cc == null || cc.isEmpty()) ? "US" : cc;
        String safeCountry = (cLong == null || cLong.isEmpty()) ? "Protected Gateway" : cLong;
        String[] dnsPair = resolveDnsServers(dnsLabel);

        try {
            Builder builder = new Builder();
            builder.setSession("MASTER VPN PRO (" + safeCc + ")");
            builder.setMtu(1400);
            builder.addAddress("10.214.0.2", 30);

            for (String dnsIp : dnsPair) {
                builder.addDnsServer(dnsIp);
            }

            // Always exclude our own package so GeoIP & relay fetches never loop into our own proxy
            try {
                builder.addDisallowedApplication(getPackageName());
            } catch (Exception ignored) {}

            if (bypassCsv != null && !bypassCsv.trim().isEmpty()) {
                for (String pkg : bypassCsv.split(",")) {
                    String cleanPkg = pkg.trim();
                    if (!cleanPkg.isEmpty()) {
                        try {
                            builder.addDisallowedApplication(cleanPkg);
                        } catch (Exception ignored) {}
                    }
                }
            }

            boolean proxyApplied = false;
            if (attachProxy && host != null && !host.isEmpty() && port > 0
                    && Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                List<String> bypassHosts = Arrays.asList("localhost", "127.0.0.1", "*.local");
                ProxyInfo proxyInfo = ProxyInfo.buildDirectProxy(host, port, bypassHosts);
                builder.setHttpProxy(proxyInfo);
                proxyApplied = true;
            }

            Notification notification = buildShihabForegroundNotification(
                    safeCountry + " • " + (proxyApplied ? (host + ":" + port) : ("DNS Shield " + dnsPair[0])));

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
                startForeground(
                        NOTIFICATION_ID,
                        notification,
                        ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
            } else {
                startForeground(NOTIFICATION_ID, notification);
            }

            mShihabTunInterface = builder.establish();
            if (mShihabTunInterface == null) {
                stopShihabTunnel();
                return;
            }

            sConnected = true;
            sConnectedSinceMs = System.currentTimeMillis();
            sActiveCountryCode = safeCc;
            sActiveCountryLong = safeCountry;
            sActiveEndpoint = (host != null && !host.isEmpty() && port > 0)
                    ? (host + ":" + port)
                    : dnsPair[0];
            sActiveModeSummary = proxyApplied
                    ? ("VpnService + Proxy (" + dnsPair[0] + ")")
                    : ("VpnService DNS Shield (" + dnsPair[0] + ")");

            new ShihabModels.ShihabPreferencesStore(this).appendLog(
                    "TUNNEL",
                    "Connected " + safeCountry + " [" + sActiveModeSummary + "] -> " + sActiveEndpoint);
        } catch (Exception ex) {
            new ShihabModels.ShihabPreferencesStore(this).appendLog(
                    "TUNNEL",
                    "Failed to establish VpnService: " + ex.getMessage());
            stopShihabTunnel();
        }
    }

    public synchronized void stopShihabTunnel() {
        boolean wasActive = sConnected || mShihabTunInterface != null;
        sConnected = false;
        sConnectedSinceMs = 0L;
        sActiveEndpoint = "";
        sActiveCountryCode = "";
        sActiveCountryLong = "";
        sActiveModeSummary = "";

        closeTunDescriptorQuietly();

        try {
            stopForeground(true);
        } catch (Exception ignored) {}
        stopSelf();

        if (wasActive) {
            new ShihabModels.ShihabPreferencesStore(this).appendLog(
                    "TUNNEL",
                    "MASTER VPN PRO VpnService tunnel disconnected cleanly.");
        }
    }

    private void closeTunDescriptorQuietly() {
        if (mShihabTunInterface != null) {
            try {
                mShihabTunInterface.close();
            } catch (Exception ignored) {}
            mShihabTunInterface = null;
        }
    }

    @Override
    public void onRevoke() {
        stopShihabTunnel();
        super.onRevoke();
    }

    @Override
    public void onDestroy() {
        closeTunDescriptorQuietly();
        sConnected = false;
        super.onDestroy();
    }

    private static String[] resolveDnsServers(String label) {
        if (label != null) {
            if (label.contains("AdGuard")) {
                return new String[]{"94.140.14.14", "94.140.15.15"};
            }
            if (label.contains("Quad9")) {
                return new String[]{"9.9.9.9", "149.112.112.112"};
            }
            if (label.contains("Google")) {
                return new String[]{"8.8.8.8", "8.8.4.4"};
            }
        }
        return new String[]{"1.1.1.1", "1.0.0.1"};
    }

    private Notification buildShihabForegroundNotification(String detailText) {
        NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && nm != null) {
            NotificationChannel ch = new NotificationChannel(
                    CHANNEL_ID,
                    "MASTER VPN PRO Status",
                    NotificationManager.IMPORTANCE_LOW);
            ch.setDescription("Active VPN tunnel & DNS protection status");
            nm.createNotificationChannel(ch);
        }

        Intent openUiIntent = new Intent(this, ShihabMainActivity.class);
        PendingIntent openPending = PendingIntent.getActivity(
                this,
                1,
                openUiIntent,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);

        Intent discIntent = new Intent(this, ShihabShieldVpnService.class);
        discIntent.setAction(ACTION_DISCONNECT);
        PendingIntent discPending = PendingIntent.getService(
                this,
                2,
                discIntent,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);

        Notification.Builder nb = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                ? new Notification.Builder(this, CHANNEL_ID)
                : new Notification.Builder(this);

        nb.setSmallIcon(android.R.drawable.ic_lock_lock)
                .setContentTitle("MASTER VPN PRO Protected")
                .setContentText(detailText)
                .setOngoing(true)
                .setContentIntent(openPending)
                .addAction(new Notification.Action.Builder(
                        android.R.drawable.ic_menu_close_clear_cancel,
                        "Disconnect",
                        discPending).build());

        return nb.build();
    }

    /**
     * Provisions a native kernel IKEv2/IPsec PSK profile on Android 11+ (API 30+)
     * when the user adds a Private IKEv2 Gateway.
     */
    public static Intent provisionShihabPrivateIkev2(
            Context ctx,
            String serverAddr,
            String identity,
            String preSharedKey) throws Exception {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) {
            throw new Exception("Native IKEv2/IPsec provisioning requires Android 11 (API 30) or newer.");
        }
        VpnManager vm = ctx.getSystemService(VpnManager.class);
        if (vm == null) {
            throw new Exception("Android VpnManager service unavailable.");
        }
        Ikev2VpnProfile profile = new Ikev2VpnProfile.Builder(serverAddr, identity)
                .setAuthPsk(preSharedKey.getBytes(StandardCharsets.UTF_8))
                .build();
        return vm.provisionVpnProfile(profile);
    }
}

