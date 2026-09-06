using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// Bestimmt die Hoersituation zwischen Zuhoerer und Sprecher und laesst die
    /// Modifier-Kette darauf laufen.
    /// <para>
    /// Die Pipeline selbst faellt bewusst keine Klangentscheidungen. Sie sammelt nur
    /// Fakten - Entfernung, blockierende Geometrie, Portale, Raeume - und uebergibt sie
    /// den Modulen. Was daraus klanglich folgt, steht ausschliesslich in den Modulen.
    /// </para>
    /// </summary>
    public class VoicePipeline
    {
        /// <summary>
        /// Mehr Treffer als das zaehlen wir nicht. Wer durch acht Waende spricht, ist
        /// ohnehin unhoerbar; ein festes Feld vermeidet Allokationen im Sekundentakt.
        /// </summary>
        private const int MaxHits = 8;

        /// <summary>
        /// Ab so vielen massiven Waenden gilt der Weg als vollstaendig blockiert.
        /// Eine einzige Innenwand muss schon dumpf klingen; zwei waeren ein Keller.
        /// </summary>
        private const float FullOcclusionHits = 1f;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[MaxHits];
        private readonly Collider[] zoneBuffer = new Collider[MaxHits];
        private readonly System.Collections.Generic.List<VoicePortal> graphPortals =
            new System.Collections.Generic.List<VoicePortal>(8);

        private bool warnedAboutEmptyProfile;

        /// <summary>
        /// Rechnet einen kompletten Auswertungsschritt fuer einen Sprecher.
        /// </summary>
        public VoiceSample Evaluate(
            VoiceProfile profile,
            Vector3 listenerPosition,
            Vector3 speakerPosition,
            float deltaTime)
        {
            return Evaluate(profile, listenerPosition, speakerPosition, deltaTime, out _);
        }

        /// <summary>
        /// Wie <see cref="Evaluate(VoiceProfile, Vector3, Vector3, float)"/>, zusaetzlich mit
        /// dem Kontext fuer Debug-Anzeigen. Der Kontext ist das Ergebnis der Messung, keine
        /// zweite Berechnung.
        /// </summary>
        public VoiceSample Evaluate(
            VoiceProfile profile,
            Vector3 listenerPosition,
            Vector3 speakerPosition,
            float deltaTime,
            out VoiceContext context)
        {
            var sample = VoiceSample.Default;
            context = default;

            if (profile == null)
            {
                return sample;
            }

            context = BuildContext(profile, listenerPosition, speakerPosition, deltaTime);

            // Ausserhalb der Hoerweite kostet jede weitere Rechnung nur Zeit.
            if (context.Distance > profile.MaxHearingDistance)
            {
                sample.Volume = 0f;
                sample.Muted = true;
                return sample;
            }

            var modifiers = profile.SortedModifiers;

            if (modifiers.Count == 0)
            {
                ApplyFallback(profile, in context, ref sample);
            }
            else
            {
                for (int i = 0; i < modifiers.Count; i++)
                {
                    var modifier = modifiers[i];
                    if (modifier == null || !modifier.Enabled) continue;
                    modifier.Apply(in context, ref sample);
                }
            }

            sample.Clamp();
            return sample;
        }

        private VoiceContext BuildContext(
            VoiceProfile profile,
            Vector3 listenerPosition,
            Vector3 speakerPosition,
            float deltaTime)
        {
            var context = new VoiceContext
            {
                Profile = profile,
                ListenerPosition = listenerPosition,
                SpeakerPosition = speakerPosition,
                Distance = Vector3.Distance(listenerPosition, speakerPosition),
                PortalOpenness = 1f,
                DeltaTime = deltaTime
            };

            MeasureLineOfSight(profile, ref context);

            context.ListenerZone = VoiceZone.FindAt(listenerPosition, profile.ZoneLayers, zoneBuffer);
            context.SpeakerZone = VoiceZone.FindAt(speakerPosition, profile.ZoneLayers, zoneBuffer);
            context.SameZone = context.ListenerZone == context.SpeakerZone;
            context.HearingDistance = context.Distance;
            context.ApparentPosition = speakerPosition;

            TryApplyGraph(ref context);

            return context;
        }

        /// <summary>
        /// Freie Sichtlinie bleibt der Schnellpfad. Nur wenn eine Wand im Weg ist
        /// und beide in Zonen stehen, darf der Graph den Umweg ueber Tueren nehmen.
        /// Ohne Zonen/Portale aendert sich nichts — Occlusion bleibt der Fallback.
        /// </summary>
        private void TryApplyGraph(ref VoiceContext context)
        {
            if (context.OcclusionAmount <= 0f) return;
            if (context.SameZone) return;
            if (context.ListenerZone == null || context.SpeakerZone == null) return;

            if (!VoiceGraph.TryFindPath(
                    context.ListenerZone,
                    context.SpeakerZone,
                    graphPortals,
                    out _))
            {
                return;
            }

            context.UsedGraph = true;
            context.OcclusionAmount = 0f;

            float length = 0f;
            float closed = 0f;
            Vector3 previous = context.ListenerPosition;

            for (int i = 0; i < graphPortals.Count; i++)
            {
                var portal = graphPortals[i];
                Vector3 at = VoiceGraph.PortalPosition(portal);
                length += Vector3.Distance(previous, at);
                previous = at;
                if (portal != null) closed += 1f - portal.Openness;
            }

            length += Vector3.Distance(previous, context.SpeakerPosition);
            context.HearingDistance = length;
            context.GraphClosedness = Mathf.Clamp01(closed);
            context.ApparentPosition = graphPortals.Count > 0
                ? VoiceGraph.PortalPosition(graphPortals[0])
                : context.SpeakerPosition;
        }

        /// <summary>
        /// Zaehlt, was zwischen den beiden steht.
        /// <para>
        /// Bekannte Grenze dieser Fassung: Gezaehlt wird ausschliesslich auf der geraden
        /// Linie. Schall, der um eine Ecke durch eine offene Tuer laeuft, wird deshalb als
        /// blockiert gewertet - zwei Spieler in benachbarten Zimmern klingen wie durch die
        /// Wand, obwohl der Flur sie akustisch verbinden wuerde. Der Raum-Portal-Graph in
        /// Phase 3 loest genau das ab.
        /// </para>
        /// </summary>
        private void MeasureLineOfSight(VoiceProfile profile, ref VoiceContext context)
        {
            Vector3 direction = context.SpeakerPosition - context.ListenerPosition;
            float distance = direction.magnitude;

            if (distance < 0.01f)
            {
                context.OcclusionAmount = 0f;
                return;
            }

            direction /= distance;

            // Ausserhalb der eigenen Kapsel starten. Ein Strahl, der im CharacterController
            // beginnt, liefert in Unity oft keine weiteren Treffer - dann klingt jede Wand
            // wie freie Sicht.
            const float StartOffset = 0.45f;
            if (distance <= StartOffset)
            {
                context.OcclusionAmount = 0f;
                return;
            }

            Vector3 origin = context.ListenerPosition + direction * StartOffset;
            float remaining = distance - StartOffset;

            int count = Physics.SphereCastNonAlloc(
                origin,
                0.08f,
                direction,
                hitBuffer,
                remaining,
                profile.OcclusionLayers,
                QueryTriggerInteraction.Ignore);

            int solidHits = 0;
            float mostOpen = 0f;
            VoicePortal mostOpenPortal = null;

            for (int i = 0; i < count; i++)
            {
                var hitCollider = hitBuffer[i].collider;
                if (hitCollider == null) continue;

                // Spielerkapseln zaehlen nie als Wand - weder die eigene noch die
                // des Sprechers. Sonst ist jede Stimme sofort "hinter einer Mauer".
                if (hitCollider is CharacterController ||
                    hitCollider.GetComponentInParent<CharacterController>() != null ||
                    hitCollider.GetComponentInParent<IProximityVoicePlayer>() != null ||
                    hitCollider.GetComponentInParent<VoiceTransparent>() != null)
                {
                    continue;
                }

                var portal = hitCollider.GetComponentInParent<VoicePortal>();

                if (portal != null)
                {
                    // Portale zaehlen nicht als Wand. Ob und wie viel sie durchlassen,
                    // entscheidet das Portal-Modul anhand der Oeffnung.
                    context.HasPortal = true;

                    // Bei mehreren Tueren auf der Linie gewinnt die offenste: Schall nimmt
                    // den leichtesten Weg, er addiert keine Hindernisse.
                    if (mostOpenPortal == null || portal.Openness > mostOpen)
                    {
                        mostOpen = portal.Openness;
                        mostOpenPortal = portal;
                    }
                    continue;
                }

                solidHits++;
            }

            if (context.HasPortal)
            {
                context.Portal = mostOpenPortal;
                context.PortalOpenness = mostOpen;
                // Der akustische Weg ist die Tuer. Rahmen und Sturz daneben
                // nicht extra als volle Wand draufrechnen - sonst ist hinter
                // einer geschlossenen Tuer gar nichts mehr zu hoeren.
                context.OcclusionAmount = 0f;
            }
            else
            {
                context.OcclusionAmount = Mathf.Clamp01(solidHits / FullOcclusionHits);
            }
        }

        /// <summary>
        /// Notbehelf fuer Profile ohne Module: reine Entfernungsdaempfung.
        /// <para>
        /// Ohne diesen Zweig waere ein leeres Profil kein hoerbarer Fehler, sondern
        /// schlimmer: Alle waeren ueberall gleich laut, und die Ursache liesse sich kaum
        /// erraten.
        /// </para>
        /// </summary>
        private void ApplyFallback(VoiceProfile profile, in VoiceContext context, ref VoiceSample sample)
        {
            if (!warnedAboutEmptyProfile)
            {
                warnedAboutEmptyProfile = true;
                EarshotVoiceLog.Warn(
                    $"VoiceProfile '{profile.name}' enthaelt keine Module. Es wirkt nur die " +
                    "Entfernungsdaempfung; Waende, Tueren und Raeume bleiben ohne Wirkung.");
            }

            sample.Volume = profile.EvaluateDistanceFalloff(context.Distance);
        }
    }
}
