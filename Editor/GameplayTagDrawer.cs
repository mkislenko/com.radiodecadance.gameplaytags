using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
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

            Object target = property.serializedObject.targetObject;
            string propPath = idProp.propertyPath;
            
            // Update button text
            void UpdateButtonText()
            {
                var serializedObject = new SerializedObject(target);
                var currentIdProp = serializedObject.FindProperty(propPath);
                int currentId = currentIdProp.intValue;
                string currentName = currentId == 0 ? "(None)" : (GameplayTagDatabase.ResolveName(currentId) ?? $"#{currentId}");
                button.text = currentName;
            }

            UpdateButtonText();

            // Click handler
            button.clicked += () =>
            {
                var rect = GUIUtility.GUIToScreenRect(button.worldBound);
                var popup = new TagSelectorWindow(property.serializedObject.targetObject, idProp.propertyPath, UpdateButtonText);
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
                var popup = new TagSelectorWindow(property.serializedObject.targetObject, idProp.propertyPath, null);
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
            private readonly UnityEngine.Object _target;
            private readonly string _idPropPath;
            private readonly Action _onValueChanged;
            private string _search = string.Empty;
            private ScrollView _scrollView;
            private VisualElement _treeRoot;

            public TagSelectorWindow(UnityEngine.Object target, string idPropPath, Action onValueChanged)
            {
                _target = target;
                _idPropPath = idPropPath;
                _onValueChanged = onValueChanged;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(360, 420);
            }

            private SerializedProperty GetIdProperty(out SerializedObject so)
            {
                so = null;
                if (_target == null) return null;
                so = new SerializedObject(_target);
                return so.FindProperty(_idPropPath);
            }

            public override void OnOpen()
            {
                GameplayTagDatabase.Build();

                try
                {
                    var idProp = GetIdProperty(out _);
                    int selId = idProp != null ? idProp.intValue : 0;
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

                var root = editorWindow.rootVisualElement;

                // Toolbar
                var toolbar = new Toolbar();
                toolbar.Add(new Label("Select Gameplay Tag") { style = { fontSize = 10, alignSelf = Align.Center, marginLeft = 5 } });
                var spacer = new VisualElement { style = { flexGrow = 1 } };
                toolbar.Add(spacer);

                var openConfigBtn = new ToolbarButton(() =>
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
                }) { text = "Open Config", style = { width = 90 } };
                toolbar.Add(openConfigBtn);

                var addTagBtn = new ToolbarButton();
                addTagBtn.text = "Add Tag";
                addTagBtn.style.width = 70;
                addTagBtn.clicked += () =>
                {
                    var rect = GUIUtility.GUIToScreenRect(addTagBtn.worldBound);
                    PopupWindow.Show(rect, new AddTagPopup(() =>
                    {
                        GameplayTagDatabase.Build();
                        RefreshTree();
                    }));
                };
                toolbar.Add(addTagBtn);
                root.Add(toolbar);

                // Search
                var searchToolbar = new Toolbar();
                var searchField = new ToolbarSearchField();
                searchField.style.flexGrow = 1;
                searchField.RegisterValueChangedCallback(evt =>
                {
                    _search = evt.newValue;
                    RefreshTree();
                });
                searchToolbar.Add(searchField);
                root.Add(searchToolbar);

                // ScrollView
                _scrollView = new ScrollView();
                _scrollView.style.flexGrow = 1;
                _treeRoot = new VisualElement();
                _scrollView.Add(_treeRoot);
                root.Add(_scrollView);

                RefreshTree();
            }

            private void RefreshTree()
            {
                _treeRoot.Clear();

                var allTags = GameplayTagConfigUtility.GetAllTags();
                var rootNode = BuildTree(allTags);

                // (None) option
                var noneContainer = CreateNodeParentElement(5);

                noneContainer.Add(new Label("(None)") { style = { flexGrow = 1 } });

                var idPropNone = GetIdProperty(out var soNone);
                bool noneSelected = idPropNone != null && idPropNone.intValue == 0;
                var noneToggle = new Toggle { value = noneSelected, style = { width = 18 } };
                noneToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        var p = GetIdProperty(out var s);
                        if (p != null)
                        {
                            p.intValue = 0;
                            s.ApplyModifiedProperties();
                            _onValueChanged?.Invoke();
                            editorWindow.Close();
                        }
                    }
                });
                noneContainer.Add(noneToggle);
                _treeRoot.Add(noneContainer);

                DrawTree(_treeRoot, rootNode, 0);
            }

            public override void OnGUI(Rect rect)
            {
                // UI Toolkit is used instead
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

            private void DrawTree(VisualElement parent, Node node, int depth)
            {
                foreach (var kv in node.Children)
                {
                    var child = kv.Value;

                    if (!string.IsNullOrEmpty(_search?.Trim()) && !PassesFilter(child))
                        continue;

                    bool hasChildren = child.Children.Count > 0;
                    bool searching = !string.IsNullOrEmpty(_search?.Trim());
                    bool expanded = searching || GetFoldout(child.FullPath);

                    var row = CreateNodeParentElement(depth * 16);

                    VisualElement contentContainer = row;

                    if (hasChildren)
                    {
                        var foldout = new Foldout { text = child.Name, value = expanded };
                        // Remove the default toggle and label from the foldout header so we can control it
                        // but it's easier to just style the foldout itself.
                        foldout.style.flexGrow = 1;
                        
                        if (!searching)
                        {
                            foldout.RegisterValueChangedCallback(evt =>
                            {
                                SetFoldout(child.FullPath, evt.newValue);
                                RefreshTree();
                            });
                        }

                        // We don't want the foldout to wrap its children because we handle recursion manually
                        var foldoutContent = foldout.Q<VisualElement>(className: "unity-foldout__content");
                        if (foldoutContent != null) foldoutContent.style.display = DisplayStyle.None;

                        row.Add(foldout);
                        
                        var header = foldout.Q<VisualElement>(className: "unity-foldout__input");
                        if (header != null)
                        {
                            // Adjust header to take full width and allow absolute positioning of our buttons if needed
                            // or just use flexbox.
                            header.style.flexGrow = 1;
                            header.style.flexDirection = FlexDirection.Row;
                        }
                    }
                    else
                    {
                        var label = new Label(child.Name) { style = { flexGrow = 1, marginLeft = 16 } };
                        row.Add(label);
                    }

                    // Selection Toggle
                    int nodeId = GameplayTagDatabase.ResolveId(child.FullPath);
                    var idProp = GetIdProperty(out _);
                    bool isSelected = idProp != null && idProp.intValue == nodeId;
                    
                    var checkToggle = new Toggle { value = isSelected, style = { width = 18, marginLeft = 4 } };
                    checkToggle.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue)
                        {
                            var p = GetIdProperty(out var s);
                            if (p != null)
                            {
                                p.intValue = nodeId;
                                s.ApplyModifiedProperties();
                                _onValueChanged?.Invoke();
                                editorWindow.Close();
                            }
                        }
                    });
                    
                    var plusBtn = new Button(() =>
                    {
                        string start = string.IsNullOrEmpty(child.FullPath) ? string.Empty : (child.FullPath.EndsWith(".") ? child.FullPath : child.FullPath + ".");
                        var rect = GUIUtility.GUIToScreenRect(row.worldBound);
                        PopupWindow.Show(rect, new AddTagPopup(() =>
                        {
                            GameplayTagDatabase.Build();
                            SetFoldout(child.FullPath, true);
                            RefreshTree();
                        }, start));
                    }) { text = "+", style = { width = 18, height = 16, paddingLeft = 0, paddingRight = 0, paddingTop = 0, paddingBottom = 0, marginLeft = 4, marginRight = 4 } };

                    // Add buttons to the end of the row/header
                    var actions = new VisualElement();
                    actions.style.flexDirection = FlexDirection.Row;
                    actions.style.position = Position.Absolute;
                    actions.style.right = 0;
                    actions.style.top = 0;
                    actions.style.bottom = 0;
                    actions.style.alignItems = Align.Center;
                    actions.Add(checkToggle);
                    actions.Add(plusBtn);
                    
                    row.Add(actions);
                    parent.Add(row);

                    if (hasChildren && expanded)
                    {
                        DrawTree(parent, child, depth + 1);
                    }
                }
            }

            private static VisualElement CreateNodeParentElement(int paddingLeft)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.paddingLeft = paddingLeft;
                row.style.height = EditorGUIUtility.singleLineHeight;
                row.style.alignItems = Align.Center;
                row.style.borderBottomWidth = 2;
                row.style.borderBottomColor = new Color(0f, 0f, 0f, 0.3f);

                return row;
            }
        }

        private sealed class AddTagPopup : PopupWindowContent
        {
            private const string InputControlName = "GameplayTag_AddTag_Input";
            private string _input = string.Empty;
            private readonly Action _onDone;

            public AddTagPopup(Action onDone, string initialInput = null)
            {
                _onDone = onDone;
                _input = initialInput ?? string.Empty;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(360, 80);
            }

            public override void OnOpen()
            {
                var root = editorWindow.rootVisualElement;
                root.style.paddingLeft = 5;
                root.style.paddingRight = 5;
                root.style.paddingTop = 5;
                root.style.paddingBottom = 5;

                var title = new Label("Add New Gameplay Tag");
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.marginBottom = 5;
                root.Add(title);

                var textField = new TextField("Full Tag");
                textField.name = InputControlName;
                textField.value = _input;
                textField.RegisterValueChangedCallback(evt => _input = evt.newValue);
                root.Add(textField);

                var buttons = new VisualElement();
                buttons.style.flexDirection = FlexDirection.Row;
                buttons.style.marginTop = 10;
                buttons.style.justifyContent = Justify.FlexEnd;

                var cancelButton = new Button(() => editorWindow.Close()) { text = "Cancel" };
                buttons.Add(cancelButton);

                var confirmButton = new Button(() =>
                {
                    TryAddTag(_input.Trim());
                    editorWindow.Close();
                    _onDone?.Invoke();
                }) { text = "Confirm" };
                
                // Disable confirm button if input is empty
                confirmButton.SetEnabled(!string.IsNullOrWhiteSpace(_input));
                textField.RegisterValueChangedCallback(evt => confirmButton.SetEnabled(!string.IsNullOrWhiteSpace(evt.newValue)));
                
                buttons.Add(confirmButton);
                root.Add(buttons);

                // Focus the text field
                textField.RegisterCallback<AttachToPanelEvent>(evt =>
                {
                    textField.Q("unity-text-input").Focus();
                });
            }

            public override void OnGUI(Rect rect)
            {
                // UI Toolkit is used instead
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
