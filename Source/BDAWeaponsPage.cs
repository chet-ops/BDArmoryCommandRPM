// BDArmoryCommandRPM - WEAPONS interface
//
// A single RasterPropMonitor page that turns the MFD into a BDArmory panel.
// While it is active:
//   * The page is locked (lockingPage): the MFD's page buttons no longer switch to
//     other pages.
//   * The physical buttons of one row select the MODE (RADAR, EW/RWR, SMS, TGP, SYS) and
//     the buttons of the other row are the mode's FUNCTION KEYS, labelled on screen.
//   * The side keys (globalButtons) have fixed functions per mode.
//   * EXIT goes back to the original menu (the page named in exitPage, marked as unlockerPage).

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace BDArmoryCommandRPM
{
    public class BDAWeaponsPage : InternalModule
    {
        // ---------- Physical buttons ----------
        // Mode row: button transform names, from left to right.
        [KSPField] public string modeButtons = "button_A,button_B,button_C,button_D,button_E,button_F,button_G";
        // What each position holds: RADAR, EW, SMS, TGP, SYS, EXIT, NEXT, "A/B" or empty.
        [KSPField] public string modeSlots = "RADAR,EW,SMS,TGP,,,EXIT";
        // Width (in half characters) of each label, so it sits over its button.
        [KSPField] public string modeWidths = "11,12,9,10,10,10,11";
        [KSPField] public bool modeRowTop = true;
        // Spaces (half characters) before the first label of each row
        [KSPField] public int modeRowPad = 0;
        [KSPField] public int functionRowPad = 0;

        // Function key row.
        [KSPField] public string functionButtons = "buttonR1,buttonR2,buttonR3,buttonR4,buttonR5,buttonR6,buttonR7";
        [KSPField] public string functionWidths = "13,10,9,10,9,10,11";
        [KSPField] public bool functionRowTop = false;
        // Position in the function row used as EXIT (-1 = none).
        [KSPField] public int exitFunctionSlot = -1;

        // globalButtons indices for: Up,Down,Left,Right,Home,X1,X2,X3,X4
        [KSPField] public string keyMap = "0,1,6,5,4,2,3,7,8";

        // Page EXIT returns to (must have unlockerPage).
        [KSPField] public string exitPage = "pALCORSTBY40x20";
        [KSPField] public string defaultMode = "RADAR";
        // Writes to KSP.log where each button is relative to the screen (to align labels)
        [KSPField] public bool logLayout = false;

        // ---------- Colours ----------
        [KSPField] public string backgroundColor = "0,0,0,255";
        [KSPField] public string gridColor = "0,140,0,255";
        [KSPField] public string ownshipColor = "0,255,0,255";
        [KSPField] public string friendlyColor = "0,200,255,255";
        [KSPField] public string hostileColor = "255,220,0,255";
        [KSPField] public string lockedColor = "255,40,40,255";
        [KSPField] public string irColor = "255,140,0,255";
        [KSPField] public string textColor = "0,255,0,255";
        [KSPField] public string highlightColor = "255,255,255,255";
        [KSPField] public string warningColor = "255,40,40,255";
        [KSPField] public string reticleColor = "255,255,255,255";
        [KSPField] public string labelColor = "206,227,246,255";
        [KSPField] public string labelActiveColor = "119,255,119,255";
        [KSPField] public string labelCurrentColor = "255,255,0,255";
        [KSPField] public string separatorColor = "255,255,255,255";
        [KSPField] public int lineThickness = 2;

        // ---------- Mode settings ----------
        [KSPField] public string rangeSteps = "2500,5000,10000,20000,40000,80000,160000";
        [KSPField] public int defaultRangeIndex = 3;
        [KSPField] public float contactSize = 9f;
        [KSPField] public float velocityVectorSeconds = 8f;
        [KSPField] public int scopeRings = 4;
        [KSPField] public float rwrSymbolSize = 12f;
        [KSPField] public int rwrMaxListed = 4;
        [KSPField] public float reticleSize = 0.12f;
        [KSPField] public bool showAttitude = true;

        private const int ModeIdBase = 100;
        private const int FuncIdBase = 200;
        private const int SlotExit = -2;
        private const int SlotEmpty = -1;
        private const int SlotNext = -3;   // switches to the next mode (MFDs with few buttons)

        private MFDContext ctx;
        private BDAMode[] modes;
        private int current;
        private int[] modeSlotIdx;
        private int[] modeSlotAlt;   // second mode on the same button ("TGP/SYS"), -1 if none
        private string[] modeBtn, funcBtn;
        private int[] modeW, funcW;
        private int[] keys;
        private bool pageActive;
        private int activationFrame = -1;
        private bool hooked;

        private readonly string[] softLabels = new string[BDAMode.MaxSoftkeys];
        private readonly bool[] softActive = new bool[BDAMode.MaxSoftkeys];
        private string tLabel, tLabelActive, tLabelCurrent, tSep;
        private string[] lineBuffer;

        private static readonly FieldInfo fPages =
            typeof(JSI.RasterPropMonitor).GetField("pages", BindingFlags.Instance | BindingFlags.NonPublic);

        public void Start()
        {
            try
            {
                ctx = new MFDContext(GetVessel);
                ctx.SetColors(backgroundColor, gridColor, ownshipColor, friendlyColor, hostileColor,
                    lockedColor, irColor, textColor, highlightColor, warningColor, reticleColor);
                ctx.lineThickness = Mathf.Clamp(lineThickness, 1, 4);

                tLabel = MFDContext.Tag(MFDContext.ParseColor(labelColor, new Color(0.8f, 0.9f, 0.96f)));
                tLabelActive = MFDContext.Tag(MFDContext.ParseColor(labelActiveColor, Color.green));
                tLabelCurrent = MFDContext.Tag(MFDContext.ParseColor(labelCurrentColor, Color.yellow));
                tSep = MFDContext.Tag(MFDContext.ParseColor(separatorColor, Color.white));

                RadarMode radar = new RadarMode(ctx);
                radar.ranges = MFDContext.ParseFloats(rangeSteps, radar.ranges);
                radar.rangeIndex = Mathf.Clamp(defaultRangeIndex, 0, radar.ranges.Length - 1);
                radar.contactSize = contactSize;
                radar.velocityVectorSeconds = velocityVectorSeconds;
                radar.scopeRings = scopeRings;

                EWMode ew = new EWMode(ctx);
                ew.symbolSize = rwrSymbolSize;
                ew.maxListed = rwrMaxListed;

                TGPMode tgp = new TGPMode(ctx);
                tgp.reticleSize = reticleSize;
                tgp.showAttitude = showAttitude;

                modes = new BDAMode[] { radar, ew, new SMSMode(ctx), tgp, new SYSMode(ctx) };

                modeBtn = MFDContext.ParseList(modeButtons);
                string[] slots = MFDContext.ParseList(modeSlots);
                modeSlotIdx = new int[modeBtn.Length];
                modeSlotAlt = new int[modeBtn.Length];
                for (int i = 0; i < modeBtn.Length; i++)
                {
                    string spec = i < slots.Length ? slots[i] : string.Empty;
                    string[] pair = spec.Split('/');
                    modeSlotIdx[i] = SlotFromName(pair[0]);
                    modeSlotAlt[i] = pair.Length > 1 ? SlotFromName(pair[1]) : -1;
                    if (modeSlotAlt[i] < 0) modeSlotAlt[i] = -1;
                }
                modeW = MFDContext.ParseInts(modeWidths, modeBtn.Length, 10);

                funcBtn = MFDContext.ParseList(functionButtons);
                funcW = MFDContext.ParseInts(functionWidths, funcBtn.Length, 10);
                keys = MFDContext.ParseInts(keyMap, 9, -1);

                current = Mathf.Max(0, SlotFromName(defaultMode));
                HookButtons();
                if (logLayout) LogLayout();
            }
            catch (Exception e)
            {
                Debug.LogError("[BDArmoryCommandRPM] Start: " + e);
            }
        }

        private Vessel GetVessel()
        {
            return vessel != null ? vessel : (part != null ? part.vessel : null);
        }

        private static int SlotFromName(string s)
        {
            switch ((s ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "RADAR": case "RDR": return 0;
                case "EW": case "RWR": return 1;
                case "SMS": case "WPN": return 2;
                case "TGP": case "CAM": return 3;
                case "SYS": case "PWR": return 4;
                case "EXIT": return SlotExit;
                case "NEXT": case "MODE": return SlotNext;
                default: return SlotEmpty;
            }
        }

        // ---------- Button layout diagnostics ----------

        private void LogLayout()
        {
            try
            {
                JSI.RasterPropMonitor rpm = null;
                for (int i = 0; i < internalProp.internalModules.Count && rpm == null; i++)
                    rpm = internalProp.internalModules[i] as JSI.RasterPropMonitor;
                if (rpm == null) return;
                Transform screen = internalProp.FindModelTransform(rpm.screenTransform);
                if (screen == null) return;

                StringBuilder sb = new StringBuilder();
                sb.Append("[BDArmoryCommandRPM] LAYOUT prop=").Append(internalProp.propName).Append(" id=").Append(internalProp.propID);
                MeshFilter mf = screen.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Bounds b = mf.sharedMesh.bounds;
                    sb.Append(" screenMin=").Append(b.min.ToString("F4")).Append(" screenMax=").Append(b.max.ToString("F4"));
                    try
                    {
                        Vector3[] v = mf.sharedMesh.vertices;
                        Vector2[] uv = mf.sharedMesh.uv;
                        for (int i = 0; i < v.Length && i < uv.Length && i < 8; i++)
                            sb.Append(" v").Append(i).Append('=').Append(v[i].ToString("F4")).Append("uv").Append(uv[i].ToString("F2"));
                    }
                    catch (Exception) { }
                }
                Debug.Log(sb.ToString());

                List<string> names = new List<string>();
                names.AddRange(modeBtn);
                names.AddRange(funcBtn);
                names.AddRange(MFDContext.ParseList(rpm.globalButtons));
                foreach (string n in names)
                {
                    if (string.IsNullOrEmpty(n)) continue;
                    Transform t = internalProp.FindModelTransform(n);
                    if (t == null) { Debug.Log("[BDArmoryCommandRPM] LAYOUT button=" + n + " (missing)"); continue; }
                    Collider c = t.GetComponent<Collider>();
                    if (c == null) c = t.GetComponentInChildren<Collider>();
                    Renderer r = t.GetComponentInChildren<Renderer>();
                    Vector3 world = c != null ? c.bounds.center : (r != null ? r.bounds.center : t.position);
                    Vector3 local = screen.InverseTransformPoint(world);
                    Debug.Log("[BDArmoryCommandRPM] LAYOUT button=" + n + " local=" + local.ToString("F4"));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmoryCommandRPM] LAYOUT error: " + e.Message);
            }
        }

        // ---------- Physical button capture ----------

        private void HookButtons()
        {
            if (hooked || internalProp == null) return;
            hooked = true;
            for (int i = 0; i < modeBtn.Length; i++)
                Hook(modeBtn[i], ModeIdBase + i);
            for (int i = 0; i < funcBtn.Length; i++)
                Hook(funcBtn[i], FuncIdBase + i);
        }

        private void Hook(string transformName, int id)
        {
            if (string.IsNullOrEmpty(transformName)) return;
            try
            {
                JSI.SmarterButton.CreateButton(internalProp, transformName, id, OnHardwareClick, OnHardwareRelease);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmoryCommandRPM] Could not hook button " + transformName + ": " + e.Message);
            }
        }

        private void OnHardwareClick(int id)
        {
            // Ignore the press that just opened this page.
            if (!pageActive || modes == null || Time.frameCount == activationFrame) return;
            BDACockpitAudio.Click();
            try
            {
                if (id >= FuncIdBase)
                {
                    int slot = id - FuncIdBase;
                    if (slot == exitFunctionSlot) ExitToMenu();
                    else if (slot < BDAMode.MaxSoftkeys) modes[current].Softkey(slot);
                }
                else
                {
                    int slot = id - ModeIdBase;
                    if (slot < 0 || slot >= modeSlotIdx.Length) return;
                    int target = modeSlotIdx[slot];
                    int alt = modeSlotAlt[slot];
                    if (alt >= 0 && target >= 0)
                    {
                        // shared button: toggles between the two modes
                        SelectMode(current == target ? alt : target);
                        return;
                    }
                    if (target == SlotExit) ExitToMenu();
                    else if (target == SlotNext) SelectMode((current + 1) % modes.Length);
                    else if (target >= 0) SelectMode(target);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmoryCommandRPM] Button " + id + ": " + e.Message);
            }
        }

        private void OnHardwareRelease(int id)
        {
            if (pageActive && modes != null) modes[current].KeyRelease();
        }

        private void SelectMode(int index)
        {
            if (index == current)
            {
                modes[current].ModeKeyAgain();
                return;
            }
            modes[current].OnDeactivate();
            current = index;
            modes[current].OnActivate();
        }

        private void ExitToMenu()
        {
            modes[current].OnDeactivate();
            if (fPages == null || internalProp == null) return;
            for (int i = 0; i < internalProp.internalModules.Count; i++)
            {
                JSI.RasterPropMonitor rpm = internalProp.internalModules[i] as JSI.RasterPropMonitor;
                if (rpm == null) continue;
                List<JSI.MonitorPage> pages = fPages.GetValue(rpm) as List<JSI.MonitorPage>;
                if (pages == null) continue;
                for (int p = 0; p < pages.Count; p++)
                {
                    if (pages[p].name == exitPage)
                    {
                        rpm.PageButtonClick(pages[p]);
                        return;
                    }
                }
                Debug.LogWarning("[BDArmoryCommandRPM] Exit page '" + exitPage + "' not found");
                return;
            }
        }

        // ---------- Methods called by RasterPropMonitor ----------

        public void PageActive(bool active, int pageNumber)
        {
            pageActive = active;
            if (modes == null) return;
            if (active && ctx.screen == null) FindScreen();
            if (active)
            {
                activationFrame = Time.frameCount;
                modes[current].OnActivate();
            }
            else
            {
                modes[current].OnDeactivate();
                ctx.SetCameraLock(false);
            }
        }

        private void FindScreen()
        {
            try
            {
                for (int i = 0; i < internalProp.internalModules.Count; i++)
                {
                    JSI.RasterPropMonitor rpm = internalProp.internalModules[i] as JSI.RasterPropMonitor;
                    if (rpm == null) continue;
                    ctx.SetScreen(internalProp.FindModelTransform(rpm.screenTransform));
                    return;
                }
            }
            catch (Exception e) { Debug.LogWarning("[BDArmoryCommandRPM] Screen not found: " + e.Message); }
        }

        public void OnDestroy()
        {
            if (ctx != null) ctx.SetCameraLock(false);
        }

        /// <summary>Side keys (globalButtons).</summary>
        public void ButtonProcessor(int button)
        {
            if (modes == null) return;
            for (int k = 0; k < keys.Length; k++)
            {
                if (keys[k] != button) continue;
                BDACockpitAudio.Click();
                try { modes[current].Key((MFDKey)k); }
                catch (Exception e) { Debug.LogWarning("[BDArmoryCommandRPM] Key " + (MFDKey)k + ": " + e.Message); }
                return;
            }
        }

        public void ButtonRelease(int button)
        {
            if (modes != null) modes[current].KeyRelease();
        }

        public void Update()
        {
            if (pageActive && modes != null && HighLogic.LoadedSceneIsFlight)
            {
                try { modes[current].Update(); }
                catch (Exception) { }
            }
            else if (ctx != null)
            {
                ctx.SetCameraLock(false);
            }
        }

        public string ShowPage(int screenWidth, int screenHeight)
        {
            if (modes == null) return "BDArmoryCommandRPM: startup error, check KSP.log";
            ctx.screenLines = screenHeight;
            if (lineBuffer == null || lineBuffer.Length != screenHeight) lineBuffer = new string[screenHeight];
            string[] lines = lineBuffer;
            for (int i = 0; i < lines.Length; i++) lines[i] = string.Empty;

            try
            {
                modes[current].Text(lines, screenWidth, 1, screenHeight - 2);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmoryCommandRPM] Text " + modes[current].Label + ": " + e.Message);
            }

            // Function key labels
            for (int i = 0; i < BDAMode.MaxSoftkeys; i++) { softLabels[i] = string.Empty; softActive[i] = false; }
            try { modes[current].GetSoftkeys(softLabels, softActive); }
            catch (Exception) { }
            if (exitFunctionSlot >= 0 && exitFunctionSlot < BDAMode.MaxSoftkeys)
            {
                softLabels[exitFunctionSlot] = "EXIT";
                softActive[exitFunctionSlot] = false;
            }

            string modeRow = BuildModeRow();
            string funcRow = BuildFunctionRow();
            int topLine = 0, bottomLine = screenHeight - 1;
            if (funcRow.Length > 0) lines[functionRowTop ? topLine : bottomLine] = funcRow;
            lines[modeRowTop ? topLine : bottomLine] = modeRow;

            return MFDContext.Join(lines);
        }

        public bool RenderPage(RenderTexture screen, float aspect)
        {
            if (!HighLogic.LoadedSceneIsFlight || modes == null) return false;
            bool drawn = false;
            try { drawn = modes[current].Render(screen, 1, ctx.screenLines - 2); }
            catch (Exception e) { Debug.LogWarning("[BDArmoryCommandRPM] Render " + modes[current].Label + ": " + e.Message); }
            if (!drawn) GL.Clear(true, true, ctx.cBackground);
            return true;
        }

        // ---------- Label rows (ASET style, half width) ----------

        private string BuildModeRow()
        {
            StringBuilder sb = new StringBuilder("[hw]");
            if (modeRowPad > 0) sb.Append(new string(' ', modeRowPad));
            for (int i = 0; i < modeBtn.Length; i++)
            {
                if (i > 0) sb.Append(tSep).Append('|');
                int idx = modeSlotIdx[i];
                int alt = modeSlotAlt[i];
                if (alt >= 0 && idx >= 0)
                {
                    bool on = current == idx || current == alt;
                    sb.Append(on ? tLabelCurrent : tLabel).Append(MFDContext.Esc(MFDContext.CenterPad(modes[idx].Label + "/" + modes[alt].Label, modeW[i])));
                    continue;
                }
                string label = idx == SlotExit ? "EXIT" : (idx == SlotNext ? "MODE: " + modes[current].Label : (idx >= 0 ? modes[idx].Label : string.Empty));
                string color = idx == current || idx == SlotNext ? tLabelCurrent : tLabel;
                sb.Append(color).Append(MFDContext.Esc(MFDContext.CenterPad(label, modeW[i])));
            }
            return sb.ToString();
        }

        private string BuildFunctionRow()
        {
            StringBuilder sb = new StringBuilder("[hw]");
            if (funcBtn.Length == 0 || (funcBtn.Length == 1 && string.IsNullOrEmpty(funcBtn[0]))) return string.Empty;
            if (functionRowPad > 0) sb.Append(new string(' ', functionRowPad));
            for (int i = 0; i < funcBtn.Length; i++)
            {
                if (i > 0) sb.Append(tSep).Append('|');
                string label = i < BDAMode.MaxSoftkeys ? softLabels[i] : string.Empty;
                bool on = i < BDAMode.MaxSoftkeys && softActive[i];
                sb.Append(on ? tLabelActive : tLabel).Append(MFDContext.Esc(MFDContext.CenterPad(label, funcW[i])));
            }
            return sb.ToString();
        }
    }
}
