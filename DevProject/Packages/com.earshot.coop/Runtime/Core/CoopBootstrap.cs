using Unity.Netcode;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Der Einstiegspunkt in der Szene. Der Setup-Wizard legt genau ein Objekt mit dieser
    /// Komponente an; von Hand reicht es, sie auf ein leeres GameObject zu ziehen.
    /// <para>
    /// Die Komponente haelt sich absichtlich zurueck: Sie erzeugt nichts hinter dem Ruecken
    /// des Nutzers, sondern prueft beim Start die Voraussetzungen und sagt in verstaendlichen
    /// Worten, was fehlt. Stillschweigend Objekte anzulegen fuehrt in fremden Projekten zu
    /// Ueberraschungen, die schwerer zu finden sind als eine klare Fehlermeldung.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot/Coop Bootstrap")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public class CoopBootstrap : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Dieses Objekt bei Szenenwechseln behalten. Empfohlen, damit die Sitzung einen Wechsel ins Spiel ueberlebt.")]
        private bool persistAcrossScenes = true;

        [SerializeField]
        [Tooltip("Beim Start pruefen, ob alle Voraussetzungen erfuellt sind, und Hinweise in die Konsole schreiben.")]
        private bool checkSetupOnStart = true;

        private static CoopBootstrap instance;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                CoopLog.Warn(
                    $"Es gibt bereits einen CoopBootstrap ('{instance.name}'). " +
                    $"Das zusaetzliche Objekt '{name}' wird entfernt.");
                Destroy(gameObject);
                return;
            }

            instance = this;

            if (persistAcrossScenes)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }

            // Fruehzeitig laden, damit die Log-Ausfuehrlichkeit ab dem ersten Frame stimmt.
            _ = CoopSettings.Instance;
        }

        private void Start()
        {
            if (checkSetupOnStart) CheckSetup();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>
        /// Prueft die Voraussetzungen und beschreibt Probleme so, dass man weiss, was zu tun
        /// ist. Dieselben Regeln pruefte der Setup-Wizard bereits im Editor - hier greifen
        /// sie noch einmal, falls die Szene spaeter von Hand veraendert wurde.
        /// </summary>
        public void CheckSetup()
        {
            var settings = CoopSettings.Instance;

            if (NetworkManager.Singleton == null)
            {
                CoopLog.Error(
                    "Kein NetworkManager in der Szene. Ohne ihn laesst sich keine Sitzung " +
                    "starten. Anlegen ueber Tools > Earshot > Setup.");
            }
            else if (NetworkManager.Singleton.NetworkConfig.PlayerPrefab == null)
            {
                CoopLog.Warn(
                    "Im NetworkManager ist kein Player Prefab eingetragen. Es werden dann " +
                    "keine Spielerfiguren erzeugt. Zuweisen ueber Tools > Earshot > Setup.");
            }

            // Unity 6.5 hat die Ueberladungen mit FindObjectsSortMode abgekuendigt, die
            // parameterlose Variante gibt es aber erst ab dort. Beide Wege offenzuhalten
            // haelt das Paket bis hinunter zu Unity 6 LTS warnungsfrei.
#if UNITY_6000_5_OR_NEWER
            int listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude).Length;
#else
            int listeners = FindObjectsByType<AudioListener>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
#endif
            if (listeners == 0)
            {
                CoopLog.Warn(
                    "Kein AudioListener in der Szene. Ohne ihn hoert der Spieler gar nichts, " +
                    "auch keine Stimmen.");
            }
            else if (listeners > 1)
            {
                CoopLog.Warn(
                    $"{listeners} AudioListener in der Szene. Unity erlaubt nur einen; " +
                    "die Stimmen werden sonst aus der falschen Position gehoert. " +
                    "Meist stammt der zusaetzliche von einer zweiten Kamera.");
            }

            if (settings.VoiceEnabled && settings.VoiceProfile == null)
            {
                CoopLog.Warn(
                    "Voice ist aktiv, aber ohne VoiceProfile. Damit gibt es nur eine " +
                    "einfache Entfernungsdaempfung - keine Tueren, Waende oder Raeume.");
            }
        }
    }
}
