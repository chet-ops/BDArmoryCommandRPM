// SYS mode: switch every BDArmory system on/off from the panel
// (radar, TGP camera, RWR, IRST, ECM, cloak, AI pilot and GUARD) plus "ALL ON".

using System;
using System.Collections.Generic;
using UnityEngine;
using BDArmory.Control;
using BDArmory.CounterMeasure;
using BDArmory.Radar;
using BDArmory.Targeting;

namespace BDArmoryCommandRPM
{
    public class SYSMode : BDAMode
    {
        private int cursor;
        private static readonly string[] shortNames = { "RADAR", "TGP", "RWR", "IRST", "ECM", "CLOAK" };
        private static readonly string[] rows = { "RADAR", "TGP CAMERA", "RWR", "IRST", "ECM JAMMER", "CLOAK", "AI PILOT", "GUARD MODE" };

        public SYSMode(MFDContext ctx) : base(ctx) { }

        public override string Label { get { return "SYS"; } }

        // ---------- State of each system: total and switched on ----------

        private void Status(int row, MissileFire wm, out int total, out int on)
        {
            total = 0; on = 0;
            Vessel v = ctx.OwnVessel;
            if (v == null) return;
            switch (row)
            {
                case 0:
                    if (wm != null && wm.radars != null)
                        foreach (ModuleRadar r in wm.radars) if (r != null) { total++; if (r.radarEnabled) on++; }
                    break;
                case 1:
                    List<ModuleTargetingCamera> tgps = ctx.Modules<ModuleTargetingCamera>();
                    for (int i = 0; i < tgps.Count; i++)
                        if (tgps[i] != null) { total++; if (tgps[i].cameraEnabled) on++; }
                    break;
                case 2:
                    RadarWarningReceiver rwr = ctx.GetRWR(wm);
                    if (rwr != null) { total = 1; on = rwr.rwrEnabled ? 1 : 0; }
                    break;
                case 3:
                    if (wm != null && wm.irsts != null)
                        foreach (ModuleIRST s in wm.irsts) if (s != null) { total++; if (s.irstEnabled) on++; }
                    break;
                case 4:
                    if (wm != null && wm.jammers != null)
                        foreach (ModuleECMJammer j in wm.jammers) if (j != null && !j.isMissileECM) { total++; if (j.jammerEnabled) on++; }
                    break;
                case 5:
                    if (wm != null && wm.cloaks != null)
                        foreach (ModuleCloakingDevice c in wm.cloaks) if (c != null) { total++; if (c.cloakEnabled) on++; }
                    break;
                case 6:
                    IBDAIControl ai = wm != null ? wm.AI : null;
                    if (ai != null) { total = 1; on = ai.pilotEnabled ? 1 : 0; }
                    break;
                case 7:
                    if (wm != null) { total = 1; on = wm.guardMode ? 1 : 0; }
                    break;
            }
        }

        /// <summary>Switches a whole system on (on=true) or off.</summary>
        private void Set(int row, bool turnOn)
        {
            MissileFire wm = ctx.GetWM();
            Vessel v = ctx.OwnVessel;
            if (v == null) return;
            switch (row)
            {
                case 0:
                    if (wm != null && wm.radars != null)
                        foreach (ModuleRadar r in wm.radars)
                        {
                            if (r == null) continue;
                            if (turnOn && !r.radarEnabled) r.EnableRadar();
                            else if (!turnOn && r.radarEnabled) r.DisableRadar();
                        }
                    break;
                case 1:
                    List<ModuleTargetingCamera> cams = ctx.Modules<ModuleTargetingCamera>();
                    if (turnOn)
                    {
                        // switch on one camera (the active one if it is on this vessel); BDArmory only shows one at a time
                        ModuleTargetingCamera cam = ModuleTargetingCamera.activeCam;
                        if (cam == null || !cams.Contains(cam)) cam = cams.Count > 0 ? cams[0] : null;
                        if (cam != null && !cam.cameraEnabled) cam.EnableCamera();
                    }
                    else
                    {
                        for (int i = 0; i < cams.Count; i++) if (cams[i] != null && cams[i].cameraEnabled) cams[i].DisableCamera();
                    }
                    break;
                case 2:
                    RadarWarningReceiver rwr = ctx.GetRWR(wm);
                    if (rwr != null)
                    {
                        if (turnOn && !rwr.rwrEnabled) rwr.EnableRWR();
                        else if (!turnOn && rwr.rwrEnabled) rwr.DisableRWR();
                    }
                    break;
                case 3:
                    if (wm != null && wm.irsts != null)
                        foreach (ModuleIRST s in wm.irsts)
                        {
                            if (s == null) continue;
                            if (turnOn && !s.irstEnabled) s.EnableIRST();
                            else if (!turnOn && s.irstEnabled) s.DisableIRST();
                        }
                    break;
                case 4:
                    if (wm != null && wm.jammers != null)
                        foreach (ModuleECMJammer j in wm.jammers)
                        {
                            if (j == null || j.isMissileECM) continue;
                            if (turnOn && !j.jammerEnabled) j.EnableJammer();
                            else if (!turnOn && j.jammerEnabled) j.DisableJammer();
                        }
                    break;
                case 5:
                    if (wm != null && wm.cloaks != null)
                        foreach (ModuleCloakingDevice c in wm.cloaks)
                        {
                            if (c == null) continue;
                            if (turnOn && !c.cloakEnabled) c.EnableCloak();
                            else if (!turnOn && c.cloakEnabled) c.DisableCloak();
                        }
                    break;
                case 6:
                    IBDAIControl ai = wm != null ? wm.AI : null;
                    if (ai != null && ai.pilotEnabled != turnOn) ai.TogglePilot();
                    break;
                case 7:
                    if (wm != null && wm.guardMode != turnOn) wm.ToggleGuardMode();
                    break;
            }
        }

        private void Toggle(int row)
        {
            int total, on;
            Status(row, ctx.GetWM(), out total, out on);
            if (total == 0) return;
            Set(row, on == 0);
        }

        /// <summary>Sensors: radar, camera, RWR and IRST. If they are all on already, switches them off.</summary>
        private void AllSensors()
        {
            MissileFire wm = ctx.GetWM();
            bool allOn = true;
            for (int row = 0; row <= 3; row++)
            {
                int total, on;
                Status(row, wm, out total, out on);
                if (total > 0 && on == 0) allOn = false;
            }
            for (int row = 0; row <= 3; row++) Set(row, !allOn);
        }

        // ---------- Keys ----------

        public override void GetSoftkeys(string[] labels, bool[] active)
        {
            MissileFire wm = ctx.GetWM();
            for (int i = 0; i < 6; i++)
            {
                int total, on;
                Status(i, wm, out total, out on);
                labels[i] = total > 0 ? shortNames[i] : "-" + shortNames[i] + "-";
                active[i] = on > 0;
            }
            labels[6] = "ALL ON";
        }

        public override void Softkey(int slot)
        {
            if (slot >= 0 && slot < 6) Toggle(slot);
            else if (slot == 6) AllSensors();
        }

        public override void Key(MFDKey key)
        {
            switch (key)
            {
                case MFDKey.Up: cursor = (cursor + rows.Length - 1) % rows.Length; break;
                case MFDKey.Down: cursor = (cursor + 1) % rows.Length; break;
                case MFDKey.X1:
                case MFDKey.Right: Set(cursor, true); break;
                case MFDKey.X2:
                case MFDKey.Left: Set(cursor, false); break;
                case MFDKey.Home: AllSensors(); break;
            }
        }

        // ---------- Text ----------

        public override void Text(string[] lines, int w, int top, int bottom)
        {
            MissileFire wm = ctx.GetWM();
            if (ctx.OwnVessel == null || wm == null)
            {
                lines[(top + bottom) / 2] = ctx.tWarning + MFDContext.Center("NO WEAPON MANAGER", w);
                return;
            }

            lines[top] = ctx.tHighlight + MFDContext.FitEsc("SYSTEMS POWER", w);
            lines[top + 1] = ctx.tText + new string('-', w);

            int y = top + 2;
            for (int i = 0; i < rows.Length && y <= bottom - 3; i++, y++)
            {
                int total, on;
                Status(i, wm, out total, out on);
                string state = total == 0 ? "N/A" : (on == 0 ? "OFF" : (total > 1 ? "ON " + on + "/" + total : "ON"));
                string sel = i == cursor ? "> " : "  ";
                int nameW = Mathf.Max(1, w - 2 - 8);
                string text = sel + MFDContext.Fit(rows[i], nameW) + MFDContext.Fit(state, 8);
                string color = total == 0 ? ctx.tText : (on > 0 ? ctx.tHighlight : ctx.tWarning);
                if (i == cursor && total > 0) color = ctx.tHostile;
                lines[y] = color + MFDContext.FitEsc(text, w);
            }

            lines[bottom - 1] = ctx.tText + MFDContext.FitEsc("UP/DN SEL  > ON  < OFF  HOME ALL", w);
        }
    }
}
