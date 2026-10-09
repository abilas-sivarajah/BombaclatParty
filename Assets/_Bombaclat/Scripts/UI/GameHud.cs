using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bombaclat
{
    /// <summary>
    /// Prototype UI drawn with IMGUI: menu, lobby, in-game HUD and results. Fast to iterate on;
    /// will be swapped for a proper UI Toolkit front end once the game loop is locked in.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        const float RefHeight = 1080f;
        const float BannerDuration = 2.6f;

        struct Popup { public ulong ClientId; public string Text; public Color Color; public float Time; }

        float m_Scale = 1f;
        float m_Width;
        string m_NameInput;
        string m_CodeInput = "";
        string m_AddressInput = "127.0.0.1";
        int m_Rounds = 10;
        bool m_Copied;

        readonly List<(string text, float time)> m_Feed = new();
        readonly List<Popup> m_Popups = new();
        string m_BannerTitle, m_BannerSubtitle;
        float m_BannerTime = -100f;

        bool m_StylesReady;
        GUIStyle m_Title, m_Subtitle, m_H1, m_H2, m_Label, m_Small, m_Panel, m_Row, m_RowActive, m_Button, m_ButtonAlt,
            m_ButtonDanger, m_BigButton, m_Field, m_Center, m_Outline, m_Swatch;
        Texture2D m_Coin, m_Crown, m_White;

        static readonly Color Ink = new(0.10f, 0.09f, 0.14f);
        static readonly Color PanelColor = new(0.07f, 0.08f, 0.15f, 0.86f);
        static readonly Color Yellow = new(1f, 0.80f, 0.16f);
        static readonly Color Muted = new(0.75f, 0.78f, 0.9f);

        void OnEnable()
        {
            BoardGameManager.FeedPosted += OnFeed;
            BoardGameManager.BannerShown += OnBanner;
            BoardGameManager.CoinsChanged += OnCoins;
            BoardGameManager.CrownsChanged += OnCrowns;
        }

        void OnDisable()
        {
            BoardGameManager.FeedPosted -= OnFeed;
            BoardGameManager.BannerShown -= OnBanner;
            BoardGameManager.CoinsChanged -= OnCoins;
            BoardGameManager.CrownsChanged -= OnCrowns;
        }

        void Start()
        {
            // Persist the generated default so the name we register with matches the one shown.
            m_NameInput = LocalProfile.Name;
            LocalProfile.Name = m_NameInput;
        }

        void OnFeed(string text)
        {
            m_Feed.Add((text, Time.time));
            if (m_Feed.Count > 7) m_Feed.RemoveAt(0);
        }

        void OnBanner(string title, string subtitle)
        {
            m_BannerTitle = title;
            m_BannerSubtitle = subtitle;
            m_BannerTime = Time.time;
        }

        void OnCoins(ulong clientId, int delta) => m_Popups.Add(new Popup
        {
            ClientId = clientId, Text = delta > 0 ? $"+{delta}" : delta.ToString(),
            Color = delta > 0 ? Yellow : new Color(1f, 0.35f, 0.35f), Time = Time.time,
        });

        void OnCrowns(ulong clientId, int delta) => m_Popups.Add(new Popup
        {
            ClientId = clientId, Text = delta > 0 ? "+1 KRONE" : "-1 KRONE",
            Color = delta > 0 ? new Color(1f, 0.92f, 0.4f) : new Color(1f, 0.35f, 0.35f), Time = Time.time + 0.15f,
        });

        void Update()
        {
            var gm = BoardGameManager.Instance;
            var keyboard = Keyboard.current;
            if (gm != null && gm.IsMyTurn && keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
                gm.RollDiceRpc();
            m_Popups.RemoveAll(p => Time.time - p.Time > 1.6f);
        }

        // ------------------------------------------------------------------ main

        void OnGUI()
        {
            EnsureStyles();
            m_Scale = Screen.height / RefHeight;
            m_Width = Screen.width / m_Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(m_Scale, m_Scale, 1f));

            var nm = NetworkManager.Singleton;
            var cm = ConnectionManager.Instance;
            var gm = BoardGameManager.Instance;
            bool connected = nm != null && nm.IsListening;

            if (connected && gm != null && gm.IsSpawned) DrawWorldOverlays(gm);

            if (!connected) DrawMenu(cm);
            else if (gm == null || !gm.IsSpawned) DrawConnecting(cm);
            else
            {
                switch (gm.Phase.Value)
                {
                    case GamePhase.Lobby: DrawLobby(gm, cm, nm.IsServer); break;
                    case GamePhase.Playing: DrawPlaying(gm, cm); break;
                    case GamePhase.GameOver: DrawResults(gm, cm, nm.IsServer); break;
                }
                DrawFeed();
            }
            DrawBanner();
        }

        // ------------------------------------------------------------------ screens

        void DrawMenu(ConnectionManager cm)
        {
            float w = 640f, h = 700f;
            var area = new Rect((m_Width - w) / 2f, (RefHeight - h) / 2f + 40f, w, h);

            Shadowed(new Rect(0f, 70f, m_Width, 120f), "BOMBACLAT PARTY", m_Title, Yellow);
            Shadowed(new Rect(0f, 175f, m_Width, 40f), "Würfeln. Sabotieren. Verraten.", m_Subtitle, Color.white);

            GUI.Box(area, GUIContent.none, m_Panel);
            GUILayout.BeginArea(area.Inset(36f));
            bool busy = cm == null || cm.Busy;
            GUI.enabled = !busy;

            GUILayout.Label("Dein Name", m_H2);
            string newName = GUILayout.TextField(m_NameInput ?? "", LocalProfile.MaxNameLength, m_Field, GUILayout.Height(56f));
            if (newName != m_NameInput) { m_NameInput = newName; LocalProfile.Name = newName; }

            GUILayout.Space(22f);
            GUILayout.Label("Online mit Kumpels", m_H2);
            if (GUILayout.Button("Spiel hosten", m_Button, GUILayout.Height(62f))) cm.HostOnline();
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            m_CodeInput = GUILayout.TextField(m_CodeInput, 12, m_Field, GUILayout.Height(58f), GUILayout.Width(300f)).ToUpperInvariant();
            if (GUILayout.Button("Mit Code beitreten", m_Button, GUILayout.Height(58f))) cm.JoinOnline(m_CodeInput);
            GUILayout.EndHorizontal();

            GUILayout.Space(22f);
            GUILayout.Label("Lokal / LAN (zum Testen)", m_H2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Lokal hosten", m_ButtonAlt, GUILayout.Height(54f), GUILayout.Width(220f))) cm.HostLocal();
            m_AddressInput = GUILayout.TextField(m_AddressInput, 40, m_Field, GUILayout.Height(54f), GUILayout.Width(170f));
            if (GUILayout.Button("Beitreten", m_ButtonAlt, GUILayout.Height(54f))) cm.JoinLocal(m_AddressInput);
            GUILayout.EndHorizontal();

            GUI.enabled = true;
            GUILayout.Space(14f);
            string status = cm == null ? "" : cm.Status;
            if (cm != null && !cm.CloudLinked && string.IsNullOrEmpty(status))
                status = "Hinweis: Online braucht noch ein verknüpftes Unity-Cloud-Projekt.";
            GUILayout.Label(status, m_Small);
            GUILayout.EndArea();
        }

        void DrawConnecting(ConnectionManager cm)
        {
            var area = Centered(520f, 220f);
            GUI.Box(area, GUIContent.none, m_Panel);
            GUILayout.BeginArea(area.Inset(30f));
            GUILayout.Label("Verbinde" + new string('.', 1 + (int)(Time.time * 2f) % 3), m_H1);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Abbrechen", m_ButtonDanger, GUILayout.Height(54f))) cm.Leave();
            GUILayout.EndArea();
        }

        void DrawLobby(BoardGameManager gm, ConnectionManager cm, bool isHost)
        {
            var area = Centered(620f, 760f);
            GUI.Box(area, GUIContent.none, m_Panel);
            GUILayout.BeginArea(area.Inset(34f));

            GUILayout.Label("Lobby", m_H1);
            string code = cm.JoinCode;
            if (!string.IsNullOrEmpty(code))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Join-Code: <color=#FFD02A><b>{code}</b></color>", m_H2, GUILayout.Height(52f));
                if (GUILayout.Button(m_Copied ? "Kopiert!" : "Kopieren", m_ButtonAlt, GUILayout.Width(150f), GUILayout.Height(48f)))
                {
                    GUIUtility.systemCopyBuffer = code;
                    m_Copied = true;
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("Lokales Spiel: Freunde treten per IP bei.", m_Small);
            }

            GUILayout.Space(12f);
            GUILayout.Label($"Spieler ({gm.Players.Count}/{BoardGameManager.MaxPlayers})", m_H2);
            for (int i = 0; i < gm.Players.Count; i++)
            {
                var p = gm.Players[i];
                GUILayout.BeginHorizontal(m_Row, GUILayout.Height(52f));
                Swatch(Palette.Player(p.ColorIndex));
                string tag = p.ClientId == NetworkManager.ServerClientId ? "  <color=#9AA3C8>(Host)</color>" : "";
                string you = p.ClientId == NetworkManager.Singleton.LocalClientId ? "  <color=#FFD02A>(du)</color>" : "";
                GUILayout.Label($"{p.Name}{tag}{you}", m_Label);
                GUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();
            if (isHost)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Runden", m_H2, GUILayout.Width(150f));
                if (GUILayout.Button("-", m_ButtonAlt, GUILayout.Width(56f), GUILayout.Height(50f))) m_Rounds = Mathf.Max(1, m_Rounds - 1);
                GUILayout.Label(m_Rounds.ToString(), m_Center, GUILayout.Width(80f), GUILayout.Height(50f));
                if (GUILayout.Button("+", m_ButtonAlt, GUILayout.Width(56f), GUILayout.Height(50f))) m_Rounds = Mathf.Min(30, m_Rounds + 1);
                GUILayout.EndHorizontal();
                GUILayout.Space(10f);
                if (GUILayout.Button("SPIEL STARTEN", m_BigButton, GUILayout.Height(78f))) gm.StartGameRpc(m_Rounds);
            }
            else
            {
                GUILayout.Label("Warte auf den Host...", m_Label);
            }
            GUILayout.Space(10f);
            if (GUILayout.Button("Verlassen", m_ButtonDanger, GUILayout.Height(48f))) { m_Copied = false; cm.Leave(); }
            GUILayout.EndArea();
        }

        void DrawPlaying(BoardGameManager gm, ConnectionManager cm)
        {
            // Round counter.
            var roundRect = new Rect((m_Width - 360f) / 2f, 20f, 360f, 64f);
            GUI.Box(roundRect, GUIContent.none, m_Panel);
            GUI.Label(roundRect, $"Runde <b>{gm.Round.Value}</b> / {gm.MaxRounds.Value}", m_Center);

            DrawScoreboard(gm);

            // Turn prompt.
            ulong current = gm.CurrentPlayerId;
            int index = gm.IndexOf(current);
            string currentName = index >= 0 ? gm.Players[index].Name.ToString() : "?";
            var bottom = new Rect((m_Width - 520f) / 2f, RefHeight - 150f, 520f, 110f);
            if (gm.IsMyTurn)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.03f;
                var button = bottom.ScaleAround(pulse);
                if (GUI.Button(button, "WÜRFELN!  <size=26>(Leertaste)</size>", m_BigButton)) gm.RollDiceRpc();
            }
            else
            {
                string verb = gm.Stage.Value switch
                {
                    TurnStage.Rolling => "würfelt",
                    TurnStage.Moving => "läuft",
                    TurnStage.Resolving => "...",
                    _ => "ist dran",
                };
                var label = new Rect(bottom.x, bottom.y + 30f, bottom.width, 70f);
                GUI.Box(label, GUIContent.none, m_Panel);
                GUI.Label(label, $"<b>{currentName}</b> {verb}", m_Center);
            }

            if (GUI.Button(new Rect(m_Width - 190f, RefHeight - 76f, 170f, 52f), "Verlassen", m_ButtonDanger)) cm.Leave();
        }

        void DrawScoreboard(BoardGameManager gm)
        {
            int count = gm.Players.Count;
            var area = new Rect(20f, 20f, 380f, 70f + count * 62f);
            GUI.Box(area, GUIContent.none, m_Panel);
            GUILayout.BeginArea(area.Inset(14f));
            GUILayout.Label($"Krone: {BoardGameManager.CrownCost} Coins", m_Small);
            ulong current = gm.CurrentPlayerId;
            for (int i = 0; i < count; i++)
            {
                var p = gm.Players[i];
                bool active = p.ClientId == current;
                GUILayout.BeginHorizontal(active ? m_RowActive : m_Row, GUILayout.Height(54f));
                Swatch(Palette.Player(p.ColorIndex));
                GUILayout.Label(p.Name.ToString(), m_Label, GUILayout.Width(150f));
                Icon(m_Crown);
                GUILayout.Label(p.Crowns.ToString(), m_Label, GUILayout.Width(40f));
                Icon(m_Coin);
                GUILayout.Label(p.Coins.ToString(), m_Label);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        void DrawResults(BoardGameManager gm, ConnectionManager cm, bool isHost)
        {
            var ranking = new List<PlayerData>();
            foreach (var p in gm.Players) ranking.Add(p);
            ranking.Sort((a, b) => a.Crowns != b.Crowns ? b.Crowns.CompareTo(a.Crowns) : b.Coins.CompareTo(a.Coins));

            var area = Centered(640f, 230f + ranking.Count * 64f);
            GUI.Box(area, GUIContent.none, m_Panel);
            GUILayout.BeginArea(area.Inset(30f));
            GUILayout.Label("Ergebnis", m_H1);
            for (int i = 0; i < ranking.Count; i++)
            {
                var p = ranking[i];
                GUILayout.BeginHorizontal(i == 0 ? m_RowActive : m_Row, GUILayout.Height(56f));
                GUILayout.Label($"{i + 1}.", m_H2, GUILayout.Width(50f));
                Swatch(Palette.Player(p.ColorIndex));
                GUILayout.Label(p.Name.ToString(), m_Label, GUILayout.Width(220f));
                Icon(m_Crown);
                GUILayout.Label(p.Crowns.ToString(), m_Label, GUILayout.Width(50f));
                Icon(m_Coin);
                GUILayout.Label(p.Coins.ToString(), m_Label);
                GUILayout.EndHorizontal();
            }
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (isHost && GUILayout.Button("Nochmal!", m_Button, GUILayout.Height(56f))) gm.BackToLobbyRpc();
            if (GUILayout.Button("Verlassen", m_ButtonDanger, GUILayout.Height(56f))) cm.Leave();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ overlays

        void DrawWorldOverlays(BoardGameManager gm)
        {
            var cam = Camera.main;
            var board = BoardView.Instance;
            if (cam == null || board == null) return;

            foreach (var p in gm.Players)
            {
                if (!board.TryGetPawn(p.ClientId, out var pawn)) continue;
                if (!ToGui(cam, pawn.HeadPosition, out var pos)) continue;

                Shadowed(new Rect(pos.x - 120f, pos.y - 10f, 240f, 36f), p.Name.ToString(), m_Outline, Palette.Player(p.ColorIndex));

                // Dice: spinning numbers, then the result counting down while the pawn walks.
                if (pawn.DieVisible && ToGui(cam, pawn.transform.position + Vector3.up * 2.35f, out var diePos))
                {
                    bool rolling = Time.time < pawn.DieRollingUntil;
                    int shown = rolling ? 1 + (int)(Time.time * 17f) % 10 : pawn.IsMoving ? pawn.StepsLeft : pawn.DieValue;
                    var rect = new Rect(diePos.x - 40f, diePos.y - 40f, 80f, 80f);
                    Shadowed(rect, shown.ToString(), m_Title, rolling ? Color.white : Yellow, 72);
                }
            }

            foreach (var popup in m_Popups)
            {
                float age = Time.time - popup.Time;
                if (age < 0f || !board.TryGetPawn(popup.ClientId, out var pawn)) continue;
                if (!ToGui(cam, pawn.HeadPosition + Vector3.up * (0.5f + age * 1.1f), out var pos)) continue;
                var color = popup.Color;
                color.a = Mathf.Clamp01(1.6f - age);
                Shadowed(new Rect(pos.x - 120f, pos.y - 30f, 240f, 60f), popup.Text, m_H1, color);
            }
        }

        void DrawFeed()
        {
            float y = RefHeight - 60f;
            for (int i = m_Feed.Count - 1; i >= 0; i--)
            {
                float age = Time.time - m_Feed[i].time;
                float alpha = Mathf.Clamp01(1f - (age - 8f) / 2f) * (i == m_Feed.Count - 1 ? 1f : 0.8f);
                if (alpha <= 0f) continue;
                var rect = new Rect(20f, y - 40f, 760f, 40f);
                Shadowed(rect, m_Feed[i].text, m_Small, new Color(1f, 1f, 1f, alpha), 0, TextAnchor.MiddleLeft);
                y -= 40f;
            }
        }

        void DrawBanner()
        {
            float age = Time.time - m_BannerTime;
            if (age > BannerDuration) return;
            float pop = age < 0.18f ? Mathf.Lerp(1.6f, 1f, age / 0.18f) : 1f;
            float alpha = Mathf.Clamp01((BannerDuration - age) / 0.4f);
            var titleRect = new Rect(0f, 250f, m_Width, 140f);

            var matrix = GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one * pop, new Vector2(m_Width / 2f, 320f) * m_Scale);
            Shadowed(titleRect, m_BannerTitle, m_Title, new Color(Yellow.r, Yellow.g, Yellow.b, alpha), 110);
            GUI.matrix = matrix;
            Shadowed(new Rect(0f, 385f, m_Width, 60f), m_BannerSubtitle, m_H1, new Color(1f, 1f, 1f, alpha));
        }

        // ------------------------------------------------------------------ helpers

        static bool ToGui(Camera cam, Vector3 world, out Vector2 gui)
        {
            var screen = cam.WorldToScreenPoint(world);
            float scale = Screen.height / RefHeight;
            gui = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
            return screen.z > 0f;
        }

        Rect Centered(float w, float h) => new((m_Width - w) / 2f, (RefHeight - h) / 2f, w, h);

        void Shadowed(Rect rect, string text, GUIStyle style, Color color, int fontSize = 0, TextAnchor? anchor = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            int oldSize = style.fontSize;
            var oldAnchor = style.alignment;
            var oldColor = style.normal.textColor;
            if (fontSize > 0) style.fontSize = fontSize;
            if (anchor.HasValue) style.alignment = anchor.Value;

            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.75f);
            float o = Mathf.Max(2f, style.fontSize / 22f);
            GUI.Label(new Rect(rect.x + o, rect.y + o, rect.width, rect.height), text, style);
            style.normal.textColor = color;
            GUI.Label(rect, text, style);

            style.fontSize = oldSize;
            style.alignment = oldAnchor;
            style.normal.textColor = oldColor;
        }

        void Swatch(Color color)
        {
            var rect = GUILayoutUtility.GetRect(30f, 30f, m_Swatch, GUILayout.Width(30f), GUILayout.Height(30f));
            rect.y += 2f;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, m_Coin);
            GUI.color = old;
            GUILayout.Space(10f);
        }

        void Icon(Texture2D texture)
        {
            var rect = GUILayoutUtility.GetRect(28f, 28f, m_Swatch, GUILayout.Width(28f), GUILayout.Height(28f));
            rect.y += 2f;
            GUI.DrawTexture(rect, texture);
            GUILayout.Space(6f);
        }

        void EnsureStyles()
        {
            if (m_StylesReady) return;
            m_StylesReady = true;

            m_White = Solid(Color.white);
            m_Coin = Circle(64, Palette.Gold, new Color(0.85f, 0.55f, 0.05f));
            m_Crown = CrownIcon(64);

            GUIStyle Text(int size, FontStyle style, Color color, TextAnchor align = TextAnchor.MiddleLeft) => new(GUI.skin.label)
            {
                fontSize = size, fontStyle = style, alignment = align, richText = true, wordWrap = false,
                normal = { textColor = color },
            };

            m_Title = Text(96, FontStyle.Bold, Yellow, TextAnchor.MiddleCenter);
            m_Subtitle = Text(32, FontStyle.Italic, Color.white, TextAnchor.MiddleCenter);
            m_H1 = Text(44, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            m_H2 = Text(28, FontStyle.Bold, Muted);
            m_Label = Text(27, FontStyle.Normal, Color.white);
            m_Small = Text(22, FontStyle.Normal, Muted);
            m_Small.wordWrap = true;
            m_Center = Text(32, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter);
            m_Outline = Text(24, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            m_Swatch = new GUIStyle();

            m_Panel = new GUIStyle { normal = { background = Rounded(PanelColor, 18) }, border = new RectOffset(20, 20, 20, 20) };
            m_Row = new GUIStyle { normal = { background = Rounded(new Color(1f, 1f, 1f, 0.05f), 10) }, border = new RectOffset(12, 12, 12, 12), padding = new RectOffset(12, 12, 8, 8), margin = new RectOffset(0, 0, 3, 3) };
            m_RowActive = new GUIStyle(m_Row) { normal = { background = Rounded(new Color(1f, 0.8f, 0.16f, 0.25f), 10) } };

            m_Button = MakeButton(Yellow, Ink, 30);
            m_ButtonAlt = MakeButton(new Color(0.30f, 0.36f, 0.62f), Color.white, 26);
            m_ButtonDanger = MakeButton(new Color(0.86f, 0.30f, 0.36f), Color.white, 24);
            m_BigButton = MakeButton(Yellow, Ink, 48);

            m_Field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 30, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(16, 16, 8, 8),
                normal = { background = Rounded(new Color(1f, 1f, 1f, 0.1f), 10), textColor = Color.white },
                focused = { background = Rounded(new Color(1f, 1f, 1f, 0.18f), 10), textColor = Color.white },
                hover = { background = Rounded(new Color(1f, 1f, 1f, 0.14f), 10), textColor = Color.white },
                border = new RectOffset(12, 12, 12, 12),
            };
        }

        GUIStyle MakeButton(Color color, Color text, int size)
        {
            Color Shade(float k) => new(color.r * k, color.g * k, color.b * k, 1f);
            return new GUIStyle(GUI.skin.button)
            {
                fontSize = size, fontStyle = FontStyle.Bold, richText = true, alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(14, 14, 14, 14), padding = new RectOffset(16, 16, 8, 8), margin = new RectOffset(4, 4, 4, 4),
                normal = { background = Rounded(color, 14), textColor = text },
                hover = { background = Rounded(Color.Lerp(color, Color.white, 0.2f), 14), textColor = text },
                active = { background = Rounded(Shade(0.8f), 14), textColor = text },
                focused = { background = Rounded(color, 14), textColor = text },
            };
        }

        static Texture2D Solid(Color color)
        {
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        static Texture2D Rounded(Color color, int radius)
        {
            int size = radius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                var c = color;
                c.a *= Mathf.Clamp01(radius - d + 0.5f);
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        static Texture2D Circle(int size, Color fill, Color rim)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                var c = d > r * 0.78f ? rim : fill;
                c.a = Mathf.Clamp01(r - d);
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        static Texture2D CrownIcon(int size)
        {
            // A simple three-pointed crown, drawn pixel by pixel (y = 0 is the bottom row).
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var gold = Palette.Gold;
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                bool band = v > 0.15f && v < 0.38f && u > 0.12f && u < 0.88f;
                bool body = v >= 0.38f && u > 0.12f && u < 0.88f;
                if (body)
                {
                    // Three peaks at u = 0.15, 0.5, 0.85 reaching up to v = 0.9.
                    float peak = Mathf.Max(Tri(u, 0.15f), Mathf.Max(Tri(u, 0.5f), Tri(u, 0.85f)));
                    body = v < 0.38f + peak * 0.52f;
                }
                tex.SetPixel(x, y, band ? gold * 0.85f + new Color(0, 0, 0, 0.15f) : body ? gold : clear);
            }
            tex.Apply();
            return tex;

            static float Tri(float u, float center) => Mathf.Clamp01(1f - Mathf.Abs(u - center) / 0.2f);
        }
    }

    static class RectExtensions
    {
        public static Rect Inset(this Rect r, float by) => new(r.x + by, r.y + by, r.width - by * 2f, r.height - by * 2f);

        public static Rect ScaleAround(this Rect r, float scale)
        {
            var c = r.center;
            var size = r.size * scale;
            return new Rect(c - size / 2f, size);
        }
    }
}
