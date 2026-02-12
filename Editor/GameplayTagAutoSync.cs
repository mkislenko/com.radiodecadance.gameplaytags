#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace RadioDecadance.GameplayTags.Editor
{
    /// <summary>
    /// Auto-syncs tags defined in GameplayTagLibrary (static GameplayTag fields) into GameplayTagConfig after compilation.
    /// </summary>
    [InitializeOnLoad]
    public static class GameplayTagAutoSync
    {
        static GameplayTagAutoSync()
        {
            // Delay a little to ensure domain is ready
            EditorApplication.delayCall += SyncAfterScriptsReload;
        }

        private static void SyncAfterScriptsReload()
        {
            try
            {
                var codeTags = CollectTagsFromLibrary();
                EnsureConfigAndApply(codeTags);
            }
            catch (Exception ex)
            {
                Debug.LogError($"GameplayTag auto-sync failed: {ex}");
            }
        }

        private static HashSet<string> CollectTagsFromLibrary()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            
            foreach (FieldInfo fieldInfo in TypeCache.GetFieldsWithAttribute<AutoGenerateTagAttribute>())
            {
                ValidateAndAddTag(fieldInfo, result);
                
            }

            return result;
        }
        

        private static void ValidateAndAddTag(FieldInfo fieldInfo, HashSet<string> acc)
        {
            if (!fieldInfo.IsStatic || !fieldInfo.IsInitOnly)
            {
                Debug.LogWarning($"Field {fieldInfo.DeclaringType?.FullName}.{fieldInfo.Name} has [AutoGenerateTag] but is not 'static readonly'. Skipping.");
                return;
            }

            if (fieldInfo.FieldType == typeof(GameplayTag))
            {
                object val = fieldInfo.GetValue(null);
                if (val is GameplayTag tag && tag.IsValid)
                {
                    // First try resolve via database, then fall back to editor-known mapping
                    string name = GameplayTagDatabase.ResolveName(tag.Id);
                    if (string.IsNullOrEmpty(name))
                    {
                        name = GameplayTag.GetKnownNameForIdInEditor(tag.Id);
                    }
                    if (!string.IsNullOrEmpty(name))
                    {
                        acc.Add(name.Trim());
                    }
                }
            }
            else
            {
                Debug.LogWarning($"Field {fieldInfo.DeclaringType?.FullName}.{fieldInfo.Name} has [AutoGenerateTag] but is not of type GameplayTag. Skipping.");
            }
        }

        private static void EnsureConfigAndApply(HashSet<string> codeTags)
        {
            // Load or create config
            var cfg = GameplayTagConfigUtility.LoadConfig();
            if (cfg == null)
            {
                
            }

            // Update generated tags and merge into tags
            Undo.RecordObject(cfg, "Update GameplayTagConfig (auto-sync)");
            cfg.SetGeneratedTags(codeTags);
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();

            // Rebuild database so drawers and comparisons work immediately
            GameplayTagDatabase.Build();
        }
    }
}
#endif
