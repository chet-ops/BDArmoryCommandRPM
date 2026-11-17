// TGP mode: the REAL image of BDArmory's targeting camera (ModuleTargetingCamera)
// with all its controls on the MFD keys.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using BDArmory.Targeting;
using BDArmory.Utils;

namespace BDArmoryCommandRPM
{
    public class TGPMode : BDAMode
    {
        public float reticleSize = 0.12f;
        public bool showAttitude = true;

        private Vector2 heldSlew = Vector2.zero;
        private int lastSlewFrame = -1;

        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type tgpType = typeof(ModuleTargetingCamera);
        private static readonly MethodInfo mSlew = tgpType.GetMethod("SlewCamera", Inst, null, new[] { typeof(Vector3) }, null);
        private static readonly MethodInfo mZoomIn = tgpType.GetMethod("ZoomIn", Inst, null, Type.EmptyTypes, null);
        private static readonly MethodInfo mZoomOut = tgpType.GetMethod("ZoomOut", Inst, null, Type.EmptyTypes, null);
        private static readonly MethodInfo mNV = tgpType.GetMethod("ToggleNV", Inst, null, Type.EmptyTypes, null);
        private static readonly MethodInfo mReset = tgpType.GetMethod("ResetCameraButton", Inst, null, Type.EmptyTypes, null);
        private static readonly MethodInfo mClear = tgpType.GetMethod("ClearTarget", Inst, null, Type.EmptyTypes, null);
        private static readonly MethodInfo mSendGPS = tgpType.GetMethod("SendGPS", Inst, null, Type.EmptyTypes, null);
        private static readonly MethodInfo mSlave = tgpType.GetMethod("SlaveTurrets", Inst, null, Type.EmptyTypes, null);
        private static readonly MethodInfo mUnslave = tgpType.GetMethod("UnslaveTurrets", Inst, null, Type.EmptyTypes, null);
        private static readonly MethodInfo mToGPS = tgpType.GetMethod("PointToGPSTarget", Inst, null, Type.EmptyTypes, null);
        private static readonly FieldInfo fZoomTimes = tgpType.GetField("zoomTimes", Inst);
        // The same motion BDArmory's window uses when the image is dragged with the mouse
        private static readonly MethodInfo mSlewMouse = tgpType.GetMethod("SlewMouseCamRoutine", Inst, null, new[] { typeof(Vector3) }, null);

        // Mouse over the panel screen (BDARMORYCOMMANDRPM_DISPLAY in Settings.cfg)
        private static bool settingsRead;
        private static bool mouseControl = true;
        private static float mouseSensitivity = 1f;
        private static bool invertWheel;
        // Key that must be held to control the camera with the mouse.
        // "Alt" = either Alt key; "None" = no key; or a Unity KeyCode (LeftControl, Mouse3...).
        private static string mouseKeyName = "Alt";
        private static KeyCode mouseKey = KeyCode.None;

        private bool dragging;
        private Vector2 lastMouse;

        public TGPMode(MFDContext ctx) : base(ctx) { }

        public override string Label { get { return "TGP"; } }

        private static void Call(MethodInfo m, ModuleTargetingCamera tgp, params object[] args)
        {
            if (m == null || tgp == null) return;
            m.Invoke(tgp, args.Length == 0 ? null : args);
        }

        // ---------- Cameras ----------

        private List<ModuleTargetingCamera> GetTGPs()
        {
            return ctx.Modules<ModuleTargetingCamera>();
        }

        private static ModuleTargetingCamera GetTGP(List<ModuleTargetingCamera> tgps)
        {
            if (tgps.Count == 0) return null;
            ModuleTargetingCamera active = ModuleTargetingCamera.activeCam;
            if (active != null && tgps.Contains(active)) return active;
            for (int i = 0; i < tgps.Count; i++)
                if (tgps[i] != null && tgps[i].cameraEnabled) return tgps[i];
            return tgps[0];
        }

        private ModuleTargetingCamera CurrentTGP()
        {
            return GetTGP(GetTGPs());
        }

        private static bool IsLive(ModuleTargetingCamera tgp)
        {
            return tgp != null && tgp.cameraEnabled && TargetingCamera.Instance != null
                && TargetingCamera.ReadyForUse && TargetingCamera.Instance.targetCamRenderTexture != null
                && ModuleTargetingCamera.activeCam == tgp;
        }

        private static string ZoomLabel(ModuleTargetingCamera tgp)
        {
            try
            {
                float[] times = fZoomTimes != null ? fZoomTimes.GetValue(tgp) as float[] : null;
                if (times != null && tgp.currentFovIndex >= 0 && tgp.currentFovIndex < times.Length)
                    return times[tgp.currentFovIndex].ToString("0.0") + "X";
            }
            catch (Exception) { }
            return "Z" + (tgp.currentFovIndex + 1);
        }

        private void NextCamera()
        {
            List<ModuleTargetingCamera> tgps = GetTGPs();
            ModuleTargetingCamera tgp = GetTGP(tgps);
            if (tgp == null || tgps.Count < 2) return;
            int next = (tgps.IndexOf(tgp) + 1) % tgps.Count;
            tgps[next].EnableCamera(); // BDA's active camera becomes this TGP
        }

        // ---------- Continuous slew ----------

        private void ApplySlew()
        {
            if (heldSlew == Vector2.zero || Time.frameCount == lastSlewFrame) return;
            lastSlewFrame = Time.frameCount;
            ModuleTargetingCamera tgp = CurrentTGP();
            if (!IsLive(tgp)) return;
            try
            {
                Call(mSlew, tgp, new Vector3(heldSlew.x, heldSlew.y, 0f));
                if (!tgp.gimbalLimitReached) BDACockpitAudio.ServoActive();
            }
            catch (Exception e) { Debug.LogWarning("[BDArmoryCommandRPM] TGP slew: " + e.Message); heldSlew = Vector2.zero; }
        }

        public override void Update()
        {
            ApplySlew();
            UpdateMouse();
        }

        public override void KeyRelease()
        {
            heldSlew = Vector2.zero;
        }

        public override void OnDeactivate()
        {
            heldSlew = Vector2.zero;
            dragging = false;
            ctx.SetCameraLock(false);
        }

        // ---------- Mouse: drag to slew the camera, wheel to zoom ----------

        private static void ReadSettings()
        {
            if (settingsRead) return;
            settingsRead = true;
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("BDARMORYCOMMANDRPM_DISPLAY");
            if (nodes == null || nodes.Length == 0) return;
            nodes[0].TryGetValue("tgpMouseControl", ref mouseControl);
            nodes[0].TryGetValue("tgpMouseSensitivity", ref mouseSensitivity);
            nodes[0].TryGetValue("tgpInvertWheel", ref invertWheel);
            nodes[0].TryGetValue("tgpMouseKey", ref mouseKeyName);
            if (string.IsNullOrEmpty(mouseKeyName)) mouseKeyName = "Alt";
            mouseKeyName = mouseKeyName.Trim();
            if (!mouseKeyName.Equals("Alt", StringComparison.OrdinalIgnoreCase) && !mouseKeyName.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                try { mouseKey = (KeyCode)Enum.Parse(typeof(KeyCode), mouseKeyName, true); }
                catch (Exception)
                {
                    Debug.LogWarning("[BDArmoryCommandRPM] Invalid tgpMouseKey: " + mouseKeyName + " (using Alt)");
                    mouseKeyName = "Alt";
                }
            }
        }

        /// <summary>Is the key that enables mouse control of the camera held down?</summary>
        private static bool MouseKeyHeld()
        {
            if (mouseKeyName.Equals("None", StringComparison.OrdinalIgnoreCase)) return true;
            if (mouseKey != KeyCode.None) return Input.GetKey(mouseKey);
            return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr);
        }

        private void UpdateMouse()
        {
            ReadSettings();
            if (!mouseControl) return;

            ModuleTargetingCamera tgp = CurrentTGP();
            bool live = IsLive(tgp);
            // Only while the key (Alt) is held: without it the mouse and wheel belong to the cockpit view
            bool held = live && MouseKeyHeld();
            bool over = held && ctx.MouseOverScreen();
            if (!held) dragging = false;

            // while controlling the camera, the wheel zooms the camera instead of the view
            ctx.SetCameraLock(over || dragging);

            if (!held)
            {
                dragging = false;
                return;
            }

            Vector2 mouse = Input.mousePosition;
            if (Input.GetMouseButtonDown(0) && over)
            {
                dragging = true;
                lastMouse = mouse;
            }

            if (dragging)
            {
                if (!Input.GetMouseButton(0))
                {
                    dragging = false;
                }
                else
                {
                    Vector2 d = mouse - lastMouse;
                    lastMouse = mouse;
                    if (d.sqrMagnitude > 0.01f)
                    {
                        // BDArmory's window uses GUI coordinates (Y down)
                        Vector3 dir = new Vector3(d.x, -d.y, 0f) * mouseSensitivity;
                        try
                        {
                            if (mSlewMouse != null)
                            {
                                tgp.StartCoroutine((System.Collections.IEnumerator)mSlewMouse.Invoke(tgp, new object[] { dir }));
                            }
                            else
                            {
                                Call(mSlew, tgp, new Vector3(d.x, d.y, 0f).normalized);
                            }
                            if (!tgp.gimbalLimitReached) BDACockpitAudio.ServoActive();
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning("[BDArmoryCommandRPM] TGP mouse: " + e.Message);
                            dragging = false;
                        }
                    }
                }
            }

            if (over)
            {
                float wheel = Input.mouseScrollDelta.y;
                if (invertWheel) wheel = -wheel;
                if (wheel > 0.01f) { Call(mZoomIn, tgp); BDACockpitAudio.ZoomClick(); }
                else if (wheel < -0.01f) { Call(mZoomOut, tgp); BDACockpitAudio.ZoomClick(); }
            }
        }

        // ---------- Keys ----------

        public override void GetSoftkeys(string[] labels, bool[] active)
        {
            List<ModuleTargetingCamera> tgps = GetTGPs();
            ModuleTargetingCamera tgp = GetTGP(tgps);
            bool live = IsLive(tgp);
            labels[0] = tgp == null ? "NO TGP" : (live ? "PWR ON" : "PWR OFF"); active[0] = live;
            labels[1] = live && tgp.groundStabilized ? "UNLOCK" : "LOCK"; active[1] = live && tgp.groundStabilized;
            labels[2] = "NV"; active[2] = live && tgp.nvMode;
            labels[3] = "SEND GPS";
            labels[4] = "TO GPS";
            labels[5] = "COM TRK"; active[5] = live && tgp.CoMLock;
            labels[6] = "TURRETS"; active[6] = live && tgp.slaveTurrets;
        }

        private static void TogglePower(ModuleTargetingCamera tgp)
        {
            if (tgp == null) return;
            if (IsLive(tgp)) tgp.DisableCamera();
            else tgp.EnableCamera();
        }

        public override void Softkey(int slot)
        {
            ModuleTargetingCamera tgp = CurrentTGP();
            if (tgp == null) return;
            if (slot == 0)
            {
                TogglePower(tgp);
                return;
            }
            if (!IsLive(tgp)) return;
            switch (slot)
            {
                case 1:
                    if (tgp.groundStabilized) Call(mClear, tgp);
                    else tgp.GroundStabilize();
                    break;
                case 2: Call(mNV, tgp); break;
                case 3: Call(mSendGPS, tgp); break;
                case 4: Call(mToGPS, tgp); break;
                case 5:
                    tgp.CoMLock = !tgp.CoMLock;
                    if (!tgp.CoMLock) { tgp.lockedVessel = null; tgp.guidingOrdnance = false; }
                    break;
                case 6: Call(tgp.slaveTurrets ? mUnslave : mSlave, tgp); break;
            }
        }

        public override void ModeKeyAgain()
        {
            NextCamera();
        }

        public override void Key(MFDKey key)
        {
            ModuleTargetingCamera tgp = CurrentTGP();
            if (tgp == null) return;

            if (key == MFDKey.Home)
            {
                TogglePower(tgp);
                return;
            }
            if (!IsLive(tgp)) return;

            switch (key)
            {
                case MFDKey.Up: heldSlew = Vector2.up; break;
                case MFDKey.Down: heldSlew = Vector2.down; break;
                case MFDKey.Left: heldSlew = Vector2.left; break;
                case MFDKey.Right: heldSlew = Vector2.right; break;
                case MFDKey.X1: tgp.radarLock = !tgp.radarLock; break;
                case MFDKey.X2: Call(mReset, tgp); break;
                case MFDKey.X3: Call(mZoomOut, tgp); BDACockpitAudio.ZoomClick(); break;
                case MFDKey.X4: Call(mZoomIn, tgp); BDACockpitAudio.ZoomClick(); break;
            }
        }

        // ---------- Text ----------

        public override void Text(string[] lines, int w, int top, int bottom)
        {
            List<ModuleTargetingCamera> tgps = GetTGPs();
            ModuleTargetingCamera tgp = GetTGP(tgps);

            if (tgp == null)
            {
                lines[(top + bottom) / 2] = ctx.tWarning + MFDContext.Center("NO TARGETING CAMERA", w);
                return;
            }

            int camIdx = tgps.IndexOf(tgp) + 1;
            if (!IsLive(tgp))
            {
                lines[top] = ctx.tText + MFDContext.FitEsc("TGP " + camIdx + "/" + tgps.Count + "  OFF", w);
                lines[(top + bottom) / 2] = ctx.tWarning + MFDContext.Center("CAMERA OFF", w);
                lines[(top + bottom) / 2 + 1] = ctx.tText + MFDContext.Center("PRESS PWR (1st KEY) OR HOME", w);
                return;
            }

            string modes = (tgp.radarLock ? " RDR" : "") + (tgp.CoMLock ? " COM" : "");
            string left = "TGP " + camIdx + "/" + tgps.Count + "  " + ZoomLabel(tgp) + modes;
            string state = tgp.groundStabilized ? "LOCK" : "SLEW";
            lines[top] = ctx.tHighlight + MFDContext.FitEsc(left, w - state.Length) + (tgp.groundStabilized ? ctx.tLocked : ctx.tText) + state;

            if (tgp.gimbalLimitReached)
                lines[top + 1] = ctx.tWarning + MFDContext.Center("GIMBAL LIMIT", w);

            int y = bottom - 2;
            lines[y] = ctx.tText + MFDContext.Center(tgp.surfaceDetected ? "LR" : "NO LR", w);
            if (tgp.groundStabilized)
            {
                float range = Vector3.Distance(tgp.groundTargetPosition, tgp.transform.position);
                lines[y + 1] = ctx.tHighlight + MFDContext.Center("RANGE " + range.ToString("0") + "m", w);
                string geo;
                try { geo = BodyUtils.FormattedGeoPos(tgp.bodyRelativeGTP, false); }
                catch (Exception) { geo = string.Empty; }
                lines[y + 2] = ctx.tText + MFDContext.Center(MFDContext.Esc(geo), w);
            }
            else if (tgp.lockedVessel != null)
            {
                lines[y + 1] = ctx.tLocked + MFDContext.Center(MFDContext.Esc("TRK " + tgp.lockedVessel.vesselName), w);
            }
        }

        // ---------- Image ----------

        public override bool Render(RenderTexture screen, int top, int bottom)
        {
            ApplySlew();
            ModuleTargetingCamera tgp = CurrentTGP();
            if (!IsLive(tgp)) return false;

            // Real image from BDArmory's camera (the same RenderTexture its window uses)
            Graphics.Blit(TargetingCamera.Instance.targetCamRenderTexture, screen);

            ctx.BeginLines(screen, false);
            try
            {
                float lp = ctx.LinePixels(screen);
                Vector2 c = new Vector2(screen.width * 0.5f, (top + bottom + 1) * 0.5f * lp);
                DrawReticle(screen, tgp, c);
                if (showAttitude) DrawAttitude(screen, tgp, bottom, lp);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmoryCommandRPM] TGP render: " + e.Message);
            }
            finally
            {
                ctx.EndLines();
            }
            return true;
        }

        private void DrawReticle(RenderTexture screen, ModuleTargetingCamera tgp, Vector2 c)
        {
            float s = Mathf.Min(screen.width, screen.height) * reticleSize;
            Color col = tgp.groundStabilized ? ctx.cLocked : ctx.cReticle;
            float gap = s * 0.12f;
            ctx.Line(c + new Vector2(-s * 0.9f, 0), c + new Vector2(-gap, 0), ctx.cReticle);
            ctx.Line(c + new Vector2(gap, 0), c + new Vector2(s * 0.9f, 0), ctx.cReticle);
            ctx.Line(c + new Vector2(0, -s * 0.9f), c + new Vector2(0, -gap), ctx.cReticle);
            ctx.Line(c + new Vector2(0, gap), c + new Vector2(0, s * 0.9f), ctx.cReticle);
            ctx.Box(c, gap, col);
            if (tgp.groundStabilized) ctx.Box(c, gap * 0.5f, col);
            ctx.Brackets(c, s * 1.3f, ctx.cReticle);
        }

        private void DrawAttitude(RenderTexture screen, ModuleTargetingCamera tgp, int bottom, float lp)
        {
            Vessel v = ctx.OwnVessel;
            if (v == null || v.ReferenceTransform == null) return;
            float r = Mathf.Min(screen.width, screen.height) * 0.075f;
            Vector2 c = new Vector2(screen.width * 0.06f + r, (bottom - 2) * lp - r * 0.4f);
            Vector3 up = ((Vector3)v.upAxis).normalized;
            Transform rt = v.ReferenceTransform;

            for (int i = 0; i < 12; i++)
            {
                float a0 = Mathf.Deg2Rad * (i * 15f), a1 = Mathf.Deg2Rad * ((i + 1) * 15f);
                ctx.Line(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, ctx.cReticle);
            }

            Vector3 localUp = rt.InverseTransformDirection(up);
            float roll = Mathf.Atan2(localUp.x, -localUp.z);
            float pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(rt.up, up), -1f, 1f));
            Vector2 dir = new Vector2(Mathf.Cos(roll), -Mathf.Sin(roll));
            Vector2 off = new Vector2(Mathf.Sin(roll), Mathf.Cos(roll)) * (pitch / (Mathf.PI * 0.5f)) * r;
            ctx.Line(c + off - dir * r * 1.2f, c + off + dir * r * 1.2f, ctx.cReticle);

            ctx.Line(c + new Vector2(-r * 0.35f, 0), c + new Vector2(r * 0.35f, 0), ctx.cOwnship);
            ctx.Line(c, c + new Vector2(0, -r * 0.2f), ctx.cOwnship);

            Vector2 rel = MFDContext.RelativeHorizontal(v, tgp.targetPointPosition);
            if (rel.sqrMagnitude > 1f)
            {
                float brg = Mathf.Atan2(rel.x, rel.y);
                ctx.Triangle(c + new Vector2(Mathf.Sin(brg), -Mathf.Cos(brg)) * r * 1.15f, r * 0.18f, ctx.cHighlight);
            }
        }
    }
}
