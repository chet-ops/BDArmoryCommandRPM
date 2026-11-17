// Base class for the WEAPONS interface modes (RADAR, EW, SMS, TGP, SYS).

using UnityEngine;

namespace BDArmoryCommandRPM
{
    public abstract class BDAMode
    {
        public const int MaxSoftkeys = 7;

        protected readonly MFDContext ctx;

        protected BDAMode(MFDContext ctx)
        {
            this.ctx = ctx;
        }

        /// <summary>Mode label shown on the mode-select row.</summary>
        public abstract string Label { get; }

        /// <summary>Fills in the function key labels (and whether each one is lit).</summary>
        public abstract void GetSoftkeys(string[] labels, bool[] active);

        /// <summary>Function key number slot (0..6) was pressed.</summary>
        public abstract void Softkey(int slot);

        /// <summary>Side key pressed.</summary>
        public virtual void Key(MFDKey key) { }

        /// <summary>Any key released.</summary>
        public virtual void KeyRelease() { }

        /// <summary>This mode's key was pressed while already in this mode.</summary>
        public virtual void ModeKeyAgain() { }

        public virtual void OnActivate() { }

        public virtual void OnDeactivate() { }

        public virtual void Update() { }

        /// <summary>Text for the content area (lines top..bottom, inclusive).</summary>
        public abstract void Text(string[] lines, int width, int top, int bottom);

        /// <summary>
        /// Background drawing. The content area spans from line top to line bottom.
        /// If it returns false, the controller clears the screen.
        /// </summary>
        public virtual bool Render(RenderTexture screen, int top, int bottom)
        {
            return false;
        }
    }
}
