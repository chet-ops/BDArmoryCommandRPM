// BDArmoryCommandRPM - shared helpers: BDArmory access, colours, text and GL drawing.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using BDArmory.Control;
using BDArmory.CounterMeasure;
using BDArmory.Radar;
using BDArmory.Weapons;
using BDArmory.Weapons.Missiles;

namespace BDArmoryCommandRPM
{
    /// <summary>Side keys with a fixed meaning (mapped from globalButtons).</summary>
    public enum MFDKey { Up, Down, Left, Right, Home, X1, X2, X3, X4 }

    public class MFDContext
    {
        private readonly Func<Vessel> vesselGetter;

        public Color cBackground, cGrid, cOwnship, cFriendly, cHostile, cLocked, cIR, cText, cHighlight, cWarning, cReticle;
        public string tText, tHighlight, tWarning, tFriendly, tHostile, tLocked;
        public int lineThickness = 2;

        /// <summary>Number of text lines on the monitor (known in ShowPage).</summary>
        public int screenLines = 20;

        private MissileFire cachedWM;
        private Vessel cachedWMVessel;
        private static Material lineMaterial;

        public MFDContext(Func<Vessel> vesselGetter)
        {
            this.vesselGetter = vesselGetter;
        }

        public void SetColors(string background, string grid, string ownship, string friendly, string hostile,
            string locked, string ir, string text, string highlight, string warning, string reticle)
        {
            cBackground = ParseColor(background, Color.black);
            cGrid = ParseColor(grid, new Color(0f, 0.55f, 0f));
            cOwnship = ParseColor(ownship, Color.green);
            cFriendly = ParseColor(friendly, Color.cyan);
            cHostile = ParseColor(hostile, Color.yellow);
            cLocked = ParseColor(locked, Color.red);
            cIR = ParseColor(ir, new Color(1f, 0.55f, 0f));
            cText = ParseColor(text, Color.green);
            cHighlight = ParseColor(highlight, Color.white);
            cWarning = ParseColor(warning, Color.red);
            cReticle = ParseColor(reticle, Color.white);

            tText = Tag(cText);
            tHighlight = Tag(cHighlight);
            tWarning = Tag(cWarning);
            tFriendly = Tag(cFriendly);
            tHostile = Tag(cHostile);
            tLocked = Tag(cLocked);
        }

        // ---------- Mouse over the panel screen ----------

        /// <summary>Screen transform of this monitor (RPM's screenTransform).</summary>
        public Transform screen;
        private Bounds screenBounds;
        private bool hasScreenBounds;
        private static int lockCounter;
        private readonly string lockId = "BDArmoryCommandRPM_MFD_" + (++lockCounter);
        private bool cameraLocked;

        public void SetScreen(Transform t)
        {
            screen = t;
            hasScreenBounds = false;
            if (t == null) return;
            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                screenBounds = mf.sharedMesh.bounds;
                // the screen is flat: give it some thickness so the ray can hit it
                Vector3 size = screenBounds.size;
                float min = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.02f;
                screenBounds.size = new Vector3(Mathf.Max(size.x, min), Mathf.Max(size.y, min), Mathf.Max(size.z, min));
                hasScreenBounds = true;
            }
        }

        public static bool InIVA
        {
            get
            {
                CameraManager cm = CameraManager.Instance;
                return cm != null && (cm.currentCameraMode == CameraManager.CameraMode.IVA
                                   || cm.currentCameraMode == CameraManager.CameraMode.Internal);
            }
        }

        /// <summary>Is the mouse pointer over this monitor's screen?</summary>
        public bool MouseOverScreen()
        {
            if (screen == null || !InIVA || InternalCamera.Instance == null) return false;
            Camera cam = InternalCamera.Instance.GetComponent<Camera>();
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (hasScreenBounds)
            {
                Ray local = new Ray(screen.InverseTransformPoint(ray.origin), screen.InverseTransformDirection(ray.direction));
                return screenBounds.IntersectRay(local);
            }
            Renderer r = screen.GetComponent<Renderer>();
            return r != null && r.bounds.IntersectRay(ray);
        }

        // The mouse wheel zooms the cockpit view: InternalCamera.Update adds
        // AXIS_MOUSEWHEEL * mouseZoomSensitivity without checking control locks. While the
        // pointer is over a screen that uses the wheel, that sensitivity is set to 0 and restored afterwards.
        private static readonly System.Reflection.FieldInfo fZoomSens = typeof(InternalCamera).GetField("mouseZoomSensitivity",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        private static readonly HashSet<string> wheelHolders = new HashSet<string>();
        private static InternalCamera wheelCam;
        private static float savedZoomSens;

        /// <summary>Takes (on=true) or gives back the mouse wheel from/to the cockpit view.</summary>
        public void SetCameraLock(bool on)
        {
            if (on == cameraLocked) return;
            cameraLocked = on;
            try
            {
                if (on) wheelHolders.Add(lockId);
                else wheelHolders.Remove(lockId);
                ApplyWheelBlock();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmoryCommandRPM] Mouse wheel: " + e.Message);
            }
        }

        private static void ApplyWheelBlock()
        {
            if (fZoomSens == null) return;
            InternalCamera ic = InternalCamera.Instance;
            bool want = wheelHolders.Count > 0 && ic != null;

            // give the sensitivity back to the camera that had it blocked
            if (wheelCam != null && (!want || wheelCam != ic))
            {
                fZoomSens.SetValue(wheelCam, savedZoomSens);
                wheelCam = null;
            }
            if (want && wheelCam == null)
            {
                savedZoomSens = (float)fZoomSens.GetValue(ic);
                fZoomSens.SetValue(ic, 0f);
                wheelCam = ic;
            }
        }

        // ---------- BDArmory access ----------

        public Vessel OwnVessel
        {
            get { return vesselGetter(); }
        }

        public MissileFire GetWM()
        {
            Vessel v = OwnVessel;
            if (v == null) return null;
            if (cachedWM == null || cachedWMVessel != v || cachedWM.vessel != v)
            {
                cachedWM = null;
                cachedWMVessel = v;
                for (int i = 0; i < v.parts.Count; i++)
                {
                    MissileFire wm = v.parts[i].FindModuleImplementing<MissileFire>();
                    if (wm != null)
                    {
                        cachedWM = wm;
                        break;
                    }
                }
            }
            return cachedWM;
        }

        // ---------- Cached module lists ----------
        // FindPartModulesImplementing walks every part and allocates a new list on every
        // call. Here the list is cached per type and rebuilt only when the vessel, its part
        // count changes or one second has passed.

        private class ModuleCache
        {
            public Vessel vessel;
            public int partCount = -1;
            public float time = -100f;
            public System.Collections.IList list;
        }

        private readonly Dictionary<Type, ModuleCache> moduleCache = new Dictionary<Type, ModuleCache>();

        /// <summary>Modules of type T on the vessel (shared list: do not modify it).</summary>
        public List<T> Modules<T>() where T : class
        {
            Vessel v = OwnVessel;
            ModuleCache c;
            if (!moduleCache.TryGetValue(typeof(T), out c))
            {
                c = new ModuleCache();
                c.list = new List<T>();
                moduleCache[typeof(T)] = c;
            }
            List<T> list = (List<T>)c.list;
            if (v == null)
            {
                list.Clear();
                c.vessel = null;
                return list;
            }
            if (c.vessel != v || c.partCount != v.parts.Count || Time.time - c.time > 1f)
            {
                c.vessel = v;
                c.partCount = v.parts.Count;
                c.time = Time.time;
                list.Clear();
                for (int i = 0; i < v.parts.Count; i++)
                {
                    PartModuleList mods = v.parts[i].Modules;
                    for (int m = 0; m < mods.Count; m++)
                    {
                        T t = mods[m] as T;
                        if (t != null) list.Add(t);
                    }
                }
            }
            return list;
        }

        public VesselRadarData GetVRD(MissileFire wm)
        {
            if (wm != null && wm.vesselRadarData != null) return wm.vesselRadarData;
            Vessel v = OwnVessel;
            if (v == null) return null;
            return v.gameObject.GetComponent<VesselRadarData>();
        }

        private static readonly FieldInfo fDisplayedTargets =
            typeof(VesselRadarData).GetField("displayedTargets", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Number of radar contacts shown by BDArmory (0 if it cannot be read).</summary>
        public static int RadarContactCount(VesselRadarData vrd)
        {
            if (vrd == null || fDisplayedTargets == null) return 0;
            try
            {
                System.Collections.ICollection list = fDisplayedTargets.GetValue(vrd) as System.Collections.ICollection;
                return list != null ? list.Count : 0;
            }
            catch (Exception) { return 0; }
        }

        // ---------- RWR ----------

        public struct RwrPing
        {
            public string type;
            public Vector3 position;
            public Vessel source;
        }

        private static Type rwrPingType;
        private static FieldInfo fPingExists, fPingSignal, fPingVessel;
        private static PropertyInfo pPingPosition;
        private static readonly FieldInfo fPingsData =
            typeof(RadarWarningReceiver).GetField("pingsData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public RadarWarningReceiver GetRWR(MissileFire wm)
        {
            if (wm != null && wm.rwr != null) return wm.rwr;
            List<RadarWarningReceiver> list = Modules<RadarWarningReceiver>();
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>Reads the active RWR pings (via reflection, tolerant to version changes).</summary>
        public static void ReadRwrPings(RadarWarningReceiver rwr, List<RwrPing> pings)
        {
            pings.Clear();
            if (rwr == null || fPingsData == null) return;
            System.Collections.IEnumerable data = fPingsData.GetValue(rwr) as System.Collections.IEnumerable;
            if (data == null) return;
            foreach (object o in data)
            {
                if (o == null) continue;
                Type t = o.GetType();
                if (t != rwrPingType)
                {
                    rwrPingType = t;
                    const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public;
                    fPingExists = t.GetField("exists", bf);
                    fPingSignal = t.GetField("signalType", bf);
                    fPingVessel = t.GetField("vessel", bf);
                    pPingPosition = t.GetProperty("position", bf);
                }
                if (fPingExists == null || pPingPosition == null) return;
                if (!(bool)fPingExists.GetValue(o)) continue;
                RwrPing p = new RwrPing();
                p.type = fPingSignal != null ? fPingSignal.GetValue(o).ToString() : "Detection";
                p.position = (Vector3)pPingPosition.GetValue(o, null);
                p.source = fPingVessel != null ? fPingVessel.GetValue(o) as Vessel : null;
                pings.Add(p);
            }
        }

        /// <summary>Total amount and capacity of a resource on the vessel.</summary>
        public static void ResourceTotals(Vessel v, string resource, out double amount, out double max)
        {
            amount = 0; max = 0;
            if (v == null) return;
            for (int i = 0; i < v.parts.Count; i++)
            {
                PartResourceList list = v.parts[i].Resources;
                for (int j = 0; j < list.Count; j++)
                {
                    PartResource r = list[j];
                    if (r.resourceName != resource) continue;
                    amount += r.amount;
                    max += r.maxAmount;
                }
            }
        }

        public static string ResourceAmount(Vessel v, string resource)
        {
            if (v == null) return "-";
            double total = 0;
            bool found = false;
            for (int i = 0; i < v.parts.Count; i++)
            {
                PartResourceList list = v.parts[i].Resources;
                for (int j = 0; j < list.Count; j++)
                {
                    PartResource r = list[j];
                    if (r.resourceName != resource) continue;
                    total += r.amount;
                    found = true;
                }
            }
            return found ? total.ToString("0") : "-";
        }

        public void DropCountermeasure(CMDropper.CountermeasureTypes type)
        {
            List<CMDropper> droppers = Modules<CMDropper>();
            for (int i = 0; i < droppers.Count; i++)
            {
                if (droppers[i] != null && droppers[i].cmType == type) droppers[i].DropCM();
            }
        }

        public static string GuidanceShort(MissileBase.TargetingModes mode)
        {
            switch (mode)
            {
                case MissileBase.TargetingModes.Radar: return "RDR";
                case MissileBase.TargetingModes.Heat: return "IR";
                case MissileBase.TargetingModes.Laser: return "LSR";
                case MissileBase.TargetingModes.Gps: return "GPS";
                case MissileBase.TargetingModes.AntiRad: return "ARM";
                case MissileBase.TargetingModes.Inertial: return "INS";
                default: return "---";
            }
        }

        /// <summary>Guidance and remaining count for missiles/bombs/torpedoes; ammo for guns.</summary>
        public static string WeaponInfo(IBDWeapon w)
        {
            if (w == null) return string.Empty;
            MissileBase mb = w as MissileBase;
            if (mb != null)
                return GuidanceShort(mb.TargetingMode) + " x" + mb.missilecount;

            string sub = w.GetSubLabel();
            if (string.IsNullOrEmpty(sub)) return string.Empty;
            int colon = sub.IndexOf(':');
            if (colon < 0) return sub;
            string rest = sub.Substring(colon + 1).Trim();
            int semi = rest.IndexOf(';');
            if (semi >= 0) rest = rest.Substring(0, semi).Trim();
            return rest + " RDS";
        }

        public static string ClassTag(WeaponClasses c)
        {
            switch (c)
            {
                case WeaponClasses.Missile: return "MSL";
                case WeaponClasses.Bomb: return "BMB";
                case WeaponClasses.Gun: return "GUN";
                case WeaponClasses.Rocket: return "RKT";
                case WeaponClasses.DefenseLaser: return "LSR";
                case WeaponClasses.SLW: return "TRP";
                default: return "???";
            }
        }

        // ---------- Geometry ----------

        public static void GetBasis(Vessel own, out Vector3 up, out Vector3 fwd, out Vector3 right)
        {
            up = ((Vector3)own.upAxis).normalized;
            Transform rt = own.ReferenceTransform != null ? own.ReferenceTransform : own.transform;
            fwd = Vector3.ProjectOnPlane(rt.up, up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(-rt.forward, up);
            fwd.Normalize();
            right = Vector3.Cross(up, fwd);
        }

        /// <summary>x = metres to the right, y = metres ahead (horizontal plane, nose up).</summary>
        // Horizontal basis of the vessel, cached for the frame (used for every contact)
        private static Vessel basisVessel;
        private static int basisFrame = -1;
        private static Vector3 basisFwd, basisRight, basisCoM;

        private static void CachedBasis(Vessel own)
        {
            if (own == basisVessel && Time.frameCount == basisFrame) return;
            Vector3 up;
            GetBasis(own, out up, out basisFwd, out basisRight);
            basisCoM = own.CoM;
            basisVessel = own;
            basisFrame = Time.frameCount;
        }

        public static Vector2 RelativeHorizontal(Vessel own, Vector3 worldPos)
        {
            CachedBasis(own);
            Vector3 d = worldPos - basisCoM;
            return new Vector2(Vector3.Dot(d, basisRight), Vector3.Dot(d, basisFwd));
        }

        public static Vector2 RelativeHorizontalDir(Vessel own, Vector3 worldDir)
        {
            CachedBasis(own);
            return new Vector2(Vector3.Dot(worldDir, basisRight), Vector3.Dot(worldDir, basisFwd));
        }

        public static float RelativeBearing(Vector2 rel)
        {
            return Mathf.Atan2(rel.x, rel.y) * Mathf.Rad2Deg;
        }

        public static Vector2 ToScope(Vector2 rel, Vector2 center, float radius, float range, out bool clipped)
        {
            Vector2 p = rel / range * radius;
            clipped = false;
            if (p.sqrMagnitude > radius * radius)
            {
                p = p.normalized * radius;
                clipped = true;
            }
            return center + new Vector2(p.x, -p.y);
        }

        /// <summary>Pixels per text line.</summary>
        public float LinePixels(RenderTexture screen)
        {
            return (float)screen.height / Mathf.Max(1, screenLines);
        }

        // ---------- Text ----------

        public static string Tag(Color c)
        {
            return "[#" + ColorUtility.ToHtmlStringRGBA(c) + "]";
        }

        public static string Esc(string s)
        {
            return string.IsNullOrEmpty(s) ? string.Empty : s.Replace("[", "[[]");
        }

        public static string Fit(string s, int width)
        {
            if (width <= 0) return string.Empty;
            if (s == null) s = string.Empty;
            if (s.Length > width) s = s.Substring(0, width);
            return s.PadRight(width);
        }

        public static string FitEsc(string s, int width)
        {
            return Esc(Fit(s, width));
        }

        public static string Center(string s, int width)
        {
            if (s == null) s = string.Empty;
            if (s.Length >= width) return s.Substring(0, width);
            int pad = (width - s.Length) / 2;
            return new string(' ', pad) + s;
        }

        public static string CenterPad(string s, int width)
        {
            return Center(s, width).PadRight(width);
        }

        public static string Dist(double meters)
        {
            if (double.IsNaN(meters) || double.IsInfinity(meters)) return "---";
            meters = Math.Abs(meters);
            if (meters < 1000) return meters.ToString("0") + "m";
            if (meters < 100000) return (meters / 1000).ToString("0.0") + "km";
            return (meters / 1000).ToString("0") + "km";
        }

        public static string Bearing(float relBearing)
        {
            int b = Mathf.RoundToInt(Mathf.Abs(relBearing));
            if (b == 0 || b == 180) return b.ToString("000") + " ";
            return b.ToString("000") + (relBearing > 0 ? "R" : "L");
        }

        public static bool Blink(float period = 0.8f)
        {
            return (Time.time % period) < period * 0.6f;
        }

        public static string Join(string[] lines)
        {
            return string.Join(Environment.NewLine, lines);
        }

        public static Color ParseColor(string s, Color fallback)
        {
            try
            {
                if (!string.IsNullOrEmpty(s)) return ConfigNode.ParseColor32(s);
            }
            catch (Exception) { }
            return fallback;
        }

        public static float[] ParseFloats(string s, float[] fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            List<float> list = new List<float>();
            foreach (string token in s.Split(','))
            {
                float f;
                if (float.TryParse(token.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out f) && f > 0)
                    list.Add(f);
            }
            return list.Count > 0 ? list.ToArray() : fallback;
        }

        public static string[] ParseList(string s)
        {
            if (s == null) return new string[0];
            string[] parts = s.Split(',');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }

        public static int[] ParseInts(string s, int count, int fallback)
        {
            string[] parts = ParseList(s);
            int[] r = new int[count];
            for (int i = 0; i < count; i++)
            {
                int v;
                r[i] = i < parts.Length && int.TryParse(parts[i], out v) ? v : fallback;
            }
            return r;
        }

        // ---------- GL drawing ----------

        public static Material LineMaterial
        {
            get
            {
                if (lineMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/Internal-Colored");
                    if (shader == null) shader = Shader.Find("KSP/Particles/Alpha Blended");
                    lineMaterial = new Material(shader);
                    lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                    if (lineMaterial.HasProperty("_SrcBlend")) lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    if (lineMaterial.HasProperty("_DstBlend")) lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    if (lineMaterial.HasProperty("_Cull")) lineMaterial.SetInt("_Cull", (int)CullMode.Off);
                    if (lineMaterial.HasProperty("_ZWrite")) lineMaterial.SetInt("_ZWrite", 0);
                    if (lineMaterial.HasProperty("_ZTest")) lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
                }
                return lineMaterial;
            }
        }

        /// <summary>Starts drawing lines. clear = clear the screen first.</summary>
        public void BeginLines(RenderTexture screen, bool clear)
        {
            if (clear) GL.Clear(true, true, cBackground);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, screen.width, screen.height, 0);
            LineMaterial.SetPass(0);
            GL.Begin(GL.LINES);
        }

        public void EndLines()
        {
            GL.End();
            GL.PopMatrix();
        }

        public void Line(Vector2 a, Vector2 b, Color c)
        {
            GL.Color(c);
            if (lineThickness <= 1)
            {
                GL.Vertex3(a.x, a.y, 0f);
                GL.Vertex3(b.x, b.y, 0f);
                return;
            }
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.right;
            Vector2 n = new Vector2(-dir.y, dir.x).normalized;
            float start = -(lineThickness - 1) * 0.5f;
            for (int i = 0; i < lineThickness; i++)
            {
                Vector2 o = n * (start + i);
                GL.Vertex3(a.x + o.x, a.y + o.y, 0f);
                GL.Vertex3(b.x + o.x, b.y + o.y, 0f);
            }
        }

        // Sine/cosine tables per segment count (circles are drawn every frame)
        private static readonly Dictionary<int, Vector2[]> unitCircles = new Dictionary<int, Vector2[]>();

        private static Vector2[] UnitCircle(int segments)
        {
            Vector2[] pts;
            if (unitCircles.TryGetValue(segments, out pts)) return pts;
            pts = new Vector2[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float a = i * 2f * Mathf.PI / segments;
                pts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            unitCircles[segments] = pts;
            return pts;
        }

        public void Circle(Vector2 center, float radius, Color c, int segments = 64)
        {
            Vector2[] pts = UnitCircle(Mathf.Max(3, segments));
            Vector2 prev = center + pts[0] * radius;
            for (int i = 1; i < pts.Length; i++)
            {
                Vector2 p = center + pts[i] * radius;
                Line(prev, p, c);
                prev = p;
            }
        }

        public void Box(Vector2 c, float h, Color col)
        {
            Vector2 a = c + new Vector2(-h, -h), b = c + new Vector2(h, -h);
            Vector2 d = c + new Vector2(h, h), e = c + new Vector2(-h, h);
            Line(a, b, col); Line(b, d, col); Line(d, e, col); Line(e, a, col);
        }

        public void Diamond(Vector2 c, float h, Color col)
        {
            Vector2 t = c + new Vector2(0, -h), r = c + new Vector2(h, 0);
            Vector2 b = c + new Vector2(0, h), l = c + new Vector2(-h, 0);
            Line(t, r, col); Line(r, b, col); Line(b, l, col); Line(l, t, col);
        }

        public void Triangle(Vector2 c, float h, Color col)
        {
            Vector2 t = c + new Vector2(0, -h), r = c + new Vector2(h, h * 0.8f), l = c + new Vector2(-h, h * 0.8f);
            Line(t, r, col); Line(r, l, col); Line(l, t, col);
        }

        public void Cross(Vector2 c, float h, Color col)
        {
            Line(c + new Vector2(-h, -h), c + new Vector2(h, h), col);
            Line(c + new Vector2(-h, h), c + new Vector2(h, -h), col);
        }

        public void Brackets(Vector2 c, float h, Color col)
        {
            float k = h * 0.45f;
            Line(c + new Vector2(-h, -h), c + new Vector2(-h + k, -h), col);
            Line(c + new Vector2(-h, -h), c + new Vector2(-h, -h + k), col);
            Line(c + new Vector2(h, -h), c + new Vector2(h - k, -h), col);
            Line(c + new Vector2(h, -h), c + new Vector2(h, -h + k), col);
            Line(c + new Vector2(-h, h), c + new Vector2(-h + k, h), col);
            Line(c + new Vector2(-h, h), c + new Vector2(-h, h - k), col);
            Line(c + new Vector2(h, h), c + new Vector2(h - k, h), col);
            Line(c + new Vector2(h, h), c + new Vector2(h, h - k), col);
        }

        public void Ownship(Vector2 c, float h, Color col)
        {
            Line(c + new Vector2(-h, h * 0.7f), c + new Vector2(0, -h), col);
            Line(c + new Vector2(0, -h), c + new Vector2(h, h * 0.7f), col);
            Line(c + new Vector2(-h * 0.5f, h * 0.3f), c + new Vector2(h * 0.5f, h * 0.3f), col);
        }

        public void ScopeGrid(Vessel own, Vector2 center, float radius, int rings, bool drawNorth)
        {
            for (int i = 1; i <= rings; i++)
                Circle(center, radius * i / rings, cGrid, i == rings ? 96 : 64);

            for (int deg = 0; deg < 360; deg += 30)
            {
                float a = deg * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
                float inner = deg % 90 == 0 ? radius * 0.92f : radius * 0.96f;
                Line(center + dir * inner, center + dir * radius, cGrid);
            }

            if (drawNorth && own != null && own.mainBody != null)
            {
                Vector2 n = RelativeHorizontalDir(own, own.mainBody.transform.up);
                if (n.sqrMagnitude > 1e-6f)
                {
                    n.Normalize();
                    Vector2 dir = new Vector2(n.x, -n.y);
                    Vector2 tip = center + dir * (radius + 4f);
                    Vector2 side = new Vector2(-dir.y, dir.x) * 8f;
                    Vector2 back = center + dir * (radius + 18f);
                    Line(tip, back + side, cHighlight);
                    Line(back + side, back - side, cHighlight);
                    Line(back - side, tip, cHighlight);
                }
            }
        }
    }
}
