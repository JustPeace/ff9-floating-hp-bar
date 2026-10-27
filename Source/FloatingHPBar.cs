// FloatingHPBar for FINAL FANTASY IX (Memoria engine)
// Shows a floating HP bar with the enemy's name and a numeric HP readout above
// every enemy in battle. HP is read through the game's own logical-HP helper,
// so vanilla bosses show their real data HP and Alternate Fantasy bosses show
// their logical HP (raw HP - 10 000) automatically.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Assets.Sources.Scripts.UI.Common;
using FF9;
using Memoria;
using Memoria.Assets;
using Memoria.Prime;
using UnityEngine;

namespace FloatingHPBar
{
    /// <summary>
    /// Memoria calls OnBattleInit once per battle, right after all BattleUnits
    /// (and therefore all enemy BTL_DATA) have been created. That is the
    /// earliest safe point to create our overlay and read enemy data.
    /// </summary>
    public sealed class FloatingHPBarBootstrap : IOverloadOnBattleInitScript
    {
        public void OnBattleInit()
        {
            try
            {
                FloatingHPBarOverlay.Ensure();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FloatingHPBar] Failed to initialise the overlay.");
            }

            // Memoria only keeps one IOverloadOnBattleInitScript implementation
            // (the last one processed wins), so the battle info panel cannot use
            // the same hook without disabling this mod. Initialise it through a
            // soft reflection call instead: if BattleInfoOverlay is installed it
            // starts at battle init, and if it is not installed this is a no-op.
            // BattleInfoOverlay also has its own battle-script-end fallback.
            TryInitialiseBattleInfoOverlay();
        }

        private static void TryInitialiseBattleInfoOverlay()
        {
            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (Int32 i = 0; i < assemblies.Length; i++)
                {
                    Assembly assembly = assemblies[i];
                    if (assembly == null || assembly.GetName().Name != "Memoria.Scripts.BattleInfoOverlay")
                        continue;

                    Type panelType = assembly.GetType("BattleInfoOverlay.BattleInfoOverlayPanel", false);
                    if (panelType == null)
                        return;

                    MethodInfo ensure = panelType.GetMethod("Ensure", BindingFlags.Public | BindingFlags.Static);
                    if (ensure != null)
                        ensure.Invoke(null, null);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FloatingHPBar] Could not initialise the Battle Info Overlay (it may not be installed).");
            }
        }
    }

    public sealed class FloatingHPBarOverlay : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // Configuration (FloatingHPBar.ini next to the mod folder)
        // ------------------------------------------------------------------
        private static Boolean s_enabled = true;
        private static Boolean s_showMaxHp = false;
        // 0 = Auto (vanilla: raw HP, Alternate Fantasy: logical HP without the +10 000 padding)
        // 1 = Raw data HP only
        // 2 = Force the old "subtract 10 000 from non-dying bosses" behaviour
        private static Int32 s_hpMode = 0;
        private static Boolean s_showName = true;
        private static Int32 s_nameFontSize = 15;
        private static Int32 s_barWidth = 156;
        private static Int32 s_barHeight = 22;
        private static Boolean s_showOnlyTargetable = false;
        private static Int32 s_offsetY = 8;

        private static FloatingHPBarOverlay s_instance;

        // ------------------------------------------------------------------
        // Runtime state
        // ------------------------------------------------------------------
        private readonly Dictionary<UInt16, UnitBar> _bars = new Dictionary<UInt16, UnitBar>();
        private readonly List<UnitBar> _visible = new List<UnitBar>();
        private readonly List<UnitBar> _scratch = new List<UnitBar>();

        private Int32 _frame;
        private readonly HashSet<UInt16> _loggedEnemies = new HashSet<UInt16>();
        private Boolean _errorLogged;
        private Camera _camera;

        private Single _scale = 1f;
        private Single _barW = 156f;
        private Single _barH = 22f;
        private Int32 _lastScreenHeight = -1;

        private GUIStyle _roundStyle;
        private GUIStyle _fillStyle;
        private GUIStyle _numberStyle;
        private GUIStyle _nameStyle;
        private Texture2D _roundTex;
        private Texture2D _fillTex;
        private Texture2D _solidTex;

        private const Single ReferenceHeight = 1080f;

        // ------------------------------------------------------------------
        // Bootstrap
        // ------------------------------------------------------------------
        public static void Ensure()
        {
            LoadConfig();

            if (s_instance != null)
            {
                s_instance.BeginBattle();
                return;
            }

            GameObject go = new GameObject("FloatingHPBar.Overlay");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            s_instance = go.AddComponent<FloatingHPBarOverlay>();
            s_instance.BeginBattle();
            Log.Message("[FloatingHPBar] Version 1.1 initialised. Enabled={0}, HpMode={1}, ShowMaxHp={2}, ShowName={3}.",
                s_enabled, s_hpMode, s_showMaxHp, s_showName);
        }

        private static void LoadConfig()
        {
            try
            {
                String dllPath = typeof(FloatingHPBarOverlay).Assembly.Location;
                if (String.IsNullOrEmpty(dllPath))
                    return;

                String scriptsDir = Path.GetDirectoryName(dllPath);
                String modDir = Path.GetFullPath(Path.Combine(scriptsDir, "../.."));
                String iniPath = Path.Combine(modDir, "FloatingHPBar.ini");
                if (!File.Exists(iniPath))
                {
                    Log.Message("[FloatingHPBar] No config file found at {0}, using defaults.", iniPath);
                    return;
                }

                foreach (String rawLine in File.ReadAllLines(iniPath))
                {
                    String line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[')
                        continue;

                    Int32 eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    String key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    String value = line.Substring(eq + 1).Trim();
                    if (value.Length == 0)
                        continue;

                    switch (key)
                    {
                        case "enabled":
                            s_enabled = ParseBool(value);
                            break;
                        case "showmaxhp":
                            s_showMaxHp = ParseBool(value);
                            break;
                        case "hpmode":
                            s_hpMode = ClampInt(ParseInt(value, s_hpMode), 0, 2);
                            break;
                        case "subtracthppadding":
                            // Deprecated compatibility key. It used to be on by default and
                            // caused vanilla bosses (Necron, Ozma...) to display 10 000 HP less
                            // than their real data.
                            s_hpMode = ParseBool(value) ? 2 : 1;
                            break;
                        case "showname":
                            s_showName = ParseBool(value);
                            break;
                        case "namefontsize":
                            s_nameFontSize = ClampInt(ParseInt(value, s_nameFontSize), 8, 40);
                            break;
                        case "barwidth":
                            s_barWidth = ClampInt(ParseInt(value, s_barWidth), 80, 400);
                            break;
                        case "barheight":
                            s_barHeight = ClampInt(ParseInt(value, s_barHeight), 12, 60);
                            break;
                        case "showonlytargetable":
                            s_showOnlyTargetable = ParseBool(value);
                            break;
                        case "offsety":
                            s_offsetY = ClampInt(ParseInt(value, s_offsetY), -200, 200);
                            break;
                    }
                }

                Log.Message("[FloatingHPBar] Config loaded from {0}.", iniPath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FloatingHPBar] Failed to read FloatingHPBar.ini; using defaults.");
            }
        }

        private static Boolean ParseBool(String value)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                default:
                    return false;
            }
        }

        private static Int32 ParseInt(String value, Int32 fallback)
        {
            Int32 result;
            if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                return result;
            return fallback;
        }

        private static Int32 ClampInt(Int32 value, Int32 min, Int32 max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void BeginBattle()
        {
            _bars.Clear();
            _visible.Clear();
            _loggedEnemies.Clear();
            _camera = null;
            _lastScreenHeight = -1;
            _frame++;
        }

        // ------------------------------------------------------------------
        // Per-frame data collection
        // ------------------------------------------------------------------
        private void Update()
        {
            if (_errorLogged)
                return;

            try
            {
                UpdateBars();
            }
            catch (Exception ex)
            {
                _errorLogged = true;
                Log.Error(ex, "[FloatingHPBar] Update failed. The overlay is now disabled for this session.");
            }
        }

        private void UpdateBars()
        {
            if (!s_enabled)
                return;

            if (Screen.height != _lastScreenHeight)
                RebuildStyles();

            FF9StateBattleSystem bs = FF9StateSystem.Battle != null ? FF9StateSystem.Battle.FF9Battle : null;
            if (bs == null)
            {
                FadeAwayAll();
                return;
            }

            // Show only when the battle actually accepts commands.
            Boolean battleVisible = bs.btl_phase == FF9StateBattleSystem.PHASE_MENU_ON
                                 || bs.btl_phase == FF9StateBattleSystem.PHASE_NORMAL
                                 || bs.btl_phase == FF9StateBattleSystem.PHASE_MENU_OFF;

            Camera cam = GetBattleCamera();
            if (cam == null)
            {
                FadeAwayAll();
                return;
            }

            Single dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.05f);
            if (dt <= 0f)
                dt = 0.016f;

            _frame++;
            _visible.Clear();

            for (BTL_DATA b = bs.btl_list.next; b != null; b = b.next)
            {
                if (b.bi.player != 0 || b.bi.slave != 0)
                    continue;
                if (b.gameObject == null)
                    continue;
                if (s_showOnlyTargetable && b.bi.target == 0)
                    continue;

                UnitBar bar;
                if (!_bars.TryGetValue(b.btl_id, out bar))
                {
                    bar = new UnitBar();
                    bar.Id = b.btl_id;
                    bar.FirstSeen = true;
                    _bars.Add(b.btl_id, bar);
                }

                bar.LastFrame = _frame;

                UInt32 rawCur = b.cur.hp;
                UInt32 rawMax = b.max.hp;
                UInt32 cur, max;
                Boolean adjusted;
                GetDisplayHp(b, rawCur, rawMax, out cur, out max, out adjusted);
                bar.Name = GetEnemyName(b);

                if (bar.FirstSeen)
                {
                    bar.DisplayHp = cur;
                    bar.TrailHp = cur;
                    bar.FirstSeen = false;
                }
                else if (cur < bar.DisplayHp - 0.01f)
                {
                    bar.Flash = 1f;
                    bar.TrailDelay = 0.42f;
                }

                bar.MaxHp = max;
                bar.TargetHp = cur;

                if (_loggedEnemies.Count < 8 && !_loggedEnemies.Contains(b.btl_id))
                {
                    _loggedEnemies.Add(b.btl_id);
                    Log.Message("[FloatingHPBar] Enemy id={0} slot={1} name='{2}' lv={3} raw={4}/{5} displayed={6}/{7} mode={8} adjusted={9} battle={10}.",
                        b.btl_id, b.bi.slot_no, bar.Name, b.level, rawCur, rawMax, cur, max, s_hpMode, adjusted,
                        FF9StateSystem.Battle != null ? FF9StateSystem.Battle.battleMapIndex : -1);
                }

                // Smoothly animate the visual fill towards the real HP value.
                Single speed = 1f - Mathf.Exp(-16f * dt);
                bar.DisplayHp = Mathf.Lerp(bar.DisplayHp, cur, speed);
                if (Mathf.Abs(bar.DisplayHp - cur) < 0.35f)
                    bar.DisplayHp = cur;

                // The pale damage trail stays for a moment, then drains.
                if (cur >= bar.TrailHp)
                {
                    bar.TrailHp = cur;
                    bar.TrailDelay = 0f;
                }
                else
                {
                    if (bar.TrailDelay > 0f)
                        bar.TrailDelay -= dt;
                    else
                        bar.TrailHp = Mathf.MoveTowards(bar.TrailHp, cur, Mathf.Max(1f, max * 0.85f) * dt);
                }

                if (bar.TrailHp < bar.DisplayHp)
                    bar.TrailHp = bar.DisplayHp;

                bar.Flash = Mathf.Max(0f, bar.Flash - dt * 4.5f);

                Boolean alive = b.bi.disappear == 0 && rawCur > 0 && b.gameObject.activeInHierarchy;
                if (battleVisible && alive)
                {
                    Vector3 worldPos = b.gameObject.transform.position;
                    if (b.height > 0)
                        worldPos.y += b.height;

                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                    bar.Depth = screenPos.z;
                    bar.HasScreen = screenPos.z > 0.01f
                                    && screenPos.x > -Screen.width
                                    && screenPos.x < Screen.width * 2f
                                    && screenPos.y > -Screen.height
                                    && screenPos.y < Screen.height * 2f;
                    bar.ScreenX = screenPos.x;
                    bar.ScreenY = Screen.height - screenPos.y;
                    bar.Alive = true;
                    bar.Alpha = Mathf.MoveTowards(bar.Alpha, 1f, dt * 9f);
                }
                else
                {
                    bar.Alive = false;
                    bar.Alpha = Mathf.MoveTowards(bar.Alpha, 0f, dt * (alive ? 4f : 7f));
                }

                if (bar.Alpha > 0.005f && bar.HasScreen)
                    _visible.Add(bar);
            }

            // Units that are no longer in btl_list fade out and are cleaned up.
            foreach (UnitBar bar in _bars.Values)
            {
                if (bar.LastFrame == _frame)
                    continue;
                bar.Alive = false;
                bar.Alpha = Mathf.MoveTowards(bar.Alpha, 0f, dt * 6f);
                if (bar.Alpha > 0.005f && bar.HasScreen)
                    _visible.Add(bar);
            }

            // Draw far units first so near units overlap them correctly.
            _visible.Sort(CompareDepth);

            if (_bars.Count > 0 && _frame % 90 == 0)
                TrimDeadBars();
        }

        private static Int32 CompareDepth(UnitBar a, UnitBar b)
        {
            if (a.Depth > b.Depth) return -1;
            if (a.Depth < b.Depth) return 1;
            return 0;
        }

        private void TrimDeadBars()
        {
            _scratch.Clear();
            foreach (KeyValuePair<UInt16, UnitBar> pair in _bars)
                _scratch.Add(pair.Value);

            foreach (UnitBar bar in _scratch)
            {
                if (!bar.Alive && bar.Alpha <= 0.005f)
                    _bars.Remove(bar.Id);
            }
        }

        private void FadeAwayAll()
        {
            Single dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.05f);
            _visible.Clear();
            foreach (UnitBar bar in _bars.Values)
            {
                bar.Alive = false;
                bar.Alpha = Mathf.MoveTowards(bar.Alpha, 0f, dt * 6f);
                if (bar.Alpha > 0.005f && bar.HasScreen)
                    _visible.Add(bar);
            }
        }

        private Camera GetBattleCamera()
        {
            if (_camera != null && _camera.gameObject.activeInHierarchy)
                return _camera;

            _camera = Camera.main;
            if (_camera == null)
            {
                GameObject go = GameObject.Find("Battle Camera");
                if (go != null)
                    _camera = go.GetComponent<Camera>();
            }
            return _camera;
        }

        // ------------------------------------------------------------------
        // HP calculation (including the 10 000 HP "padding" used by bosses)
        // ------------------------------------------------------------------
        private static void GetDisplayHp(BTL_DATA b, UInt32 rawCur, UInt32 rawMax, out UInt32 cur, out UInt32 max, out Boolean adjusted)
        {
            cur = rawCur;
            max = rawMax;
            adjusted = false;

            if (b.bi.player != 0)
                return;

            try
            {
                if (s_hpMode == 1)
                    return;

                if (s_hpMode == 2)
                {
                    // Legacy behaviour: subtract 10 000 from every "non-dying" vanilla boss.
                    // This is not always the real padding (Necron has 65 535 real HP, for
                    // example), so it is no longer the default.
                    if (btl_para.IsNonDyingVanillaBoss(b) && rawMax > 10000)
                    {
                        adjusted = true;
                        max = rawMax - 10000;
                        cur = rawCur > 10000 ? rawCur - 10000 : 0;
                        if (cur > max)
                            cur = max;
                    }
                    return;
                }

                // Auto: use the game's own logical HP. In vanilla this is exactly the
                // enemy's data HP (the 10 000 script trigger is already accounted for by
                // the game). In Alternate Fantasy mode Memoria removes the +10 000
                // padding only for enemies flagged by their AI script.
                UInt32 logicalCur = btl_para.GetLogicalHP(b, false);
                UInt32 logicalMax = btl_para.GetLogicalHP(b, true);
                if (logicalMax != rawMax || logicalCur != rawCur)
                    adjusted = true;
                if (logicalMax > 0)
                    max = logicalMax;
                cur = logicalCur;
                if (cur > max)
                    cur = max;
            }
            catch (Exception)
            {
                // Keep raw values if the game data is not ready for some reason.
            }
        }

        private static String GetEnemyName(BTL_DATA b)
        {
            try
            {
                if (Localization.UseSecondaryLanguage && b.typeNo != Byte.MaxValue)
                {
                    String localized = FF9TextTool.BattleText(b.typeNo);
                    if (!String.IsNullOrEmpty(localized))
                        return localized;
                }
            }
            catch (Exception)
            {
                // Fall through to the battle-scene enemy name.
            }

            try
            {
                ENEMY enemy = btl_util.getEnemyPtr(b);
                if (enemy != null && enemy.et != null && !String.IsNullOrEmpty(enemy.et.name))
                    return enemy.et.name;
            }
            catch (Exception)
            {
                // Keep the bar without a name if the enemy data is unavailable.
            }

            return String.Empty;
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------
        private void OnGUI()
        {
            if (!s_enabled || _visible.Count == 0)
                return;
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            if (_roundStyle == null || Screen.height != _lastScreenHeight)
                RebuildStyles();

            Color oldColor = GUI.color;
            Int32 oldDepth = GUI.depth;
            Matrix4x4 oldMatrix = GUI.matrix;

            try
            {
                GUI.depth = -100;

                for (Int32 i = 0; i < _visible.Count; i++)
                    DrawBar(_visible[i]);
            }
            catch (Exception ex)
            {
                _errorLogged = true;
                Log.Error(ex, "[FloatingHPBar] OnGUI failed. The overlay is now disabled for this session.");
            }
            finally
            {
                GUI.color = oldColor;
                GUI.depth = oldDepth;
                GUI.matrix = oldMatrix;
            }
        }

        private void DrawBar(UnitBar bar)
        {
            Single scale = _scale;
            Single w = _barW;
            Single h = _barH;
            Single x = bar.ScreenX - w * 0.5f;
            Single y = bar.ScreenY - h - s_offsetY * scale;

            if (x > Screen.width || x + w < 0f || y > Screen.height || y + h < 0f)
                return;

            Single a = bar.Alpha;
            if (a <= 0.005f)
                return;

            Single ratio = bar.MaxHp > 0 ? Mathf.Clamp01(bar.DisplayHp / bar.MaxHp) : 0f;
            Single trailRatio = bar.MaxHp > 0 ? Mathf.Clamp01(bar.TrailHp / bar.MaxHp) : 0f;
            Single targetRatio = bar.MaxHp > 0 ? Mathf.Clamp01(bar.TargetHp / bar.MaxHp) : 0f;

            Color hpColor = HpColor(targetRatio);
            Single lowPulse = targetRatio < 0.25f
                ? 0.72f + 0.28f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f))
                : 1f;

            Rect barRect = new Rect(x, y, w, h);
            Rect shadowRect = new Rect(x + 0.5f * scale, y + 2.5f * scale, w, h);
            Rect borderRect = new Rect(x - 1.5f * scale, y - 1.5f * scale, w + 3f * scale, h + 3f * scale);

            // Soft drop shadow.
            GUI.color = new Color(0f, 0f, 0f, 0.42f * a);
            GUI.Box(shadowRect, GUIContent.none, _roundStyle);

            // Thin gold border; turns red-ish when the enemy is close to death.
            Color borderColor = targetRatio < 0.25f
                ? Color.Lerp(new Color(0.94f, 0.84f, 0.57f), hpColor, 0.65f)
                : new Color(0.94f, 0.84f, 0.57f);
            GUI.color = new Color(borderColor.r, borderColor.g, borderColor.b, 0.72f * a * lowPulse);
            GUI.Box(borderRect, GUIContent.none, _roundStyle);

            // Dark glass background.
            GUI.color = new Color(0.035f, 0.032f, 0.045f, 0.88f * a);
            GUI.Box(barRect, GUIContent.none, _roundStyle);

            Single pad = 2.4f * scale;
            Rect inner = new Rect(x + pad, y + pad, w - pad * 2f, h - pad * 2f);

            // Pale damage trail behind the fill.
            if (trailRatio > ratio && trailRatio > 0.001f)
                DrawFill(inner, trailRatio, new Color(1f, 0.94f, 0.78f, 0.78f * a));

            // Main bar (solid colour + glossy top edge).
            if (ratio > 0.001f)
            {
                DrawFill(inner, ratio, new Color(hpColor.r, hpColor.g, hpColor.b, 0.98f * a));

                Rect gloss = new Rect(inner.x, inner.y, inner.width * ratio, inner.height * 0.46f);
                if (gloss.width > 2f)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.26f * a);
                    GUI.Box(gloss, GUIContent.none, _fillStyle);
                }

                Rect bottom = new Rect(inner.x, inner.y + inner.height * 0.70f, inner.width * ratio, inner.height * 0.30f);
                if (bottom.width > 2f)
                {
                    GUI.color = new Color(0f, 0f, 0f, 0.16f * a);
                    GUI.Box(bottom, GUIContent.none, _fillStyle);
                }
            }

            // Damage flash.
            if (bar.Flash > 0.01f)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.30f * bar.Flash * a);
                GUI.Box(inner, GUIContent.none, _fillStyle);
            }

            // 25 % / 50 % / 75 % tick marks.
            GUI.color = new Color(0f, 0f, 0f, 0.30f * a);
            Single tickHeight = inner.height * 0.52f;
            for (Int32 i = 1; i <= 3; i++)
            {
                Single tx = inner.x + inner.width * (i * 0.25f);
                GUI.DrawTexture(new Rect(tx, inner.y + (inner.height - tickHeight) * 0.5f, Mathf.Max(1f, scale), tickHeight), _solidTex);
            }

            // Number inside the bar.
            String text;
            Int32 fontSize = Mathf.Max(8, Mathf.RoundToInt(_barH * 0.68f));
            UInt32 displayCurrent = (UInt32)Mathf.RoundToInt(Mathf.Max(0f, bar.TargetHp));
            if (s_showMaxHp)
            {
                text = displayCurrent.ToString(CultureInfo.InvariantCulture) + " / " + ((UInt32)Mathf.RoundToInt(bar.MaxHp)).ToString(CultureInfo.InvariantCulture);
                fontSize = Mathf.Max(8, Mathf.RoundToInt(fontSize * 0.82f));
            }
            else
            {
                text = displayCurrent.ToString(CultureInfo.InvariantCulture);
            }

            if (text.Length > 5)
                fontSize = Mathf.Max(8, Mathf.RoundToInt(fontSize * (5f / text.Length)));

            DrawText(barRect, text, Color.white, a, fontSize);

            DrawNamePlate(bar, x, y, w, h);
        }

        private void DrawNamePlate(UnitBar bar, Single barX, Single barY, Single barW, Single barH)
        {
            if (!s_showName || String.IsNullOrEmpty(bar.Name))
                return;

            Single a = bar.Alpha;
            if (a <= 0.005f)
                return;

            Single scale = _scale;
            Int32 fontSize = Mathf.Max(9, Mathf.RoundToInt(s_nameFontSize * scale));
            _nameStyle.fontSize = fontSize;
            Vector2 textSize = _nameStyle.CalcSize(new GUIContent(bar.Name));

            Single maxTextWidth = Mathf.Min(Screen.width * 0.55f, Mathf.Max(barW, 260f * scale));
            while (textSize.x > maxTextWidth && fontSize > 8)
            {
                fontSize--;
                _nameStyle.fontSize = fontSize;
                textSize = _nameStyle.CalcSize(new GUIContent(bar.Name));
            }

            Single plateW = Mathf.Max(barW, textSize.x + 14f * scale);
            Single plateH = Mathf.Max(barH * 0.82f, textSize.y + 6f * scale);
            Single px = barX + barW * 0.5f - plateW * 0.5f;
            Single py = barY - plateH - 3f * scale;

            if (px < 0f)
                px = 0f;
            if (px + plateW > Screen.width)
                px = Screen.width - plateW;
            if (py < 0f)
                py = 0f;

            GUI.color = new Color(0f, 0f, 0f, 0.50f * a);
            GUI.Box(new Rect(px + 0.5f * scale, py + 1.5f * scale, plateW, plateH), GUIContent.none, _roundStyle);

            GUI.color = new Color(0.94f, 0.84f, 0.57f, 0.55f * a);
            GUI.Box(new Rect(px - 1f * scale, py - 1f * scale, plateW + 2f * scale, plateH + 2f * scale), GUIContent.none, _roundStyle);

            GUI.color = new Color(0.035f, 0.032f, 0.045f, 0.78f * a);
            GUI.Box(new Rect(px, py, plateW, plateH), GUIContent.none, _roundStyle);

            DrawText(new Rect(px, py, plateW, plateH), bar.Name, new Color(0.98f, 0.95f, 0.88f), a, fontSize, _nameStyle);
        }

        private void DrawFill(Rect inner, Single ratio, Color color)
        {
            Single width = inner.width * Mathf.Clamp01(ratio);
            if (width <= 0.5f)
                return;

            Rect rect = new Rect(inner.x, inner.y, width, inner.height);
            if (width < 14f * _scale)
            {
                GUI.color = color;
                GUI.DrawTexture(rect, _solidTex);
            }
            else
            {
                GUI.color = color;
                GUI.Box(rect, GUIContent.none, _fillStyle);
            }
        }

        private void DrawText(Rect rect, String text, Color color, Single alpha, Int32 fontSize)
        {
            DrawText(rect, text, color, alpha, fontSize, _numberStyle);
        }

        private void DrawText(Rect rect, String text, Color color, Single alpha, Int32 fontSize, GUIStyle style)
        {
            // Reset the tint inherited from the bar/ticks; otherwise the labels
            // would be multiplied by the last GUI.color (usually dark).
            GUI.color = Color.white;

            style.fontSize = fontSize;

            Single outline = Mathf.Max(1f, Mathf.Round(1.15f * _scale));
            style.normal.textColor = new Color(0f, 0f, 0f, 0.9f * alpha);

            GUI.Label(new Rect(rect.x - outline, rect.y, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x + outline, rect.y, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x, rect.y - outline, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x, rect.y + outline, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x - outline, rect.y - outline, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x + outline, rect.y + outline, rect.width, rect.height), text, style);

            style.normal.textColor = new Color(color.r, color.g, color.b, alpha);
            GUI.Label(rect, text, style);
        }

        private void RebuildStyles()
        {
            _lastScreenHeight = Screen.height;
            _scale = Mathf.Clamp(Screen.height / ReferenceHeight, 0.78f, 1.7f);
            _barW = s_barWidth * _scale;
            _barH = s_barHeight * _scale;

            if (_roundTex == null)
                _roundTex = CreateRoundedTexture(48, 12);
            if (_fillTex == null)
                _fillTex = CreateRoundedTexture(48, 9);
            if (_solidTex == null)
                _solidTex = CreateSolidTexture();

            if (_roundStyle == null)
            {
                _roundStyle = new GUIStyle();
                _roundStyle.normal.background = _roundTex;
                _roundStyle.border = new RectOffset(12, 12, 12, 12);
            }

            if (_fillStyle == null)
            {
                _fillStyle = new GUIStyle();
                _fillStyle.normal.background = _fillTex;
                _fillStyle.border = new RectOffset(9, 9, 9, 9);
            }

            if (_numberStyle == null)
            {
                _numberStyle = new GUIStyle();
                _numberStyle.alignment = TextAnchor.MiddleCenter;
                _numberStyle.clipping = TextClipping.Overflow;
                _numberStyle.wordWrap = false;
                _numberStyle.richText = false;
                _numberStyle.fontStyle = FontStyle.Bold;
            }

            if (_nameStyle == null)
            {
                _nameStyle = new GUIStyle();
                _nameStyle.alignment = TextAnchor.MiddleCenter;
                _nameStyle.clipping = TextClipping.Overflow;
                _nameStyle.wordWrap = false;
                _nameStyle.richText = false;
                _nameStyle.fontStyle = FontStyle.Bold;
            }

            Int32 fontSize = Mathf.Max(10, Mathf.RoundToInt(_barH * 0.68f));
            if (_numberStyle.font == null)
            {
                try
                {
                    // Include CJK-capable system fonts first so translated enemy names
                    // (Chinese in the FF9 Chinese mod) render correctly.
                    Font osFont = Font.CreateDynamicFontFromOSFont(
                        new String[]
                        {
                            "Microsoft YaHei UI", "Microsoft YaHei",
                            "Hiragino Sans GB Std Bold", "Hiragino Sans GB",
                            "PingFang SC", "Heiti SC", "STHeiti",
                            "SimHei", "SimSun", "Noto Sans CJK SC", "WenQuanYi Micro Hei",
                            "Arial Unicode MS", "Segoe UI Semibold", "Segoe UI", "Arial"
                        }, fontSize);
                    _numberStyle.font = osFont;
                    _nameStyle.font = osFont;
                }
                catch (Exception)
                {
                    _numberStyle.font = null;
                    _nameStyle.font = null;
                }
            }
            else if (_nameStyle.font == null)
            {
                _nameStyle.font = _numberStyle.font;
            }
        }

        private static Texture2D CreateRoundedTexture(Int32 size, Int32 radius)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;

            Color32[] pixels = new Color32[size * size];
            Single half = (size - 1) * 0.5f;
            Single innerHalf = half - radius;

            for (Int32 y = 0; y < size; y++)
            {
                for (Int32 x = 0; x < size; x++)
                {
                    Single qx = Mathf.Abs(x - half) - innerHalf;
                    Single qy = Mathf.Abs(y - half) - innerHalf;
                    Single outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    Single distance = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    Single coverage = Mathf.Clamp01(0.5f - distance);
                    pixels[y * size + x] = new Color32(255, 255, 255, (Byte)Mathf.RoundToInt(coverage * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateSolidTexture()
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, true);
            return texture;
        }

        private static Color HpColor(Single ratio)
        {
            Color low = new Color(0.90f, 0.27f, 0.30f);
            Color mid = new Color(0.96f, 0.72f, 0.24f);
            Color high = new Color(0.31f, 0.83f, 0.53f);

            if (ratio >= 0.5f)
                return Color.Lerp(mid, high, Mathf.Clamp01((ratio - 0.5f) / 0.5f));
            return Color.Lerp(low, mid, Mathf.Clamp01(ratio / 0.5f));
        }

        // ------------------------------------------------------------------
        private sealed class UnitBar
        {
            public UInt16 Id;
            public String Name;
            public Int32 LastFrame;
            public Boolean FirstSeen;
            public Boolean Alive;
            public Boolean HasScreen;
            public Single ScreenX;
            public Single ScreenY;
            public Single Depth;
            public Single Alpha;
            public Single DisplayHp;
            public Single TrailHp;
            public Single TargetHp;
            public Single MaxHp;
            public Single TrailDelay;
            public Single Flash;
        }
    }
}
