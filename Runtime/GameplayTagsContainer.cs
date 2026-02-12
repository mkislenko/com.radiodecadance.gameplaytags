using System;
using System.Collections.Generic;
using System.Linq;

namespace RadioDecadance.GameplayTags
{
    /// <summary>
    /// A lightweight container for multiple gameplay tags with hierarchy-aware query helpers.
    /// </summary>
    [Serializable]
    public struct GameplayTagsContainer
    {
        // We keep it serialized-friendly (List) while exposing read-only APIs.
        public List<GameplayTag> Tags;

        public GameplayTagsContainer(IEnumerable<GameplayTag> tags)
        {
            Tags = tags != null ? new List<GameplayTag>(tags) : new List<GameplayTag>();
        }

        public static GameplayTagsContainer From(params GameplayTag[] tags)
        {
            return new GameplayTagsContainer(tags);
        }

        public int Count => Tags != null ? Tags.Count : 0;
        public bool IsEmpty => Count == 0;

        /// <summary>
        /// Adds a tag if valid and not already present (exact match).
        /// </summary>
        public void Add(GameplayTag tag)
        {
            if (!tag.IsValid) return;
            if (Tags == null) Tags = new List<GameplayTag>();
            if (!Tags.Contains(tag)) Tags.Add(tag);
        }

        /// <summary>
        /// Removes a tag by exact match.
        /// </summary>
        public bool Remove(GameplayTag tag)
        {
            if (Tags == null) return false;
            return Tags.Remove(tag);
        }

        public void Clear()
        {
            if (Tags == null) return;
            Tags.Clear();
        }

        /// <summary>
        /// Hierarchy-aware contains: true if any stored tag equals the provided tag or is a child of it.
        /// Equivalent to checking "Has tag or its parent".
        /// </summary>
        public readonly bool HasTag(GameplayTag tagOrParent)
        {
            if (!tagOrParent.IsValid || Tags == null || Tags.Count == 0) return false;
            for (int i = 0; i < Tags.Count; i++)
            {
                var t = Tags[i];
                if (tagOrParent.MatchesOrChildOf(t))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Exact contains: true if the exact tag exists in the container.
        /// </summary>
        public readonly bool HasTagExact(GameplayTag exact)
        {
            if (!exact.IsValid || Tags == null || Tags.Count == 0) return false;
            return Tags.Contains(exact);
        }

        /// <summary>
        /// True if any tag from 'other' is present in this container (hierarchy-aware match).
        /// </summary>
        public readonly bool HasAny(GameplayTagsContainer other)
        {
            if (Tags == null || Tags.Count == 0 || other.Tags == null || other.Tags.Count == 0) return false;
            for (int i = 0; i < other.Tags.Count; i++)
            {
                if (HasTag(other.Tags[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// True if any exact tag from 'other' is present in this container.
        /// </summary>
        public readonly bool HasAnyTagExact(GameplayTagsContainer other)
        {
            if (Tags == null || Tags.Count == 0 || other.Tags == null || other.Tags.Count == 0) return false;
            for (int i = 0; i < other.Tags.Count; i++)
            {
                if (HasTagExact(other.Tags[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// True if all tags from 'other' are present in this container (hierarchy-aware for each tag).
        /// </summary>
        public readonly bool HasAll(GameplayTagsContainer other)
        {
            if (other.Tags == null || other.Tags.Count == 0) return true;
            if (Tags == null || Tags.Count == 0) return false;
            for (int i = 0; i < other.Tags.Count; i++)
            {
                if (!HasTag(other.Tags[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// True if any exact tag from 'other' exists in this container. Alias of HasAnyTagExact for convenience.
        /// </summary>
        public readonly bool HasAnyExact(GameplayTagsContainer other)
        {
            return HasAnyTagExact(other);
        }

        /// <summary>
        /// True if all tags from 'other' exist exactly in this container.
        /// </summary>
        public readonly bool HasAllExact(GameplayTagsContainer other)
        {
            if (other.Tags == null || other.Tags.Count == 0) return true;
            if (Tags == null || Tags.Count == 0) return false;
            for (int i = 0; i < other.Tags.Count; i++)
            {
                if (!HasTagExact(other.Tags[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// Exposes tags as a read-only snapshot.
        /// </summary>
        public IReadOnlyList<GameplayTag> GetTags() => Tags ?? (IReadOnlyList<GameplayTag>)Array.Empty<GameplayTag>();

        public override string ToString()
        {
            if (Tags == null || Tags.Count == 0) return "{}";
            return "{" + string.Join(", ", Tags.Select(t => t.ToString())) + "}";
        }
    }
}
