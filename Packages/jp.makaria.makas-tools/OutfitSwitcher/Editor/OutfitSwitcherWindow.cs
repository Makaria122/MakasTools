using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace OutfitSwitcher.Editor
{
    public class OutfitSwitcherWindow : EditorWindow
    {
        private static readonly string[] CategoryLabels = { "Public（通常メニュー）", "Restricted（制限メニュー）" };
        private static readonly string[] ModeLabels = { "排他（どれか1つだけ）", "個別 ON/OFF" };

        private VRCAvatarDescriptor _avatar;

        // Play モードの出入りでシーンのオブジェクトが作り直されても選び直せるよう、安定した ID でも覚えておく
        [SerializeField] private string _avatarId;
        private OutfitSwitcherSettings _settings;
        private SerializedObject _so;
        private ReorderableList _groupList;
        private ReorderableList _itemList;
        private int _itemListGroup = -1;
        private ReorderableList _partList;

        private Vector2 _scroll;
        private bool _foldMenu = true;
        private bool _foldRestricted = true;
        private bool _foldParams;
        private bool _foldQuest = true;
        private bool _foldPreview = true;
        private bool _foldLinks;
        private bool _previewQuest;

        private GUIStyle _placeholderStyle;
        private GUIStyle _dropStyle;

        [MenuItem("Maka's Tools/OutfitSwitcher")]
        public static void Open()
        {
            GetWindow<OutfitSwitcherWindow>("OutfitSwitcher");
        }

        public static void Open(OutfitSwitcherSettings settings)
        {
            var window = GetWindow<OutfitSwitcherWindow>("OutfitSwitcher");
            window.SetAvatar(settings.GetComponentInParent<VRCAvatarDescriptor>(true));
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            OnSelectionChange();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode) RestoreAvatar();
            Repaint();
        }

        private void RestoreAvatar()
        {
            if (string.IsNullOrEmpty(_avatarId) || !GlobalObjectId.TryParse(_avatarId, out var id)) return;
            var avatar = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as VRCAvatarDescriptor;
            if (avatar != null) SetAvatar(avatar);
            else _avatarId = null; // 削除済みなど。毎フレーム探し直さない
        }

        private void OnUndoRedo()
        {
            _itemList = null;
            _partList = null;
            Repaint();
        }

        private void OnSelectionChange()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var go = Selection.activeGameObject;
            if (go == null || go.scene.IsValid() == false) return;

            var descriptor = go.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (descriptor != null && descriptor != _avatar) SetAvatar(descriptor);
            Repaint();
        }

        private void SetAvatar(VRCAvatarDescriptor avatar)
        {
            _avatar = avatar;
            _avatarId = avatar != null ? GlobalObjectId.GetGlobalObjectIdSlow(avatar).ToString() : null;
            Bind(avatar != null ? avatar.GetComponentInChildren<OutfitSwitcherSettings>(true) : null);
        }

        private void Bind(OutfitSwitcherSettings settings)
        {
            _settings = settings;
            _so = settings != null ? new SerializedObject(settings) : null;
            _groupList = null;
            _itemList = null;
            _partList = null;
        }

        // ================================================================ GUI

        private void OnGUI()
        {
            InitStyles();

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox("Play モード中は編集できません。Play モードを終了すると元のアバターを再表示します。", MessageType.Info);
                return;
            }

            if (_avatar == null) RestoreAvatar();

            EditorGUI.BeginChangeCheck();
            var avatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("アバター", _avatar, typeof(VRCAvatarDescriptor), true);
            if (EditorGUI.EndChangeCheck()) SetAvatar(avatar);

            if (_avatar == null)
            {
                EditorGUILayout.HelpBox("ヒエラルキーでアバターを選択するか、上の欄に VRC Avatar Descriptor を指定してください。", MessageType.Info);
                return;
            }

            if (_settings == null) SetAvatar(_avatar);
            DrawTransfer();
            if (_settings == null)
            {
                EditorGUILayout.HelpBox("このアバターにはまだ OutfitSwitcher がありません。", MessageType.Info);
                if (GUILayout.Button("OutfitSwitcher を追加", GUILayout.Height(30))) CreateSettings();
                return;
            }

            // ドメインリロード後は _settings だけ復元され SerializedObject は失われる
            if (_so == null || _so.targetObject != _settings) Bind(_settings);

            _so.Update();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawMenuSettings();
            DrawRestrictedSettings();
            DrawParameterSettings();
            DrawQuestSettings();

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Clamp(position.width * 0.35f, 220, 360))))
                    DrawGroupList();
                using (new EditorGUILayout.VerticalScope())
                    DrawGroupDetail();
            }

            EditorGUILayout.Space(8);
            DrawLinkRules();

            _so.ApplyModifiedProperties();

            EditorGUILayout.Space(8);
            DrawPreview();
            DrawIssues();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>PC 版と Quest 版など、別のアバターへ設定をコピーする</summary>
        private void DrawTransfer()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_settings == null))
                {
                    if (GUILayout.Button(new GUIContent("設定をコピー",
                            "このアバターの OutfitSwitcher の設定をクリップボードにコピーします。別のアバター（Quest 版など）を選んで「貼り付け」できます")))
                    {
                        OutfitSwitcherTransfer.CopyToClipboard(_settings, _avatar.transform);
                        ShowNotification(new GUIContent("設定をコピーしました"));
                    }
                }

                using (new EditorGUI.DisabledScope(!OutfitSwitcherTransfer.ClipboardHasSettings()))
                {
                    if (GUILayout.Button(new GUIContent("設定を貼り付け",
                            "コピーした設定でこのアバターの設定を上書きします。オブジェクトは同じパス（無ければ同じ名前）のものに付け直します")))
                        PasteSettings();
                }
            }
        }

        private void PasteSettings()
        {
            var root = _avatar.transform;
            var plan = OutfitSwitcherTransfer.Plan(EditorGUIUtility.systemCopyBuffer, root);
            if (plan == null)
            {
                EditorUtility.DisplayDialog("OutfitSwitcher", "クリップボードの内容を読み込めませんでした。もう一度コピーしてください。", "OK");
                return;
            }

            var objects = plan.SceneObjects.ToList();
            var byName = objects.Where(r => r.Status == OutfitSwitcherTransfer.ResolveStatus.Name).ToList();
            var missing = objects.Where(r => r.Status == OutfitSwitcherTransfer.ResolveStatus.Missing).ToList();

            string List(IEnumerable<string> lines)
            {
                var all = lines.ToList();
                var shown = all.Take(15).Select(l => "  " + l);
                return string.Join("\n", all.Count > 15 ? shown.Append($"  …ほか {all.Count - 15} 件") : shown);
            }

            var message = new StringBuilder();
            message.AppendLine($"「{plan.SourceAvatar}」の設定を「{_avatar.name}」に貼り付けます。");
            message.AppendLine(_settings != null ? "今の設定は上書きされます（Undo で元に戻せます）。" : "OutfitSwitcher を新しく作ります。");
            message.AppendLine();
            message.AppendLine($"オブジェクト: 同じパス {objects.Count - byName.Count - missing.Count} / 名前で一致 {byName.Count} / 見つからない {missing.Count}");
            if (byName.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("【名前で一致（パスが違う）】");
                message.AppendLine(List(byName.Select(r =>
                {
                    var go = r.Value as GameObject ?? ((Component)r.Value).gameObject;
                    return $"{r.SourcePath} → {OutfitSwitcherTransfer.RelativePath(go.transform, root)}";
                })));
            }

            if (missing.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("【見つからない（空欄になります）】");
                message.AppendLine(List(missing.Select(r => r.SourcePath)));
                message.AppendLine("対象が空欄になった衣装は、このアバターでは使えない衣装として扱われます（パラメータは残るので同期はずれません）。");
            }

            if (!EditorUtility.DisplayDialog("OutfitSwitcher: 設定の貼り付け", message.ToString().TrimEnd(), "貼り付け", "キャンセル")) return;

            var settings = OutfitSwitcherTransfer.Apply(plan, root);
            Bind(settings);
            Debug.Log("[OutfitSwitcher] 設定を貼り付けました。\n" + message, settings);
            GUIUtility.ExitGUI();
        }

        private void CreateSettings()
        {
            var go = new GameObject("OutfitSwitcher");
            Undo.RegisterCreatedObjectUndo(go, "Add OutfitSwitcher");
            go.transform.SetParent(_avatar.transform, false);
            var settings = go.AddComponent<OutfitSwitcherSettings>();
            Bind(settings);
        }

        private void DrawMenuSettings()
        {
            _foldMenu = EditorGUILayout.BeginFoldoutHeaderGroup(_foldMenu, "メニュー設定");
            if (_foldMenu)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    MenuTargetField(_so.FindProperty(nameof(OutfitSwitcherSettings.menuTarget)), "生成先",
                        "空欄 = ルートメニュー（自動）");
                    var hasTarget = _so.FindProperty(nameof(OutfitSwitcherSettings.menuTarget)).objectReferenceValue != null;
                    var flatten = _so.FindProperty(nameof(OutfitSwitcherSettings.flattenPublicMenu));
                    using (new EditorGUI.DisabledScope(!hasTarget))
                        EditorGUILayout.PropertyField(flatten, new GUIContent("サブメニューを作らない",
                            "生成先を指定した時だけ有効です。ボタンを生成先のメニューに直接並べます"));
                    using (new EditorGUI.DisabledScope(hasTarget && flatten.boolValue))
                    {
                        EditorGUILayout.PropertyField(_so.FindProperty(nameof(OutfitSwitcherSettings.menuName)), new GUIContent("メニュー名"));
                        EditorGUILayout.PropertyField(_so.FindProperty(nameof(OutfitSwitcherSettings.menuIcon)), new GUIContent("アイコン"));
                    }
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawRestrictedSettings()
        {
            _foldRestricted = EditorGUILayout.BeginFoldoutHeaderGroup(_foldRestricted, "制限メニュー設定");
            if (_foldRestricted)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    MenuTargetField(_so.FindProperty(nameof(OutfitSwitcherSettings.restrictedMenuTarget)), "生成先",
                        "空欄 = 通常メニューの中");
                    var hasTarget = _so.FindProperty(nameof(OutfitSwitcherSettings.restrictedMenuTarget)).objectReferenceValue != null;
                    var flatten = _so.FindProperty(nameof(OutfitSwitcherSettings.flattenRestrictedMenu));
                    using (new EditorGUI.DisabledScope(!hasTarget))
                        EditorGUILayout.PropertyField(flatten, new GUIContent("サブメニューを作らない",
                            "生成先を指定した時だけ有効です。ボタンを生成先のメニューに直接並べます"));
                    using (new EditorGUI.DisabledScope(hasTarget && flatten.boolValue))
                    {
                        EditorGUILayout.PropertyField(_so.FindProperty(nameof(OutfitSwitcherSettings.restrictedMenuName)), new GUIContent("メニュー名"));
                        EditorGUILayout.PropertyField(_so.FindProperty(nameof(OutfitSwitcherSettings.restrictedMenuIcon)), new GUIContent("アイコン"));
                    }

                    var useLock = _so.FindProperty(nameof(OutfitSwitcherSettings.useRestrictedLock));
                    EditorGUILayout.PropertyField(useLock, new GUIContent("ロックを使う",
                        "ON にすると、制限メニュー先頭の「ロック解除」を ON にしている間だけ制限衣装が表示されます。ロック解除は保存されず、ワールド移動・再ログインで必ずロック状態に戻ります。"));
                    if (useLock.boolValue)
                    {
                        using (new EditorGUI.IndentLevelScope())
                        {
                            EditorGUILayout.PropertyField(_so.FindProperty(nameof(OutfitSwitcherSettings.unlockLabel)), new GUIContent("ロック解除の表示名"));
                            EditorGUILayout.PropertyField(_so.FindProperty(nameof(OutfitSwitcherSettings.unlockIcon)), new GUIContent("ロック解除のアイコン"));
                        }
                    }

                    EditorGUILayout.HelpBox(
                        "メニューを分けても衣装はアバターに含まれ、ON にすれば他のプレイヤーにも同期して見えます。" +
                        "表示する場所（VRChat の利用規約を含む）はご自身で判断してください。",
                        MessageType.None);
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawParameterSettings()
        {
            var bits = OutfitSwitcherModel.SyncedBits(_settings);
            _foldParams = EditorGUILayout.BeginFoldoutHeaderGroup(_foldParams,
                $"パラメータ設定（同期 {bits} / {OutfitSwitcherModel.MaxSyncedBits} bit）");
            if (_foldParams)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_so.FindProperty(nameof(OutfitSwitcherSettings.parameterPrefix)), new GUIContent("パラメータ名の接頭辞"));
                    EditorGUILayout.LabelField("同期ビット数は OutfitSwitcher の分だけです。アバター全体の合計は VRChat SDK のビルドパネルで確認してください。",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawQuestSettings()
        {
            _foldQuest = EditorGUILayout.BeginFoldoutHeaderGroup(_foldQuest, "Quest 設定");
            if (_foldQuest)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField("現在のビルド対象",
                        OutfitSwitcherModel.IsQuestBuild() ? "Quest（Android / iOS）" : "PC");
                    EditorGUILayout.PropertyField(_so.FindProperty(nameof(OutfitSwitcherSettings.removePcOnlyObjects)),
                        new GUIContent("PC のみの衣装を削除",
                            "Quest ビルドで「PC のみ」衣装の対象オブジェクトを削除します。Quest でも使う衣装と共有しているもの、パラメータを使うギミックを含むものは残して非表示にします"));
                    EditorGUILayout.LabelField(
                        "「PC のみ」の衣装は Quest ではメニューに出ません。PC で選ばれた時は、Quest からは代わりの衣装が表示されます。" +
                        "対象オブジェクトを削除したり EditorOnly にした衣装も、自動で同じ扱いになります。パラメータは PC と Quest で常に同じです。",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void MenuTargetField(SerializedProperty prop, string label, string placeholder)
        {
            EditorGUI.BeginChangeCheck();
            var value = EditorGUILayout.ObjectField(new GUIContent(label,
                    "VRCExpressionsMenu アセット、または MA Menu Item（SubMenu/子オブジェクト）・MA Menu Group を持つ GameObject を指定できます。"),
                prop.objectReferenceValue, typeof(Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                if (value == null || value is VRCExpressionsMenu || value is GameObject) prop.objectReferenceValue = value;
                else if (value is Component c) prop.objectReferenceValue = c.gameObject;
            }

            if (prop.objectReferenceValue == null)
            {
                using (new EditorGUI.IndentLevelScope())
                    EditorGUILayout.LabelField(" ", placeholder, EditorStyles.miniLabel);
            }
        }

        // ---------------------------------------------------------------- Entries (ボタン / グループ)

        private SerializedProperty GroupsProp => _so.FindProperty(nameof(OutfitSwitcherSettings.groups));

        private void DrawGroupList()
        {
            if (_groupList == null)
            {
                _groupList = new ReorderableList(_so, GroupsProp, true, true, true, true)
                {
                    drawHeaderCallback = r => EditorGUI.LabelField(r, "メニュー項目（上から順に並ぶ）"),
                    drawElementCallback = DrawGroupElement,
                    onAddDropdownCallback = (r, l) =>
                    {
                        var menu = new GenericMenu();
                        menu.AddItem(new GUIContent("ボタン（単体の ON/OFF）"), false, () => AddFromMenu(() => AddButton(null)));
                        menu.AddItem(new GUIContent("グループ（衣装をまとめる）"), false,
                            () => AddFromMenu(() => AddEntry(OutfitEntryKind.Group, "新しいグループ", OutfitGroupMode.Exclusive, false)));
                        menu.DropDown(r);
                    },
                    onSelectCallback = _ => _itemList = null,
                    onReorderCallback = _ => _itemList = null,
                    onRemoveCallback = l =>
                    {
                        ReorderableList.defaultBehaviours.DoRemoveButton(l);
                        _itemList = null;
                    },
                };
            }

            _groupList.serializedProperty = GroupsProp;
            _groupList.DoLayoutList();

            DropArea("ここにオブジェクトをドロップ\n→ 1つずつボタンとして追加", gos =>
            {
                foreach (var go in gos) AddButton(go);
            });
            DropArea("ここにオブジェクトをドロップ\n→ まとめて排他グループにする", gos =>
            {
                var g = AddEntry(OutfitEntryKind.Group, "新しい衣装グループ", OutfitGroupMode.Exclusive, false);
                var items = g.FindPropertyRelative(nameof(OutfitGroup.items));
                foreach (var go in gos) AddItem(items, go);
                var firstActive = Array.FindIndex(gos, go => go.activeSelf);
                g.FindPropertyRelative(nameof(OutfitGroup.defaultIndex)).intValue = firstActive;
            });
        }

        /// <summary>GenericMenu のコールバックは OnGUI の外で呼ばれるので、SerializedObject を自前で更新する</summary>
        private void AddFromMenu(Action add)
        {
            _so.Update();
            add();
            _so.ApplyModifiedProperties();
            Repaint();
        }

        private void DrawGroupElement(Rect rect, int index, bool active, bool focused)
        {
            var g = GroupsProp.GetArrayElementAtIndex(index);
            var name = g.FindPropertyRelative(nameof(OutfitGroup.name)).stringValue;
            var isButton = g.FindPropertyRelative(nameof(OutfitGroup.kind)).enumValueIndex == (int)OutfitEntryKind.Button;
            var items = g.FindPropertyRelative(nameof(OutfitGroup.items));
            var restricted = g.FindPropertyRelative(nameof(OutfitGroup.category)).enumValueIndex == (int)OutfitCategory.Restricted;
            var partlyRestricted = !restricted && !isButton && Enumerable.Range(0, items.arraySize)
                .Any(i => items.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(OutfitItem.restricted)).boolValue);
            var pcOnlyCount = Enumerable.Range(0, items.arraySize)
                .Count(i => items.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(OutfitItem.pcOnly)).boolValue);
            var toggle = g.FindPropertyRelative(nameof(OutfitGroup.mode)).enumValueIndex == (int)OutfitGroupMode.Toggle;
            var count = g.FindPropertyRelative(nameof(OutfitGroup.items)).arraySize;

            rect.y += 1;
            rect.height = EditorGUIUtility.singleLineHeight;
            var displayName = string.IsNullOrWhiteSpace(name) ? "（名前なし）" : name;
            var label = isButton
                ? $"●  {displayName}"
                : $"▶  {displayName}  ({(toggle ? "ON/OFF" : "排他")} ×{count})";
            if (restricted || partlyRestricted)
            {
                var tagText = restricted ? "Private" : "一部 Private";
                var tagWidth = restricted ? 56 : 80;
                var tagRect = new Rect(rect.xMax - tagWidth, rect.y, tagWidth, rect.height);
                rect.width -= tagWidth + 2;
                var prev = GUI.color;
                GUI.color = new Color(1f, 0.55f, 0.55f);
                GUI.Label(tagRect, tagText, EditorStyles.miniButton);
                GUI.color = prev;
            }

            if (pcOnlyCount > 0)
            {
                var tagText = pcOnlyCount == count ? "PC" : "一部 PC";
                var tagWidth = pcOnlyCount == count ? 30 : 56;
                var tagRect = new Rect(rect.xMax - tagWidth, rect.y, tagWidth, rect.height);
                rect.width -= tagWidth + 2;
                var prev = GUI.color;
                GUI.color = new Color(0.55f, 0.75f, 1f);
                GUI.Label(tagRect, tagText, EditorStyles.miniButton);
                GUI.color = prev;
            }

            EditorGUI.LabelField(rect, label);
        }

        private SerializedProperty AddEntry(OutfitEntryKind kind, string name, OutfitGroupMode mode, bool useSubmenu)
        {
            var groups = GroupsProp;
            groups.arraySize++;
            var g = groups.GetArrayElementAtIndex(groups.arraySize - 1);

            // 配列追加時は直前の要素がコピーされるので全フィールドを初期化する
            g.FindPropertyRelative(nameof(OutfitGroup.kind)).enumValueIndex = (int)kind;
            g.FindPropertyRelative(nameof(OutfitGroup.name)).stringValue = name;
            g.FindPropertyRelative(nameof(OutfitGroup.icon)).objectReferenceValue = null;
            g.FindPropertyRelative(nameof(OutfitGroup.category)).enumValueIndex = (int)OutfitCategory.Public;
            g.FindPropertyRelative(nameof(OutfitGroup.mode)).enumValueIndex = (int)mode;
            g.FindPropertyRelative(nameof(OutfitGroup.useSubmenu)).boolValue = useSubmenu;
            g.FindPropertyRelative(nameof(OutfitGroup.parameterName)).stringValue = "";
            g.FindPropertyRelative(nameof(OutfitGroup.saved)).boolValue = true;
            g.FindPropertyRelative(nameof(OutfitGroup.synced)).boolValue = true;
            g.FindPropertyRelative(nameof(OutfitGroup.showNoneButton)).boolValue = false;
            g.FindPropertyRelative(nameof(OutfitGroup.defaultIndex)).intValue = -1;
            g.FindPropertyRelative(nameof(OutfitGroup.items)).arraySize = 0;

            if (_groupList != null) _groupList.index = groups.arraySize - 1;
            _itemList = null;
            return g;
        }

        private SerializedProperty AddButton(GameObject go)
        {
            var g = AddEntry(OutfitEntryKind.Button, go != null ? go.name : "新しいボタン", OutfitGroupMode.Toggle, false);
            AddItem(g.FindPropertyRelative(nameof(OutfitGroup.items)), go);
            return g;
        }

        private static void AddItem(SerializedProperty items, GameObject go)
        {
            items.arraySize++;
            var item = items.GetArrayElementAtIndex(items.arraySize - 1);
            item.FindPropertyRelative(nameof(OutfitItem.id)).stringValue = OutfitSwitcherLinks.NewId();
            item.FindPropertyRelative(nameof(OutfitItem.label)).stringValue = go != null ? go.name : "";
            item.FindPropertyRelative(nameof(OutfitItem.icon)).objectReferenceValue = null;
            item.FindPropertyRelative(nameof(OutfitItem.defaultOn)).boolValue = go != null && go.activeSelf;
            item.FindPropertyRelative(nameof(OutfitItem.restricted)).boolValue = false;
            item.FindPropertyRelative(nameof(OutfitItem.pcOnly)).boolValue = false;
            item.FindPropertyRelative(nameof(OutfitItem.fallbackIndex)).intValue = -1;
            var targets = item.FindPropertyRelative(nameof(OutfitItem.targets));
            targets.arraySize = go != null ? 1 : 0;
            if (go != null) targets.GetArrayElementAtIndex(0).objectReferenceValue = go;
            item.FindPropertyRelative(nameof(OutfitItem.parts)).arraySize = 0;
        }

        private static void AddPart(SerializedProperty parts, GameObject go)
        {
            parts.arraySize++;
            var part = parts.GetArrayElementAtIndex(parts.arraySize - 1);
            part.FindPropertyRelative(nameof(OutfitPart.id)).stringValue = OutfitSwitcherLinks.NewId();
            part.FindPropertyRelative(nameof(OutfitPart.label)).stringValue = go != null ? go.name : "";
            part.FindPropertyRelative(nameof(OutfitPart.icon)).objectReferenceValue = null;
            part.FindPropertyRelative(nameof(OutfitPart.defaultOn)).boolValue = go == null || go.activeSelf;
            part.FindPropertyRelative(nameof(OutfitPart.saved)).boolValue = true;
            var objects = part.FindPropertyRelative(nameof(OutfitPart.objects));
            objects.arraySize = 0;
            if (go != null) AddPartObject(objects, go, true);
        }

        private static readonly GUIContent[] ActiveLabels = { new GUIContent("ON"), new GUIContent("OFF") };

        /// <summary>MA の Object Toggle と同じく、パーツを ON にした時のオブジェクトの状態を ON / OFF で選ぶ</summary>
        private static void ActivePopup(Rect rect, SerializedProperty active,
            string tooltip = "パーツを ON にした時のオブジェクトの状態\nON: パーツ ON で表示、OFF で非表示\nOFF: パーツ ON で非表示、OFF で表示")
        {
            var content = new GUIContent("", tooltip);
            EditorGUI.BeginProperty(rect, content, active);
            EditorGUI.BeginChangeCheck();
            var selected = EditorGUI.Popup(rect, content, active.boolValue ? 0 : 1, ActiveLabels);
            if (EditorGUI.EndChangeCheck()) active.boolValue = selected == 0;
            EditorGUI.EndProperty();
        }

        private static void AddPartObject(SerializedProperty objects, GameObject go, bool active)
        {
            objects.arraySize++;
            var o = objects.GetArrayElementAtIndex(objects.arraySize - 1);
            o.FindPropertyRelative(nameof(OutfitPartObject.target)).objectReferenceValue = go;
            o.FindPropertyRelative(nameof(OutfitPartObject.active)).boolValue = active;
        }

        private static void CopyParts(SerializedProperty src, SerializedProperty dst)
        {
            dst.arraySize = 0;
            for (var p = 0; p < src.arraySize; p++)
            {
                var s = src.GetArrayElementAtIndex(p);
                AddPart(dst, null);
                var d = dst.GetArrayElementAtIndex(dst.arraySize - 1);
                // 移動なので ID も引き継ぎ、連動オブジェクトの条件が外れないようにする
                d.FindPropertyRelative(nameof(OutfitPart.id)).stringValue = s.FindPropertyRelative(nameof(OutfitPart.id)).stringValue;
                d.FindPropertyRelative(nameof(OutfitPart.label)).stringValue = s.FindPropertyRelative(nameof(OutfitPart.label)).stringValue;
                d.FindPropertyRelative(nameof(OutfitPart.icon)).objectReferenceValue = s.FindPropertyRelative(nameof(OutfitPart.icon)).objectReferenceValue;
                d.FindPropertyRelative(nameof(OutfitPart.defaultOn)).boolValue = s.FindPropertyRelative(nameof(OutfitPart.defaultOn)).boolValue;
                d.FindPropertyRelative(nameof(OutfitPart.saved)).boolValue = s.FindPropertyRelative(nameof(OutfitPart.saved)).boolValue;
                var so = s.FindPropertyRelative(nameof(OutfitPart.objects));
                var dobj = d.FindPropertyRelative(nameof(OutfitPart.objects));
                for (var t = 0; t < so.arraySize; t++)
                {
                    var o = so.GetArrayElementAtIndex(t);
                    AddPartObject(dobj, (GameObject)o.FindPropertyRelative(nameof(OutfitPartObject.target)).objectReferenceValue,
                        o.FindPropertyRelative(nameof(OutfitPartObject.active)).boolValue);
                }
            }
        }

        // ---------------------------------------------------------------- Entry detail

        private void DrawGroupDetail()
        {
            var groups = GroupsProp;
            var index = _groupList?.index ?? -1;
            if (index < 0 || index >= groups.arraySize)
            {
                EditorGUILayout.HelpBox("左のリストからボタンかグループを選択してください。", MessageType.Info);
                return;
            }

            var g = groups.GetArrayElementAtIndex(index);
            var group = _settings.groups.ElementAtOrDefault(index);
            if (g.FindPropertyRelative(nameof(OutfitGroup.kind)).enumValueIndex == (int)OutfitEntryKind.Button)
                DrawButtonDetail(index, g, group);
            else
                DrawGroupSettings(index, g, group);
        }

        private void DrawButtonDetail(int index, SerializedProperty g, OutfitGroup group)
        {
            EditorGUILayout.LabelField("ボタン設定", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(g.FindPropertyRelative(nameof(OutfitGroup.name)), new GUIContent("表示名"));
            EditorGUILayout.PropertyField(g.FindPropertyRelative(nameof(OutfitGroup.icon)), new GUIContent("アイコン"));
            DrawCategoryPopup(g);

            var items = g.FindPropertyRelative(nameof(OutfitGroup.items));
            if (items.arraySize == 0) AddItem(items, null);
            var item = items.GetArrayElementAtIndex(0);
            EditorGUILayout.PropertyField(item.FindPropertyRelative(nameof(OutfitItem.defaultOn)), new GUIContent("初期状態で ON"));
            EditorGUILayout.PropertyField(item.FindPropertyRelative(nameof(OutfitItem.pcOnly)), PcOnlyLabel);
            DrawParameterFields(g, group);

            EditorGUILayout.Space(4);
            var targets = item.FindPropertyRelative(nameof(OutfitItem.targets));
            EditorGUILayout.PropertyField(targets, new GUIContent($"対象オブジェクト（{targets.arraySize}）",
                "ボタンを ON にすると、ここにあるオブジェクトがまとめて表示されます"), true);

            DropArea("ここにオブジェクトをドロップ → 対象に追加", gos =>
            {
                foreach (var go in gos)
                {
                    targets.arraySize++;
                    targets.GetArrayElementAtIndex(targets.arraySize - 1).objectReferenceValue = go;
                }
            });

            DrawPartList(item, group != null ? OutfitSwitcherModel.DisplayLabel(group, 0) : "");
            DrawMoveToGroup(index, g, item);
        }

        private int _moveTargetGroup;

        /// <summary>ボタンをグループの衣装として移動する。グループのパラメータを共有するので、他の衣装と排他で切り替わる</summary>
        private void DrawMoveToGroup(int index, SerializedProperty button, SerializedProperty item)
        {
            var groups = GroupsProp;
            var candidates = Enumerable.Range(0, groups.arraySize)
                .Where(i => groups.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(OutfitGroup.kind)).enumValueIndex == (int)OutfitEntryKind.Group)
                .ToList();
            if (candidates.Count == 0) return;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("グループに移動", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("グループの衣装にすると、同じパラメータで他の衣装と切り替わるようになります。" +
                                       "Restricted のボタンは「制限メニューに置く」が ON の状態で移動します。", EditorStyles.wordWrappedMiniLabel);

            var names = candidates.Select(i =>
            {
                var name = groups.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(OutfitGroup.name)).stringValue;
                return string.IsNullOrWhiteSpace(name) ? $"（名前なし {i + 1}）" : name;
            }).ToArray();

            using (new EditorGUILayout.HorizontalScope())
            {
                _moveTargetGroup = Mathf.Clamp(_moveTargetGroup, 0, candidates.Count - 1);
                _moveTargetGroup = EditorGUILayout.Popup(_moveTargetGroup, names);
                if (!GUILayout.Button("このグループに移動", GUILayout.Width(130))) return;
            }

            var dstIndex = candidates[_moveTargetGroup];
            var dstItems = groups.GetArrayElementAtIndex(dstIndex).FindPropertyRelative(nameof(OutfitGroup.items));
            AddItem(dstItems, null);
            var moved = dstItems.GetArrayElementAtIndex(dstItems.arraySize - 1);
            moved.FindPropertyRelative(nameof(OutfitItem.id)).stringValue = item.FindPropertyRelative(nameof(OutfitItem.id)).stringValue;
            moved.FindPropertyRelative(nameof(OutfitItem.label)).stringValue = button.FindPropertyRelative(nameof(OutfitGroup.name)).stringValue;
            moved.FindPropertyRelative(nameof(OutfitItem.icon)).objectReferenceValue = button.FindPropertyRelative(nameof(OutfitGroup.icon)).objectReferenceValue;
            moved.FindPropertyRelative(nameof(OutfitItem.restricted)).boolValue =
                button.FindPropertyRelative(nameof(OutfitGroup.category)).enumValueIndex == (int)OutfitCategory.Restricted;
            moved.FindPropertyRelative(nameof(OutfitItem.pcOnly)).boolValue = item.FindPropertyRelative(nameof(OutfitItem.pcOnly)).boolValue;

            var srcTargets = item.FindPropertyRelative(nameof(OutfitItem.targets));
            var dstTargets = moved.FindPropertyRelative(nameof(OutfitItem.targets));
            dstTargets.arraySize = srcTargets.arraySize;
            for (var t = 0; t < srcTargets.arraySize; t++)
                dstTargets.GetArrayElementAtIndex(t).objectReferenceValue = srcTargets.GetArrayElementAtIndex(t).objectReferenceValue;
            CopyParts(item.FindPropertyRelative(nameof(OutfitItem.parts)), moved.FindPropertyRelative(nameof(OutfitItem.parts)));

            groups.DeleteArrayElementAtIndex(index);
            _groupList.index = dstIndex > index ? dstIndex - 1 : dstIndex;
            _itemList = null;
            _so.ApplyModifiedProperties();
            GUIUtility.ExitGUI();
        }

        private void DrawGroupSettings(int index, SerializedProperty g, OutfitGroup group)
        {
            var mode = g.FindPropertyRelative(nameof(OutfitGroup.mode));
            var isExclusive = mode.enumValueIndex == (int)OutfitGroupMode.Exclusive;

            EditorGUILayout.LabelField("グループ設定", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(g.FindPropertyRelative(nameof(OutfitGroup.name)), new GUIContent("グループ名"));
            EditorGUILayout.PropertyField(g.FindPropertyRelative(nameof(OutfitGroup.icon)), new GUIContent("アイコン"));
            DrawCategoryPopup(g);

            EditorGUI.BeginChangeCheck();
            var newMode = EditorGUILayout.Popup("切り替え方式", mode.enumValueIndex, ModeLabels);
            if (EditorGUI.EndChangeCheck())
            {
                mode.enumValueIndex = newMode;
                isExclusive = newMode == (int)OutfitGroupMode.Exclusive;
                _itemList = null;
            }

            EditorGUILayout.PropertyField(g.FindPropertyRelative(nameof(OutfitGroup.useSubmenu)), new GUIContent("サブメニューにまとめる"));
            DrawParameterFields(g, group);

            var items = g.FindPropertyRelative(nameof(OutfitGroup.items));
            if (isExclusive)
            {
                DrawDefaultIndexPopup(g, items);
                if (group != null && OutfitSwitcherModel.DefaultItemIndex(group) >= 0)
                    EditorGUILayout.PropertyField(g.FindPropertyRelative(nameof(OutfitGroup.showNoneButton)),
                        new GUIContent("「なし」ボタンを追加", "全部 OFF にするボタンをメニューに追加します。ボタンを解除すると初期衣装に戻ります"));
            }

            EditorGUILayout.Space(4);
            var groupRestricted = g.FindPropertyRelative(nameof(OutfitGroup.category)).enumValueIndex == (int)OutfitCategory.Restricted;
            DrawItemList(index, g, items, isExclusive, groupRestricted);

            DropArea("ここにオブジェクトをドロップ → 衣装として追加", gos =>
            {
                foreach (var go in gos) AddItem(items, go);
            });

            var selected = _itemList?.index ?? -1;
            if (selected >= 0 && selected < items.arraySize)
            {
                var label = group != null && selected < group.items.Count
                    ? OutfitSwitcherModel.ItemLabel(group.items[selected], selected)
                    : "";
                DrawPartList(items.GetArrayElementAtIndex(selected), label);
            }
            else
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("衣装を選択すると、その衣装の中のパーツ（個別 ON/OFF）を設定できます。", EditorStyles.wordWrappedMiniLabel);
            }
        }

        /// <summary>衣装内のパーツ。1つ以上あると、メニューでは衣装名のサブメニューに衣装とパーツのトグルが並ぶ</summary>
        private void DrawPartList(SerializedProperty item, string itemLabel)
        {
            var parts = item.FindPropertyRelative(nameof(OutfitItem.parts));

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField($"「{itemLabel}」のパーツ（個別 ON/OFF）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("衣装の中のオブジェクトを個別に ON/OFF できます。パーツがあると、メニューでは衣装名のサブメニューに" +
                                       "衣装のトグルとパーツのトグルが並びます。パーツは衣装の対象オブジェクトの子にしてください（衣装を脱ぐと一緒に隠れます）。",
                EditorStyles.wordWrappedMiniLabel);

            if (_partList == null || _partList.serializedProperty == null || _partList.serializedProperty.propertyPath != parts.propertyPath)
            {
                _partList = new ReorderableList(_so, parts, true, true, true, true)
                {
                    drawHeaderCallback = r => EditorGUI.LabelField(r, "パーツ（上から順にメニューに並ぶ）"),
                    onAddCallback = l => AddPart(l.serializedProperty, null),
                };
            }

            var line = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;

            _partList.serializedProperty = parts;
            _partList.elementHeightCallback = i =>
            {
                var objects = parts.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(OutfitPart.objects));
                // 表示名・トグル・見出し・オブジェクトの行・追加ボタン
                return (4 + objects.arraySize) * (line + spacing) + spacing * 2;
            };
            _partList.drawElementCallback = (rect, i, a, f) =>
            {
                var part = parts.GetArrayElementAtIndex(i);
                var r = new Rect(rect.x, rect.y + spacing, rect.width, line);

                var labelProp = part.FindPropertyRelative(nameof(OutfitPart.label));
                var labelRect = new Rect(r.x, r.y, r.width - 124, line);
                EditorGUI.PropertyField(labelRect, labelProp, GUIContent.none);
                if (string.IsNullOrEmpty(labelProp.stringValue)) DrawPlaceholder(labelRect, "表示名（空欄 = オブジェクト名）", false);
                EditorGUI.ObjectField(new Rect(r.xMax - 120, r.y, 120, line), part.FindPropertyRelative(nameof(OutfitPart.icon)),
                    typeof(Texture2D), GUIContent.none);

                r.y += line + spacing;
                ToggleLeft(new Rect(r.x, r.y, r.width / 2 - 2, line), part.FindPropertyRelative(nameof(OutfitPart.defaultOn)),
                    new GUIContent("初期状態で ON"));
                ToggleLeft(new Rect(r.x + r.width / 2 + 2, r.y, r.width / 2 - 2, line), part.FindPropertyRelative(nameof(OutfitPart.saved)),
                    new GUIContent("保存する", "OFF にすると、ワールド移動・再ログインのたびに「初期状態で ON」の状態に戻ります。同期はグループの設定に従います"));

                r.y += line + spacing;
                var objects = part.FindPropertyRelative(nameof(OutfitPart.objects));
                EditorGUI.LabelField(r, $"対象オブジェクト（{objects.arraySize}）　パーツを ON にした時の状態", EditorStyles.miniLabel);

                var removeAt = -1;
                for (var o = 0; o < objects.arraySize; o++)
                {
                    r.y += line + spacing;
                    var obj = objects.GetArrayElementAtIndex(o);
                    EditorGUI.PropertyField(new Rect(r.x + 10, r.y, r.width - 10 - 90, line),
                        obj.FindPropertyRelative(nameof(OutfitPartObject.target)), GUIContent.none);
                    ActivePopup(new Rect(r.xMax - 84, r.y, 58, line), obj.FindPropertyRelative(nameof(OutfitPartObject.active)));
                    if (GUI.Button(new Rect(r.xMax - 22, r.y, 22, line), "×")) removeAt = o;
                }

                r.y += line + spacing;
                if (GUI.Button(new Rect(r.x + 10, r.y, 120, line), "+ オブジェクト追加")) AddPartObject(objects, null, true);
                if (removeAt >= 0) objects.DeleteArrayElementAtIndex(removeAt);
            };

            _partList.DoLayoutList();

            DropArea("ここにオブジェクトをドロップ → パーツとして追加", gos =>
            {
                foreach (var go in gos) AddPart(parts, go);
            });
        }

        private static void DrawCategoryPopup(SerializedProperty g)
        {
            var category = g.FindPropertyRelative(nameof(OutfitGroup.category));
            EditorGUI.BeginChangeCheck();
            var newCategory = EditorGUILayout.Popup("表示先", category.enumValueIndex, CategoryLabels);
            if (EditorGUI.EndChangeCheck()) category.enumValueIndex = newCategory;
        }

        private void DrawParameterFields(SerializedProperty g, OutfitGroup group)
        {
            var paramName = g.FindPropertyRelative(nameof(OutfitGroup.parameterName));
            EditorGUILayout.PropertyField(paramName, new GUIContent("パラメータ名"));
            if (string.IsNullOrEmpty(paramName.stringValue) && group != null)
                DrawPlaceholder(GUILayoutUtility.GetLastRect(), OutfitSwitcherModel.GroupParameter(_settings, group) + "（自動）", true);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(g.FindPropertyRelative(nameof(OutfitGroup.saved)), new GUIContent("保存する"));
                EditorGUILayout.PropertyField(g.FindPropertyRelative(nameof(OutfitGroup.synced)), new GUIContent("同期する"));
            }
        }

        private void DrawDefaultIndexPopup(SerializedProperty group, SerializedProperty items)
        {
            var defaultIndex = group.FindPropertyRelative(nameof(OutfitGroup.defaultIndex));
            var labels = new List<string> { "なし" };
            for (var i = 0; i < items.arraySize; i++)
            {
                var label = items.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(OutfitItem.label)).stringValue;
                labels.Add($"{i + 1}. {(string.IsNullOrWhiteSpace(label) ? "（名前なし）" : label)}");
            }

            if (labels.Count == 0) return;

            const int offset = 1;
            var current = Mathf.Clamp(defaultIndex.intValue + offset, 0, labels.Count - 1);
            EditorGUI.BeginChangeCheck();
            var selected = EditorGUILayout.Popup("初期衣装", current, labels.ToArray());
            if (EditorGUI.EndChangeCheck()) defaultIndex.intValue = selected - offset;
        }

        private static readonly GUIContent PcOnlyLabel = new GUIContent("PC のみ",
            "Quest ではメニューに出さず、PC で選ばれた時は代わりの衣装を表示します。パラメータは PC と同じまま残ります");

        private void DrawItemList(int groupIndex, SerializedProperty g, SerializedProperty items, bool isExclusive, bool groupRestricted)
        {
            if (_itemList == null || _itemListGroup != groupIndex)
            {
                _itemListGroup = groupIndex;
                _itemList = new ReorderableList(_so, items, true, true, true, true)
                {
                    drawHeaderCallback = r => EditorGUI.LabelField(r, "衣装（上から順にメニューに並ぶ）"),
                    onAddCallback = l => AddItem(l.serializedProperty, null),
                };
            }

            // 並べ替えで初期衣装・代わりの衣装の指定がずれないように付け替える
            _itemList.onReorderCallbackWithDetails = (l, from, to) =>
            {
                int Remap(int x)
                {
                    if (x == from) return to;
                    if (from < to && x > from && x <= to) return x - 1;
                    if (to < from && x >= to && x < from) return x + 1;
                    return x;
                }

                var defaultIndex = g.FindPropertyRelative(nameof(OutfitGroup.defaultIndex));
                if (defaultIndex.intValue >= 0) defaultIndex.intValue = Remap(defaultIndex.intValue);
                for (var i = 0; i < items.arraySize; i++)
                {
                    var fallback = items.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(OutfitItem.fallbackIndex));
                    if (fallback.intValue >= 0) fallback.intValue = Remap(fallback.intValue);
                }
            };

            var line = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;

            _itemList.serializedProperty = items;
            _itemList.elementHeightCallback = i =>
            {
                var targets = items.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(OutfitItem.targets));
                const int rows = 3;
                return rows * (line + spacing) + EditorGUI.GetPropertyHeight(targets, true) + spacing * 3;
            };
            _itemList.drawElementCallback = (rect, i, a, f) =>
            {
                var item = items.GetArrayElementAtIndex(i);
                var r = new Rect(rect.x, rect.y + spacing, rect.width, line);

                var labelProp = item.FindPropertyRelative(nameof(OutfitItem.label));
                var iconRect = new Rect(r.xMax - 120, r.y, 120, line);
                var labelRect = new Rect(r.x, r.y, r.width - 124, line);
                EditorGUI.PropertyField(labelRect, labelProp, GUIContent.none);
                if (string.IsNullOrEmpty(labelProp.stringValue)) DrawPlaceholder(labelRect, "表示名（空欄 = オブジェクト名）", false);
                var icon = item.FindPropertyRelative(nameof(OutfitItem.icon));
                EditorGUI.ObjectField(iconRect, icon, typeof(Texture2D), GUIContent.none);

                r.y += line + spacing;
                var half = new Rect(r.x, r.y, r.width / 2 - 2, line);
                var restrictedLabel = new GUIContent("制限メニューに置く",
                    "ボタンだけを制限メニューに置きます。パラメータはグループと共有なので、選ぶと他の衣装と切り替わります");
                using (new EditorGUI.DisabledScope(groupRestricted))
                {
                    if (groupRestricted) EditorGUI.ToggleLeft(half, restrictedLabel, true);
                    else ToggleLeft(half, item.FindPropertyRelative(nameof(OutfitItem.restricted)), restrictedLabel);
                }

                if (!isExclusive)
                {
                    var right = new Rect(r.x + r.width / 2 + 2, r.y, r.width / 2 - 2, line);
                    ToggleLeft(right, item.FindPropertyRelative(nameof(OutfitItem.defaultOn)), new GUIContent("初期状態で ON"));
                }

                r.y += line + spacing;
                half = new Rect(r.x, r.y, r.width / 2 - 2, line);
                var pcOnly = item.FindPropertyRelative(nameof(OutfitItem.pcOnly));
                ToggleLeft(half, pcOnly, PcOnlyLabel);
                if (isExclusive && pcOnly.boolValue)
                {
                    var right = new Rect(r.x + r.width / 2 + 2, r.y, r.width / 2 - 2, line);
                    DrawFallbackPopup(right, g, items, i);
                }

                r.y += line + spacing;
                var targets = item.FindPropertyRelative(nameof(OutfitItem.targets));
                r.height = EditorGUI.GetPropertyHeight(targets, true);
                r.x += 10;
                r.width -= 10;
                var partCount = item.FindPropertyRelative(nameof(OutfitItem.parts)).arraySize;
                EditorGUI.PropertyField(r, targets,
                    new GUIContent($"対象オブジェクト（{targets.arraySize}）{(partCount > 0 ? $"  ／ パーツ {partCount}" : "")}"), true);
            };

            _itemList.DoLayoutList();
        }

        /// <summary>Quest で代わりに表示する衣装。先頭は「初期衣装」</summary>
        private static void DrawFallbackPopup(Rect rect, SerializedProperty g, SerializedProperty items, int itemIndex)
        {
            var fallback = items.GetArrayElementAtIndex(itemIndex).FindPropertyRelative(nameof(OutfitItem.fallbackIndex));
            var indices = new List<int> { -1 };
            var labels = new List<string> { "代わり: 初期衣装" };
            for (var i = 0; i < items.arraySize; i++)
            {
                if (i == itemIndex) continue;
                var other = items.GetArrayElementAtIndex(i);
                if (other.FindPropertyRelative(nameof(OutfitItem.pcOnly)).boolValue) continue;
                var label = other.FindPropertyRelative(nameof(OutfitItem.label)).stringValue;
                indices.Add(i);
                labels.Add($"代わり: {i + 1}. {(string.IsNullOrWhiteSpace(label) ? "（名前なし）" : label)}");
            }

            var current = Mathf.Max(0, indices.IndexOf(fallback.intValue));
            EditorGUI.BeginChangeCheck();
            var selected = EditorGUI.Popup(rect, current, labels.ToArray());
            if (EditorGUI.EndChangeCheck()) fallback.intValue = indices[selected];
        }

        // ---------------------------------------------------------------- 高度な設定: 連動オブジェクト

        private static readonly string[] ConnectorLabels = { "AND", "OR" };

        /// <summary>条件に選べる衣装・パーツ・ロック解除</summary>
        private readonly struct LinkSource
        {
            public readonly LinkSourceKind Kind;
            public readonly int Group, Item, Part;
            public readonly string Label;

            public LinkSource(LinkSourceKind kind, int group, int item, int part, string label)
            {
                Kind = kind;
                Group = group;
                Item = item;
                Part = part;
                Label = label;
            }
        }

        private List<LinkSource> CollectLinkSources()
        {
            var sources = new List<LinkSource>();
            for (var gi = 0; gi < _settings.groups.Count; gi++)
            {
                var group = _settings.groups[gi];
                var isButton = OutfitSwitcherModel.IsButton(group);
                // ポップアップは「/」でサブメニューになるので、名前の「/」は置き換える
                var groupName = Escape(string.IsNullOrWhiteSpace(group.name) ? $"（名前なし {gi + 1}）" : group.name);
                var root = isButton ? "ボタン" : groupName;
                var suffix = isButton ? "" : OutfitSwitcherModel.IsExclusive(group) ? "（選択中）" : "";
                for (var ii = 0; ii < group.items.Count; ii++)
                {
                    var itemName = Escape(OutfitSwitcherModel.DisplayLabel(group, ii));
                    sources.Add(new LinkSource(LinkSourceKind.Item, gi, ii, -1, $"{root}/{itemName}{suffix}"));
                    var parts = group.items[ii].parts;
                    for (var pi = 0; pi < parts.Count; pi++)
                        sources.Add(new LinkSource(LinkSourceKind.Part, gi, ii, pi,
                            $"{root}/{itemName} のパーツ/{Escape(OutfitSwitcherModel.PartLabel(parts[pi], pi))}"));
                }
            }

            sources.Add(new LinkSource(LinkSourceKind.Unlock, -1, -1, -1, $"ロック解除（{Escape(_settings.unlockLabel)}）"));
            return sources;
        }

        private static string Escape(string s) => s.Replace("/", "／");

        private string SourceId(LinkSource s)
        {
            switch (s.Kind)
            {
                case LinkSourceKind.Item: return _settings.groups[s.Group].items[s.Item].id;
                case LinkSourceKind.Part: return _settings.groups[s.Group].items[s.Item].parts[s.Part].id;
                default: return "";
            }
        }

        /// <summary>参照する衣装・パーツの ID。旧データで空欄なら、ここで初めて割り当てる</summary>
        private string EnsureSourceId(LinkSource s)
        {
            if (s.Kind == LinkSourceKind.Unlock) return "";
            var item = GroupsProp.GetArrayElementAtIndex(s.Group).FindPropertyRelative(nameof(OutfitGroup.items)).GetArrayElementAtIndex(s.Item);
            var idProp = s.Kind == LinkSourceKind.Item
                ? item.FindPropertyRelative(nameof(OutfitItem.id))
                : item.FindPropertyRelative(nameof(OutfitItem.parts)).GetArrayElementAtIndex(s.Part).FindPropertyRelative(nameof(OutfitPart.id));
            if (string.IsNullOrEmpty(idProp.stringValue)) idProp.stringValue = OutfitSwitcherLinks.NewId();
            return idProp.stringValue;
        }

        private void DrawLinkRules()
        {
            var rules = _so.FindProperty(nameof(OutfitSwitcherSettings.linkRules));
            _foldLinks = EditorGUILayout.BeginFoldoutHeaderGroup(_foldLinks, $"高度な設定: 連動オブジェクト（{rules.arraySize}）");
            if (_foldLinks)
            {
                EditorGUILayout.LabelField(
                    "衣装・パーツの ON / OFF の組み合わせで、別のオブジェクトを表示・非表示にします（例: パーツ A と B が両方 OFF なら C を表示）。" +
                    "AND は OR より先に結びつきます。NOT は「OFF の時」「選択していない時」の意味です。今あるパラメータから判定するので、パラメータは増えません。",
                    EditorStyles.wordWrappedMiniLabel);

                var sources = CollectLinkSources();
                var removeRule = -1;
                var moveRule = (from: -1, to: -1);
                for (var r = 0; r < rules.arraySize; r++)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        var action = DrawLinkRule(rules.GetArrayElementAtIndex(r), r, sources);
                        if (action == -1) removeRule = r;
                        else if (action == 1 && r > 0) moveRule = (r, r - 1);
                        else if (action == 2 && r < rules.arraySize - 1) moveRule = (r, r + 1);
                    }
                }

                if (removeRule >= 0) rules.DeleteArrayElementAtIndex(removeRule);
                else if (moveRule.from >= 0) rules.MoveArrayElement(moveRule.from, moveRule.to);

                if (GUILayout.Button("+ 連動オブジェクトのルールを追加"))
                {
                    rules.arraySize++;
                    var rule = rules.GetArrayElementAtIndex(rules.arraySize - 1);
                    rule.FindPropertyRelative(nameof(LinkRule.name)).stringValue = "";
                    var conditions = rule.FindPropertyRelative(nameof(LinkRule.conditions));
                    conditions.arraySize = 0;
                    AddLinkCondition(conditions);
                    rule.FindPropertyRelative(nameof(LinkRule.objects)).arraySize = 0;
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void AddLinkCondition(SerializedProperty conditions)
        {
            conditions.arraySize++;
            var c = conditions.GetArrayElementAtIndex(conditions.arraySize - 1);
            c.FindPropertyRelative(nameof(LinkCondition.connector)).enumValueIndex = (int)LinkConnector.And;
            c.FindPropertyRelative(nameof(LinkCondition.not)).boolValue = false;
            c.FindPropertyRelative(nameof(LinkCondition.kind)).enumValueIndex = (int)LinkSourceKind.Item;
            c.FindPropertyRelative(nameof(LinkCondition.sourceId)).stringValue = "";
        }

        /// <summary>ルール1つ分。戻り値: -1 = 削除、1 = 上へ、2 = 下へ、0 = なし</summary>
        private int DrawLinkRule(SerializedProperty rule, int index, List<LinkSource> sources)
        {
            var action = 0;
            using (new EditorGUILayout.HorizontalScope())
            {
                var nameProp = rule.FindPropertyRelative(nameof(LinkRule.name));
                EditorGUILayout.PropertyField(nameProp, GUIContent.none);
                if (string.IsNullOrEmpty(nameProp.stringValue))
                    DrawPlaceholder(GUILayoutUtility.GetLastRect(), $"名前（空欄 = 連動 {index + 1}）", false);
                if (GUILayout.Button("↑", GUILayout.Width(24))) action = 1;
                if (GUILayout.Button("↓", GUILayout.Width(24))) action = 2;
                if (GUILayout.Button("削除", GUILayout.Width(44))) action = -1;
            }

            // 条件
            var conditions = rule.FindPropertyRelative(nameof(LinkRule.conditions));
            var labels = new[] { "（選択してください）" }.Concat(sources.Select(s => s.Label)).ToArray();
            var removeCondition = -1;
            for (var k = 0; k < conditions.arraySize; k++)
            {
                var c = conditions.GetArrayElementAtIndex(k);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (k == 0)
                    {
                        EditorGUILayout.LabelField("IF", EditorStyles.boldLabel, GUILayout.Width(52));
                    }
                    else
                    {
                        var connector = c.FindPropertyRelative(nameof(LinkCondition.connector));
                        EditorGUI.BeginChangeCheck();
                        var selectedConnector = EditorGUILayout.Popup(connector.enumValueIndex, ConnectorLabels, GUILayout.Width(52));
                        if (EditorGUI.EndChangeCheck()) connector.enumValueIndex = selectedConnector;
                    }

                    var not = c.FindPropertyRelative(nameof(LinkCondition.not));
                    EditorGUI.BeginChangeCheck();
                    var notValue = EditorGUILayout.ToggleLeft(new GUIContent("NOT", "ON の時は「OFF の時」「選択していない時」の意味になります"),
                        not.boolValue, GUILayout.Width(48));
                    if (EditorGUI.EndChangeCheck()) not.boolValue = notValue;

                    var kind = (LinkSourceKind)c.FindPropertyRelative(nameof(LinkCondition.kind)).enumValueIndex;
                    var sourceId = c.FindPropertyRelative(nameof(LinkCondition.sourceId)).stringValue;
                    var current = sources.FindIndex(s => s.Kind == kind &&
                                                         (kind == LinkSourceKind.Unlock || (!string.IsNullOrEmpty(sourceId) && SourceId(s) == sourceId)));
                    var shown = labels;
                    if (current < 0 && (kind == LinkSourceKind.Unlock || !string.IsNullOrEmpty(sourceId)))
                        shown = new[] { "（参照先が見つかりません）" }.Concat(labels.Skip(1)).ToArray();

                    EditorGUI.BeginChangeCheck();
                    var selected = EditorGUILayout.Popup(current + 1, shown);
                    if (EditorGUI.EndChangeCheck() && selected > 0)
                    {
                        var s = sources[selected - 1];
                        c.FindPropertyRelative(nameof(LinkCondition.kind)).enumValueIndex = (int)s.Kind;
                        c.FindPropertyRelative(nameof(LinkCondition.sourceId)).stringValue = EnsureSourceId(s);
                    }

                    if (GUILayout.Button("×", GUILayout.Width(22))) removeCondition = k;
                }
            }

            if (removeCondition >= 0) conditions.DeleteArrayElementAtIndex(removeCondition);
            if (GUILayout.Button("+ 条件", GUILayout.Width(80))) AddLinkCondition(conditions);

            // 対象オブジェクト
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("THEN（条件を満たした時の状態。満たさない時は逆になります）", EditorStyles.miniBoldLabel);
            var objects = rule.FindPropertyRelative(nameof(LinkRule.objects));
            var removeObject = -1;
            for (var o = 0; o < objects.arraySize; o++)
            {
                var obj = objects.GetArrayElementAtIndex(o);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(obj.FindPropertyRelative(nameof(OutfitPartObject.target)), GUIContent.none);
                    var popupRect = GUILayoutUtility.GetRect(58, EditorGUIUtility.singleLineHeight, GUILayout.Width(58));
                    ActivePopup(popupRect, obj.FindPropertyRelative(nameof(OutfitPartObject.active)),
                        "条件を満たした時のオブジェクトの状態\nON: 満たすと表示、満たさないと非表示\nOFF: 満たすと非表示、満たさないと表示");
                    if (GUILayout.Button("×", GUILayout.Width(22))) removeObject = o;
                }
            }

            if (removeObject >= 0) objects.DeleteArrayElementAtIndex(removeObject);
            if (GUILayout.Button("+ オブジェクト", GUILayout.Width(100))) AddPartObject(objects, null, true);
            DropArea("ここにオブジェクトをドロップ → 対象に追加（ON）", gos =>
            {
                foreach (var go in gos) AddPartObject(objects, go, true);
            });

            // 読み下した文。編集中の値はまだ _settings に反映されていないので、1フレーム遅れて追従する
            if (index < _settings.linkRules.Count)
                EditorGUILayout.LabelField(OutfitSwitcherLinks.Describe(_settings, _settings.linkRules[index]), EditorStyles.wordWrappedMiniLabel);

            return action;
        }

        // ---------------------------------------------------------------- Preview / Issues

        private void DrawPreview()
        {
            _foldPreview = EditorGUILayout.BeginFoldoutHeaderGroup(_foldPreview, "メニュー構成プレビュー");
            if (_foldPreview)
            {
                _previewQuest = GUILayout.Toolbar(_previewQuest ? 1 : 0, new[] { "PC", "Quest" }) == 1;
                EditorGUILayout.HelpBox(BuildPreview(_settings, _previewQuest), MessageType.None);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static string BuildPreview(OutfitSwitcherSettings s, bool quest)
        {
            var sb = new StringBuilder();
            var groups = OutfitSwitcherModel.ActiveGroups(s).ToList();
            var publicGroups = groups.Where(g => OutfitSwitcherModel.MenuItemIndices(g, false, quest).Count > 0).ToList();
            var restrictedGroups = groups.Where(g => OutfitSwitcherModel.MenuItemIndices(g, true, quest).Count > 0).ToList();
            var separate = s.restrictedMenuTarget != null;

            if (groups.Count == 0) return "（対象オブジェクトのある衣装グループがまだありません）";

            void Line(int depth, string text) => sb.Append(' ', depth * 4).AppendLine(text);

            bool NoneHere(OutfitGroup g, bool side) =>
                OutfitSwitcherModel.HasNoneButton(g) && OutfitSwitcherModel.NoneButtonOnRestrictedSide(g, quest) == side;

            int GroupEntries(OutfitGroup g, bool side) => OutfitSwitcherModel.UsesSubmenu(g)
                ? 1
                : OutfitSwitcherModel.MenuItemIndices(g, side, quest).Count + (NoneHere(g, side) ? 1 : 0);

            void Group(int depth, OutfitGroup g, bool side)
            {
                var d = depth;
                if (OutfitSwitcherModel.UsesSubmenu(g))
                {
                    Line(depth, $"▶ {g.name}");
                    d++;
                }

                if (NoneHere(g, side))
                    Line(d, $"○ {OutfitSwitcherGenerator.NoneLabel}  [値 {OutfitSwitcherModel.NoneValue(g)}]");

                foreach (var i in OutfitSwitcherModel.MenuItemIndices(g, side, quest))
                {
                    var item = g.items[i];
                    var isDefault = OutfitSwitcherModel.IsExclusive(g) ? OutfitSwitcherModel.DefaultItemIndex(g) == i : item.defaultOn;
                    var mark = OutfitSwitcherModel.IsExclusive(g) ? "○" : "□";
                    var value = OutfitSwitcherModel.IsExclusive(g) ? $"  [値 {OutfitSwitcherModel.ExclusiveValue(g, i)}]" : "";
                    var label = OutfitSwitcherModel.DisplayLabel(g, i);
                    var itemDepth = d;
                    if (OutfitSwitcherModel.UsesItemSubmenu(item))
                    {
                        Line(d, $"▶ {label}");
                        itemDepth++;
                    }

                    Line(itemDepth, $"{mark} {label}{(isDefault ? "  (初期)" : "")}{value}");
                    foreach (var p in OutfitSwitcherModel.MenuPartIndices(item))
                    {
                        var part = item.parts[p];
                        var hidden = part.objects.Count(o => o != null && o.target != null && !o.active);
                        Line(itemDepth, $"□ {OutfitSwitcherModel.PartLabel(part, p)}{(part.defaultOn ? "  (初期)" : "")}" +
                                        $"{(part.saved ? "" : "  (保存されない)")}{(hidden > 0 ? $"  (OFF にする対象: {hidden})" : "")}  [パーツ]");
                    }
                }
            }

            string Overflow(int count) => count > VRCExpressionsMenu.MAX_CONTROLS ? $"  ※{count}項目: MA が自動で「More」ページに分割" : "";

            void Restricted(int depth)
            {
                var count = restrictedGroups.Sum(g => GroupEntries(g, true)) + (OutfitSwitcherModel.UsesLock(s) ? 1 : 0);
                var inner = depth;
                if (OutfitSwitcherModel.FlattenRestricted(s))
                {
                    sb.AppendLine($"（生成先に直接 {count} 項目を追加）");
                }
                else
                {
                    Line(depth, $"▶ {s.restrictedMenuName}  [制限]{Overflow(count)}");
                    inner++;
                }

                if (OutfitSwitcherModel.UsesLock(s)) Line(inner, $"□ {s.unlockLabel}  (保存されない)");
                foreach (var g in restrictedGroups) Group(inner, g, true);
            }

            if (publicGroups.Count > 0 || (restrictedGroups.Count > 0 && !separate))
            {
                sb.AppendLine($"[生成先: {TargetName(s.menuTarget, "ルートメニュー（自動）")}]");
                var count = publicGroups.Sum(g => GroupEntries(g, false)) + (restrictedGroups.Count > 0 && !separate ? 1 : 0);
                var depth = 0;
                if (!OutfitSwitcherModel.FlattenPublic(s))
                {
                    Line(0, $"▶ {s.menuName}{Overflow(count)}");
                    depth = 1;
                }
                else if (count > 0)
                {
                    sb.AppendLine($"（生成先に直接 {count} 項目を追加）");
                }

                foreach (var g in publicGroups) Group(depth, g, false);
                if (restrictedGroups.Count > 0 && !separate) Restricted(depth);
            }

            if (restrictedGroups.Count > 0 && separate)
            {
                sb.AppendLine();
                sb.AppendLine($"[生成先: {TargetName(s.restrictedMenuTarget, "")}]");
                Restricted(0);
            }

            // メニューに出ない衣装の値を受け取った時の表示
            var unavailable = new List<string>();
            foreach (var g in groups)
            for (var i = 0; i < g.items.Count; i++)
            {
                if (OutfitSwitcherModel.IsItemAvailable(g, i, quest) || g.items[i].targets.Count == 0) continue;
                var label = OutfitSwitcherModel.DisplayLabel(g, i);
                if (OutfitSwitcherModel.IsExclusive(g))
                {
                    var f = OutfitSwitcherModel.FallbackItemIndex(g, i, quest);
                    var shown = f >= 0 ? OutfitSwitcherModel.ItemLabel(g.items[f], f) : "何も表示しない（要注意）";
                    unavailable.Add($"× {label}  [値 {OutfitSwitcherModel.ExclusiveValue(g, i)}] → {shown}");
                }
                else
                {
                    unavailable.Add($"× {label} → 常に非表示");
                }
            }

            if (unavailable.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[メニューに出ない衣装（PC 側で選ばれた時の表示）]");
                foreach (var line in unavailable) Line(0, line);
            }

            if (s.linkRules.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[連動オブジェクト]");
                for (var r = 0; r < s.linkRules.Count; r++)
                    Line(0, $"{OutfitSwitcherLinks.RuleLabel(s.linkRules[r], r)}: " +
                            OutfitSwitcherLinks.Describe(s, s.linkRules[r]).Replace("\n", " "));
            }

            return sb.ToString().TrimEnd();
        }

        private static string TargetName(Object target, string fallback)
        {
            return target != null ? target.name : fallback;
        }

        private void DrawIssues()
        {
            // EditorOnly のオブジェクトはビルド時に丸ごと取り除かれるので、何も生成されない
            if (!OutfitSwitcherModel.IsUsableTarget(_settings.gameObject))
            {
                EditorGUILayout.HelpBox(
                    "OutfitSwitcher のオブジェクト（または親）に EditorOnly タグが付いているため、ビルド時に何も生成されません。",
                    MessageType.Error);
                if (_settings.CompareTag("EditorOnly") && GUILayout.Button("EditorOnly タグを外す"))
                {
                    Undo.RecordObject(_settings.gameObject, "Remove EditorOnly tag");
                    _settings.gameObject.tag = "Untagged";
                }
            }

            var issues = OutfitSwitcherModel.Validate(_settings, _avatar.transform);
            foreach (var issue in issues)
            {
                var type = issue.Severity == IssueSeverity.Error ? MessageType.Error
                    : issue.Severity == IssueSeverity.Warning ? MessageType.Warning
                    : MessageType.Info;
                EditorGUILayout.HelpBox(issue.Message, type);
            }

            if (issues.Count == 0 && OutfitSwitcherModel.IsUsableTarget(_settings.gameObject))
                EditorGUILayout.HelpBox("問題はありません。Play モードまたはアップロード時に自動で生成されます。", MessageType.Info);
        }

        // ---------------------------------------------------------------- Helpers

        private void InitStyles()
        {
            if (_placeholderStyle != null) return;
            _placeholderStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.gray } };
            _dropStyle = new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
        }

        private void DrawPlaceholder(Rect fieldRect, string text, bool hasPrefixLabel)
        {
            if (Event.current.type != EventType.Repaint) return;
            var r = fieldRect;
            if (hasPrefixLabel)
            {
                var offset = EditorGUIUtility.labelWidth;
                r.x += offset;
                r.width -= offset;
            }

            r.x += 3;
            _placeholderStyle.Draw(r, text, false, false, false, false);
        }

        /// <summary>チェックボックスを左、ラベルをすぐ右に置く。PropertyField だとラベル幅の分だけ離れて分かりにくいため</summary>
        private static void ToggleLeft(Rect rect, SerializedProperty prop, GUIContent label)
        {
            label = EditorGUI.BeginProperty(rect, label, prop);
            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.ToggleLeft(rect, label, prop.boolValue);
            if (EditorGUI.EndChangeCheck()) prop.boolValue = value;
            EditorGUI.EndProperty();
        }

        private void DropArea(string text, Action<GameObject[]> onDrop)
        {
            var rect = GUILayoutUtility.GetRect(0, 38, GUILayout.ExpandWidth(true));
            GUI.Box(rect, text, _dropStyle);

            var e = Event.current;
            if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !rect.Contains(e.mousePosition))
                return;

            var gos = DragAndDrop.objectReferences.OfType<GameObject>().Where(go => go.scene.IsValid()).ToArray();
            if (gos.Length == 0) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                onDrop(gos.OrderBy(go => go.transform.GetSiblingIndex()).ToArray());
            }

            e.Use();
        }
    }
}
