using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RadioDecadance.GameplayTags.Editor
{
    [CustomPropertyDrawer(typeof(GameplayTag))]
    public class GameplayTagDrawer : PropertyDrawer
    {
        private static readonly Dictionary<string, bool> s_Foldout = new Dictionary<string, bool>(StringComparer.Ordinal);

        private sealed class Node
        {
            public string Name;
            public string FullPath;
            public SortedDictionary<string, Node> Children = new SortedDictionary<string, Node>(StringComparer.Ordinal);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // property is a struct; find its 'id' int field
            var idProp = property.FindPropertyRelative("id");
            int currentId = idProp != null ? idProp.intValue : 0;

            string currentName = currentId == 0 ? "(None)" : (GameplayTagDatabase.ResolveName(currentId) ?? $"#{currentId}");

            EditorGUI.BeginProperty(position, label, property);

            bool hasLabel = label != null && !string.IsNullOrEmpty(label.text);

            Rect fieldRect = position;
            if (hasLabel)
            {
                // Draw label and compute field rect like a standard property
                Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
                fieldRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, position.width - EditorGUIUtility.labelWidth, position.height);
                EditorGUI.LabelField(labelRect, label);
            }

            // Handle right-click context menu for Copy (support both ContextClick and MouseDown on RMB)
            Event evt = Event.current;
            if ((evt.type == EventType.ContextClick || (evt.type == EventType.MouseDown && evt.button == 1)) && fieldRect.Contains(evt.mousePosition))
            {
                var menu = new GenericMenu();
                bool hasValue = currentId != 0 && !string.IsNullOrEmpty(GameplayTagDatabase.ResolveName(currentId));
                menu.AddItem(new GUIContent("Copy"), false, () =>
                {
                    string txt = currentId == 0 ? string.Empty : (GameplayTagDatabase.ResolveName(currentId) ?? string.Empty);
                    EditorGUIUtility.systemCopyBuffer = txt;
                });
                menu.ShowAsContext();
                evt.Use();
                // Prevent the button below from reacting to this right-click
                // Early return is not strictly necessary, but avoids visual press state
            }

            // Entire field acts as a button that opens the selector popup (left-click only)
            bool clicked = GUI.Button(fieldRect, currentName, EditorStyles.objectField);
            if (clicked && Event.current != null && Event.current.button == 0)
            {
                // Anchor popup to the field rect
                var popup = new TagSelectorPopup(property.serializedObject, idProp);
                PopupWindow.Show(fieldRect, popup);
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        private static bool GetFoldout(string path)
        {
            // By default, foldouts are collapsed (not expanded)
            if (string.IsNullOrEmpty(path)) return false;
            if (s_Foldout.TryGetValue(path, out var v)) return v;
            s_Foldout[path] = false;
            return false;
        }

        private static void SetFoldout(string path, bool value)
        {
            if (string.IsNullOrEmpty(path)) return;
            s_Foldout[path] = value;
        }

        private sealed class TagSelectorPopup : PopupWindowContent
        {
            private readonly SerializedObject _so;
            private readonly SerializedProperty _idProp;
            private Vector2 _scroll;
            private Rect _lastActivatorRect;
            private string _search = string.Empty;

            public TagSelectorPopup(SerializedObject so, SerializedProperty idProp)
            {
                _so = so;
                _idProp = idProp;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(360, 420);
            }

            public override void OnOpen()
            {
                // Ensure database up to date
                GameplayTagDatabase.Build();

                // Expand all parent paths for the currently selected tag so it becomes visible
                try
                {
                    int selId = _idProp != null ? _idProp.intValue : 0;
                    if (selId != 0)
                    {
                        string selectedName = GameplayTagDatabase.ResolveName(selId);
                        if (!string.IsNullOrEmpty(selectedName))
                        {
                            string path = string.Empty;
                            var parts = selectedName.Split('.');
                            for (int i = 0; i < parts.Length; i++)
                            {
                                path = string.IsNullOrEmpty(path) ? parts[i] : path + "." + parts[i];
                                SetFoldout(path, true);
                            }
                        }
                    }
                }
                catch { /* no-op */ }
            }

            public override void OnGUI(Rect rect)
            {
                // Remember activator rect for reopening after Add Tag
                if (Event.current.type == EventType.Repaint)
                {
                    _lastActivatorRect = rect;
                }

                var allTags = GameplayTagConfigUtility.GetAllTags();
                // Build tree including implicit parents
                var root = new Node { Name = string.Empty, FullPath = string.Empty };
                foreach (var tag in allTags)
                {
                    if (string.IsNullOrWhiteSpace(tag)) continue;
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
                    }
                }

                GUILayout.BeginVertical();

                // Header row with Open Config and Add Tag buttons
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                GUILayout.Label("Select Gameplay Tag", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open Config", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    var cfg = GameplayTagConfigUtility.LoadConfig();
                    if (cfg != null)
                    {
                        Selection.activeObject = cfg;
                        EditorGUIUtility.PingObject(cfg);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Gameplay Tags", "GameplayTagConfig asset not found. Create one under Resources to store tags.", "OK");
                    }
                }
                if (GUILayout.Button("Add Tag", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    // Open add tag popup; keep this selector open. After adding, rebuild and refresh this window.
                    PopupWindow.Show(_lastActivatorRect, new AddTagPopup(() =>
                    {
                        GameplayTagDatabase.Build();
                        // Refresh current selector contents
                        editorWindow.Repaint();
                    }));
                }
                GUILayout.EndHorizontal();

                // Search field
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                var searchStyle = GUI.skin.FindStyle("ToolbarSearchTextField") ?? GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarSearchField;
                var cancelStyle = GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? GUI.skin.FindStyle("ToolbarSeachCancelButton");
                string newSearch = GUILayout.TextField(_search, searchStyle);
                if (cancelStyle != null)
                {
                    if (GUILayout.Button(GUIContent.none, cancelStyle))
                    {
                        newSearch = string.Empty;
                        GUI.FocusControl(null);
                    }
                }
                GUILayout.EndHorizontal();
                if (!string.Equals(_search, newSearch, StringComparison.Ordinal))
                {
                    _search = newSearch;
                }

                _scroll = GUILayout.BeginScrollView(_scroll);
                // "None" option as the first item in the tree
                GUILayout.BeginHorizontal();
                GUILayout.Label("(None)", GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                bool noneSelected = _idProp != null && _idProp.intValue == 0;
                bool noneToggle = GUILayout.Toggle(noneSelected, GUIContent.none, GUILayout.Width(18));
                if (noneToggle && !noneSelected)
                {
                    _so.Update();
                    _idProp.intValue = 0;
                    _so.ApplyModifiedProperties();
                    editorWindow.Close();
                    GUILayout.EndHorizontal();
                    GUILayout.EndScrollView();
                    GUILayout.EndVertical();
                    return;
                }
                GUILayout.EndHorizontal();

                DrawTree(root, 0);
                GUILayout.EndScrollView();

                GUILayout.EndVertical();
            }

            private bool NodeMatchesFilter(Node node)
            {
                if (string.IsNullOrEmpty(_search)) return true;
                string term = _search.Trim();
                if (term.Length == 0) return true;
                return node.FullPath?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                       || node.Name?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private bool HasVisibleDescendant(Node node)
            {
                if (node == null || node.Children == null || node.Children.Count == 0) return false;
                foreach (var kv in node.Children)
                {
                    var child = kv.Value;
                    if (NodeMatchesFilter(child) || HasVisibleDescendant(child)) return true;
                }
                return false;
            }

            private bool PassesFilter(Node node)
            {
                if (string.IsNullOrEmpty(_search)) return true;
                string term = _search.Trim();
                if (term.Length == 0) return true;
                return NodeMatchesFilter(node) || HasVisibleDescendant(node);
            }

            private void DrawTree(Node node, int depth)
            {
                foreach (var kv in node.Children)
                {
                    var child = kv.Value;

                    // Filter: skip nodes that neither match nor have matching descendants when searching
                    if (!string.IsNullOrEmpty(_search?.Trim()) && !PassesFilter(child))
                        continue;

                    bool hasChildren = child.Children.Count > 0;

                    // Manual rect-based row to prevent right-side controls from overlapping foldout clickable area
                    Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                    // Indentation
                    float indent = depth * 16f;
                    var indented = new Rect(row.x + indent, row.y, row.width - indent, row.height);

                    // Reserve right area for checkbox and '+' button
                    const float btnW = 18f;
                    const float pad = 4f;
                    float actionsWidth = btnW + pad + btnW;
                    var leftRect = new Rect(indented.x, indented.y, Mathf.Max(0f, indented.width - actionsWidth), indented.height);
                    var checkRect = new Rect(indented.x + indented.width - actionsWidth, indented.y, btnW, indented.height);
                    var plusRect = new Rect(checkRect.x + btnW + pad, indented.y, btnW, indented.height);

                    bool expanded = GetFoldout(child.FullPath);
                    bool searching = !string.IsNullOrEmpty(_search?.Trim());
                    if (searching)
                    {
                        // while searching, auto-expand to reveal matching descendants
                        expanded = true;
                    }

                    // Draw foldout with label so text also toggles expansion
                    if (hasChildren)
                    {
                        bool newExpanded = EditorGUI.Foldout(leftRect, expanded, child.Name, true);
                        if (!searching && newExpanded != expanded)
                        {
                            SetFoldout(child.FullPath, newExpanded);
                        }
                    }
                    else
                    {
                        GUI.Label(leftRect, child.Name);
                    }

                    // Checkbox to select this tag
                    int nodeId = GameplayTagDatabase.ResolveId(child.FullPath);
                    bool isSelected = _idProp != null && _idProp.intValue == nodeId;
                    bool newChecked = GUI.Toggle(checkRect, isSelected, GUIContent.none);
                    if (newChecked != isSelected)
                    {
                        _so.Update();
                        _idProp.intValue = newChecked ? nodeId : 0;
                        _so.ApplyModifiedProperties();
                        editorWindow.Close();
                        return;
                    }

                    // Small + button to add a child tag under this path
                    if (GUI.Button(plusRect, "+", EditorStyles.miniButton))
                    {
                        string start = string.IsNullOrEmpty(child.FullPath) ? string.Empty : (child.FullPath.EndsWith(".") ? child.FullPath : child.FullPath + ".");
                        PopupWindow.Show(_lastActivatorRect, new AddTagPopup(() =>
                        {
                            GameplayTagDatabase.Build();
                            SetFoldout(child.FullPath, true);
                            editorWindow.Repaint();
                        }, start));
                    }

                    if (!hasChildren) continue;
                    if (searching || GetFoldout(child.FullPath))
                    {
                        DrawTree(child, depth + 1);
                    }
                }
            }
        }

        private sealed class AddTagPopup : PopupWindowContent
        {
            private const string InputControlName = "GameplayTag_AddTag_Input";
            private string _input = string.Empty;
            private readonly Action _onDone;
            // Flags to perform focus and caret move exactly once after the control is created
            private bool _focusPending = true;
            private bool _caretPending = true;

            public AddTagPopup(Action onDone, string initialInput = null)
            {
                _onDone = onDone;
                _input = initialInput ?? string.Empty;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(360, 80);
            }

            public override void OnGUI(Rect rect)
            {
                GUILayout.Label("Add New Gameplay Tag", EditorStyles.boldLabel);

                // Name the control so we can focus it and place caret at the end
                GUI.SetNextControlName(InputControlName);
                _input = EditorGUILayout.TextField("Full Tag", _input);

                // Request focus once when the popup opens
                if (_focusPending)
                {
                    EditorGUI.FocusTextInControl(InputControlName);
                    _focusPending = false;
                    // We will set caret after focus has been applied
                    _caretPending = true;
                }

                // When the field is focused, move caret to end exactly once
                if (_caretPending && GUI.GetNameOfFocusedControl() == InputControlName)
                {
                    var te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                    if (te != null)
                    {
                        int len = _input?.Length ?? 0;
                        te.text = _input ?? string.Empty; // ensure TextEditor has the current text
                        te.cursorIndex = len;
                        te.selectIndex = len; // place caret at end without selection
                    }
                    _caretPending = false;
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Cancel"))
                {
                    editorWindow.Close();
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_input)))
                {
                    if (GUILayout.Button("Confirm"))
                    {
                        TryAddTag(_input.Trim());
                        editorWindow.Close();
                        _onDone?.Invoke();
                    }
                }
                GUILayout.EndHorizontal();
            }

            private static void TryAddTag(string fullPath)
            {
                if (string.IsNullOrEmpty(fullPath)) return;
                var cfg = GameplayTagConfigUtility.LoadConfig();
                if (cfg == null)
                {
                    EditorUtility.DisplayDialog("Gameplay Tags", "GameplayTagConfig asset not found. Create one under Resources to store tags.", "OK");
                    return;
                }

                var so = new SerializedObject(cfg);
                var tagsProp = so.FindProperty("tags");
                // Check existence
                for (int i = 0; i < tagsProp.arraySize; i++)
                {
                    if (string.Equals(tagsProp.GetArrayElementAtIndex(i).stringValue, fullPath, StringComparison.Ordinal))
                    {
                        // Already exists
                        so.ApplyModifiedProperties();
                        return;
                    }
                }
                int idx = tagsProp.arraySize;
                tagsProp.InsertArrayElementAtIndex(idx);
                tagsProp.GetArrayElementAtIndex(idx).stringValue = fullPath;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(cfg);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
