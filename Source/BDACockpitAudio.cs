// BDArmoryCommandRPM - cockpit sounds for BDArmory.
//
// Watches BDArmory's state on the active vessel and plays avionics cues:
// radar (on/off, sweep, new contact, lock, target lost), GUARD, ECM/cloak,
// AI pilot (with disconnect warning), damage (caution/warning), countermeasures
// bingo/empty, TGP camera (servo, zoom, lock) and MFD button clicks.
//
// Sounds are synthesized at startup. Any of them can be replaced with your own .ogg/.wav
// from GameData/BDArmoryCommandRPM/Settings.cfg (BDARMORYCOMMANDRPM_AUDIO node, clip_* values).
// BDArmory already plays its own sounds for: RWR, incoming missile, IR growl, ARM and weapon change.

using System;
using System.Collections.Generic;
using UnityEngine;
using BDArmory.Control;
using BDArmory.CounterMeasure;
using BDArmory.Radar;
using BDArmory.Targeting;

namespace BDArmoryCommandRPM
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDACockpitAudio : MonoBehaviour
    {
        public static BDACockpitAudio Instance { get; private set; }

        // ---------- Settings (Settings.cfg) ----------
        private bool enabledAudio = true;
        private bool onlyInIVA = true;
        private float masterVolume = 0.8f;
        private bool radarSweep = false;
        private float sweepVolume = 0.35f;
        private bool buttonClicks = true;
        private float bingoFraction = 0.25f;
        private float cautionHP = 0.5f;
        private float warningHP = 0.25f;
        private bool hornetRWR = true;            // AN/ALR-67 (F/A-18) style tones instead of BDArmory's RWR audio
        private bool muteBDAMissileWarning = true; // mutes BDArmory's generic missile warning
        private float rwrVolume = 0.9f;
        private float alarmVolume = 1f;            // alarms and RWR: independent of "volume", only the game's ship volume
        private float newEmitterMemory = 10f;     // seconds without seeing an emitter before it is announced again

        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private AudioSource fx, loop, rwrLoop;

        // ---------- Previous state ----------
        private MFDContext ctx;
        private Vessel lastVessel;
        private bool primed;
        private bool lastRadarOn, lastGuard, lastAI, lastJam, lastCloak, lastTgpLock;
        private int lastContacts, lastLocks, lastHPBand;
        private bool flareBingo, chaffBingo, flareEmpty, chaffEmpty;
        private float nextSweep, nextContactBlip, userUnlockTime = -10f, nextPoll;
        private readonly List<MFDContext.RwrPing> pings = new List<MFDContext.RwrPing>();
        private string rwrLoopClip;
        private static readonly System.Reflection.FieldInfo fRwrAudio =
            typeof(RadarWarningReceiver).GetField("audioSource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        private bool servoWanted;
        private float servoUntil, nextCMCheck;
        private AudioSource mutedRwrAudio, mutedWarnAudio;
        private readonly Dictionary<long, float> emitters = new Dictionary<long, float>();
        private readonly List<long> staleEmitters = new List<long>();
        private readonly Dictionary<ModuleRadar, float> sweepPeriods = new Dictionary<ModuleRadar, float>();

        // ---------- Startup ----------

        public void Awake()
        {
            Instance = this;
            ctx = new MFDContext(() => FlightGlobals.ActiveVessel);
            LoadSettings();

            fx = gameObject.AddComponent<AudioSource>();
            SetupSource(fx, false);
            loop = gameObject.AddComponent<AudioSource>();
            SetupSource(loop, true);
            rwrLoop = gameObject.AddComponent<AudioSource>();
            SetupSource(rwrLoop, true);

            try { BuildClips(); }
            catch (Exception e) { Debug.LogError("[BDArmoryCommandRPM] Sounds: " + e); enabledAudio = false; }
        }

        public void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // give the audio back to BDArmory
            try { SetBDAMute(null, null); }
            catch (Exception) { }
        }

        private static void SetupSource(AudioSource s, bool looping)
        {
            s.playOnAwake = false;
            s.loop = looping;
            s.spatialBlend = 0f;   // 2D: it is the cockpit speaker
            s.dopplerLevel = 0f;
            s.bypassListenerEffects = true;
        }

        private ConfigNode settingsNode;

        private void LoadSettings()
        {
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("BDARMORYCOMMANDRPM_AUDIO");
            if (nodes == null || nodes.Length == 0) return;
            settingsNode = nodes[0];
            ConfigNode n = settingsNode;
            n.TryGetValue("enabled", ref enabledAudio);
            n.TryGetValue("onlyInIVA", ref onlyInIVA);
            n.TryGetValue("volume", ref masterVolume);
            n.TryGetValue("radarSweep", ref radarSweep);
            n.TryGetValue("sweepVolume", ref sweepVolume);
            n.TryGetValue("buttonClicks", ref buttonClicks);
            n.TryGetValue("bingoFraction", ref bingoFraction);
            n.TryGetValue("cautionHP", ref cautionHP);
            n.TryGetValue("warningHP", ref warningHP);
            n.TryGetValue("hornetRWR", ref hornetRWR);
            n.TryGetValue("muteBDAMissileWarning", ref muteBDAMissileWarning);
            n.TryGetValue("rwrVolume", ref rwrVolume);
            n.TryGetValue("alarmVolume", ref alarmVolume);
            n.TryGetValue("newEmitterMemory", ref newEmitterMemory);
        }

        private readonly HashSet<string> alarmClips = new HashSet<string>();

        private void Alarm(string name, float[] data)
        {
            alarmClips.Add(name);
            Add(name, data, true, true);
        }

        private void Add(string name, float[] data, bool speaker = true, bool alarm = false)
        {
            // Replacement with your own file: clip_<name> = Folder/File (no extension)
            string path = settingsNode != null ? settingsNode.GetValue("clip_" + name) : null;
            if (!string.IsNullOrEmpty(path))
            {
                AudioClip custom = GameDatabase.Instance.GetAudioClip(path);
                if (custom != null)
                {
                    clips[name] = custom;
                    return;
                }
                Debug.LogWarning("[BDArmoryCommandRPM] Sound '" + path + "' not found, using the synthesized one");
            }
            clips[name] = SoundLib.Make(name, data, speaker, alarm);
        }

        private void BuildClips()
        {
            // MFD
            Add("click", SoundLib.Mix(SoundLib.Noise(0.012f, 0.5f, 0.6f), SoundLib.Tone(2200, 0.012f, 0.25f, SoundLib.Wave.Sine, 0.001f, 0.008f)));

            // Radar
            Add("radarOn", SoundLib.Concat(SoundLib.Noise(0.015f, 0.4f, 0.5f),
                SoundLib.Mix(SoundLib.Sweep(250, 950, 0.55f, 0.35f, SoundLib.Wave.Sine, 0.05f, 0.08f),
                             SoundLib.Sweep(500, 1900, 0.55f, 0.08f, SoundLib.Wave.Sine, 0.05f, 0.08f)),
                SoundLib.Tone(1200, 0.06f, 0.4f)));
            Add("radarOff", SoundLib.Concat(SoundLib.Tone(1200, 0.06f, 0.4f),
                SoundLib.Sweep(950, 200, 0.5f, 0.3f, SoundLib.Wave.Sine, 0.01f, 0.15f)));
            Add("sweep", SoundLib.Tone(2600, 0.018f, 0.25f, SoundLib.Wave.Sine, 0.002f, 0.012f));
            Add("contact", SoundLib.Tone(1600, 0.05f, 0.45f));
            Add("lock", SoundLib.Repeat(SoundLib.Concat(SoundLib.Tone(1000, 0.055f, 0.55f), SoundLib.Tone(1400, 0.055f, 0.55f)), 0.02f, 3));
            Alarm("lockLost", SoundLib.Concat(SoundLib.Tone(1200, 0.12f, 0.9f), SoundLib.Silence(0.03f), SoundLib.Tone(700, 0.22f, 0.9f)));
            Add("unlock", SoundLib.Tone(900, 0.06f, 0.4f));

            // Systems
            Add("guardOn", SoundLib.Concat(SoundLib.Tone(880, 0.1f, 0.5f), SoundLib.Silence(0.04f), SoundLib.Tone(1320, 0.15f, 0.5f)));
            Add("guardOff", SoundLib.Concat(SoundLib.Tone(1320, 0.1f, 0.5f), SoundLib.Silence(0.04f), SoundLib.Tone(880, 0.15f, 0.5f)));
            Add("ewOn", SoundLib.Repeat(SoundLib.Concat(SoundLib.Tone(1200, 0.04f, 0.45f), SoundLib.Tone(1500, 0.04f, 0.45f)), 0.03f, 2));
            Add("ewOff", SoundLib.Repeat(SoundLib.Concat(SoundLib.Tone(1500, 0.04f, 0.45f), SoundLib.Tone(1200, 0.04f, 0.45f)), 0.03f, 2));
            Add("apEngage", SoundLib.Chime(1046, 0.6f, 0.6f));
            // autopilot disconnect "wailer"
            Alarm("apDisconnect", SoundLib.Warble(650, 920, 0.11f, 1.5f, 0.9f, SoundLib.Wave.Soft));

            // Warnings
            Alarm("caution", SoundLib.Concat(SoundLib.Chime(1046, 0.35f, 0.9f), SoundLib.Chime(784, 0.6f, 0.9f)));
            Alarm("warning", SoundLib.Warble(1000, 1250, 0.08f, 1.2f, 0.9f, SoundLib.Wave.Square));
            Alarm("bingo", SoundLib.Repeat(SoundLib.Tone(800, 0.15f, 0.9f), 0.1f, 3));
            Alarm("empty", SoundLib.Tone(500, 0.6f, 0.9f));

            // AN/ALR-67 (F/A-18C) style RWR, following the documented tone patterns:
            //  - status change / new emitter: single 455 Hz tone for 0.335 s
            //  - fighter (AI) emitter: 0.335 s burst of four tones 550-520-490-470 Hz (~0.083 s each)
            //  - STT (you are locked): alternates 455 Hz and 555 Hz every 0.25 s while it lasts
            //  - missile guidance/launch: alternates 455 Hz (0.066 s) and 555 Hz (0.1 s) while it lasts
            const SoundLib.Wave rw = SoundLib.Wave.Soft;
            Alarm("rwrNew", SoundLib.Tone(455, 0.335f, 0.9f, rw, 0.004f, 0.01f));
            Alarm("rwrNewAI", SoundLib.Concat(SoundLib.Tone(550, 0.083f, 0.9f, rw, 0.002f, 0.002f), SoundLib.Tone(520, 0.084f, 0.9f, rw, 0.002f, 0.002f),
                                           SoundLib.Tone(490, 0.084f, 0.9f, rw, 0.002f, 0.002f), SoundLib.Tone(470, 0.084f, 0.9f, rw, 0.002f, 0.01f)));
            Alarm("rwrSTT", SoundLib.Repeat(SoundLib.Concat(SoundLib.Tone(455, 0.25f, 0.9f, rw, 0.003f, 0.003f), SoundLib.Tone(555, 0.25f, 0.9f, rw, 0.003f, 0.003f)), 0f, 2));
            Alarm("rwrLaunch", SoundLib.Repeat(SoundLib.Concat(SoundLib.Tone(455, 0.066f, 0.9f, rw, 0.002f, 0.002f), SoundLib.Tone(555, 0.1f, 0.9f, rw, 0.002f, 0.002f)), 0f, 6));

            // TGP camera
            Add("tgpLock", SoundLib.Repeat(SoundLib.Tone(1800, 0.04f, 0.45f), 0.03f, 2));
            Add("zoom", SoundLib.Mix(SoundLib.Noise(0.03f, 0.35f, 0.3f), SoundLib.Sweep(600, 900, 0.05f, 0.15f, SoundLib.Wave.Sine)));
            Add("servo", SoundLib.ServoLoop(), false);
        }

        // ---------- API for the MFD ----------

        public static void Click()
        {
            if (Instance != null && Instance.buttonClicks) Instance.Play("click", 0.6f);
        }

        public static void ZoomClick()
        {
            if (Instance != null) Instance.Play("zoom", 0.7f);
        }

        /// <summary>The user dropped a target on purpose (not a "target lost").</summary>
        public static void NotifyUserUnlock()
        {
            if (Instance != null) Instance.userUnlockTime = Time.time;
        }

        /// <summary>Call every frame while the TGP camera is slewing.</summary>
        public static void ServoActive()
        {
            if (Instance == null) return;
            Instance.servoWanted = true;
            Instance.servoUntil = Time.time + 0.12f;
        }

        // ---------- Playback ----------

        private bool CanPlay
        {
            get
            {
                if (!enabledAudio || FlightDriver.Pause) return false;
                if (!onlyInIVA) return true;
                CameraManager cm = CameraManager.Instance;
                return cm != null && (cm.currentCameraMode == CameraManager.CameraMode.IVA
                                   || cm.currentCameraMode == CameraManager.CameraMode.Internal);
            }
        }

        private float Volume
        {
            get { return Mathf.Clamp01(GameSettings.SHIP_VOLUME * masterVolume); }
        }

        /// <summary>Volume of alarms and RWR: only the game's ship volume times alarmVolume.</summary>
        private float AlarmLevel
        {
            get { return Mathf.Clamp01(GameSettings.SHIP_VOLUME * alarmVolume); }
        }

        private void Play(string name, float vol = 1f)
        {
            AudioClip c;
            if (!CanPlay || !clips.TryGetValue(name, out c) || c == null) return;
            fx.PlayOneShot(c, Mathf.Clamp01(vol * (alarmClips.Contains(name) ? AlarmLevel : Volume)));
        }

        // ---------- Monitoring ----------

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            UpdateServo();

            Vessel v = FlightGlobals.ActiveVessel;
            if (v != lastVessel)
            {
                lastVessel = v;
                primed = false; // stay quiet when switching vessels
                emitters.Clear();
                sweepPeriods.Clear();
                SetBDAMute(null, null); // the previous vessel gets its audio back
            }
            if (v == null || Time.time < nextPoll) return;
            nextPoll = Time.time + 0.05f;

            MissileFire wm = ctx.GetWM();
            if (wm == null) { primed = false; SetRwrLoop(null); SetBDAMute(null, null); return; }

            try { Poll(v, wm); }
            catch (Exception e) { Debug.LogWarning("[BDArmoryCommandRPM] Audio: " + e.Message); }
        }

        private void UpdateServo()
        {
            bool on = servoWanted && Time.time < servoUntil && CanPlay;
            if (on)
            {
                AudioClip c;
                if (!loop.isPlaying && clips.TryGetValue("servo", out c))
                {
                    loop.clip = c;
                    loop.Play();
                }
                loop.volume = 0.35f * Volume;
            }
            else if (loop.isPlaying)
            {
                loop.Stop();
                servoWanted = false;
            }
        }

        private void Poll(Vessel v, MissileFire wm)
        {
            VesselRadarData vrd = ctx.GetVRD(wm);
            UpdateRWR(v, wm);

            // Radar
            int enabled = 0;
            float sweepPeriod = 0f;
            List<ModuleRadar> radars = wm.radars;
            if (radars != null)
            {
                for (int i = 0; i < radars.Count; i++)
                {
                    ModuleRadar r = radars[i];
                    if (r == null || !r.radarEnabled) continue;
                    enabled++;
                    if (!radarSweep) continue;
                    float period;
                    if (!sweepPeriods.TryGetValue(r, out period))
                    {
                        float speed = Mathf.Max(1f, r.scanRotationSpeed);
                        float fov;
                        period = r.omnidirectional || !float.TryParse(r.directionalFieldOfView,
                            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fov)
                            ? 360f / speed : 2f * fov / speed;
                        sweepPeriods[r] = period;
                    }
                    if (sweepPeriod <= 0f || period < sweepPeriod) sweepPeriod = period;
                }
            }
            bool radarOn = enabled > 0;
            int contacts = MFDContext.RadarContactCount(vrd);
            int locks = vrd != null ? vrd.numLockedTargets : 0;

            bool guard = wm.guardMode;
            IBDAIControl ai = wm.AI;
            bool aiOn = ai != null && ai.pilotEnabled;

            bool jam = false, cloak = false;
            if (wm.jammers != null)
                foreach (ModuleECMJammer j in wm.jammers) if (j != null && !j.isMissileECM && j.jammerEnabled) jam = true;
            if (wm.cloaks != null)
                foreach (ModuleCloakingDevice c in wm.cloaks) if (c != null && c.cloakEnabled) cloak = true;

            int hpBand = 0;
            if (wm.totalHP > 0)
            {
                float frac = wm.currentHP / wm.totalHP;
                hpBand = frac <= warningHP ? 2 : (frac <= cautionHP ? 1 : 0);
            }

            bool tgpLock = false;
            ModuleTargetingCamera cam = ModuleTargetingCamera.activeCam;
            if (cam != null && cam.vessel == v && cam.cameraEnabled) tgpLock = cam.groundStabilized;

            if (!primed)
            {
                // First read: store the state without playing anything
                lastRadarOn = radarOn; lastContacts = contacts; lastLocks = locks; lastGuard = guard; lastAI = aiOn;
                lastJam = jam; lastCloak = cloak; lastHPBand = hpBand; lastTgpLock = tgpLock;
                CheckCountermeasures(v, true);
                primed = true;
                return;
            }

            if (radarOn != lastRadarOn) Play(radarOn ? "radarOn" : "radarOff");

            if (radarOn && radarSweep && sweepPeriod > 0f && Time.time >= nextSweep)
            {
                nextSweep = Time.time + Mathf.Clamp(sweepPeriod, 0.5f, 10f);
                Play("sweep", sweepVolume);
            }

            if (locks > lastLocks) Play("lock");
            else if (locks < lastLocks) Play(Time.time - userUnlockTime < 1f ? "unlock" : "lockLost");
            else if (contacts > lastContacts && Time.time >= nextContactBlip)
            {
                nextContactBlip = Time.time + 0.3f;
                Play("contact");
            }

            if (guard != lastGuard) Play(guard ? "guardOn" : "guardOff");
            if (aiOn != lastAI) Play(aiOn ? "apEngage" : "apDisconnect");
            if (jam != lastJam || cloak != lastCloak) Play((jam && !lastJam) || (cloak && !lastCloak) ? "ewOn" : "ewOff");

            if (hpBand > lastHPBand) Play(hpBand == 2 ? "warning" : "caution");

            if (tgpLock && !lastTgpLock) Play("tgpLock");

            if (Time.time >= nextCMCheck)
            {
                nextCMCheck = Time.time + 1f; // walking every part's resources: once per second is enough
                CheckCountermeasures(v, false);
            }

            lastRadarOn = radarOn; lastContacts = contacts; lastLocks = locks; lastGuard = guard; lastAI = aiOn;
            lastJam = jam; lastCloak = cloak; lastHPBand = hpBand; lastTgpLock = tgpLock;
        }

        // ---------- F/A-18 style RWR ----------

        private static bool IsLaunch(string t)
        {
            return t == "MissileLaunch" || t == "MWS" || t == "Torpedo";
        }

        private static bool IsLock(string t)
        {
            return t == "MissileLock" || t == "TorpedoLock";
        }

        private static bool IsAirborne(string t)
        {
            return t == "Fighter" || t == "AWACS";
        }

        private void UpdateRWR(Vessel v, MissileFire wm)
        {
            RadarWarningReceiver rwr = ctx.GetRWR(wm);

            // Mute BDArmory's own audio only when our tones can play
            // (otherwise, outside the cockpit with onlyInIVA, no warning would be heard)
            bool ours = CanPlay;
            AudioSource bdaRwr = rwr != null && fRwrAudio != null ? fRwrAudio.GetValue(rwr) as AudioSource : null;
            bool muteWarning = ours && hornetRWR && muteBDAMissileWarning;
            SetBDAMute(ours && hornetRWR ? bdaRwr : null, muteWarning ? wm.warningAudioSource : null);

            if (!hornetRWR || rwr == null || !rwr.rwrEnabled)
            {
                // without an RWR, BDArmory's incoming missile warning is still replaced by the launch tone
                SetRwrLoop(muteWarning && wm.missileIsIncoming ? "rwrLaunch" : null);
                emitters.Clear();
                return;
            }

            MFDContext.ReadRwrPings(rwr, pings);
            bool launch = false;
            bool stt = false;
            string newTone = null;

            for (int i = 0; i < pings.Count; i++)
            {
                MFDContext.RwrPing p = pings[i];
                if (IsLaunch(p.type)) launch = true;
                else if (IsLock(p.type)) stt = true;

                if (p.type == "Jamming") continue;
                // key: emitting vessel (or 20° sector if unknown) + air/surface
                long id = p.source != null ? (long)p.source.persistentId
                    : (1L << 40) + Mathf.RoundToInt(MFDContext.RelativeBearing(MFDContext.RelativeHorizontal(v, p.position)) / 20f);
                long key = id * 2 + (IsAirborne(p.type) ? 1 : 0);
                float last;
                bool known = emitters.TryGetValue(key, out last) && Time.time - last < newEmitterMemory;
                emitters[key] = Time.time;
                if (!known && primed && !IsLaunch(p.type))
                    newTone = IsAirborne(p.type) ? "rwrNewAI" : (newTone ?? "rwrNew");
            }

            // forget old emitters
            staleEmitters.Clear();
            foreach (KeyValuePair<long, float> kv in emitters)
                if (Time.time - kv.Value > newEmitterMemory * 3f) staleEmitters.Add(kv.Key);
            for (int i = 0; i < staleEmitters.Count; i++) emitters.Remove(staleEmitters[i]);

            // If BDArmory detects an incoming missile and its warning is muted, play the launch tone
            if (wm.missileIsIncoming && muteWarning) launch = true;

            SetRwrLoop(launch ? "rwrLaunch" : (stt ? "rwrSTT" : null));
            if (newTone != null && rwrLoopClip == null) Play(newTone, rwrVolume);
        }

        /// <summary>Mutes these BDArmory audio sources and unmutes the previous ones.</summary>
        private void SetBDAMute(AudioSource rwrAudio, AudioSource warnAudio)
        {
            if (mutedRwrAudio != rwrAudio)
            {
                if (mutedRwrAudio != null) mutedRwrAudio.mute = false;
                mutedRwrAudio = rwrAudio;
            }
            if (mutedRwrAudio != null && !mutedRwrAudio.mute) mutedRwrAudio.mute = true;

            if (mutedWarnAudio != warnAudio)
            {
                if (mutedWarnAudio != null) mutedWarnAudio.mute = false;
                mutedWarnAudio = warnAudio;
            }
            if (mutedWarnAudio != null && !mutedWarnAudio.mute) mutedWarnAudio.mute = true;
        }

        private void SetRwrLoop(string name)
        {
            if (!CanPlay) name = null;
            if (name == rwrLoopClip)
            {
                if (name != null) rwrLoop.volume = Mathf.Clamp01(rwrVolume * AlarmLevel);
                return;
            }
            rwrLoopClip = name;
            rwrLoop.Stop();
            AudioClip c;
            if (name != null && clips.TryGetValue(name, out c))
            {
                rwrLoop.clip = c;
                rwrLoop.volume = Mathf.Clamp01(rwrVolume * AlarmLevel);
                rwrLoop.Play();
            }
        }

        private void CheckCountermeasures(Vessel v, bool silent)
        {
            CheckCM(v, "CMFlare", ref flareBingo, ref flareEmpty, silent);
            CheckCM(v, "CMChaff", ref chaffBingo, ref chaffEmpty, silent);
        }

        private void CheckCM(Vessel v, string res, ref bool bingo, ref bool empty, bool silent)
        {
            double amount, max;
            MFDContext.ResourceTotals(v, res, out amount, out max);
            if (max <= 0) { bingo = false; empty = false; return; }

            bool nowEmpty = amount < 1;
            bool nowBingo = amount / max <= bingoFraction;
            if (!silent)
            {
                if (nowEmpty && !empty) Play("empty");
                else if (nowBingo && !bingo) Play("bingo");
            }
            bingo = nowBingo;
            empty = nowEmpty;
        }
    }
}
