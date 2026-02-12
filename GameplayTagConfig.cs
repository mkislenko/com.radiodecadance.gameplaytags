using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RadioDecadance.GameplayTags
{
    /// <summary>
    /// Central registry for all gameplay tags used across the project.
    /// Make this asset addressable with the key/address "GameplayTagConfig" to load it at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "GameplayTagConfig", menuName = "Gameplay/Gameplay Tag Config")]
    public sealed class GameplayTagConfig : ScriptableObject
    {
        [Tooltip("List of allowed gameplay tags. Use a dot notation for hierarchy (e.g., Combat.Damage.Fire)."),
         SerializeField]
        private List<string> tags = new List<string>();

        // Tracks tags generated from code (GameplayTagLibrary). Used to lock editing and for auto-sync.
        [SerializeField]
        private List<string> generatedTags = new List<string>();

        public IReadOnlyList<string> Tags => tags;

        public IReadOnlyList<string> GeneratedTags => generatedTags;

        /// <summary>
        /// Returns a cleaned, distinct, and sorted list of tags.
        /// </summary>
        public List<string> GetSanitizedTags()
        {
            return tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();
        }

        public List<string> GetSanitizedGeneratedTags()
        {
            return generatedTags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();
        }

        public void SetGeneratedTags(IEnumerable<string> newGenerated)
        {
            // Capture old generated list to be able to remove those entries from the main list
            var oldGenerated = new HashSet<string>(generatedTags ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            // Normalize new generated list
            var newGen = newGenerated?.Where(s => !string.IsNullOrWhiteSpace(s))
                                      .Select(s => s.Trim())
                                      .Distinct(StringComparer.Ordinal)
                                      .OrderBy(s => s, StringComparer.Ordinal)
                                      .ToList() ?? new List<string>();

            // Remove all old generated tags from the main list so renamed/removed code tags don't linger
            if (tags == null) tags = new List<string>();
            tags = tags.Where(t => !oldGenerated.Contains(t)).ToList();

            // Merge: add all new generated + keep any remaining (true custom) tags
            tags = newGen.Concat(tags)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(t => t, StringComparer.Ordinal)
                         .ToList();

            // Store new generated list
            generatedTags = newGen;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Sanitize both lists and keep tags consistent with generated
            var gen = GetSanitizedGeneratedTags();
            if (!generatedTags.SequenceEqual(gen, StringComparer.Ordinal))
                generatedTags = gen;

            var sanitized = GetSanitizedTags();

            // Ensure all generated are present
            foreach (var g in generatedTags)
            {
                if (!sanitized.Contains(g, StringComparer.Ordinal))
                {
                    sanitized.Add(g);
                }
            }
            sanitized = sanitized.Distinct(StringComparer.Ordinal)
                                 .OrderBy(t => t, StringComparer.Ordinal)
                                 .ToList();

            if (tags.Count != sanitized.Count || !tags.SequenceEqual(sanitized, StringComparer.Ordinal))
            {
                tags = sanitized;
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
#endif
    }

    /// <summary>
    /// Runtime/editor helper to access the gameplay tag configuration.
    /// </summary>
    public static class GameplayTagConfigUtility
    {
        private static GameplayTagConfig _cached;
        private const string AddressableKey = "GameplayTagConfig";

        public static GameplayTagConfig LoadConfig()
        {
            if (_cached != null) return _cached;

            // Try loading via Addressables (synchronously waits for completion)
            try
            {
                var handle = Addressables.LoadAssetAsync<GameplayTagConfig>(AddressableKey);
                _cached = handle.WaitForCompletion();
            }
            catch (Exception)
            {
                // ignored - fallback below
            }
            if (_cached != null) return _cached;

#if UNITY_EDITOR
            // In editor, try to locate it anywhere in the project so drawers can work without Addressables set up yet
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:GameplayTagConfig");
            if (guids != null && guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                _cached = UnityEditor.AssetDatabase.LoadAssetAtPath<GameplayTagConfig>(path);
            }
#endif
            return _cached;
        }

        public static List<string> GetAllTags()
        {
            var cfg = LoadConfig();
            if (cfg == null) return new List<string>();
            return cfg.GetSanitizedTags();
        }

        public static bool IsValid(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            var tags = GetAllTags();
            return tags.Contains(tag, StringComparer.Ordinal);
        }
    }
}
