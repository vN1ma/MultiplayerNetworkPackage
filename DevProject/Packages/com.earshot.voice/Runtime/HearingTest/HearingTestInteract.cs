using UnityEngine;

namespace Earshot.Voice
{
    /// <summary>
    /// E = naechster Testton, F = naechste Tuer.
    /// </summary>
    public sealed class HearingTestInteract : MonoBehaviour
    {
        [SerializeField] private float speakerRange = 5.5f;
        [SerializeField] private float doorRange = 3.2f;

        public static string LastHint { get; private set; } = "";

        private void Update()
        {
            var speaker = FindNearest<VoiceTestSpeaker>(speakerRange);
            var door = FindNearest<HearingTestDoor>(doorRange);

            if (Input.GetKeyDown(KeyCode.E) && speaker != null)
            {
                speaker.Toggle();
            }

            if (Input.GetKeyDown(KeyCode.F) && door != null)
            {
                door.Toggle();
            }

            LastHint = BuildHint(speaker, door);
        }

        private void OnDisable()
        {
            LastHint = "";
        }

        private string BuildHint(VoiceTestSpeaker speaker, HearingTestDoor door)
        {
            string text = "";
            if (speaker != null)
            {
                text += speaker.PlaybackHint;
            }

            if (door != null) text += "F: " + door.Label;
            return text;
        }

        private T FindNearest<T>(float range) where T : Component
        {
#if UNITY_6000_5_OR_NEWER
            var found = Object.FindObjectsByType<T>(FindObjectsInactive.Exclude);
#else
            var found = Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
            T best = null;
            float bestDist = range;
            Vector3 origin = transform.position;
            for (int i = 0; i < found.Length; i++)
            {
                var item = found[i];
                if (item == null) continue;
                float d = Vector3.Distance(origin, item.transform.position);
                if (d >= bestDist) continue;
                bestDist = d;
                best = item;
            }

            return best;
        }
    }
}
