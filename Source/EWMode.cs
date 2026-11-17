// EW mode: RWR (threats by bearing), ECM jammers, cloaking and countermeasures.
// RWR pings are read via reflection to tolerate version changes.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using BDArmory.Control;
using BDArmory.CounterMeasure;
using BDArmory.Radar;

namespace BDArmoryCommandRPM
{
    public class EWMode : BDAMode
    {
        public float symbolSize = 12f;
        public int maxListed = 4;

        private readonly List<MFDContext.RwrPing> pings = new List<MFDContext.RwrPing>();

        public EWMode(MFDContext ctx) : base(ctx) { }

        public override string Label { get { return "EW/RWR"; } }

        // ---------- Data ----------

        private static bool IsMissileThreat(string t)
        {
            return t == "MissileLaunch" || t == "MissileLock" || t == "MWS" || t == "Torpedo" || t == "TorpedoLock";
        }

        private static string Short(string t)
        {
            switch (t)
            {
                case "SAM": return "SAM";
                case "Fighter": return "FTR";
                case "AWACS": return "AEW";
                case "MissileLaunch": return "LNCH";
                case "MissileLock": return "MLCK";
                case "Detection": return "DET";
                case "Sonar": return "SNR";
                case "Torpedo": return "TRP";
                case "TorpedoLock": return "TLCK";
                case "Jamming": return "JAM";
                case "MWS": return "MWS";
                default: return t ?? "?";
            }
        }

        private static int Priority(string t)
        {
            if (t == "MissileLaunch" || t == "MWS" || t == "Torpedo") return 0;
            if (t == "MissileLock" || t == "TorpedoLock") return 1;
            if (t == "SAM" || t == "Fighter") return 2;
            return 3;
        }

        private RadarWarningReceiver GetRWR(MissileFire wm)
        {
            return ctx.GetRWR(wm);
        }

        private void ReadPings(RadarWarningReceiver rwr)
        {
            MFDContext.ReadRwrPings(rwr, pings);
            pings.Sort((a, b) => Priority(a.type).CompareTo(Priority(b.type)));
        }

        private static void CountEW(MissileFire wm, out int jt, out int jo, out int ct, out int co)
        {
            jt = jo = ct = co = 0;
            if (wm == null) return;
            List<ModuleECMJammer> jammers = wm.jammers;
            if (jammers != null)
                for (int i = 0; i < jammers.Count; i++)
                    if (jammers[i] != null && !jammers[i].isMissileECM) { jt++; if (jammers[i].jammerEnabled) jo++; }
            List<ModuleCloakingDevice> cloaks = wm.cloaks;
            if (cloaks != null)
                for (int i = 0; i < cloaks.Count; i++)
                    if (cloaks[i] != null) { ct++; if (cloaks[i].cloakEnabled) co++; }
        }

        private static void ToggleJammers(MissileFire wm)
        {
            List<ModuleECMJammer> list = wm.jammers;
            if (list == null || list.Count == 0) return;
            bool anyOn = false;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && !list[i].isMissileECM && list[i].jammerEnabled) { anyOn = true; break; }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null || list[i].isMissileECM) continue;
                if (anyOn && list[i].jammerEnabled) list[i].DisableJammer();
                else if (!anyOn && !list[i].jammerEnabled) list[i].EnableJammer();
            }
        }

        private static void ToggleCloaks(MissileFire wm)
        {
            List<ModuleCloakingDevice> list = wm.cloaks;
            if (list == null || list.Count == 0) return;
            bool anyOn = false;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].cloakEnabled) { anyOn = true; break; }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                if (anyOn && list[i].cloakEnabled) list[i].DisableCloak();
                else if (!anyOn && !list[i].cloakEnabled) list[i].EnableCloak();
            }
        }

        // ---------- Keys ----------

        public override void GetSoftkeys(string[] labels, bool[] active)
        {
            MissileFire wm = ctx.GetWM();
            RadarWarningReceiver rwr = GetRWR(wm);
            int jt, jo, ct, co;
            CountEW(wm, out jt, out jo, out ct, out co);
            Vessel v = ctx.OwnVessel;

            labels[0] = "RWR"; active[0] = rwr != null && rwr.rwrEnabled;
            labels[1] = jt > 0 ? "ECM" : "NO ECM"; active[1] = jo > 0;
            labels[2] = ct > 0 ? "CLOAK" : "NO CLK"; active[2] = co > 0;
            labels[3] = "FLR " + MFDContext.ResourceAmount(v, "CMFlare");
            labels[4] = "CHF " + MFDContext.ResourceAmount(v, "CMChaff");
            labels[5] = "SMK " + MFDContext.ResourceAmount(v, "CMSmoke");
            labels[6] = string.Empty;
        }

        public override void Softkey(int slot)
        {
            MissileFire wm = ctx.GetWM();
            Vessel v = ctx.OwnVessel;
            switch (slot)
            {
                case 0:
                    RadarWarningReceiver rwr = GetRWR(wm);
                    if (rwr != null)
                    {
                        if (rwr.rwrEnabled) rwr.DisableRWR();
                        else rwr.EnableRWR();
                    }
                    break;
                case 1: if (wm != null) ToggleJammers(wm); break;
                case 2: if (wm != null) ToggleCloaks(wm); break;
                case 3: ctx.DropCountermeasure(CMDropper.CountermeasureTypes.Flare); break;
                case 4: ctx.DropCountermeasure(CMDropper.CountermeasureTypes.Chaff); break;
                case 5: ctx.DropCountermeasure(CMDropper.CountermeasureTypes.Smoke); break;
            }
        }

        public override void Key(MFDKey key)
        {
            switch (key)
            {
                case MFDKey.Home: Softkey(0); break;
                case MFDKey.Up: Softkey(1); break;
                case MFDKey.Down: Softkey(2); break;
                case MFDKey.Left: Softkey(3); break;
                case MFDKey.Right: Softkey(4); break;
                case MFDKey.X1: Softkey(5); break;
            }
        }

        // ---------- Text ----------

        public override void Text(string[] lines, int w, int top, int bottom)
        {
            Vessel own = ctx.OwnVessel;
            MissileFire wm = ctx.GetWM();
            RadarWarningReceiver rwr = GetRWR(wm);

            if (own == null || rwr == null)
            {
                pings.Clear();
                lines[top] = ctx.tWarning + MFDContext.FitEsc("NO RWR", w);
            }
            else
            {
                try { ReadPings(rwr); }
                catch (Exception e) { Debug.LogWarning("[BDArmoryCommandRPM] RWR read: " + e.Message); pings.Clear(); }
                lines[top] = (rwr.rwrEnabled ? ctx.tText : ctx.tWarning)
                    + MFDContext.FitEsc("RWR " + (rwr.rwrEnabled ? "ON " : "OFF") + "   THREATS " + pings.Count, w);
            }

            int jt, jo, ct, co;
            CountEW(wm, out jt, out jo, out ct, out co);
            string ew = "ECM " + (jt == 0 ? "--" : (jo > 0 ? "ON" : "OFF")) + "  CLK " + (ct == 0 ? "--" : (co > 0 ? "ON" : "OFF"));
            lines[top + 1] = ctx.tText + MFDContext.FitEsc(ew, w);

            bool missile = wm != null && wm.missileIsIncoming;
            for (int i = 0; i < pings.Count && !missile; i++) if (IsMissileThreat(pings[i].type)) missile = true;
            if (missile && MFDContext.Blink())
                lines[top + 2] = ctx.tWarning + MFDContext.Center("!! MISSILE !!", w);

            if (own == null) return;
            int listed = Mathf.Min(pings.Count, Mathf.Max(0, maxListed));
            int start = bottom - listed + 1;
            for (int i = 0; i < listed; i++)
            {
                MFDContext.RwrPing p = pings[i];
                Vector2 rel = MFDContext.RelativeHorizontal(own, p.position);
                string row = MFDContext.Fit(Short(p.type), 5) + MFDContext.Bearing(MFDContext.RelativeBearing(rel)) + " "
                           + MFDContext.Fit(MFDContext.Dist(rel.magnitude), 7) + (p.source != null ? p.source.vesselName : string.Empty);
                lines[start + i] = (IsMissileThreat(p.type) ? ctx.tWarning : ctx.tHostile) + MFDContext.FitEsc(row, w);
            }
        }

        // ---------- Drawing ----------

        public override bool Render(RenderTexture screen, int top, int bottom)
        {
            Vessel own = ctx.OwnVessel;
            if (own == null) return false;

            float lp = ctx.LinePixels(screen);
            float y0 = (top + 3) * lp, y1 = (bottom - maxListed + 1) * lp;
            Vector2 center = new Vector2(screen.width * 0.5f, (y0 + y1) * 0.5f);
            float radius = Mathf.Max(20f, Mathf.Min(screen.width * 0.5f - 10f, (y1 - y0) * 0.5f - 6f));
            bool blink = MFDContext.Blink(0.6f);

            ctx.BeginLines(screen, true);
            try
            {
                ctx.ScopeGrid(own, center, radius, 2, false);
                ctx.Ownship(center, symbolSize, ctx.cOwnship);
                for (int i = 0; i < pings.Count; i++)
                {
                    MFDContext.RwrPing p = pings[i];
                    Vector2 rel = MFDContext.RelativeHorizontal(own, p.position);
                    if (rel.sqrMagnitude < 1f) continue;
                    Vector2 dir = rel.normalized;
                    bool msl = IsMissileThreat(p.type);
                    Vector2 pos = center + new Vector2(dir.x, -dir.y) * (msl ? radius * 0.5f : radius * 0.85f);
                    Color col = msl ? (blink ? ctx.cWarning : ctx.cHighlight) : ctx.cHostile;
                    DrawSymbol(p.type, pos, col);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmoryCommandRPM] RWR render: " + e.Message);
            }
            finally
            {
                ctx.EndLines();
            }
            return true;
        }

        private void DrawSymbol(string type, Vector2 p, Color col)
        {
            float s = symbolSize;
            switch (type)
            {
                case "SAM": ctx.Box(p, s * 0.8f, col); break;
                case "Fighter": ctx.Triangle(p, s, col); break;
                case "AWACS": ctx.Diamond(p, s, col); ctx.Line(p + new Vector2(-s, 0), p + new Vector2(s, 0), col); break;
                case "MissileLaunch":
                case "MWS":
                case "Torpedo": ctx.Cross(p, s, col); ctx.Circle(p, s * 1.2f, col, 12); break;
                case "MissileLock":
                case "TorpedoLock": ctx.Diamond(p, s, col); ctx.Diamond(p, s * 0.5f, col); break;
                case "Jamming":
                    ctx.Line(p + new Vector2(-s, 0), p + new Vector2(-s * 0.3f, -s * 0.6f), col);
                    ctx.Line(p + new Vector2(-s * 0.3f, -s * 0.6f), p + new Vector2(s * 0.3f, s * 0.6f), col);
                    ctx.Line(p + new Vector2(s * 0.3f, s * 0.6f), p + new Vector2(s, 0), col);
                    break;
                default: ctx.Circle(p, s * 0.6f, col, 10); break;
            }
        }
    }
}
