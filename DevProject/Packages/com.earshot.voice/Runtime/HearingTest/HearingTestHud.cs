using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Kurze Steuerungshilfe und Status der naechsten Tuer/Lautsprecher.
    /// </summary>
    public sealed class HearingTestHud : MonoBehaviour
    {
        private void OnGUI()
        {
            const int width = 460;
            var box = new Rect(16f, 16f, width, 88f);
            GUI.Box(box, "");
            GUI.Label(new Rect(28f, 24f, width - 24f, 72f),
                "WASD bewegen   Maus umschauen   Linksklick sperrt die Maus\n" +
                "E  Ton im naechsten Raum an/aus\n" +
                "F  naechste Tuer auf/zu   Esc  Maus frei\n" +
                Status());
        }

        private static string Status()
        {
            var pos = HearingTestInteract.LastHint;
            return string.IsNullOrEmpty(pos) ? "" : pos;
        }
    }
}
