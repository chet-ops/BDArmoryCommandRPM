// SMS DATA page (like the DATA page on the F/A-18 MFDs):
//  - guidance of the selected weapon and where it gets its target from (camera, GPS, radar, IR...)
//  - TGT<TGP: hands the camera's target to the weapon according to its guidance
//  - GPS point list (select, delete, point the camera at it)
//  - missile/bomb settings (the same ones BDArmory shows in the part menu) and weapon manager settings
//
// Settings are read from KSPFields visible in flight with a UI_FloatRange or UI_Toggle control,
// so only the ones each missile has are shown, and BDArmory's min/max limits are respected.
// A change is applied to every identical missile on the vessel.

using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.Localization;
using BDArmory.Control;
using BDArmory.Radar;
using BDArmory.Targeting;
using BDArmory.UI;
using BDArmory.Utils;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;

namespace BDArmoryCommandRPM
{
    public class SMSDataPage
    {
        private readonly MFDContext ctx;
        private int cursor;
        private string message;
        private float messageUntil;
        private bool messageBad;

        private class Setting
        {
            public BaseField field;
            public PartModule host;
            public bool allSameMissiles;
            public string label;
        }

        private readonly List<Setting> settings = new List<Setting>();

        public SMSDataPage(MFDContext ctx)
        {
            this.ctx = ctx;
        }

        // ---------- Selected weapon ----------

        public static MissileBase SelectedMissile(MissileFire wm)
        {
            if (wm == null) return null;
            IBDWeapon w = wm.selectedWeapon;
            if (w == null) return null;
            WeaponClasses c = w.GetWeaponClass();
            if (c != WeaponClasses.Missile && c != WeaponClasses.Bomb && c != WeaponClasses.SLW) return null;
            MissileBase m = wm.CurrentMissile;
            if (m != null && m.vessel == wm.vessel && m.GetShortName() == w.GetShortName()) return m;
            Part p = w.GetPart();
            return p != null ? p.FindModuleImplementing<MissileBase>() : null;
        }

        private ModuleTargetingCamera ActiveTGP()
        {
            Vessel v = ctx.OwnVessel;
            ModuleTargetingCamera active = ModuleTargetingCamera.activeCam;
            if (active != null && active.vessel == v && active.cameraEnabled) return active;
            List<ModuleTargetingCamera> list = ctx.Modules<ModuleTargetingCamera>();
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].cameraEnabled) return list[i];
            return null;
        }

        private void Say(string text, bool bad)
        {
            message = text;
            messageBad = bad;
            messageUntil = Time.time + 3f;
        }

        // ---------- Keys ----------

        public void GetSoftkeys(string[] labels, bool[] active)
        {
            MissileFire wm = ctx.GetWM();
            labels[0] = "TGT<TGP";
            labels[1] = "GPS >";
            labels[2] = "FIRE";
            labels[3] = "BACK";
            labels[4] = "RIPPLE"; active[4] = wm != null && wm.rippleFire;
            labels[5] = "GPS DEL";
            labels[6] = "CAM>GPS";
        }

        /// <summary>Returns true to go back to the weapon list.</summary>
        public bool Softkey(int slot)
        {
            MissileFire wm = ctx.GetWM();
            if (wm == null) return slot == 3;
            switch (slot)
            {
                case 0: TargetFromTGP(wm); break;
                case 1: NextGPS(wm); break;
                case 2: wm.AGFire(new KSPActionParam(KSPActionGroup.None, KSPActionType.Activate)); break;
                case 3: return true;
                case 4: wm.ToggleRippleFire(); break;
                case 5: DeleteGPS(wm); break;
                case 6: CameraToGPS(wm); break;
            }
            return false;
        }

        /// <summary>Returns true to go back to the weapon list.</summary>
        public bool Key(MFDKey key)
        {
            MissileFire wm = ctx.GetWM();
            switch (key)
            {
                case MFDKey.Up: cursor--; break;
                case MFDKey.Down: cursor++; break;
                case MFDKey.Left: Change(-1); break;
                case MFDKey.Right: Change(+1); break;
                case MFDKey.Home: return true;
                case MFDKey.X1: if (wm != null) wm.ToggleArm(); break;
                case MFDKey.X2: if (wm != null) TargetFromTGP(wm); break;
                case MFDKey.X3: if (wm != null) NextGPS(wm); break;
                case MFDKey.X4: if (wm != null) CameraToGPS(wm); break;
            }
            if (settings.Count > 0) cursor = Mathf.Clamp(cursor, 0, settings.Count - 1);
            else cursor = 0;
            return false;
        }

        // ---------- Target from the camera ----------

        private void TargetFromTGP(MissileFire wm)
        {
            MissileBase m = SelectedMissile(wm);
            ModuleTargetingCamera tgp = ActiveTGP();
            if (tgp == null) { Say("NO TGP - TURN ON CAMERA", true); return; }
            if (!tgp.groundStabilized) tgp.GroundStabilize();
            if (!tgp.groundStabilized) { Say("TGP: NO POINT TO LOCK", true); return; }

            MissileBase.TargetingModes mode = m != null ? m.TargetingMode : MissileBase.TargetingModes.None;
            switch (mode)
            {
                case MissileBase.TargetingModes.Laser:
                    // laser missiles follow the point locked by the active camera
                    Say("LASER SPOT FROM TGP", false);
                    break;

                case MissileBase.TargetingModes.Gps:
                case MissileBase.TargetingModes.None:
                    MakeGPSFromTGP(wm, tgp);
                    break;

                default:
                    // radar, IR, anti-radiation, inertial: radar-lock the vessel the camera is looking at
                    Vessel target = VesselAtTGP(tgp);
                    if (target == null) { Say("TGP: NO VESSEL TRACKED", true); break; }
                    VesselRadarData vrd = ctx.GetVRD(wm);
                    if (vrd != null && vrd.TryLockTarget(target))
                        Say("RDR LOCK " + target.vesselName, false);
                    else
                    {
                        // no radar: at least leave the position as a GPS point
                        MakeGPSFromTGP(wm, tgp);
                        Say("NO RDR LOCK - GPS SET", true);
                    }
                    break;
            }
        }

        private void MakeGPSFromTGP(MissileFire wm, ModuleTargetingCamera tgp)
        {
            Vessel v = ctx.OwnVessel;
            if (v == null) return;
            Vessel tracked = VesselAtTGP(tgp);
            Vector3d geo = VectorUtils.WorldPositionToGeoCoords(tgp.groundTargetPosition, v.mainBody);
            List<GPSTargetInfo> list = BDATargetManager.GPSTargetList(wm.Team);
            string name = tracked != null ? tracked.vesselName : "TGP " + (list.Count + 1);
            if (name.Length > 12) name = name.Substring(0, 12);
            GPSTargetInfo info = new GPSTargetInfo(geo, name);
            list.Add(info);
            BDATargetManager.Instance.SaveGPSTargets();
            wm.designatedGPSCoordsIndex = list.Count - 1;
            wm.designatedGPSInfo = info;
            Say("GPS SET: " + name, false);
        }

        /// <summary>Vessel tracked by the camera (CoM tracking) or the one closest to the locked point.</summary>
        private Vessel VesselAtTGP(ModuleTargetingCamera tgp)
        {
            if (tgp.CoMLock && tgp.lockedVessel != null) return tgp.lockedVessel;
            Vessel own = ctx.OwnVessel;
            Vessel best = null;
            float bestSq = 60f * 60f;
            List<Vessel> vessels = FlightGlobals.Vessels;
            for (int i = 0; i < vessels.Count; i++)
            {
                Vessel v = vessels[i];
                if (v == null || !v.loaded || v == own) continue;
                if (v.vesselType == VesselType.Debris || v.vesselType == VesselType.SpaceObject || v.vesselType == VesselType.Flag) continue;
                float sq = (v.CoM - tgp.groundTargetPosition).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = v; }
            }
            return best;
        }

        // ---------- GPS ----------

        private void NextGPS(MissileFire wm)
        {
            List<GPSTargetInfo> list = BDATargetManager.GPSTargetList(wm.Team);
            if (list.Count == 0) { Say("NO GPS POINTS", true); return; }
            wm.SelectNextGPSTarget();
            Say("GPS: " + wm.designatedGPSInfo.name, false);
        }

        private void DeleteGPS(MissileFire wm)
        {
            List<GPSTargetInfo> list = BDATargetManager.GPSTargetList(wm.Team);
            int i = wm.designatedGPSCoordsIndex;
            if (i < 0 || i >= list.Count) { Say("NO GPS SELECTED", true); return; }
            string name = list[i].name;
            list.RemoveAt(i);
            BDATargetManager.Instance.SaveGPSTargets();
            if (list.Count == 0)
            {
                wm.designatedGPSCoordsIndex = -1;
                wm.designatedGPSInfo = new GPSTargetInfo(Vector3d.zero, string.Empty);
            }
            else
            {
                wm.designatedGPSCoordsIndex = Mathf.Clamp(i, 0, list.Count - 1);
                wm.designatedGPSInfo = list[wm.designatedGPSCoordsIndex];
            }
            Say("GPS DELETED: " + name, false);
        }

        private static readonly System.Reflection.MethodInfo mToGPS = typeof(ModuleTargetingCamera).GetMethod("PointToGPSTarget",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        private void CameraToGPS(MissileFire wm)
        {
            if (wm.designatedGPSCoords == Vector3d.zero) { Say("NO GPS SELECTED", true); return; }
            ModuleTargetingCamera tgp = ActiveTGP();
            if (tgp == null || mToGPS == null) { Say("NO TGP - TURN ON CAMERA", true); return; }
            mToGPS.Invoke(tgp, null);
            Say("TGP -> " + wm.designatedGPSInfo.name, false);
        }

        // ---------- Settings ----------

        private void BuildSettings(MissileFire wm, MissileBase m)
        {
            settings.Clear();
            if (m != null) AddFields(m, true, null);
            // from the weapon manager, only what affects missiles (the rest is in its own menu)
            if (wm != null && m != null) AddFields(wm, false, wmFields);
        }

        private static readonly string[] wmFields = { "maxMissilesOnTarget" };

        private void AddFields(PartModule module, bool allSame, string[] only)
        {
            for (int i = 0; i < module.Fields.Count; i++)
            {
                BaseField f = module.Fields[i];
                if (f == null || !f.guiActive) continue;
                if (only != null && Array.IndexOf(only, f.name) < 0) continue;
                UI_Control ui = f.uiControlFlight;
                if (ui == null || !ui.controlEnabled || (ui.scene & UI_Scene.Flight) == 0) continue;
                if (!(ui is UI_FloatRange) && !(ui is UI_Toggle)) continue;
                string label = string.IsNullOrEmpty(f.guiName) ? f.name : Localizer.Format(f.guiName);
                label = label.Replace(":", string.Empty).Trim().ToUpperInvariant();
                settings.Add(new Setting { field = f, host = module, allSameMissiles = allSame, label = label });
            }
        }

        private void Change(int dir)
        {
            if (cursor < 0 || cursor >= settings.Count) return;
            Setting s = settings[cursor];
            try
            {
                object newValue = NextValue(s, dir);
                if (newValue == null) return;
                Apply(s.host, s.field.name, newValue);
                if (s.allSameMissiles)
                {
                    MissileBase m = s.host as MissileBase;
                    List<MissileBase> all = ctx.Modules<MissileBase>();
                    for (int i = 0; i < all.Count; i++)
                    {
                        MissileBase o = all[i];
                        if (o == null || o == m || o.HasFired || o.GetShortName() != m.GetShortName()) continue;
                        Apply(o, s.field.name, newValue);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmoryCommandRPM] DATA: could not change " + s.field.name + ": " + e.Message);
            }
        }

        private static object NextValue(Setting s, int dir)
        {
            object cur = s.field.GetValue(s.host);
            UI_Toggle toggle = s.field.uiControlFlight as UI_Toggle;
            if (toggle != null)
            {
                bool b = cur is bool && (bool)cur;
                return !b;
            }
            UI_FloatRange range = s.field.uiControlFlight as UI_FloatRange;
            if (range == null || cur == null) return null;
            float v = Convert.ToSingle(cur);
            float step = range.stepIncrement > 0f ? range.stepIncrement : (range.maxValue - range.minValue) / 20f;
            float nv = Mathf.Clamp(v + dir * step, range.minValue, range.maxValue);
            if (range.stepIncrement > 0f)
                nv = Mathf.Clamp(range.minValue + Mathf.Round((nv - range.minValue) / range.stepIncrement) * range.stepIncrement, range.minValue, range.maxValue);
            Type t = s.field.FieldInfo != null ? s.field.FieldInfo.FieldType : typeof(float);
            if (t == typeof(int)) return Mathf.RoundToInt(nv);
            if (t == typeof(double)) return (double)nv;
            return nv;
        }

        private static void Apply(PartModule module, string fieldName, object value)
        {
            BaseField f = module.Fields[fieldName];
            if (f == null) return;
            object old = f.GetValue(module);
            f.SetValue(value, module);
            UI_Control ui = f.uiControlFlight;
            if (ui != null && ui.onFieldChanged != null) ui.onFieldChanged(f, old);
        }

        private static string ValueText(Setting s)
        {
            object v = s.field.GetValue(s.host);
            UI_Toggle toggle = s.field.uiControlFlight as UI_Toggle;
            if (toggle != null)
            {
                bool b = v is bool && (bool)v;
                string txt = b ? toggle.displayEnabledText : toggle.displayDisabledText;
                if (string.IsNullOrEmpty(txt)) txt = b ? "ON" : "OFF";
                return txt.ToUpperInvariant();
            }
            if (v is float || v is double || v is int)
            {
                float f = Convert.ToSingle(v);
                if (!string.IsNullOrEmpty(s.field.guiFormat))
                {
                    try { return f.ToString(s.field.guiFormat); } catch (FormatException) { }
                }
                return Mathf.Abs(f) >= 100f ? f.ToString("0") : f.ToString("0.##");
            }
            return v != null ? v.ToString() : "-";
        }

        // ---------- Text ----------

        public void Text(string[] lines, int w, int top, int bottom)
        {
            Vessel own = ctx.OwnVessel;
            MissileFire wm = ctx.GetWM();
            if (own == null || wm == null)
            {
                lines[(top + bottom) / 2] = ctx.tWarning + MFDContext.Center("NO WEAPON MANAGER", w);
                return;
            }

            IBDWeapon wpn = wm.selectedWeapon;
            MissileBase m = SelectedMissile(wm);
            BuildSettings(wm, m);
            if (settings.Count > 0) cursor = Mathf.Clamp(cursor, 0, settings.Count - 1);

            int y = top;
            string title = "DATA  " + (wpn != null ? wpn.GetShortName() : "NO WEAPON");
            string count = m != null ? "x" + m.missilecount : string.Empty;
            lines[y++] = ctx.tHighlight + MFDContext.FitEsc(title, w - count.Length) + count;

            if (m == null)
            {
                lines[y++] = ctx.tText + MFDContext.FitEsc(wpn == null ? "SELECT A MISSILE OR BOMB" : "NOT A MISSILE OR BOMB", w);
            }
            else
            {
                string guid = "GUID " + GuidanceName(m.TargetingMode);
                if (m.TargetingModeTerminal != MissileBase.TargetingModes.None && m.TargetingModeTerminal != m.TargetingMode)
                    guid += " > " + GuidanceName(m.TargetingModeTerminal);
                lines[y++] = ctx.tText + MFDContext.FitEsc(guid + "  " + m.GuidanceMode.ToString().ToUpperInvariant(), w);

                bool ok;
                string tgt = TargetStatus(wm, m, own, out ok);
                lines[y++] = (ok ? ctx.tFriendly : ctx.tWarning) + MFDContext.FitEsc(tgt, w);
            }

            // Selected GPS point
            List<GPSTargetInfo> gps = BDATargetManager.GPSTargetList(wm.Team);
            string g = "GPS ";
            if (wm.designatedGPSCoords != Vector3d.zero)
            {
                Vector3 pos = VectorUtils.GetWorldSurfacePostion(wm.designatedGPSCoords, own.mainBody);
                int idx = wm.designatedGPSCoordsIndex;
                g += (idx >= 0 ? (idx + 1) + "/" + gps.Count + " " : string.Empty) + wm.designatedGPSInfo.name + "  " + MFDContext.Dist(Vector3.Distance(pos, own.CoM));
            }
            else g += gps.Count > 0 ? "--- (" + gps.Count + " SAVED)" : "--- (NONE)";
            lines[y++] = ctx.tText + MFDContext.FitEsc(g, w);

            string ripple = "RIPPLE " + (wm.rippleFire ? "ON" : "OFF");
            lines[y++] = ctx.tText + MFDContext.FitEsc(ripple, w);
            lines[y++] = ctx.tText + new string('-', w);

            // Settings
            int last = bottom - 1;
            int rows = Mathf.Max(1, last - y + 1);
            int first = Mathf.Clamp(cursor - rows / 2, 0, Mathf.Max(0, settings.Count - rows));
            if (settings.Count == 0)
                lines[y] = ctx.tText + MFDContext.FitEsc("NO SETTINGS", w);
            for (int r = 0; r < rows && first + r < settings.Count; r++)
            {
                int i = first + r;
                Setting s = settings[i];
                bool sel = i == cursor;
                string val = ValueText(s);
                if (sel) val = "<" + val + ">";
                string label = (sel ? "> " : "  ") + s.label;
                int lw = Mathf.Max(1, w - val.Length - 1);
                lines[y + r] = (sel ? ctx.tHighlight : ctx.tText) + MFDContext.FitEsc(MFDContext.Fit(label, lw) + " " + val, w);
            }

            // Bottom line: message or help
            if (!string.IsNullOrEmpty(message) && Time.time < messageUntil)
                lines[bottom] = (messageBad ? ctx.tWarning : ctx.tFriendly) + MFDContext.Center(message, w);
            else
                lines[bottom] = ctx.tText + MFDContext.Center("UP/DN SELECT  L/R CHANGE  HOME BACK", w);
        }

        private static string GuidanceName(MissileBase.TargetingModes mode)
        {
            switch (mode)
            {
                case MissileBase.TargetingModes.Radar: return "RADAR";
                case MissileBase.TargetingModes.Heat: return "IR";
                case MissileBase.TargetingModes.Laser: return "LASER";
                case MissileBase.TargetingModes.Gps: return "GPS";
                case MissileBase.TargetingModes.AntiRad: return "ANTI-RAD";
                case MissileBase.TargetingModes.Inertial: return "INERTIAL";
                default: return "NONE";
            }
        }

        private string TargetStatus(MissileFire wm, MissileBase m, Vessel own, out bool ok)
        {
            ok = false;
            switch (m.TargetingMode)
            {
                case MissileBase.TargetingModes.Laser:
                    {
                        ModuleTargetingCamera tgp = ActiveTGP();
                        if (tgp == null) return "TGT NO TGP";
                        if (!tgp.groundStabilized) return "TGT TGP NOT LOCKED";
                        ok = true;
                        return "TGT TGP SPOT  " + MFDContext.Dist(Vector3.Distance(tgp.groundTargetPosition, own.CoM));
                    }
                case MissileBase.TargetingModes.Gps:
                    if (wm.designatedGPSCoords == Vector3d.zero) return "TGT NO GPS POINT";
                    ok = true;
                    return "TGT GPS " + wm.designatedGPSInfo.name;
                case MissileBase.TargetingModes.Heat:
                    if (wm.heatTarget.exists)
                    {
                        ok = true;
                        Vessel hv = wm.heatTarget.vessel;
                        return "TGT IR LOCK " + (hv != null ? hv.vesselName + " " + MFDContext.Dist(Vector3.Distance(hv.CoM, own.CoM)) : string.Empty);
                    }
                    return "TGT IR SEEKING";
                case MissileBase.TargetingModes.Radar:
                case MissileBase.TargetingModes.Inertial:
                case MissileBase.TargetingModes.AntiRad:
                    {
                        VesselRadarData vrd = ctx.GetVRD(wm);
                        if (vrd != null && vrd.locked && vrd.lockedTargetData.vessel != null)
                        {
                            ok = true;
                            Vessel tv = vrd.lockedTargetData.vessel;
                            return "TGT RDR " + tv.vesselName + " " + MFDContext.Dist(Vector3.Distance(tv.CoM, own.CoM));
                        }
                        if (m.TargetingMode == MissileBase.TargetingModes.AntiRad) return "TGT ARM: WAITING EMITTER";
                        return "TGT NO RADAR LOCK";
                    }
                default:
                    ok = true;
                    return m.GetWeaponClass() == WeaponClasses.Bomb ? "TGT UNGUIDED BOMB (CCIP)" : "TGT UNGUIDED";
            }
        }
    }
}
