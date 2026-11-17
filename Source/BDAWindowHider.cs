// BDArmoryCommandRPM - hides BDArmory's floating windows while you are in the cockpit (IVA).
//
// BDArmory opens its radar window and its Target Camera window as soon as they are switched on,
// including when they are switched on from the MFD. Inside the cockpit everything is already on the
// screens, so while in IVA BDArmory's windows are hidden (what F2 does, but only for BDArmory) and
// they come back when you leave the cockpit. The sensors keep working: only the UI is hidden.
//
// Disable it in Settings.cfg (BDARMORYCOMMANDRPM_DISPLAY node, hideBDAWindowsInIVA = false).

using System;
using UnityEngine;
using BDArmory.UI;

namespace BDArmoryCommandRPM
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDAWindowHider : MonoBehaviour
    {
        private bool hideInIVA = true;
        private bool hiddenByUs;
        private bool previousState = true;

        public void Start()
        {
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("BDARMORYCOMMANDRPM_DISPLAY");
            if (nodes != null && nodes.Length > 0)
                nodes[0].TryGetValue("hideBDAWindowsInIVA", ref hideInIVA);
        }

        private static bool InIVA
        {
            get
            {
                CameraManager cm = CameraManager.Instance;
                return cm != null && (cm.currentCameraMode == CameraManager.CameraMode.IVA
                                   || cm.currentCameraMode == CameraManager.CameraMode.Internal);
            }
        }

        public void Update()
        {
            if (!hideInIVA) return;
            try
            {
                bool iva = InIVA && !MapView.MapIsEnabled;
                if (iva && !hiddenByUs)
                {
                    previousState = BDArmorySetup.GAME_UI_ENABLED;
                    BDArmorySetup.GAME_UI_ENABLED = false;
                    hiddenByUs = true;
                }
                else if (!iva && hiddenByUs)
                {
                    Restore();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmoryCommandRPM] Hiding BDArmory windows disabled: " + e.Message);
                hideInIVA = false;
            }
        }

        private void Restore()
        {
            BDArmorySetup.GAME_UI_ENABLED = previousState;
            hiddenByUs = false;
        }

        public void OnDestroy()
        {
            if (hiddenByUs)
            {
                try { Restore(); } catch (Exception) { }
            }
        }
    }
}
