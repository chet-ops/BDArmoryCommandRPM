// SMS mode: weapons, ARM/GUARD, fire, engagement modes, AI pilot and status.

using System;
using UnityEngine;
using BDArmory.Control;
using BDArmory.CounterMeasure;
using BDArmory.Weapons;

namespace BDArmoryCommandRPM
{
    public class SMSMode : BDAMode
    {
        // 0 = all, 1 = air, 2 = surface, 3 = missiles
        private int engageMode = 0;
        private static readonly string[] engageNames = { "ENG ALL", "ENG AIR", "ENG SRF", "ENG MSL" };

        private readonly SMSDataPage data;
        private bool dataPage;

        public SMSMode(MFDContext ctx) : base(ctx)
        {
            data = new SMSDataPage(ctx);
        }

        public override void ModeKeyAgain()
        {
            dataPage = !dataPage; // pressing SMS again: weapon list <-> DATA
        }

        public override string Label { get { return "SMS"; } }

        public override void GetSoftkeys(string[] labels, bool[] active)
        {
            if (dataPage) { data.GetSoftkeys(labels, active); return; }
            MissileFire wm = ctx.GetWM();
            labels[0] = "ARM"; active[0] = wm != null && wm.isArmed;
            labels[1] = "GUARD"; active[1] = wm != null && wm.guardMode;
            labels[2] = "FIRE";
            labels[3] = "DATA";
            IBDAIControl ai = wm != null ? wm.AI : null;
            labels[4] = ai != null ? "AI PILOT" : "NO AI"; active[4] = ai != null && ai.pilotEnabled;
            labels[5] = engageNames[engageMode];
            labels[6] = "WPN >";
        }

        public override void Softkey(int slot)
        {
            if (dataPage)
            {
                if (data.Softkey(slot)) dataPage = false;
                return;
            }
            MissileFire wm = ctx.GetWM();
            if (wm == null) return;
            switch (slot)
            {
                case 0: wm.ToggleArm(); break;
                case 1: wm.ToggleGuardMode(); break;
                case 2: wm.AGFire(new KSPActionParam(KSPActionGroup.None, KSPActionType.Activate)); break;
                case 3: dataPage = true; break;
                case 4:
                    IBDAIControl ai = wm.AI;
                    if (ai != null) ai.TogglePilot();
                    break;
                case 5: CycleEngageMode(wm); break;
                case 6: wm.CycleWeapon(true); break;
            }
        }

        public override void Key(MFDKey key)
        {
            if (dataPage)
            {
                if (data.Key(key)) dataPage = false;
                return;
            }
            MissileFire wm = ctx.GetWM();
            if (wm == null) return;
            switch (key)
            {
                case MFDKey.Up: wm.CycleWeapon(false); break;
                case MFDKey.Down: wm.CycleWeapon(true); break;
                case MFDKey.Home: wm.ToggleGuardMode(); break;
                case MFDKey.X1: wm.ToggleArm(); break;
                case MFDKey.X2: CycleEngageMode(wm); break;
                case MFDKey.X3: dataPage = true; break;
                case MFDKey.Left: ctx.DropCountermeasure(CMDropper.CountermeasureTypes.Flare); break;
                case MFDKey.Right: ctx.DropCountermeasure(CMDropper.CountermeasureTypes.Chaff); break;
            }
        }

        private void CycleEngageMode(MissileFire wm)
        {
            engageMode = (engageMode + 1) % 4;
            bool air = engageMode == 0 || engageMode == 1;
            bool srf = engageMode == 0 || engageMode == 2;
            bool msl = engageMode == 0 || engageMode == 3;
            bool slw = engageMode == 0 || engageMode == 2;
            if (wm.engageAir != air) wm.ToggleEngageAir();
            if (wm.engageSrf != srf) wm.ToggleEngageSrf();
            if (wm.engageMissile != msl) wm.ToggleEngageMissile();
            if (wm.engageSLW != slw) wm.ToggleEngageSLW();
        }

        public override void Text(string[] lines, int w, int top, int bottom)
        {
            if (dataPage) { data.Text(lines, w, top, bottom); return; }
            Vessel own = ctx.OwnVessel;
            MissileFire wm = ctx.GetWM();
            if (own == null || wm == null)
            {
                lines[(top + bottom) / 2] = ctx.tWarning + MFDContext.Center("NO WEAPON MANAGER", w);
                return;
            }

            string state = (wm.isArmed ? "ARMED" : "SAFE ") + "  " + (wm.guardMode ? "GUARD ON " : "GUARD OFF") + "  TEAM " + wm.teamString;
            lines[top] = (wm.isArmed ? ctx.tWarning : ctx.tText) + MFDContext.FitEsc(state, w);

            string engage = "ENG " + (wm.engageAir ? "AIR " : "--- ") + (wm.engageSrf ? "SRF " : "--- ")
                          + (wm.engageMissile ? "MSL " : "--- ") + (wm.engageSLW ? "SLW" : "---");
            string hp = wm.totalHP > 0 ? "HP " + Mathf.RoundToInt(100f * wm.currentHP / wm.totalHP) + "%" : string.Empty;
            lines[top + 1] = ctx.tText + MFDContext.FitEsc(engage, w - hp.Length) + MFDContext.Esc(hp);
            lines[top + 2] = ctx.tText + new string('-', w);

            // Bottom block: separator, target, countermeasures, warning
            int bottomBlock = 4;
            int listTop = top + 3;
            int listRows = Mathf.Max(1, bottom - bottomBlock - listTop + 1);
            IBDWeapon[] weapons = wm.weaponArray;
            int count = weapons != null ? weapons.Length : 0;
            int selected = Mathf.Clamp(wm.weaponIndex, 0, Mathf.Max(0, count - 1));
            int first = Mathf.Clamp(selected - listRows / 2, 0, Mathf.Max(0, count - listRows));

            for (int row = 0; row < listRows && first + row < count; row++)
            {
                int idx = first + row;
                IBDWeapon wpn = weapons[idx];
                string name = wpn != null ? wpn.GetShortName() : "NONE";
                string sub = MFDContext.WeaponInfo(wpn);
                string cls = wpn != null ? MFDContext.ClassTag(wpn.GetWeaponClass()) : "   ";
                int subWidth = Mathf.Min(sub.Length, w / 3);
                int nameWidth = Mathf.Max(1, w - 2 - 4 - subWidth - 1);
                string text = (idx == selected ? "> " : "  ") + cls + " " + MFDContext.Fit(name, nameWidth) + " " + MFDContext.Fit(sub, subWidth);
                lines[listTop + row] = (idx == selected ? ctx.tHighlight : ctx.tText) + MFDContext.FitEsc(text, w);
            }

            int y = bottom - bottomBlock + 1;
            lines[y++] = ctx.tText + new string('-', w);

            string tgt = "TGT ---";
            if (wm.currentTarget != null && wm.currentTarget.Vessel != null)
            {
                Vessel tv = wm.currentTarget.Vessel;
                tgt = "TGT " + tv.vesselName + " " + MFDContext.Dist(Vector3.Distance(tv.CoM, own.CoM));
            }
            lines[y++] = ctx.tHostile + MFDContext.FitEsc(tgt, w);

            string cm = "FLR " + MFDContext.ResourceAmount(own, "CMFlare") + "  CHF " + MFDContext.ResourceAmount(own, "CMChaff")
                      + "  SMK " + MFDContext.ResourceAmount(own, "CMSmoke");
            lines[y++] = ctx.tText + MFDContext.FitEsc(cm, w);

            if (wm.missileIsIncoming)
            {
                string warn = "MISSILE " + MFDContext.Dist(wm.incomingMissileDistance);
                if (wm.incomingMissileTime < 1000f) warn += "  T-" + wm.incomingMissileTime.ToString("0.0") + "s";
                lines[y] = (MFDContext.Blink() ? ctx.tWarning : ctx.tHighlight) + MFDContext.Center("!! " + warn + " !!", w);
            }
            else if (wm.underFire)
            {
                lines[y] = ctx.tWarning + MFDContext.Center("UNDER FIRE", w);
            }
        }
    }
}
