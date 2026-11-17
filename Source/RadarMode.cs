// RADAR mode: nose-up PPI with BDArmory radar/sonar and IRST contacts.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using BDArmory.Control;
using BDArmory.Radar;

namespace BDArmoryCommandRPM
{
    public class RadarMode : BDAMode
    {
        public float[] ranges = { 2500, 5000, 10000, 20000, 40000, 80000, 160000 };
        public int rangeIndex = 3;
        public float contactSize = 9f;
        public float velocityVectorSeconds = 8f;
        public int scopeRings = 4;

        private static readonly FieldInfo fDisplayedTargets =
            typeof(VesselRadarData).GetField("displayedTargets", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo fDisplayedIRTargets =
            typeof(VesselRadarData).GetField("displayedIRTargets", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly List<RadarDisplayData> emptyRadar = new List<RadarDisplayData>();
        private static readonly List<IRSTDisplayData> emptyIR = new List<IRSTDisplayData>();

        // Sweep trail (screen angles of the last frames, per radar)
        public int sweepTrail = 14;
        private readonly Dictionary<ModuleRadar, List<float>> sweepHistory = new Dictionary<ModuleRadar, List<float>>();

        public RadarMode(MFDContext ctx) : base(ctx) { }

        public override string Label { get { return "RADAR"; } }

        public override void OnDeactivate()
        {
            sweepHistory.Clear(); // don't keep radars from previous vessels
        }

        private float CurrentRange
        {
            get { return ranges[Mathf.Clamp(rangeIndex, 0, ranges.Length - 1)]; }
        }

        // ---------- Data ----------

        private static List<RadarDisplayData> RadarContacts(VesselRadarData vrd)
        {
            if (vrd == null || fDisplayedTargets == null) return emptyRadar;
            try { return fDisplayedTargets.GetValue(vrd) as List<RadarDisplayData> ?? emptyRadar; }
            catch (Exception) { return emptyRadar; }
        }

        private static List<IRSTDisplayData> IRContacts(VesselRadarData vrd)
        {
            if (vrd == null || fDisplayedIRTargets == null) return emptyIR;
            try { return fDisplayedIRTargets.GetValue(vrd) as List<IRSTDisplayData> ?? emptyIR; }
            catch (Exception) { return emptyIR; }
        }

        private static bool TryGetActiveLock(VesselRadarData vrd, out RadarDisplayData data)
        {
            data = default(RadarDisplayData);
            if (vrd == null || !vrd.locked || vrd.numLockedTargets == 0) return false;
            try
            {
                data = vrd.lockedTargetData;
                return true;
            }
            catch (Exception) { return false; }
        }

        private static void RadarStatus(MissileFire wm, out int total, out int enabled, out bool sonar)
        {
            total = 0; enabled = 0; sonar = false;
            if (wm == null) return;
            List<ModuleRadar> radars = wm.radars;
            if (radars == null) return;
            for (int i = 0; i < radars.Count; i++)
            {
                if (radars[i] == null) continue;
                total++;
                if (radars[i].radarEnabled)
                {
                    enabled++;
                    if (radars[i].sonarMode != ModuleRadar.SonarModes.None) sonar = true;
                }
            }
        }

        // ---------- Keys ----------

        public override void GetSoftkeys(string[] labels, bool[] active)
        {
            MissileFire wm = ctx.GetWM();
            VesselRadarData vrd = ctx.GetVRD(wm);
            int total, enabled; bool sonar;
            RadarStatus(wm, out total, out enabled, out sonar);

            labels[0] = sonar ? "SONAR" : "RDR PWR"; active[0] = enabled > 0;
            labels[1] = "TGT <";
            labels[2] = "TGT >";
            labels[3] = "UNLOCK";
            labels[4] = "UNLK ALL";
            labels[5] = "SLAVE"; active[5] = vrd != null && vrd.slaveTurrets;
            labels[6] = "RNG " + MFDContext.Dist(CurrentRange);
        }

        public override void Softkey(int slot)
        {
            MissileFire wm = ctx.GetWM();
            if (wm == null) return;
            VesselRadarData vrd = ctx.GetVRD(wm);
            switch (slot)
            {
                case 0: ToggleRadars(wm); break;
                case 1: if (vrd != null) vrd.TargetPrev(); break;
                case 2: if (vrd != null) vrd.TargetNext(); break;
                case 3: if (vrd != null && vrd.locked) { BDACockpitAudio.NotifyUserUnlock(); vrd.UnlockCurrentTarget(); } break;
                case 4: if (vrd != null) { BDACockpitAudio.NotifyUserUnlock(); vrd.UnlockAllTargets(); } break;
                case 5:
                    if (vrd != null)
                    {
                        if (vrd.slaveTurrets) vrd.UnslaveTurrets();
                        else vrd.SlaveTurrets();
                    }
                    break;
                case 6: rangeIndex = (rangeIndex + 1) % ranges.Length; break;
            }
        }

        public override void Key(MFDKey key)
        {
            switch (key)
            {
                case MFDKey.Up: rangeIndex = Mathf.Min(rangeIndex + 1, ranges.Length - 1); return;
                case MFDKey.Down: rangeIndex = Mathf.Max(rangeIndex - 1, 0); return;
            }
            MissileFire wm = ctx.GetWM();
            if (wm == null) return;
            switch (key)
            {
                case MFDKey.Left: Softkey(1); break;
                case MFDKey.Right: Softkey(2); break;
                case MFDKey.Home: Softkey(0); break;
                case MFDKey.X1: Softkey(5); break;
                case MFDKey.X2: Softkey(3); break;
                case MFDKey.X3: wm.CycleWeapon(false); break;
                case MFDKey.X4: wm.CycleWeapon(true); break;
            }
        }

        private static void ToggleRadars(MissileFire wm)
        {
            List<ModuleRadar> radars = wm.radars;
            if (radars == null || radars.Count == 0) return;
            bool anyOn = false;
            for (int i = 0; i < radars.Count; i++)
                if (radars[i] != null && radars[i].radarEnabled) { anyOn = true; break; }
            for (int i = 0; i < radars.Count; i++)
            {
                ModuleRadar r = radars[i];
                if (r == null) continue;
                if (anyOn && r.radarEnabled) r.DisableRadar();
                else if (!anyOn && !r.radarEnabled) r.EnableRadar();
            }
        }

        // ---------- Text ----------

        public override void Text(string[] lines, int w, int top, int bottom)
        {
            Vessel own = ctx.OwnVessel;
            MissileFire wm = ctx.GetWM();
            if (own == null || wm == null)
            {
                lines[(top + bottom) / 2] = ctx.tWarning + MFDContext.Center("NO WEAPON MANAGER", w);
                return;
            }

            VesselRadarData vrd = ctx.GetVRD(wm);
            int total, enabled; bool sonar;
            RadarStatus(wm, out total, out enabled, out sonar);
            int locks = vrd != null ? vrd.numLockedTargets : 0;

            string status = total == 0 ? "NO RDR" : (enabled > 0 ? "ON " + enabled + "/" + total : "OFF " + total);
            string header = (sonar ? "SNR " : "RDR ") + status + "  RNG " + MFDContext.Dist(CurrentRange) + "  LCK " + locks;
            lines[top] = (enabled > 0 ? ctx.tText : ctx.tWarning) + MFDContext.FitEsc(header, w);

            string flags = (wm.guardMode ? "GRD " : "    ") + (wm.isArmed ? "ARM" : "SAFE");
            string weapon = wm.weaponIndex > 0 && wm.weaponArray != null && wm.weaponIndex < wm.weaponArray.Length && wm.weaponArray[wm.weaponIndex] != null
                ? wm.weaponArray[wm.weaponIndex].GetShortName() : "----";
            int wpnWidth = Mathf.Max(0, w - flags.Length - 1);
            lines[top + 1] = ctx.tText + MFDContext.FitEsc(weapon, wpnWidth) + " " + (wm.isArmed ? ctx.tWarning : ctx.tText) + MFDContext.Esc(flags);

            if (wm.missileIsIncoming && MFDContext.Blink())
                lines[top + 2] = ctx.tWarning + MFDContext.Center("!! MISSILE " + MFDContext.Dist(wm.incomingMissileDistance) + " !!", w);

            RadarDisplayData lockData;
            int info = bottom - 2;
            if (TryGetActiveLock(vrd, out lockData))
            {
                Vector3 pos = lockData.targetData.position;
                Vector2 rel = MFDContext.RelativeHorizontal(own, pos);
                float range = Vector3.Distance(pos, own.CoM);
                Vessel tv = lockData.vessel;
                lines[info] = ctx.tLocked + MFDContext.FitEsc("TGT " + (tv != null ? tv.vesselName : "UNKNOWN"), w);
                string l2 = "R " + MFDContext.Dist(range) + "  BRG " + MFDContext.Bearing(MFDContext.RelativeBearing(rel));
                if (tv != null) l2 += "  ALT " + MFDContext.Dist(tv.altitude);
                lines[info + 1] = ctx.tHighlight + MFDContext.FitEsc(l2, w);
                if (tv != null)
                {
                    Vector3 los = (pos - own.CoM).normalized;
                    Vector3 relVel = (Vector3)(tv.srf_velocity - own.srf_velocity);
                    float closure = -Vector3.Dot(relVel, los);
                    lines[info + 2] = ctx.tHighlight + MFDContext.FitEsc("GS " + tv.srfSpeed.ToString("0") + "m/s  VC " + closure.ToString("0") + "m/s", w);
                }
            }
            else
            {
                int contacts = RadarContacts(vrd).Count + IRContacts(vrd).Count;
                lines[info + 1] = ctx.tText + MFDContext.FitEsc("CONTACTS " + contacts + "   NO LOCK", w);
            }
        }

        // ---------- Drawing ----------

        public override bool Render(RenderTexture screen, int top, int bottom)
        {
            Vessel own = ctx.OwnVessel;
            if (own == null) return false;
            MissileFire wm = ctx.GetWM();
            VesselRadarData vrd = ctx.GetVRD(wm);
            float range = CurrentRange;

            float lp = ctx.LinePixels(screen);
            float y0 = (top + 3) * lp, y1 = (bottom - 2) * lp;
            Vector2 center = new Vector2(screen.width * 0.5f, (y0 + y1) * 0.5f);
            float radius = Mathf.Max(20f, Mathf.Min(screen.width * 0.5f - 10f, (y1 - y0) * 0.5f - 22f));

            ctx.BeginLines(screen, true);
            try
            {
                ctx.ScopeGrid(own, center, radius, Mathf.Max(1, scopeRings), true);
                ctx.Ownship(center, contactSize * 1.2f, ctx.cOwnship);
                if (wm != null)
                {
                    DrawFov(own, wm, center, radius);
                    DrawSweep(own, wm, center, radius);
                    DrawRadarContacts(own, wm, vrd, center, radius, range);
                    DrawIRContacts(own, vrd, center, radius, range);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmoryCommandRPM] Radar render: " + e.Message);
            }
            finally
            {
                ctx.EndLines();
            }
            return true;
        }

        /// <summary>Screen angle (0 = nose, positive = right) of a world direction.</summary>
        private float ScreenAngle(Vessel own, Vector3 worldDir)
        {
            Vector2 d = MFDContext.RelativeHorizontalDir(own, worldDir);
            if (d.sqrMagnitude < 1e-8f) return float.NaN;
            return Mathf.Atan2(d.x, d.y);
        }

        private static Vector2 AngleDir(float a)
        {
            return new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
        }

        /// <summary>Scan wedge of directional radars, using their real limits.</summary>
        private void DrawFov(Vessel own, MissileFire wm, Vector2 center, float radius)
        {
            List<ModuleRadar> radars = wm.radars;
            if (radars == null) return;
            Color c = ctx.cGrid;
            for (int i = 0; i < radars.Count; i++)
            {
                ModuleRadar r = radars[i];
                if (r == null || !r.radarEnabled || r.omnidirectional || r.radarAzLimits == null || r.radarAzLimits.Length < 2) continue;
                if (r.currForward.sqrMagnitude < 1e-6f || r.currUp.sqrMagnitude < 1e-6f) continue;
                float a0 = ScreenAngle(own, Quaternion.AngleAxis(r.radarAzLimits[0], r.currUp) * r.currForward);
                float a1 = ScreenAngle(own, Quaternion.AngleAxis(r.radarAzLimits[1], r.currUp) * r.currForward);
                if (float.IsNaN(a0) || float.IsNaN(a1)) continue;
                ctx.Line(center, center + AngleDir(a0) * radius, c);
                ctx.Line(center, center + AngleDir(a1) * radius, c);
            }
        }

        /// <summary>Sweep line of every active radar, with a fading trail.</summary>
        private void DrawSweep(Vessel own, MissileFire wm, Vector2 center, float radius)
        {
            List<ModuleRadar> radars = wm.radars;
            if (radars == null) return;
            for (int i = 0; i < radars.Count; i++)
            {
                ModuleRadar r = radars[i];
                if (r == null) continue;
                List<float> hist;
                if (!sweepHistory.TryGetValue(r, out hist))
                {
                    hist = new List<float>();
                    sweepHistory[r] = hist;
                }
                if (!r.radarEnabled || !r.canScan || r.currForward.sqrMagnitude < 1e-6f || r.currUp.sqrMagnitude < 1e-6f)
                {
                    hist.Clear();
                    continue;
                }

                float a = ScreenAngle(own, Quaternion.AngleAxis(r.currentAngle, r.currUp) * r.currForward);
                if (float.IsNaN(a)) continue;
                hist.Add(a);
                if (hist.Count > Mathf.Max(1, sweepTrail)) hist.RemoveRange(0, hist.Count - Mathf.Max(1, sweepTrail));

                // trail: lines getting fainter further back
                Color bright = ctx.cOwnship;
                for (int k = 0; k < hist.Count - 1; k++)
                {
                    float t = (float)(k + 1) / hist.Count;
                    Color c = new Color(bright.r, bright.g, bright.b, bright.a * t * 0.45f);
                    ctx.Line(center, center + AngleDir(hist[k]) * radius, c);
                }
                ctx.Line(center, center + AngleDir(a) * radius, bright);
            }
        }

        private void DrawRadarContacts(Vessel own, MissileFire wm, VesselRadarData vrd, Vector2 center, float radius, float range)
        {
            List<RadarDisplayData> contacts = RadarContacts(vrd);
            RadarDisplayData active;
            bool hasActive = TryGetActiveLock(vrd, out active);

            for (int i = 0; i < contacts.Count; i++)
            {
                RadarDisplayData d = contacts[i];
                if (!d.targetData.exists) continue;

                bool clipped;
                Vector2 p = MFDContext.ToScope(MFDContext.RelativeHorizontal(own, d.targetData.position), center, radius, range, out clipped);
                bool friendly = d.targetData.Team != null && wm.Team != null && wm.Team.IsFriendly(d.targetData.Team);
                bool isMissile = d.targetData.targetInfo != null && d.targetData.targetInfo.isMissile;
                Color col = d.locked ? ctx.cLocked : (friendly ? ctx.cFriendly : ctx.cHostile);

                if (clipped)
                {
                    ctx.Box(p, contactSize * 0.35f, col);
                    continue;
                }

                if (isMissile) ctx.Diamond(p, contactSize * 0.8f, col);
                else if (friendly) ctx.Circle(p, contactSize * 0.8f, col, 12);
                else ctx.Box(p, contactSize * 0.7f, col);

                Vector2 v = MFDContext.RelativeHorizontalDir(own, d.targetData.velocity) * velocityVectorSeconds / range * radius;
                if (v.sqrMagnitude > 4f)
                {
                    if (v.magnitude > radius * 0.3f) v = v.normalized * radius * 0.3f;
                    ctx.Line(p, p + new Vector2(v.x, -v.y), col);
                }

                if (d.locked)
                {
                    bool isActive = hasActive && active.vessel != null && active.vessel == d.vessel;
                    ctx.Brackets(p, contactSize * (isActive ? 2.0f : 1.5f), isActive ? ctx.cHighlight : ctx.cLocked);
                }
            }
        }

        private void DrawIRContacts(Vessel own, VesselRadarData vrd, Vector2 center, float radius, float range)
        {
            List<IRSTDisplayData> contacts = IRContacts(vrd);
            for (int i = 0; i < contacts.Count; i++)
            {
                IRSTDisplayData d = contacts[i];
                if (!d.targetData.exists) continue;
                bool clipped;
                Vector2 p = MFDContext.ToScope(MFDContext.RelativeHorizontal(own, d.targetData.position), center, radius, range, out clipped);
                if (clipped) continue;
                ctx.Circle(p, contactSize * 0.6f, ctx.cIR, 10);
                ctx.Line(p + new Vector2(-contactSize, 0), p + new Vector2(contactSize, 0), ctx.cIR);
            }
        }
    }
}
