package com.shihab.shieldvpn;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.Path;
import android.view.MotionEvent;
import android.view.View;

/**
 * Shihab Shield VPN — Interactive Touch-Rotatable 3D Fibonacci Globe View for Android.
 * Supports 3D, Shield, Orbit, and 2D Equirectangular camera modes + Origin->Exit great-circle arcs.
 * Engineered by Mehedi Hasan Shihab (sshihabb007).
 */
public class ShihabGlobeView extends View {

    private static final int NUM_POINTS = 720;
    private final float[] mBaseLat = new float[NUM_POINTS];
    private final float[] mBaseLon = new float[NUM_POINTS];
    private final boolean[] mIsLand = new boolean[NUM_POINTS];

    private float mRotYaw = -0.55f;
    private float mRotPitch = 0.28f;
    private float mLastTouchX;
    private float mLastTouchY;
    private boolean mDragging = false;
    private float mAnimPhase = 0f;

    private String mCameraMode = "3D"; // 3D | Shield | Orbit | 2D
    private boolean mConnected = false;
    private double mOriginLat = 23.81;
    private double mOriginLon = 90.41;
    private double mTargetLat = 40.71;
    private double mTargetLon = -74.01;

    private final Paint mSpherePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint mRimPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint mDotPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint mArcPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint mMarkerPaint = new Paint(Paint.ANTI_ALIAS_FLAG);

    private final Runnable mTicker = new Runnable() {
        @Override
        public void run() {
            if (!isAttachedToWindow()) return;
            mAnimPhase += 0.045f;
            if (!mDragging && !"2D".equals(mCameraMode)) {
                float speed = "Orbit".equals(mCameraMode) ? 0.018f : 0.005f;
                mRotYaw += speed;
            }
            invalidate();
            postDelayed(this, 36L);
        }
    };

    public ShihabGlobeView(Context context) {
        super(context);
        initFibonacciSphere();

        mSpherePaint.setStyle(Paint.Style.FILL);
        mSpherePaint.setColor(Color.parseColor("#111722"));

        mRimPaint.setStyle(Paint.Style.STROKE);
        mRimPaint.setStrokeWidth(2.4f);

        mArcPaint.setStyle(Paint.Style.STROKE);
        mArcPaint.setStrokeWidth(3.5f);
        mArcPaint.setStrokeCap(Paint.Cap.ROUND);
    }

    private void initFibonacciSphere() {
        final double goldenAngle = Math.PI * (3.0 - Math.sqrt(5.0));
        for (int i = 0; i < NUM_POINTS; i++) {
            double y = 1.0 - (i / (double) (NUM_POINTS - 1)) * 2.0;
            double latDeg = Math.toDegrees(Math.asin(y));
            double theta = goldenAngle * i;
            double lonDeg = ((Math.toDegrees(theta) % 360.0) + 540.0) % 360.0 - 180.0;
            mBaseLat[i] = (float) latDeg;
            mBaseLon[i] = (float) lonDeg;
            mIsLand[i] = approxContinentalLand(latDeg, lonDeg);
        }
    }

    private static boolean approxContinentalLand(double lat, double lon) {
        // North America
        if (lat >= 15 && lat <= 70 && lon >= -165 && lon <= -55) return true;
        // South America
        if (lat >= -55 && lat <= 14 && lon >= -82 && lon <= -35) return true;
        // Europe
        if (lat >= 36 && lat <= 70 && lon >= -10 && lon <= 45) return true;
        // Africa
        if (lat >= -35 && lat <= 36 && lon >= -17 && lon <= 51) return true;
        // Asia
        if (lat >= 8 && lat <= 72 && lon >= 45 && lon <= 146) return true;
        // SE Asia & Maritime
        if (lat >= -10 && lat <= 22 && lon >= 95 && lon <= 140) return true;
        // Australia
        if (lat >= -42 && lat <= -11 && lon >= 113 && lon <= 154) return true;
        return false;
    }

    public void setCameraMode(String mode) {
        mCameraMode = mode == null ? "3D" : mode;
        invalidate();
    }

    public void setRouteEndpoints(double originLat, double originLon, double targetLat, double targetLon, boolean connected) {
        mOriginLat = originLat;
        mOriginLon = originLon;
        mTargetLat = targetLat;
        mTargetLon = targetLon;
        mConnected = connected;
        // Smoothly orient globe toward target longitude
        mRotYaw = (float) (-Math.toRadians(targetLon) + Math.PI * 0.5);
        invalidate();
    }

    @Override
    protected void onAttachedToWindow() {
        super.onAttachedToWindow();
        removeCallbacks(mTicker);
        post(mTicker);
    }

    @Override
    protected void onDetachedFromWindow() {
        removeCallbacks(mTicker);
        super.onDetachedFromWindow();
    }

    @Override
    public boolean onTouchEvent(MotionEvent event) {
        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                mDragging = true;
                mLastTouchX = event.getX();
                mLastTouchY = event.getY();
                getParent().requestDisallowInterceptTouchEvent(true);
                return true;
            case MotionEvent.ACTION_MOVE:
                float dx = event.getX() - mLastTouchX;
                float dy = event.getY() - mLastTouchY;
                mLastTouchX = event.getX();
                mLastTouchY = event.getY();
                mRotYaw += dx * 0.008f;
                mRotPitch = Math.max(-1.1f, Math.min(1.1f, mRotPitch + dy * 0.008f));
                invalidate();
                return true;
            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_CANCEL:
                mDragging = false;
                getParent().requestDisallowInterceptTouchEvent(false);
                return true;
        }
        return super.onTouchEvent(event);
    }

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        int w = getWidth();
        int h = getHeight();
        if (w <= 10 || h <= 10) return;

        float cx = w * 0.5f;
        float cy = h * 0.5f;

        if ("2D".equals(mCameraMode)) {
            drawFlat2dProjection(canvas, w, h);
            return;
        }

        float radius = Math.min(w, h) * 0.42f;
        canvas.drawCircle(cx, cy, radius, mSpherePaint);

        int accentColor = mConnected ? Color.parseColor("#10B981") : Color.parseColor("#38BDF8");
        mRimPaint.setColor(accentColor);
        mRimPaint.setAlpha(140);
        canvas.drawCircle(cx, cy, radius, mRimPaint);

        if ("Shield".equals(mCameraMode)) {
            mRimPaint.setAlpha(75);
            canvas.drawOval(cx - radius * 1.18f, cy - radius * 0.45f,
                    cx + radius * 1.18f, cy + radius * 0.45f, mRimPaint);
            canvas.drawOval(cx - radius * 0.45f, cy - radius * 1.12f,
                    cx + radius * 0.45f, cy + radius * 1.12f, mRimPaint);
        }

        for (int i = 0; i < NUM_POINTS; i++) {
            float[] proj = projectPoint3D(mBaseLat[i], mBaseLon[i], cx, cy, radius);
            if (proj[2] < -0.15f) continue;
            boolean front = proj[2] >= 0f;
            if (mIsLand[i]) {
                mDotPaint.setColor(mConnected ? Color.parseColor("#34D399") : Color.parseColor("#60C5FA"));
                mDotPaint.setAlpha(front ? 215 : 55);
                canvas.drawCircle(proj[0], proj[1], front ? 3.1f : 1.8f, mDotPaint);
            } else if (front && (i % 3 == 0)) {
                mDotPaint.setColor(Color.parseColor("#2A3649"));
                mDotPaint.setAlpha(95);
                canvas.drawCircle(proj[0], proj[1], 1.5f, mDotPaint);
            }
        }

        // Draw great-circle arc Origin -> Target
        Path arcPath = new Path();
        boolean started = false;
        int steps = 28;
        for (int s = 0; s <= steps; s++) {
            double t = s / (double) steps;
            double iLat = mOriginLat + (mTargetLat - mOriginLat) * t;
            double iLon = mOriginLon + (mTargetLon - mOriginLon) * t;
            float lift = (float) (1.0 + Math.sin(Math.PI * t) * 0.16);
            float[] p = projectPoint3D(iLat, iLon, cx, cy, radius * lift);
            if (p[2] >= -0.25f) {
                if (!started) {
                    arcPath.moveTo(p[0], p[1]);
                    started = true;
                } else {
                    arcPath.lineTo(p[0], p[1]);
                }
            }
        }
        mArcPaint.setColor(accentColor);
        mArcPaint.setAlpha(210);
        canvas.drawPath(arcPath, mArcPaint);

        float[] origP = projectPoint3D(mOriginLat, mOriginLon, cx, cy, radius);
        if (origP[2] >= 0f) {
            mMarkerPaint.setColor(Color.parseColor("#F59E0B"));
            canvas.drawCircle(origP[0], origP[1], 5.5f, mMarkerPaint);
        }

        float[] dstP = projectPoint3D(mTargetLat, mTargetLon, cx, cy, radius);
        if (dstP[2] >= -0.05f) {
            float pulse = (float) (7.0 + Math.sin(mAnimPhase * 2.0) * 4.5);
            mMarkerPaint.setColor(accentColor);
            mMarkerPaint.setAlpha(85);
            canvas.drawCircle(dstP[0], dstP[1], pulse, mMarkerPaint);
            mMarkerPaint.setAlpha(255);
            canvas.drawCircle(dstP[0], dstP[1], 6.0f, mMarkerPaint);
        }
    }

    private void drawFlat2dProjection(Canvas canvas, int w, int h) {
        float pad = 18f;
        float mapW = w - pad * 2f;
        float mapH = h - pad * 2f;

        for (int i = 0; i < NUM_POINTS; i++) {
            if (!mIsLand[i]) continue;
            float x = pad + ((mBaseLon[i] + 180f) / 360f) * mapW;
            float y = pad + ((90f - mBaseLat[i]) / 180f) * mapH;
            mDotPaint.setColor(mConnected ? Color.parseColor("#34D399") : Color.parseColor("#60C5FA"));
            mDotPaint.setAlpha(185);
            canvas.drawCircle(x, y, 2.8f, mDotPaint);
        }

        float ox = (float) (pad + ((mOriginLon + 180.0) / 360.0) * mapW);
        float oy = (float) (pad + ((90.0 - mOriginLat) / 180.0) * mapH);
        float tx = (float) (pad + ((mTargetLon + 180.0) / 360.0) * mapW);
        float ty = (float) (pad + ((90.0 - mTargetLat) / 180.0) * mapH);

        Path p = new Path();
        p.moveTo(ox, oy);
        p.quadTo((ox + tx) * 0.5f, Math.min(oy, ty) - 28f, tx, ty);
        mArcPaint.setColor(mConnected ? Color.parseColor("#10B981") : Color.parseColor("#38BDF8"));
        canvas.drawPath(p, mArcPaint);

        mMarkerPaint.setColor(Color.parseColor("#F59E0B"));
        canvas.drawCircle(ox, oy, 5.5f, mMarkerPaint);
        mMarkerPaint.setColor(mConnected ? Color.parseColor("#10B981") : Color.parseColor("#38BDF8"));
        canvas.drawCircle(tx, ty, 6.5f, mMarkerPaint);
    }

    private float[] projectPoint3D(double latDeg, double lonDeg, float cx, float cy, float radius) {
        double lat = Math.toRadians(latDeg);
        double lon = Math.toRadians(lonDeg) + mRotYaw;

        double x = Math.cos(lat) * Math.cos(lon);
        double z = Math.cos(lat) * Math.sin(lon);
        double y = Math.sin(lat);

        double cosP = Math.cos(mRotPitch);
        double sinP = Math.sin(mRotPitch);
        double y2 = y * cosP - z * sinP;
        double z2 = y * sinP + z * cosP;

        float sx = (float) (cx + x * radius);
        float sy = (float) (cy - y2 * radius);
        return new float[]{sx, sy, (float) z2};
    }
}

