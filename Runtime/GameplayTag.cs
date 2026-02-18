using System;
using System.Collections.Generic;
using UnityEngine;

namespace RadioDecadance.GameplayTags
{
    /// <summary>
    /// Attribute to mark static readonly GameplayTag fields for automatic synchronization into GameplayTagConfig.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class AutoGenerateTagAttribute : Attribute
    {
    }

    /// <summary>
    /// Lightweight handle for a gameplay tag. Stores only an int id at runtime.
    /// In the Editor, it draws as a string selector.
    /// </summary>
    [Serializable]
    public struct GameplayTag : IEquatable<GameplayTag>
    {
        public int Id;

        public static readonly GameplayTag None = new GameplayTag(0);

        #if UNITY_EDITOR
        // Editor-only map to remember names for tags created via FromString during editor domain.
        private static readonly Dictionary<int, string> _editorKnownNames = new Dictionary<int, string>();
        #endif

        private GameplayTag(int id)
        {
            this.Id = id;
        }

        public static GameplayTag FromId(int id) => new GameplayTag(id);

        /// <summary>
        /// Create a tag from its string path (e.g., "Effect.Slow"). If the string is null/empty, returns None.
        /// Works in both Editor and runtime; it will compute an id even if not present in config.
        /// </summary>
        public static GameplayTag FromString(string tagPath)
        {
            if (string.IsNullOrEmpty(tagPath)) return None;
            int id = GameplayTagDatabase.ComputeId(tagPath);
            #if UNITY_EDITOR
            _editorKnownNames.TryAdd(id, tagPath);
            #endif
            return new GameplayTag(id);
        }

        public bool IsNone => Id == 0;
        public bool IsValid => Id != 0;

        public bool Equals(GameplayTag other) => Id == other.Id;
        public override bool Equals(object obj) => obj is GameplayTag other && Equals(other);
        public override int GetHashCode() => Id;

        public static bool operator ==(GameplayTag a, GameplayTag b) => a.Id == b.Id;
        public static bool operator !=(GameplayTag a, GameplayTag b) => a.Id != b.Id;
        public static implicit operator GameplayTag(int id) => GameplayTag.FromId(id);
        public static implicit operator GameplayTag(string id) => GameplayTag.FromString(id);
        public static implicit operator string(GameplayTag tag) => tag.ToString();

        /// <summary>
        /// Returns true if this tag is exactly equal to other or is a child of other in the hierarchy.
        /// </summary>
        public bool MatchesOrChildOf(GameplayTag parent)
        {
            if (parent.IsNone) return false;
            if (Id == parent.Id) return true;
            return GameplayTagDatabase.IsChildOf(Id, parent.Id);
        }
        
        /// <summary>
        /// Returns the human-readable name from the database if known. In Editor it always tries to resolve; in runtime only if present in config.
        /// </summary>
        public override string ToString()
        {
            string name = GameplayTagDatabase.ResolveName(Id);
            #if UNITY_EDITOR
            if (string.IsNullOrEmpty(name))
            {
                _editorKnownNames.TryGetValue(Id, out name);
            }
            #endif
            return string.IsNullOrEmpty(name) ? $"#{Id}" : name;
        }

        /// <summary>
        /// Returns the last part of the tag path (e.g., "Slow" in "Effect.Slow").
        /// </summary>
        /// <returns></returns>
        public string LastTagString()
        {
            var fullName = ToString();
            int lastDotIndex = fullName.LastIndexOf('.');

            return lastDotIndex >= 0 ? fullName.Substring(lastDotIndex + 1) : fullName;
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Editor-only helper to retrieve the original string used to create a tag id within this editor domain.
        /// Returns null if unknown.
        /// </summary>
        public static string GetKnownNameForIdInEditor(int id)
        {
            _editorKnownNames.TryGetValue(id, out var name);
            return name;
        }
        #endif

    }

    /// <summary>
    /// Central runtime database for gameplay tags. Builds lookup tables from GameplayTagConfig.
    /// </summary>
    public static class GameplayTagDatabase
    {
        private static bool _built;
        private static readonly Dictionary<string, int> _nameToId = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<int, string> _idToName = new Dictionary<int, string>();
        // parent links: childId -> set of ancestor ids (direct and indirect)
        private static readonly Dictionary<int, HashSet<int>> _ancestors = new Dictionary<int, HashSet<int>>();

        private static void EnsureBuilt()
        {
            if (_built) return;
            Build();
        }

        public static void Build()
        {
            _nameToId.Clear();
            _idToName.Clear();
            _ancestors.Clear();

            var tags = GameplayTagConfigUtility.GetAllTags();
            foreach (var t in tags)
            {
                int id = ComputeId(t);
                if (!_nameToId.ContainsKey(t)) _nameToId.Add(t, id);
                if (!_idToName.ContainsKey(id)) _idToName.Add(id, t);

                // Build ancestors for this tag
                var parts = t.Split('.');
                int accumCount = parts.Length;
                if (accumCount > 1)
                {
                    var set = GetOrCreate(_ancestors, id);
                    string current = string.Empty;
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        current = i == 0 ? parts[0] : current + "." + parts[i];
                        int pid = ComputeId(current);
                        set.Add(pid);
                        // also add mapping for parent name->id if not present (virtual parent)
                        if (!_nameToId.ContainsKey(current)) _nameToId[current] = pid;
                        if (!_idToName.ContainsKey(pid)) _idToName[pid] = current;
                        // and union transitive ancestors if available
                        if (_ancestors.TryGetValue(pid, out var parentAnc))
                        {
                            foreach (var ga in parentAnc) set.Add(ga);
                        }
                    }
                }
            }

            _built = true;
        }

        private static HashSet<int> GetOrCreate(Dictionary<int, HashSet<int>> dict, int key)
        {
            if (!dict.TryGetValue(key, out var set))
            {
                set = new HashSet<int>();
                dict[key] = set;
            }
            return set;
        }

        /// <summary>
        /// Deterministic 32-bit FNV-1a hash of the tag string. Case-sensitive and uses '.' as provided.
        /// </summary>
        public static int ComputeId(string tagPath)
        {
            unchecked
            {
                const int offset = (int)2166136261;
                const int prime = 16777619;
                int hash = offset;
                for (int i = 0; i < tagPath.Length; i++)
                {
                    hash ^= tagPath[i];
                    hash *= prime;
                }
                // Avoid 0 as a valid id
                if (hash == 0) hash = 1;
                return hash;
            }
        }

        /// <summary>
        /// Checks if childId is a descendant of parentId using precomputed ancestor sets.
        /// Falls back to name-based check if not built.
        /// </summary>
        public static bool IsChildOf(int childId, int parentId)
        {
            if (childId == 0 || parentId == 0) return false;
            EnsureBuilt();
            if (_ancestors.TryGetValue(childId, out var anc))
            {
                return anc.Contains(parentId) || childId == parentId;
            }
            // Fallback: compare names via prefix
            string childName = ResolveName(childId);
            string parentName = ResolveName(parentId);
            if (string.IsNullOrEmpty(childName) || string.IsNullOrEmpty(parentName)) return false;
            if (childName.Equals(parentName, StringComparison.Ordinal)) return true;
            return childName.StartsWith(parentName + ".", StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolve name for an id if known (depends on config). Returns null if unknown.
        /// </summary>
        public static string ResolveName(int id)
        {
            EnsureBuilt();
            return _idToName.TryGetValue(id, out var n) ? n : null;
        }

        public static int ResolveId(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            EnsureBuilt();
            if (_nameToId.TryGetValue(name, out var id)) return id;
            return ComputeId(name);
        }
    }
}
