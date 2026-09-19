using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Runtime-Debug-HUD fuer den Raum-Portal-Graphen: zeigt als grosses,
    /// verschiebbares Vollbild-Fenster (Einstellungsfenster-Stil), in
    /// welcher <see cref="VoiceZone"/> man selbst steht und ueber welchen Weg
    /// (Tueren, Treppen, Offenheitsgrad) der Schall zu Kollegen und Walkie-
    /// Geraeten laufen WUERDE — Luftlinie vs. Graph-Laufweg, exakt wie
    /// <see cref="VoicePipeline"/> ihn hoert. Tueren werden mit ihrem
    /// Offenheitsgrad aufgelistet, gesperrte Wege als Occlusion erkannt.
    /// <para>
    /// Rein diagnostisch: kein Einfluss auf irgendeinen Audio-Pfad. Wird wie
    /// <see cref="WalkieSidetoneCapture"/> automatisch am VoiceRuntime-Objekt
    /// erzeugt und mit der Toggle-Taste (Default F7) ein-/ausgeblendet.
    /// </para>
    /// <para>
    /// Bekannte Luecke, die das HUD bewusst sichtbar macht: der Walkie-Geraeteton
    /// (<see cref="WalkieDeviceOutput"/>) daempft nach reiner Luftlinie und nutzt
    /// den Graphen NICHT — siehe docs/OFFENE-PUNKTE.md.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot Voice/Voice Graph Debug HUD")]
    public sealed class VoiceGraphDebugHUD : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Taste, die das HUD ein-/ausblendet. Default F7. Hinweis: Kollidiert mit dem Leak-Hunt-F7, wenn 'Diagnostic Hotkeys Enabled' an WalkieSidetoneCapture aktiviert ist - dann hier z.B. F6 eintragen.")]
        private KeyCode toggleKey = KeyCode.F7;

        [SerializeField]
        [Tooltip("Layer, auf denen die VoiceZone-Collider liegen. Default: alles.")]
        private LayerMask zoneLayers = ~0;

        [SerializeField, Min(0.05f)]
        [Tooltip("Aktualisierungstakt des HUD-Inhalts in Sekunden.")]
        private float refreshSeconds = 0.25f;

        [SerializeField, Range(10, 32)]
        [Tooltip("Schriftgroesse des HUD-Inhalts im Vollbild-Fenster.")]
        private int fontSize = 16;

        private const int MaxTargetCards = 12;
        private const int WindowId = 0x45415253; // "EARS"

        private bool visible;
        private bool showWorldPath = true;
        private float nextRefresh = -1f;
        private string hudText = "Earshot Graph Debug - wird geladen ...";
        private GUIStyle labelStyle;
        private Rect windowRect;
        private bool windowRectInitialized;
        private Vector2 scrollPosition;

        private readonly Collider[] zoneProbeBuffer = new Collider[16];
        private readonly List<VoicePortal> pathPortals = new List<VoicePortal>(8);
        private readonly List<EarshotProximityVoice> players = new List<EarshotProximityVoice>(8);
        private readonly List<EarshotWalkieTalkie> walkies = new List<EarshotWalkieTalkie>(8);
        private readonly StringBuilder text = new StringBuilder(1024);

        // Welt-Linien des Schallwegs (pro Ziel): Segment von A nach B mit
        // Offenheitsgrad des Portals, das am Segmentende durchquert wird.
        private readonly List<Vector3> segmentFrom = new List<Vector3>(32);
        private readonly List<Vector3> segmentTo = new List<Vector3>(32);
        private readonly List<float> segmentOpenness = new List<float>(32);

        internal static VoiceGraphDebugHUD EnsureOn(VoiceRuntime runtime)
        {
            if (runtime == null) return null;
            var hud = runtime.GetComponent<VoiceGraphDebugHUD>();
            if (hud == null)
            {
                hud = runtime.gameObject.AddComponent<VoiceGraphDebugHUD>();
            }

            return hud;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                visible = !visible;
                if (visible) nextRefresh = -1f;
            }

            if (!visible) return;

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, refreshSeconds);
                RebuildContent();
            }

            DrawWorldPath();
        }

        private void OnGUI()
        {
            if (!visible) return;

            if (labelStyle == null || labelStyle.fontSize != fontSize)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    wordWrap = true,
                    fontSize = fontSize
                };
            }

            // Erstes Oeffnen: Fenster fuellt die Flaeche bis auf einen Rand,
            // danach bleibt die (vom Nutzer ggf. verschobene) Position erhalten.
            if (!windowRectInitialized)
            {
                float marginX = Mathf.Max(24f, Screen.width * 0.04f);
                float marginY = Mathf.Max(24f, Screen.height * 0.04f);
                windowRect = new Rect(
                    marginX,
                    marginY,
                    Screen.width - marginX * 2f,
                    Screen.height - marginY * 2f);
                windowRectInitialized = true;
            }

            // Nach Aufloesungs-/Fensterwechsel im Bild halten.
            windowRect.width = Mathf.Min(windowRect.width, Screen.width - 8f);
            windowRect.height = Mathf.Min(windowRect.height, Screen.height - 8f);
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Screen.width - windowRect.width));
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Screen.height - windowRect.height));

            windowRect = GUILayout.Window(
                WindowId,
                windowRect,
                DrawWindowContent,
                "EARSHOT GRAPH DEBUG  (" + toggleKey + " schliesst)",
                GUI.skin.window);
        }

        private void DrawWindowContent(int windowId)
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            GUILayout.Label(hudText, labelStyle);
            GUILayout.EndScrollView();

            GUILayout.Space(6f);
            showWorldPath = GUILayout.Toggle(
                showWorldPath,
                "Schallweg als Welt-Linien (rot = Tuer zu, gruen = offen; nur mit Gizmos sichtbar)");

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }


        private void RebuildContent()
        {
            text.Clear();
            segmentFrom.Clear();
            segmentTo.Clear();
            segmentOpenness.Clear();

            if (!TryGetListener(out Vector3 listenerPos, out string listenerName))
            {
                hudText = "<b>EARSHOT GRAPH DEBUG</b>\nKein aktiver AudioListener in der Szene.";
                return;
            }

            VoiceGraph.RebuildIfNeeded();
            var listenerZone = VoiceZone.FindAtOrNearest(listenerPos, zoneLayers, zoneProbeBuffer);

            text.AppendLine("<b>EARSHOT GRAPH DEBUG</b>  (" + toggleKey + " = aus)");
            text.Append("DU: ")
                .Append(listenerZone != null ? listenerZone.ZoneName : "keine Zone")
                .Append("  @ ")
                .Append(listenerPos.ToString("0.0"))
                .Append("  (")
                .Append(listenerName)
                .AppendLine(")");
            text.Append("Graph: ")
                .Append(VoiceGraph.NodeCount)
                .Append(" Raeume, ")
                .Append(VoiceGraph.ConnectionCount)
                .AppendLine(" Verbindungen");
            text.AppendLine();

            CollectTargets(listenerPos);

            int cards = 0;
            for (int i = 0; i < players.Count && cards < MaxTargetCards; i++, cards++)
            {
                AppendTarget(
                    "Spieler " + players[i].DisplayName,
                    players[i].Position,
                    listenerPos,
                    listenerZone,
                    null);
            }

            for (int i = 0; i < walkies.Count && cards < MaxTargetCards; i++, cards++)
            {
                var walkie = walkies[i];
                AppendTarget(
                    "Walkie '" + walkie.name + "' (Kanal " + walkie.ChannelId + ")",
                    walkie.AudioAnchor.position,
                    listenerPos,
                    listenerZone,
                    walkie);
            }

            if (cards == 0)
            {
                text.AppendLine("Keine Ziele: keine Remote-Spieler und keine Walkies in der Szene.");
            }

            int remaining = players.Count + walkies.Count - cards;
            if (remaining > 0)
            {
                text.Append("+ ")
                    .Append(remaining)
                    .AppendLine(" weitere Ziele (Anzeige gedeckelt)");
            }

            hudText = text.ToString();
        }

        private void AppendTarget(
            string title,
            Vector3 targetPos,
            Vector3 listenerPos,
            VoiceZone listenerZone,
            EarshotWalkieTalkie walkie)
        {
            float airLine = Vector3.Distance(listenerPos, targetPos);
            var targetZone = VoiceZone.FindAtOrNearest(targetPos, zoneLayers, zoneProbeBuffer);

            text.Append("<b>").Append(title).Append("</b>  ·  ")
                .AppendLine(targetZone != null ? targetZone.ZoneName : "keine Zone");

            if (listenerZone == null || targetZone == null)
            {
                text.Append("   Luftlinie ")
                    .Append(airLine.ToString("0.0"))
                    .AppendLine(" m - Zone(n) unbekannt, Graph-Weg unbestimmbar");
                AppendWalkieNote(walkie);
                text.AppendLine();
                return;
            }

            if (listenerZone == targetZone)
            {
                text.Append("   GLEICHER RAUM - Luftlinie ")
                    .Append(airLine.ToString("0.0"))
                    .AppendLine(" m, kein Portalweg noetig");
                AppendWalkieNote(walkie);
                text.AppendLine();
                return;
            }

            if (!VoiceGraph.TryFindPath(listenerZone, targetZone, pathPortals, out _))
            {
                text.Append("   Luftlinie ")
                    .Append(airLine.ToString("0.0"))
                    .AppendLine(" m - KEIN Weg durch Tueren -> Occlusion/Sichtlinie");
                AppendWalkieNote(walkie);
                text.AppendLine();
                return;
            }

            // Laufweg so rechnen, wie VoicePipeline.TryApplyGraph ihn hoeren
            // wuerde (inkl. Tuer-/Ecken-Aufschlag und Portal-Penalty).
            float length = 0f;
            float closed = 0f;
            for (int i = 0; i < pathPortals.Count; i++)
            {
                var portal = pathPortals[i];
                Vector3 at = VoiceGraph.PortalPosition(portal);
                Vector3 next = i + 1 < pathPortals.Count
                    ? VoiceGraph.PortalPosition(pathPortals[i + 1])
                    : targetPos;

                if (i == 0)
                {
                    length += Vector3.Distance(listenerPos, at);
                }

                length += VoiceGraph.HopLength(at, next, portal);
                if (portal != null) closed += 1f - portal.Openness;
            }

            if (pathPortals.Count == 0)
            {
                length = airLine;
            }

            float closedness = Mathf.Clamp01(closed);

            text.Append("   Luftlinie ")
                .Append(airLine.ToString("0.0"))
                .Append(" m  ·  <b>LAUFWEG ")
                .Append(length.ToString("0.0"))
                .AppendLine(" m</b>");

            text.Append("   Du");
            int closedCount = 0;
            Vector3 prev = listenerPos;
            for (int i = 0; i < pathPortals.Count; i++)
            {
                var portal = pathPortals[i];
                float open = portal != null ? portal.Openness : 1f;
                bool stair = portal != null && portal.Kind == VoicePortalKind.Stair;
                bool isClosed = open < 0.5f;
                if (isClosed) closedCount++;

                text.Append(" -> [")
                    .Append(stair ? "Treppe " : "Tuer ")
                    .Append((open * 100f).ToString("0"))
                    .Append('%');
                if (isClosed) text.Append(" ZU");
                text.Append(']');

                Vector3 at = VoiceGraph.PortalPosition(portal);
                AddSegment(prev, at, open);
                prev = at;
            }

            text.AppendLine(" -> Ziel");



            int roomsBetween = Mathf.Max(0, pathPortals.Count - 1);
            text.Append("   ")
                .Append(pathPortals.Count)
                .Append(" Portal(e) (")
                .Append(closedCount)
                .Append(" zu), ")
                .Append(roomsBetween)
                .Append(roomsBetween == 1 ? " Raum dazwischen" : " Raeume dazwischen")
                .Append(", Geschlossenheit ")
                .Append((closedness * 100f).ToString("0"))
                .AppendLine("%");

            AddSegment(prev, targetPos, 1f);
            AppendWalkieNote(walkie);
            text.AppendLine();
        }

        private void AppendWalkieNote(EarshotWalkieTalkie walkie)
        {
            if (walkie == null) return;

            string state = !walkie.PoweredOn
                ? "aus"
                : walkie.IsTransmitting ? "sendet" : "an";
            text.Append("   <color=#ffaa00>ACHTUNG: Walkie (")
                .Append(state)
                .Append(") daempft nach LUFTLINIE (MaxHearingDistance ")
                .Append(walkie.MaxHearingDistance.ToString("0.0"))
                .AppendLine(" m), NICHT nach Graph-Weg - siehe docs/OFFENE-PUNKTE.md</color>");
        }

        private void AddSegment(Vector3 from, Vector3 to, float openness)
        {
            segmentFrom.Add(from);
            segmentTo.Add(to);
            segmentOpenness.Add(openness);
        }

        private void DrawWorldPath()
        {
            if (!showWorldPath) return;

            for (int i = 0; i < segmentFrom.Count; i++)
            {
                Color color = Color.Lerp(Color.red, Color.green, Mathf.Clamp01(segmentOpenness[i]));
                Debug.DrawLine(segmentFrom[i], segmentTo[i], color, 0f, depthTest: false);
            }
        }


        private void CollectTargets(Vector3 listenerPos)
        {
            players.Clear();
            walkies.Clear();

#if UNITY_6000_5_OR_NEWER
            var foundPlayers = FindObjectsByType<EarshotProximityVoice>(FindObjectsInactive.Exclude);
            var foundWalkies = FindObjectsByType<EarshotWalkieTalkie>(FindObjectsInactive.Exclude);
#else
            var foundPlayers = FindObjectsByType<EarshotProximityVoice>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var foundWalkies = FindObjectsByType<EarshotWalkieTalkie>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif

            for (int i = 0; i < foundPlayers.Length; i++)
            {
                if (foundPlayers[i] == null || foundPlayers[i].IsLocalPlayer) continue;
                players.Add(foundPlayers[i]);
            }

            for (int i = 0; i < foundWalkies.Length; i++)
            {
                if (foundWalkies[i] == null) continue;
                walkies.Add(foundWalkies[i]);
            }

            players.Sort((a, b) =>
                (a.Position - listenerPos).sqrMagnitude.CompareTo(
                    (b.Position - listenerPos).sqrMagnitude));
            walkies.Sort((a, b) =>
                (a.AudioAnchor.position - listenerPos).sqrMagnitude.CompareTo(
                    (b.AudioAnchor.position - listenerPos).sqrMagnitude));
        }

        private static bool TryGetListener(out Vector3 position, out string name)
        {
            position = default;
            name = null;

#if UNITY_6000_5_OR_NEWER
            var listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);
#else
            var listeners = FindObjectsByType<AudioListener>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif

            for (int i = 0; i < listeners.Length; i++)
            {
                var listener = listeners[i];
                if (listener == null || !listener.isActiveAndEnabled) continue;
                position = listener.transform.position;
                name = listener.gameObject.name;
                return true;
            }

            return false;
        }
    }
}
