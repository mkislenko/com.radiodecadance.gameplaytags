using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using PopupWindow = UnityEditor.PopupWindow;

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

        // UI Toolkit version - called by new UI system
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;

            // Find the 'id' field
            var idProp = property.FindPropertyRelative("id");
            if (idProp == null)
            {
                var errorLabel = new Label("Error: 'id' property not found");
                errorLabel.style.color = Color.red;
                return errorLabel;
            }

            // Label
            var label = new Label(property.displayName);
            label.AddToClassList("unity-base-field__label");
            label.style.minWidth = 120;
            container.Add(label);

            // Button to show current tag and open selector
            var button = new Button();
            button.style.flexGrow = 1;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;

            // Update button text
            void UpdateButtonText()
            {
                int currentId = idProp.intValue;
                string currentName = currentId == 0 ? "(None)" : (GameplayTagDatabase.ResolveName(currentId) ?? $"#{currentId}");
                button.text = currentName;
            }

            UpdateButtonText();

            // Click handler
            button.clicked += () =>
            {
                var rect = GUIUtility.GUIToScreenRect(button.worldBound);
                var popup = new TagSelectorWindow(property.serializedObject, idProp, UpdateButtonText);
                PopupWindow.Show(rect, popup);
            };

            // Context menu for copy
            button.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Copy", action =>
                {
                    int currentId = idProp.intValue;
                    string txt = currentId == 0 ? string.Empty : (GameplayTagDatabase.ResolveName(currentId) ?? string.Empty);
                    EditorGUIUtility.systemCopyBuffer = txt;
                });
            }));

            container.Add(button);

            // Track property changes to update button text
            container.RegisterCallback<AttachToPanelEvent>(evt =>
            {
                container.TrackPropertyValue(idProp, prop => UpdateButtonText());
            });

            return container;
        }

        // IMGUI fallback for compatibility
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var idProp = property.FindPropertyRelative("id");
            int currentId = idProp != null ? idProp.intValue : 0;
            string currentName = currentId == 0 ? "(None)" : (GameplayTagDatabase.ResolveName(currentId) ?? $"#{currentId}");

            EditorGUI.BeginProperty(position, label, property);

            bool hasLabel = label != null && !string.IsNullOrEmpty(label.text);
            Rect fieldRect = position;
            if (hasLabel)
            {
                Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
                fieldRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, position.width - EditorGUIUtility.labelWidth, position.height);
                EditorGUI.LabelField(labelRect, label);
            }

            // Context menu
            Event evt = Event.current;
            if ((evt.type == EventType.ContextClick || (evt.type == EventType.MouseDown && evt.button == 1)) && fieldRect.Contains(evt.mousePosition))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Copy"), false, () =>
                {
                    string txt = currentId == 0 ? string.Empty : (GameplayTagDatabase.ResolveName(currentId) ?? string.Empty);
                    EditorGUIUtility.systemCopyBuffer = txt;
                });
                menu.ShowAsContext();
                evt.Use();
            }

            // Button
            bool clicked = GUI.Button(fieldRect, currentName, EditorStyles.objectField);
            if (clicked && Event.current != null && Event.current.button == 0)
            {
                var popup = new TagSelectorWindow(property.serializedObject, idProp, null);
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

        private sealed class TagSelectorWindow : PopupWindowContent
        {
            private readonly SerializedObject _so;
            private readonly SerializedProperty _idProp;
            private readonly Action _onValueChanged;
            private Vector2 _scroll;
            private Rect _lastActivatorRect;
            private string _search = string.Empty;

            public TagSelectorWindow(SerializedObject so, SerializedProperty idProp, Action onValueChanged)
            {
                _so = so;
                _idProp = idProp;
                _onValueChanged = onValueChanged;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(360, 420);
            }

            public override void OnOpen()
            {
                GameplayTagDatabase.Build();

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
                if (Event.current.type == EventType.Repaint)
                {
                    _lastActivatorRect = rect;
                }

                var allTags = GameplayTagConfigUtility.GetAllTags();
                var root = BuildTree(allTags);

                GUILayout.BeginVertical();

                // Header
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
                    PopupWindow.Show(_lastActivatorRect, new AddTagPopup(() =>
                    {
                        GameplayTagDatabase.Build();
                        editorWindow.Repaint();
                    }));
                }
                GUILayout.EndHorizontal();

                // Search
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

                // None option
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
                    _onValueChanged?.Invoke();
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

            private Node BuildTree(List<string> allTags)
            {
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
                return root;
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

                    if (!string.IsNullOrEmpty(_search?.Trim()) && !PassesFilter(child))
                        continue;

                    bool hasChildren = child.Children.Count > 0;

                    Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                    float indent = depth * 16f;
                    var indented = new Rect(row.x + indent, row.y, row.width - indent, row.height);

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
                        expanded = true;
                    }

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

                    int nodeId = GameplayTagDatabase.ResolveId(child.FullPath);
                    bool isSelected = _idProp != null && _idProp.intValue == nodeId;
                    bool newChecked = GUI.Toggle(checkRect, isSelected, GUIContent.none);
                    if (newChecked != isSelected)
                    {
                        _so.Update();
                        _idProp.intValue = newChecked ? nodeId : 0;
                        _so.ApplyModifiedProperties();
                        _onValueChanged?.Invoke();
                        editorWindow.Close();
                        return;
                    }

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

                GUI.SetNextControlName(InputControlName);
                _input = EditorGUILayout.TextField("Full Tag", _input);

                if (_focusPending)
                {
                    EditorGUI.FocusTextInControl(InputControlName);
                    _focusPending = false;
                    _caretPending = true;
                }

                if (_caretPending && GUI.GetNameOfFocusedControl() == InputControlName)
                {
                    var te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                    if (te != null)
                    {
                        int len = _input?.Length ?? 0;
                        te.text = _input ?? string.Empty;
                        te.cursorIndex = len;
                        te.selectIndex = len;
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
                for (int i = 0; i < tagsProp.arraySize; i++)
                {
                    if (string.Equals(tagsProp.GetArrayElementAtIndex(i).stringValue, fullPath, StringComparison.Ordinal))
                    {
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
