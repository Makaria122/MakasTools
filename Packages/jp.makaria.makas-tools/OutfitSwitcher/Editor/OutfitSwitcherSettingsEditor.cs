using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace OutfitSwitcher.Editor
{
    [CustomEditor(typeof(OutfitSwitcherSettings))]
    public class OutfitSwitcherSettingsEditor : UnityEditor.Editor
    {
        private bool _showRaw;

        public override void OnInspectorGUI()
        {
            var settings = (OutfitSwitcherSettings)target;

            if (GUILayout.Button("OutfitSwitcher ウィンドウを開く", GUILayout.Height(32)))
                OutfitSwitcherWindow.Open(settings);

            var groups = OutfitSwitcherModel.ActiveGroups(settings).ToList();
            EditorGUILayout.LabelField(
                $"グループ {groups.Count} 個 / 衣装 {groups.Sum(g => g.items.Count)} 個 / 同期 {OutfitSwitcherModel.SyncedBits(settings)} bit",
                EditorStyles.miniLabel);

            var avatar = settings.GetComponentInParent<VRCAvatarDescriptor>(true);
            var issues = OutfitSwitcherModel.Validate(settings, avatar != null ? avatar.transform : null);
            var errors = issues.Count(i => i.Severity == IssueSeverity.Error);
            var warnings = issues.Count(i => i.Severity == IssueSeverity.Warning);
            if (errors > 0 || warnings > 0)
                EditorGUILayout.HelpBox($"エラー {errors} 件 / 警告 {warnings} 件。詳細はウィンドウで確認してください。",
                    errors > 0 ? MessageType.Error : MessageType.Warning);

            EditorGUILayout.Space();
            _showRaw = EditorGUILayout.Foldout(_showRaw, "生データ（デバッグ用）");
            if (_showRaw) DrawDefaultInspector();
        }
    }
}
