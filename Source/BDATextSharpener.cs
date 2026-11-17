// BDArmoryCommandRPM - sharp text on RasterPropMonitor screens.
//
// RPM draws glyphs with bilinear filtering and a half-texel offset, so every glyph is averaged
// with its neighbours: blurry text and thin "slivers" of the next glyph in the atlas. Since the
// glyphs are drawn at their real size (1:1), "Point" filtering makes them sharp without changing
// any page layout. Anisotropic filtering is also enabled on the screen texture so it reads better
// at an angle.
//
// Applied to every RPM screen on the active vessel. Disable it in Settings.cfg
// (BDARMORYCOMMANDRPM_DISPLAY node, sharpText = false).

using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace BDArmoryCommandRPM
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDATextSharpener : MonoBehaviour
    {
        private bool sharpText = true;
        private int screenAniso = 4;
        private float nextCheck;

        private static readonly FieldInfo fTextRenderer =
            typeof(JSI.RasterPropMonitor).GetField("textRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo fScreenTexture =
            typeof(JSI.RasterPropMonitor).GetField("screenTexture", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo fFontRenderers;
        private static FieldInfo fFontTexture;

        public void Start()
        {
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("BDARMORYCOMMANDRPM_DISPLAY");
            if (nodes != null && nodes.Length > 0)
            {
                nodes[0].TryGetValue("sharpText", ref sharpText);
                nodes[0].TryGetValue("screenAniso", ref screenAniso);
            }
        }

        public void Update()
        {
            if (!sharpText || Time.time < nextCheck) return;
            nextCheck = Time.time + 2f; // RPM may reset the filter when it creates new screens

            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return;
            try
            {
                for (int i = 0; i < v.parts.Count; i++)
                {
                    InternalModel im = v.parts[i].internalModel;
                    if (im == null || im.props == null) continue;
                    for (int p = 0; p < im.props.Count; p++)
                    {
                        InternalProp prop = im.props[p];
                        for (int m = 0; m < prop.internalModules.Count; m++)
                        {
                            JSI.RasterPropMonitor rpm = prop.internalModules[m] as JSI.RasterPropMonitor;
                            if (rpm != null) Sharpen(rpm);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmoryCommandRPM] Sharp text disabled: " + e.Message);
                sharpText = false;
            }
        }

        private void Sharpen(JSI.RasterPropMonitor rpm)
        {
            if (fScreenTexture != null && screenAniso > 0)
            {
                RenderTexture rt = fScreenTexture.GetValue(rpm) as RenderTexture;
                if (rt != null && rt.anisoLevel != screenAniso) rt.anisoLevel = screenAniso;
            }

            if (fTextRenderer == null) return;
            object tr = fTextRenderer.GetValue(rpm);
            if (tr == null) return;
            if (fFontRenderers == null)
                fFontRenderers = tr.GetType().GetField("fontRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
            IEnumerable list = fFontRenderers != null ? fFontRenderers.GetValue(tr) as IEnumerable : null;
            if (list == null) return;
            foreach (object fr in list)
            {
                if (fr == null) continue;
                if (fFontTexture == null)
                    fFontTexture = fr.GetType().GetField("fontTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Texture2D tex = fFontTexture != null ? fFontTexture.GetValue(fr) as Texture2D : null;
                if (tex != null && tex.filterMode != FilterMode.Point) tex.filterMode = FilterMode.Point;
            }
        }
    }
}
