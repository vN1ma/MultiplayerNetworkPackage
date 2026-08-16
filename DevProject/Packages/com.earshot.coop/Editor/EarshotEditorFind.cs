using UnityEngine;

namespace Earshot.EditorTools
{
    internal static class EarshotEditorFind
    {
        public static T[] All<T>(bool includeInactive) where T : Object
        {
#if UNITY_6000_5_OR_NEWER
            return Object.FindObjectsByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#else
            return Object.FindObjectsByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#endif
        }

        public static T First<T>(bool includeInactive) where T : Object
        {
            var found = All<T>(includeInactive);
            return found.Length > 0 ? found[0] : null;
        }
    }
}
