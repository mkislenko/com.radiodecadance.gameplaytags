using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RadioDecadance.GameplayTags.Editor
{
    [CustomEditor(typeof(GameplayTagConfig))]
    public class GameplayTagConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty _tagsProp;
        private SerializedProperty _generatedProp;

        // Foldout state per full path
        private static readonly Dictionary<string, bool> _foldout = new Dictionary<string, bool>(StringComparer.Ordinal);

        // UI state
        private string _newTagInput = string.Empty;
        private string _renamingPath = null;
        private string _renameInput = string.Empty;

        private void OnEnable()
        {
            _tagsProp = serializedObject.FindProperty("tags");
            _generatedProp = serializedObject.FindProperty("generatedTags");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Build sets and structures
            var allTags = GetStringArray(_tagsProp).Select(t => t?.Trim()).Where(t => !string.IsNullOrEmpty(t)).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();
            var generatedSet = new HashSet<string>(GetStringArray(_generatedProp), StringComparer.Ordinal);

            var root = BuildTree(allTags, generatedSet);

            EditorGUILayout.LabelField("Gameplay Tags", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Hierarchy is inferred from dot-separated paths. Generated (code) tags are read-only.", MessageType.None);

            EditorGUILayout.Space(4);

            // Add Tag input
            EditorGUILayout.BeginHorizontal();
            _newTagInput = EditorGUILayout.TextField("", _newTagInput);
            if (GUILayout.Button("Add Tag", GUILayout.Width(100)))
            {
                if (TryAddTag(_newTagInput))
                {
                    _newTagInput = string.Empty;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // Render tree
            bool changed = false;
            RenderTree(root, 0, ref changed);

            if (changed)
            {
                // Ensure sort and uniqueness
                var updated = GetStringArray(_tagsProp).Select(t => t?.Trim()).Where(t => !string.IsNullOrEmpty(t)).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();
                SetStringArray(_tagsProp, updated);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private class Node
        {
            public string Name;           // segment name
            public string FullPath;       // full tag path up to this node
            public bool IsTag;            // exactly matches a tag entry
            public bool IsGenerated;      // the exact tag entry is generated
            public SortedDictionary<string, Node> Children = new SortedDictionary<string, Node>(StringComparer.Ordinal);
        }

        private Node BuildTree(List<string> tags, HashSet<string> generated)
        {
            var root = new Node { Name = string.Empty, FullPath = string.Empty };
            foreach (var tag in tags)
            {
                var parts = tag.Split('.');
                Node current = root;
                string currentPath = string.Empty;
                for (int i = 0; i < parts.Length; i++)
                {
                    string seg = parts[i];
                    currentPath = string.IsNullOrEmpty(currentPath) ? seg : currentPath + "." + seg;
                    if (!current.Children.TryGetValue(seg, out var child))
                    {
                        child = new Node { Name = seg, FullPath = currentPath };
                        current.Children.Add(seg, child);
                    }
                    current = child;
                    if (i == parts.Length - 1)
                    {
                        current.IsTag = true;
                        current.IsGenerated = generated.Contains(tag);
                    }
                }
            }
            return root;
        }

        private void RenderTree(Node node, int depth, ref bool changed)
        {
            // Render children of this node
            foreach (var kv in node.Children)
            {
                var child = kv.Value;
                bool hasChildren = child.Children.Count > 0;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(depth * 16f);

                if (hasChildren)
                {
                    bool isOpen = GetFoldout(child.FullPath);
                    bool newOpen = EditorGUILayout.Foldout(isOpen, child.Name, true);
                    if (newOpen != isOpen)
                    {
                        _foldout[child.FullPath] = newOpen;
                    }
                }
                else
                {
                    // leaf without children: draw label
                    GUILayout.Label(child.Name, EditorStyles.label);
                }

                GUILayout.FlexibleSpace();

                // If this node corresponds to an actual tag, we can show controls
                if (child.IsTag)
                {
                    using (new EditorGUI.DisabledScope(child.IsGenerated))
                    {
                        if (!child.IsGenerated)
                        {
                            // Rename UI or buttons
                            if (_renamingPath == child.FullPath)
                            {
                                _renameInput = EditorGUILayout.TextField(_renameInput, GUILayout.MinWidth(150));
                                if (GUILayout.Button("Confirm", GUILayout.Width(70)))
                                {
                                    string newName = _renameInput?.Trim();
                                    if (!string.IsNullOrEmpty(newName) && !TagExists(newName))
                                    {
                                        RemoveTag(child.FullPath);
                                        InsertTag(newName);
                                        changed = true;
                                    }
                                    _renamingPath = null;
                                    _renameInput = string.Empty;
                                    EditorGUILayout.EndHorizontal();
                                    // Skip further rendering this row after rename action
                                    continue;
                                }
                                if (GUILayout.Button("Cancel", GUILayout.Width(60)))
                                {
                                    _renamingPath = null;
                                    _renameInput = string.Empty;
                                    EditorGUILayout.EndHorizontal();
                                    continue;
                                }
                            }
                            else
                            {
                                if (GUILayout.Button("Rename", GUILayout.Width(60)))
                                {
                                    _renamingPath = child.FullPath;
                                    _renameInput = child.FullPath;
                                }
                                if (GUILayout.Button("-", GUILayout.Width(20)))
                                {
                                    RemoveTag(child.FullPath);
                                    changed = true;
                                    EditorGUILayout.EndHorizontal();
                                    // Skip rendering children if removed
                                    continue;
                                }
                            }
                        }
                        else
                        {
                            // small lock icon space placeholder
                            GUILayout.Space(24);
                        }
                    }
                }
                else
                {
                    GUILayout.Space(24);
                }

                EditorGUILayout.EndHorizontal();

                // Children
                if ((child.Children.Count == 0) || GetFoldout(child.FullPath))
                {
                    RenderTree(child, depth + 1, ref changed);
                }
            }
        }

        private static bool GetFoldout(string path)
        {
            if (string.IsNullOrEmpty(path)) return true; // root open
            if (_foldout.TryGetValue(path, out var v)) return v;
            _foldout[path] = true;
            return true;
        }

        private void AddNewUniqueTag(string parentPath)
        {
            string baseName = "New";
            string candidate = string.IsNullOrEmpty(parentPath) ? baseName : parentPath + "." + baseName;
            var existing = new HashSet<string>(GetStringArray(_tagsProp), StringComparer.Ordinal);
            int idx = 1;
            while (existing.Contains(candidate))
            {
                candidate = string.IsNullOrEmpty(parentPath) ? $"{baseName}{idx}" : $"{parentPath}.{baseName}{idx}";
                idx++;
            }
            InsertTag(candidate);
        }

        private void InsertTag(string fullPath)
        {
            int idx = _tagsProp.arraySize;
            _tagsProp.InsertArrayElementAtIndex(idx);
            _tagsProp.GetArrayElementAtIndex(idx).stringValue = fullPath;
        }

        private void RemoveTag(string fullPath)
        {
            for (int i = 0; i < _tagsProp.arraySize; i++)
            {
                var el = _tagsProp.GetArrayElementAtIndex(i);
                if (el.stringValue == fullPath)
                {
                    _tagsProp.DeleteArrayElementAtIndex(i);
                    return;
                }
            }
        }

        private static IEnumerable<string> GetStringArray(SerializedProperty arrayProp)
        {
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                yield return arrayProp.GetArrayElementAtIndex(i).stringValue;
            }
        }

        private static void SetStringArray(SerializedProperty arrayProp, List<string> items)
        {
            arrayProp.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                arrayProp.GetArrayElementAtIndex(i).stringValue = items[i];
            }
        }

        private bool TryAddTag(string input)
        {
            string fullPath = input?.Trim();
            if (string.IsNullOrEmpty(fullPath)) return false;
            if (TagExists(fullPath)) return false;
            InsertTag(fullPath);
            return true;
        }

        private bool TagExists(string fullPath)
        {
            for (int i = 0; i < _tagsProp.arraySize; i++)
            {
                if (string.Equals(_tagsProp.GetArrayElementAtIndex(i).stringValue, fullPath, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }
}
