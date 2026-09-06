using UnityEngine;

namespace Earshot.Voice.Tests
{
    /// <summary>
    /// Stand-in fuer einen Avatar, ohne MonoBehaviour und ohne Netzwerk.
    /// </summary>
    internal sealed class FakeVoicePlayer : IProximityVoicePlayer
    {
        public string PlayerId { get; set; } = string.Empty;
        public bool IsLocalPlayer { get; set; }
        public string DisplayName { get; set; } = "fake";

        public bool HasIdentity => !string.IsNullOrEmpty(PlayerId);
        public Transform VoiceAnchor => null;
        public Vector3 Position => default;
    }
}
