using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OutfitSwitcher.Editor
{
    /// <summary>
    /// 設定を別のアバター（PC 版 → Quest 版など）へコピーする。
    /// シーン内のオブジェクトはアバターからの相対パスで記録し、貼り付け先で同じパス（無ければ同じ名前）のものに付け直す。
    /// </summary>
    public static class OutfitSwitcherTransfer
    {
        private const string Marker = "MakasTools.OutfitSwitcher.Copy/1";

        [Serializable]
        internal class Payload
        {
            public string marker = Marker;
            public string sourceAvatar;
            /// <summary>設定オブジェクトのアバターからの相対パス</summary>
            public string settingsPath;
            public string json;
            public List<Reference> refs = new List<Reference>();
        }

        [Serializable]
        internal class Reference
        {
            public string property;
            /// <summary>シーン内のオブジェクトなら true（avatarPath で探す）。false ならアセット</summary>
            public bool sceneObject;
            public string avatarPath;
            /// <summary>コンポーネントへの参照なら型名（GameObject なら空）</summary>
            public string componentType;
            public string asset;
            /// <summary>見つからなかった時の表示用</summary>
            public string name;
        }

        public enum ResolveStatus
        {
            /// <summary>同じパスで見つかった</summary>
            Path,
            /// <summary>パスは違うが、同じ名前のオブジェクトが1つだけあった</summary>
            Name,
            /// <summary>見つからない（空欄にする）</summary>
            Missing,
            /// <summary>アセット（そのまま参照）</summary>
            Asset,
        }

        public class ResolvedReference
        {
            public string Property;
            public string SourcePath;
            public ResolveStatus Status;
            public Object Value;
        }

        public class PastePlan
        {
            public string SourceAvatar;
            public string SettingsPath;
            public List<ResolvedReference> References = new List<ResolvedReference>();
            internal Payload Payload;

            /// <summary>シーンのオブジェクトをパスごとにまとめた結果（同じオブジェクトを複数箇所で参照していても1つ）</summary>
            public IEnumerable<ResolvedReference> SceneObjects => References
                .Where(r => r.Status != ResolveStatus.Asset)  // 見つからないアセットも含める
                .GroupBy(r => r.SourcePath).Select(g => g.First());
        }

        // ---------------------------------------------------------------- コピー

        public static string Serialize(OutfitSwitcherSettings settings, Transform avatarRoot)
        {
            var payload = new Payload
            {
                sourceAvatar = avatarRoot.name,
                settingsPath = RelativePath(settings.transform, avatarRoot),
                json = EditorJsonUtility.ToJson(settings),
            };

            foreach (var prop in ObjectReferences(new SerializedObject(settings)))
            {
                var value = prop.objectReferenceValue;
                if (value == null) continue;

                var reference = new Reference { property = prop.propertyPath, name = value.name };
                if (EditorUtility.IsPersistent(value))
                {
                    reference.asset = GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
                }
                else
                {
                    var go = value as GameObject ?? (value as Component)?.gameObject;
                    // アバターの外のオブジェクトは貼り付け先に無いので記録しない（空欄になる）
                    if (go == null || !go.transform.IsChildOf(avatarRoot)) continue;
                    reference.sceneObject = true;
                    reference.avatarPath = RelativePath(go.transform, avatarRoot);
                    if (value is Component c) reference.componentType = c.GetType().AssemblyQualifiedName;
                }

                payload.refs.Add(reference);
            }

            return JsonUtility.ToJson(payload);
        }

        public static void CopyToClipboard(OutfitSwitcherSettings settings, Transform avatarRoot)
        {
            EditorGUIUtility.systemCopyBuffer = Serialize(settings, avatarRoot);
        }

        public static bool ClipboardHasSettings()
        {
            var text = EditorGUIUtility.systemCopyBuffer;
            return !string.IsNullOrEmpty(text) && text.Contains(Marker);
        }

        // ---------------------------------------------------------------- 貼り付け

        /// <summary>貼り付け先でオブジェクトを探した結果。まだ何も変更しない</summary>
        public static PastePlan Plan(string text, Transform targetRoot)
        {
            Payload payload;
            try
            {
                payload = JsonUtility.FromJson<Payload>(text);
            }
            catch (ArgumentException)
            {
                return null;
            }

            if (payload == null || payload.marker != Marker) return null;

            var plan = new PastePlan { SourceAvatar = payload.sourceAvatar, SettingsPath = payload.settingsPath, Payload = payload };
            foreach (var r in payload.refs)
            {
                var resolved = new ResolvedReference
                {
                    Property = r.property,
                    SourcePath = r.sceneObject ? r.avatarPath : $"（アセット）{r.name}",
                };
                if (!r.sceneObject)
                {
                    resolved.Status = ResolveStatus.Asset;
                    resolved.Value = GlobalObjectId.TryParse(r.asset, out var id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) : null;
                    if (resolved.Value == null) resolved.Status = ResolveStatus.Missing;
                }
                else
                {
                    var t = FindByPath(targetRoot, r.avatarPath, out var byName);
                    resolved.Status = t == null ? ResolveStatus.Missing : byName ? ResolveStatus.Name : ResolveStatus.Path;
                    if (t != null)
                    {
                        if (string.IsNullOrEmpty(r.componentType))
                        {
                            resolved.Value = t.gameObject;
                        }
                        else
                        {
                            var type = Type.GetType(r.componentType);
                            resolved.Value = type != null ? t.GetComponent(type) : null;
                            if (resolved.Value == null) resolved.Status = ResolveStatus.Missing;
                        }
                    }
                }

                plan.References.Add(resolved);
            }

            return plan;
        }

        /// <summary>貼り付け先の設定を上書きする（無ければ作る）。Undo 1回で元に戻せる</summary>
        public static OutfitSwitcherSettings Apply(PastePlan plan, Transform targetRoot)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Paste OutfitSwitcher Settings");
            var group = Undo.GetCurrentGroup();

            var settings = targetRoot.GetComponentInChildren<OutfitSwitcherSettings>(true);
            if (settings == null)
            {
                var parentPath = plan.SettingsPath.Contains("/") ? plan.SettingsPath.Substring(0, plan.SettingsPath.LastIndexOf('/')) : "";
                var parent = FindByPath(targetRoot, parentPath, out _) ?? targetRoot;
                var name = string.IsNullOrEmpty(plan.SettingsPath) ? "OutfitSwitcher" : plan.SettingsPath.Split('/').Last();
                var go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Paste OutfitSwitcher Settings");
                go.transform.SetParent(parent, false);
                settings = go.AddComponent<OutfitSwitcherSettings>();
            }
            else
            {
                Undo.RecordObject(settings, "Paste OutfitSwitcher Settings");
            }

            EditorJsonUtility.FromJsonOverwrite(plan.Payload.json, settings);

            // JSON の中の参照はコピー元のオブジェクトを指しているので、すべて付け直す（見つからないものは空欄）
            var so = new SerializedObject(settings);
            var values = plan.References.ToDictionary(r => r.Property, r => r.Value);
            foreach (var prop in ObjectReferences(so))
                prop.objectReferenceValue = values.TryGetValue(prop.propertyPath, out var v) ? v : null;
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(group);
            return settings;
        }

        // ---------------------------------------------------------------- Helpers

        /// <summary>設定のオブジェクト参照（Unity 内部の m_ で始まるものは除く）</summary>
        private static List<SerializedProperty> ObjectReferences(SerializedObject so)
        {
            var result = new List<SerializedProperty>();
            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (it.propertyPath.StartsWith("m_")) continue;
                result.Add(it.Copy());
            }

            return result;
        }

        public static string RelativePath(Transform t, Transform root)
        {
            var names = new List<string>();
            for (; t != null && t != root; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        /// <summary>相対パスで探す。無ければ同じ名前のオブジェクトが1つだけある時にそれを返す（byName = true）</summary>
        private static Transform FindByPath(Transform root, string path, out bool byName)
        {
            byName = false;
            if (string.IsNullOrEmpty(path)) return root;

            // Transform.Find は名前に「/」を含むオブジェクトを探せないので、1段ずつたどる
            var found = root;
            foreach (var segment in path.Split('/'))
            {
                found = found.Cast<Transform>().FirstOrDefault(c => c.name == segment);
                if (found == null) break;
            }

            if (found != null) return found;

            var name = path.Split('/').Last();
            var candidates = root.GetComponentsInChildren<Transform>(true).Where(t => t != root && t.name == name).ToList();
            if (candidates.Count != 1) return null;
            byName = true;
            return candidates[0];
        }
    }
}
