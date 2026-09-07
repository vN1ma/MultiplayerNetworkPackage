using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Optionales Funkgeraet als Welt-Objekt. Stimme laeuft ueber einen eigenen Vivox-Kanal
    /// und wird am Geraet abgespielt (Hand, Handgelenk oder Boden).
    /// <para>
    /// Input, Aufheben und Optik gehoeren ins Spiel. Hier nur:
    /// An/Aus, Senden freigeben, Push-to-Talk, Kanal und Klang.
    /// </para>
    /// </summary>
    [AddComponentMenu("Earshot Voice/Walkie Talkie")]
    [DisallowMultipleComponent]
    public sealed class EarshotWalkieTalkie : MonoBehaviour
    {
        [Header("Kanal")]
        [SerializeField]
        [Tooltip("Logische Funkkanal-ID. Gleiche ID = gleiches Netz. Beliebig viele Geraete.")]
        private string channelId = "default";

        [Header("Zustand")]
        [SerializeField]
        [Tooltip("Beim Start eingeschaltet.")]
        private bool startPowered;

        [SerializeField]
        [Tooltip("Spiel setzt true, wenn das Geraet in der Hand ist und senden darf.")]
        private bool canTransmit;

        [Header("Klang")]
        [SerializeField, Range(0.05f, 1f)]
        [Tooltip("Lautstaerke der Funkstimme am Geraet (fremde Stimmen).")]
        private float radioVolume = 0.65f;

        [SerializeField, Range(0.05f, 1f)]
        [Tooltip("Wie laut du dich selbst am anderen Walkie hoerst (Sidetone). Niedriger = weniger Feedback.")]
        private float sidetoneWorldVolume = 0.35f;

        [SerializeField, Range(100f, 4000f)]
        [Tooltip("Unteres Funkband. Niedriger = weniger roboterhaft.")]
        private float highPassHz = 300f;

        [SerializeField, Range(1000f, 8000f)]
        [Tooltip("Oberes Funkband. Hoeher = weniger dumpf/roboterhaft.")]
        private float lowPassHz = 5200f;

        [SerializeField, Range(0f, 1.5f)]
        [Tooltip("Lokales Walkie-Delay: Stimme kommt leicht versetzt am Empfaenger an.")]
        private float transmissionDelaySeconds = 0.18f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Alter-Funk-Charakter (Bit-/Sample-Reduktion + leichte Verzerrung), damit Walkie-Stimme sich hoerbar von Mund-Stimme unterscheidet.")]
        private float radioCrunch = 0.35f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Mund-/Naehe-Stimme des Senders, solange er funkt (Funk ersetzt Mund).")]
        private float mouthVolumeWhileTransmitting = 0.12f;

        [SerializeField, Min(1f)]
        [Tooltip("Ab dieser Entfernung zum Geraet ist der Funkton praktisch weg (Leak-Grenze).")]
        private float maxHearingDistance = 8f;

        [SerializeField]
        [Tooltip("Leer = dieses Transform. Position der hoerbaren Funkstimme.")]
        private Transform audioAnchor;

        private bool poweredOn;
        private bool transmitting;
        private bool isLocallyOwned = true;
        private WalkieDeviceOutput deviceOutput;

        public string ChannelId => WalkieRules.SanitizeChannelId(channelId);

        public bool PoweredOn => poweredOn;

        public bool CanTransmit => canTransmit;

        public bool IsTransmitting => transmitting;

        public Transform AudioAnchor => audioAnchor != null ? audioAnchor : transform;

        public float RadioVolume => radioVolume;

        public float SidetoneWorldVolume => Mathf.Clamp(sidetoneWorldVolume, 0.05f, 1f);

        public float HighPassHz => highPassHz;

        public float LowPassHz => lowPassHz;

        public float TransmissionDelaySeconds => WalkieRules.ClampDelaySeconds(transmissionDelaySeconds);

        public float RadioCrunch => Mathf.Clamp01(radioCrunch);

        /// <summary>
        /// Wahr nur auf der Instanz, die dem LOKALEN Spieler-Client gehoert (z.B. NetworkObject.IsOwner
        /// bei Netcode). Default true fuer Abwaertskompatibilitaet ohne Netzwerk. Siehe <see cref="SetLocalOwnership"/>.
        /// </summary>
        public bool IsLocallyOwned => isLocallyOwned;

        public float MouthVolumeWhileTransmitting => Mathf.Clamp01(mouthVolumeWhileTransmitting);

        public float MaxHearingDistance => Mathf.Max(1f, maxHearingDistance);

        /// <summary>Ein-/Ausschalten (Spiel: z.B. Q).</summary>
        public void SetPowered(bool on)
        {
            if (poweredOn == on) return;
            poweredOn = on;

            if (!poweredOn && transmitting)
            {
                SetTransmitting(false);
            }

            WalkieTalkieRegistry.NotifyChanged();
            VoiceSessionLog.Note(
                poweredOn
                    ? $"WALKIE an Kanal '{ChannelId}'"
                    : $"WALKIE aus Kanal '{ChannelId}'");
        }

        /// <summary>
        /// Spiel setzt true, wenn das Geraet in der Hand ist. Am Handgelenk/Boden: false
        /// (nur Empfang).
        /// </summary>
        public void SetCanTransmit(bool value)
        {
            if (canTransmit == value) return;
            canTransmit = value;

            if (!canTransmit && transmitting)
            {
                SetTransmitting(false);
            }

            WalkieTalkieRegistry.NotifyChanged();
        }

        /// <summary>
        /// Push-to-Talk (Spiel: Linksklick halten). Nur wenn an und <see cref="CanTransmit"/>.
        /// Half-Duplex: waehrenddessen kein Empfang.
        /// </summary>
        public void SetTransmitting(bool value)
        {
            if (value)
            {
                if (!isLocallyOwned)
                {
                    EarshotVoiceLog.Warn(
                        $"WALKIE '{ChannelId}': SetTransmitting(true) auf nicht-lokal-besessener " +
                        "Instanz ignoriert. Siehe SetLocalOwnership() — vermutlich ruft das Spiel " +
                        "PTT auf allen Clients statt nur beim Besitzer auf.");
                    return;
                }

                if (!WalkieRules.CanTransmit(poweredOn, canTransmit)) return;
                if (transmitting) return;

                // Nur ein lokales Geraet sendet gleichzeitig.
                var devices = WalkieTalkieRegistry.Devices;
                for (int i = 0; i < devices.Count; i++)
                {
                    var other = devices[i];
                    if (other != null && other != this && other.IsTransmitting)
                    {
                        other.SetTransmitting(false);
                    }
                }

                transmitting = true;
                WalkieTalkieRegistry.SetLocalTransmit(this, true);
                VoiceSessionLog.Note($"WALKIE PTT an '{ChannelId}'");
                LogChannelSiblingsForDebug(devices);
            }
            else
            {
                if (!transmitting) return;
                transmitting = false;
                WalkieTalkieRegistry.SetLocalTransmit(this, false);
                VoiceSessionLog.Note($"WALKIE PTT aus '{ChannelId}'");
            }
        }

        /// <summary>
        /// WICHTIG bei Netzwerk-Objekten: Einmal pro Client aufrufen, sobald klar ist, ob
        /// DIESER Client der Besitzer ist (z.B. <c>SetLocalOwnership(networkObject.IsOwner)</c>
        /// direkt nach dem Spawn). Ohne diesen Aufruf bleibt die Instanz "owned" (Default true) —
        /// das ist nur fuer Einzelspieler-/Netzwerk-freie Tests sicher.
        /// <para>
        /// Wird eine Walkie-Instanz eines FREMDEN Spielers faelschlich als "owned" behandelt
        /// (z.B. weil ein NetworkVariable-Callback <see cref="SetTransmitting"/> auf ALLEN
        /// Clients statt nur beim Besitzer aufruft), denkt dieser Client faelschlich, ER sende:
        /// eigenes Mikrofon startet (Freeze durch <c>Microphone.Start</c>), Sidetone spielt
        /// Phantom-Ton ab. Deshalb: PTT/CanTransmit nur ueber die lokal-besessene Instanz steuern.
        /// </para>
        /// </summary>
        public void SetLocalOwnership(bool isLocal)
        {
            isLocallyOwned = isLocal;
            if (!isLocallyOwned && transmitting)
            {
                SetTransmitting(false);
            }
        }

        /// <summary>
        /// Debug-Hilfe: listet beim Sendestart alle anderen eingeschalteten Geraete auf
        /// demselben Kanal samt Entfernung im Session-Log. Hilft, ein unsynchronisiertes
        /// Duplikat (z.B. Ego-Sichtmodell ohne eigenes SetTransmitting) zu entlarven, das
        /// die eigene Stimme faelschlich ganz nah am Ohr abspielt.
        /// </summary>
        private void LogChannelSiblingsForDebug(
            System.Collections.Generic.IReadOnlyList<EarshotWalkieTalkie> devices)
        {
            string wanted = ChannelId;
            Vector3 myPos = AudioAnchor.position;

            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (d == null || d == this) continue;
                if (!string.Equals(d.ChannelId, wanted, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!d.PoweredOn) continue;

                float dist = Vector3.Distance(myPos, d.AudioAnchor.position);
                VoiceSessionLog.Note(
                    $"WALKIE DEBUG: anderes Geraet auf '{wanted}': '{d.gameObject.name}', " +
                    $"{dist:0.00} m entfernt, CanTransmit={d.CanTransmit}");

                if (dist < 0.5f)
                {
                    VoiceSessionLog.Alert(
                        $"WALKIE DEBUG: '{d.gameObject.name}' ist SEHR NAH ({dist:0.00} m) — " +
                        "moeglicher Duplikat-/Sichtmodell-Verdacht, siehe SetLocalOwnership-Doku.");
                }
            }
        }

        public void SetChannelId(string value)
        {
            string next = WalkieRules.SanitizeChannelId(value);
            if (string.Equals(ChannelId, next, System.StringComparison.OrdinalIgnoreCase)) return;

            bool wasTx = transmitting;
            if (wasTx) SetTransmitting(false);

            channelId = next;
            WalkieTalkieRegistry.NotifyChanged();

            if (wasTx) SetTransmitting(true);
        }

        public void SetAudioAnchor(Transform anchor)
        {
            audioAnchor = anchor;
        }

        private void OnEnable()
        {
            poweredOn = startPowered;
            transmitting = false;
            EnsureDeviceOutput();
            WalkieTalkieRegistry.Register(this);
        }

        private void OnDisable()
        {
            if (transmitting) SetTransmitting(false);
            WalkieTalkieRegistry.Unregister(this);
        }

        private void EnsureDeviceOutput()
        {
            if (deviceOutput != null) return;

            var existing = GetComponentInChildren<WalkieDeviceOutput>(true);
            if (existing != null)
            {
                deviceOutput = existing;
                deviceOutput.Bind(this);
                return;
            }

            var go = new GameObject("WalkieOutput");
            go.transform.SetParent(AudioAnchor, false);
            go.transform.localPosition = Vector3.zero;
            deviceOutput = go.AddComponent<WalkieDeviceOutput>();
            deviceOutput.Bind(this);
        }

        private void OnValidate()
        {
            channelId = WalkieRules.SanitizeChannelId(channelId);
            transmissionDelaySeconds = WalkieRules.ClampDelaySeconds(transmissionDelaySeconds);
            mouthVolumeWhileTransmitting = Mathf.Clamp01(mouthVolumeWhileTransmitting);
            radioVolume = Mathf.Clamp(radioVolume, 0.05f, 1f);
            sidetoneWorldVolume = Mathf.Clamp(sidetoneWorldVolume, 0.05f, 1f);
            radioCrunch = Mathf.Clamp01(radioCrunch);
            maxHearingDistance = Mathf.Max(1f, maxHearingDistance);
            if (highPassHz > lowPassHz) lowPassHz = highPassHz;
        }
    }
}
