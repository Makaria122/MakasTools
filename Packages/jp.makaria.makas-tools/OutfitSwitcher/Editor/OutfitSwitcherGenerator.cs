using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ControlType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType;

namespace OutfitSwitcher.Editor
{
    /// <summary>
    /// OutfitSwitcherSettings から FX レイヤー・アニメーション・MA のメニュー/パラメータ コンポーネントを生成する。
    /// ビルド中のアバター（クローン）に対してのみ実行される。
    /// </summary>
    public class OutfitSwitcherGenerator
    {
        public const string NoneLabel = "なし";

        private readonly OutfitSwitcherSettings _settings;
        private readonly Transform _avatarRoot;
        private readonly List<AnimatorControllerParameter> _animatorParams = new List<AnimatorControllerParameter>();
        private readonly List<AnimatorControllerLayer> _layers = new List<AnimatorControllerLayer>();
        private readonly List<ParameterConfig> _maParams = new List<ParameterConfig>();
        private readonly bool _quest;
        private readonly HashSet<GameObject> _removed;

        /// <param name="quest">Quest（Android / iOS）向けに生成する。「PC のみ」の衣装を使えない扱いにする</param>
        /// <param name="removed">ビルドで削除するオブジェクト。アニメーションに含めない</param>
        public OutfitSwitcherGenerator(OutfitSwitcherSettings settings, Transform avatarRoot,
            bool quest = false, IEnumerable<GameObject> removed = null)
        {
            _settings = settings;
            _avatarRoot = avatarRoot;
            _quest = quest;
            _removed = new HashSet<GameObject>(removed ?? Enumerable.Empty<GameObject>());
        }

        public void Generate()
        {
            var groups = OutfitSwitcherModel.ActiveGroups(_settings).ToList();
            if (groups.Count == 0) return;

            var generatedRoot = CreateChild(_settings.transform, "OutfitSwitcher_Generated");

            var unlockParam = OutfitSwitcherModel.UnlockParameter(_settings);
            DeclareParameters(groups, unlockParam);

            foreach (var group in groups)
            {
                if (OutfitSwitcherModel.IsExclusive(group)) BuildExclusiveLayer(group, unlockParam);
                else BuildToggleLayers(group, unlockParam);
                BuildPartLayers(group);
            }

            // 衣装・パーツより後に置き、同じオブジェクトを扱う場合は連動オブジェクトを優先する
            BuildLinkLayers();

            var controller = new AnimatorController
            {
                name = "OutfitSwitcher FX",
                parameters = _animatorParams.ToArray(),
                layers = _layers.ToArray(),
            };

            var merge = generatedRoot.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = controller;
            merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Absolute;
            merge.matchAvatarWriteDefaults = true;
            merge.deleteAttachedAnimator = false;

            var parameters = generatedRoot.AddComponent<ModularAvatarParameters>();
            parameters.parameters = _maParams;

            BuildMenus(generatedRoot.transform, groups, unlockParam);
        }

        // ---------------------------------------------------------------- Parameters

        /// <summary>
        /// パラメータは設定だけから決める（対象オブジェクトの有無やプラットフォームを見ない）。
        /// PC 版と Quest 版でパラメータが一致しないと同期が壊れるため。
        /// </summary>
        private void DeclareParameters(List<OutfitGroup> groups, string unlockParam)
        {
            if (OutfitSwitcherModel.UsesLock(_settings))
            {
                // リモートでも同じ表示になるよう同期する。保存はせず、毎回ロック状態から始める
                AddParameter(unlockParam, AnimatorControllerParameterType.Bool, 0, saved: false, synced: true);
            }

            foreach (var group in groups)
            {
                if (OutfitSwitcherModel.IsExclusive(group))
                {
                    AddParameter(OutfitSwitcherModel.GroupParameter(_settings, group), AnimatorControllerParameterType.Int, 0,
                        group.saved, group.synced);
                }
                else
                {
                    for (var i = 0; i < group.items.Count; i++)
                        AddParameter(OutfitSwitcherModel.ItemParameter(_settings, group, i), AnimatorControllerParameterType.Bool,
                            group.items[i].defaultOn ? 1 : 0, group.saved, group.synced);
                }

                // パーツは使えない衣装（Quest で PC のみ）の分も宣言する
                for (var i = 0; i < group.items.Count; i++)
                for (var p = 0; p < group.items[i].parts.Count; p++)
                    AddParameter(OutfitSwitcherModel.PartParameter(_settings, group, i, p), AnimatorControllerParameterType.Bool,
                        group.items[i].parts[p].defaultOn ? 1 : 0, group.items[i].parts[p].saved, group.synced);
            }
        }

        // ---------------------------------------------------------------- Animator

        // 削除するオブジェクトの子（衣装内のパーツなど）も一緒に消えるので除く
        private bool IsAnimated(GameObject target) => OutfitSwitcherModel.IsUsableTarget(target)
                                                      && !_removed.Any(r => target.transform.IsChildOf(r.transform));

        private void BuildExclusiveLayer(OutfitGroup group, string unlockParam)
        {
            var param = OutfitSwitcherModel.GroupParameter(_settings, group);
            var count = group.items.Count;
            var allTargets = group.items.SelectMany(i => i.targets).Where(IsAnimated).Distinct().ToList();

            // 使えない衣装（Quest で PC のみ、対象が削除済み・EditorOnly）の値は欠番にせず、代わりの衣装を表示する。
            // PC 側で選ばれた値がそのまま届くので、何も表示しない状態（裸）にならないようにするため
            var shownItem = new int[count];
            for (var i = 0; i < count; i++)
                shownItem[i] = OutfitSwitcherModel.IsItemAvailable(group, i, _quest)
                    ? i
                    : OutfitSwitcherModel.FallbackItemIndex(group, i, _quest);

            bool IsLocked(int i) => i >= 0 && OutfitSwitcherModel.IsItemLocked(_settings, group, i);

            // ロック中に制限衣装が選ばれていたら初期衣装に戻す。初期衣装が使えないか制限対象なら「なし」
            var defaultItem = OutfitSwitcherModel.DefaultItemIndex(group);
            var lockFallback = defaultItem >= 0 && OutfitSwitcherModel.IsItemAvailable(group, defaultItem, _quest) && !IsLocked(defaultItem)
                ? defaultItem
                : -1;
            var hasNone = defaultItem < 0 || OutfitSwitcherModel.HasNoneButton(group);
            var needsNone = hasNone || shownItem.Any(i => i < 0) || (lockFallback < 0 && shownItem.Any(IsLocked));

            var sm = NewStateMachine(group.name);

            AnimatorState none = null;
            if (needsNone)
            {
                none = sm.AddState("None", new Vector3(300, 0));
                none.motion = MakeClip($"{group.name}_None", allTargets.Select(t => (t, false)));
            }

            var itemStates = new AnimatorState[count];
            for (var i = 0; i < count; i++)
            {
                if (shownItem[i] != i) continue;
                var item = group.items[i];
                var label = OutfitSwitcherModel.ItemLabel(item, i);
                var shown = new HashSet<GameObject>(item.targets.Where(IsAnimated));
                var state = sm.AddState(label, new Vector3(300, 60 * (i + 1)));
                state.motion = MakeClip($"{group.name}_{label}", allTargets.Select(t => (t, shown.Contains(t))));
                itemStates[i] = state;
            }

            AnimatorState StateOf(int i) => i >= 0 ? itemStates[i] : none;

            for (var i = 0; i < count; i++)
            {
                var selected = (AnimatorConditionMode.Equals, (float)OutfitSwitcherModel.ExclusiveValue(group, i), param);
                var dst = shownItem[i];
                if (IsLocked(dst))
                {
                    AnyStateTo(sm, StateOf(dst), selected, Unlocked(unlockParam));
                    AnyStateTo(sm, StateOf(lockFallback), selected, Locked(unlockParam));
                }
                else
                {
                    AnyStateTo(sm, StateOf(dst), selected);
                }
            }

            if (hasNone)
                AnyStateTo(sm, none, (AnimatorConditionMode.Equals, OutfitSwitcherModel.NoneValue(group), param));

            var initial = defaultItem >= 0 ? shownItem[defaultItem] : -1;
            if (IsLocked(initial)) initial = lockFallback;
            sm.defaultState = StateOf(initial);

            AddLayer(sm);
        }

        private void BuildToggleLayers(OutfitGroup group, string unlockParam)
        {
            for (var i = 0; i < group.items.Count; i++)
            {
                var item = group.items[i];
                var targets = item.targets.Where(IsAnimated).Distinct().ToList();
                if (targets.Count == 0) continue;

                var locked = OutfitSwitcherModel.IsItemLocked(_settings, group, i);

                var label = OutfitSwitcherModel.DisplayLabel(group, i);
                var param = OutfitSwitcherModel.ItemParameter(_settings, group, i);

                var sm = NewStateMachine(OutfitSwitcherModel.IsButton(group) ? label : $"{group.name}/{label}");
                var off = sm.AddState("OFF", new Vector3(300, 0));
                off.motion = MakeClip($"{group.name}_{label}_OFF", targets.Select(t => (t, false)));

                // 使えない衣装（Quest で PC のみ）が残っている場合は、パラメータに関係なく常に非表示にする
                if (!OutfitSwitcherModel.IsItemAvailable(group, i, _quest))
                {
                    sm.defaultState = off;
                    AddLayer(sm);
                    continue;
                }
                var on = sm.AddState("ON", new Vector3(300, 60));
                on.motion = MakeClip($"{group.name}_{label}_ON", targets.Select(t => (t, true)));

                if (locked) AnyStateTo(sm, on, (AnimatorConditionMode.If, 0, param), Unlocked(unlockParam));
                else AnyStateTo(sm, on, (AnimatorConditionMode.If, 0, param));
                AnyStateTo(sm, off, (AnimatorConditionMode.IfNot, 0, param));
                if (locked)
                    AnyStateTo(sm, off, Locked(unlockParam));

                sm.defaultState = !locked && item.defaultOn ? on : off;
                AddLayer(sm);
            }
        }

        /// <summary>
        /// 衣装内のパーツを Bool で ON/OFF する。パーツは衣装の子にある前提なので、衣装の表示条件（ロック・代わりの衣装）は
        /// 見ない。衣装が非表示なら親ごと隠れる。
        /// </summary>
        private void BuildPartLayers(OutfitGroup group)
        {
            for (var i = 0; i < group.items.Count; i++)
            {
                // 使えない衣装（Quest で PC のみ、対象なし）のパーツは、パラメータだけ残してレイヤーは作らない
                if (!OutfitSwitcherModel.IsItemAvailable(group, i, _quest)) continue;

                var item = group.items[i];
                var itemLabel = OutfitSwitcherModel.DisplayLabel(group, i);
                for (var p = 0; p < item.parts.Count; p++)
                {
                    var part = item.parts[p];
                    // 同じオブジェクトが重複していたら最初の指定を使う（表示と隠すの両方は検証でエラー）
                    var objects = part.objects
                        .Where(o => o != null && IsAnimated(o.target))
                        .GroupBy(o => o.target).Select(g => g.First()).ToList();
                    if (objects.Count == 0) continue;

                    var label = OutfitSwitcherModel.PartLabel(part, p);
                    var param = OutfitSwitcherModel.PartParameter(_settings, group, i, p);
                    var path = OutfitSwitcherModel.IsButton(group) ? $"{itemLabel}/{label}" : $"{group.name}/{itemLabel}/{label}";

                    var sm = NewStateMachine(path);
                    var off = sm.AddState("OFF", new Vector3(300, 0));
                    off.motion = MakeClip($"{path}_OFF", objects.Select(o => (o.target, !o.active)));
                    var on = sm.AddState("ON", new Vector3(300, 60));
                    on.motion = MakeClip($"{path}_ON", objects.Select(o => (o.target, o.active)));

                    AnyStateTo(sm, on, (AnimatorConditionMode.If, 0, param));
                    AnyStateTo(sm, off, (AnimatorConditionMode.IfNot, 0, param));
                    sm.defaultState = part.defaultOn ? on : off;
                    AddLayer(sm);
                }
            }
        }

        /// <summary>
        /// 連動オブジェクト。条件（AND のまとまりの OR）を満たす遷移と、満たさない遷移を Any State から張る。
        /// 両者は排他で漏れがないので、どの値の組み合わせでもどちらか一方にだけ遷移する。
        /// </summary>
        private void BuildLinkLayers()
        {
            for (var r = 0; r < _settings.linkRules.Count; r++)
            {
                var rule = _settings.linkRules[r];
                if (!OutfitSwitcherLinks.TryBuild(_settings, rule, out var whenTrue, out var whenFalse, out _)) continue;

                var objects = rule.objects
                    .Where(o => o != null && IsAnimated(o.target))
                    .GroupBy(o => o.target).Select(g => g.First()).ToList();
                if (objects.Count == 0) continue;

                var path = $"Link/{r + 1} {OutfitSwitcherLinks.RuleLabel(rule, r)}";
                var sm = NewStateMachine(path);
                var off = sm.AddState("条件を満たさない", new Vector3(300, 0));
                off.motion = MakeClip($"{path}_False", objects.Select(o => (o.target, !o.active)));
                var on = sm.AddState("条件を満たす", new Vector3(300, 60));
                on.motion = MakeClip($"{path}_True", objects.Select(o => (o.target, o.active)));

                // 片方が空なら常にもう片方（遷移は不要）
                if (whenTrue.Count > 0 && whenFalse.Count > 0)
                {
                    foreach (var term in whenTrue) AnyStateTo(sm, on, term.Select(ToCondition).ToArray());
                    foreach (var term in whenFalse) AnyStateTo(sm, off, term.Select(ToCondition).ToArray());
                }

                sm.defaultState = OutfitSwitcherLinks.EvaluateDefault(whenTrue) ? on : off;
                AddLayer(sm);
            }
        }

        private static (AnimatorConditionMode, float, string) ToCondition(OutfitSwitcherLinks.Literal l)
        {
            if (l.IsInt) return (l.Not ? AnimatorConditionMode.NotEqual : AnimatorConditionMode.Equals, l.Value, l.Parameter);
            return (l.Not ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If, 0, l.Parameter);
        }

        private static AnimatorStateMachine NewStateMachine(string name)
        {
            return new AnimatorStateMachine
            {
                name = name,
                hideFlags = HideFlags.HideInHierarchy,
                anyStatePosition = new Vector3(0, 0),
                entryPosition = new Vector3(0, 60),
                exitPosition = new Vector3(0, 120),
            };
        }

        private void AddLayer(AnimatorStateMachine sm)
        {
            _layers.Add(new AnimatorControllerLayer
            {
                name = "OutfitSwitcher/" + sm.name,
                stateMachine = sm,
                defaultWeight = 1,
            });
        }

        private static (AnimatorConditionMode, float, string) Unlocked(string unlockParam) =>
            (AnimatorConditionMode.If, 0, unlockParam);

        private static (AnimatorConditionMode, float, string) Locked(string unlockParam) =>
            (AnimatorConditionMode.IfNot, 0, unlockParam);

        /// <summary>全条件を AND で満たした時に Any State から遷移する</summary>
        private static void AnyStateTo(AnimatorStateMachine sm, AnimatorState dst,
            params (AnimatorConditionMode mode, float threshold, string param)[] conditions)
        {
            var t = sm.AddAnyStateTransition(dst);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = 0;
            t.canTransitionToSelf = false;
            foreach (var (mode, threshold, param) in conditions) t.AddCondition(mode, threshold, param);
        }

        private AnimationClip MakeClip(string name, IEnumerable<(GameObject target, bool active)> states)
        {
            var clip = new AnimationClip { name = name };
            foreach (var (target, active) in states)
            {
                var path = AnimationUtility.CalculateTransformPath(target.transform, _avatarRoot);
                var binding = EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive");
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1f / 60, active ? 1 : 0));
            }

            return clip;
        }

        private void AddParameter(string name, AnimatorControllerParameterType type, int defaultValue, bool saved, bool synced)
        {
            _animatorParams.Add(new AnimatorControllerParameter
            {
                name = name,
                type = type,
                defaultInt = defaultValue,
                defaultBool = defaultValue != 0,
            });

            _maParams.Add(new ParameterConfig
            {
                nameOrPrefix = name,
                syncType = type == AnimatorControllerParameterType.Int ? ParameterSyncType.Int : ParameterSyncType.Bool,
                localOnly = !synced,
                saved = saved,
                defaultValue = defaultValue,
                hasExplicitDefaultValue = true,
            });
        }

        // ---------------------------------------------------------------- Menu

        private void BuildMenus(Transform generatedRoot, List<OutfitGroup> groups, string unlockParam)
        {
            // グループ単位ではなく衣装（ボタン）単位で置き場所を分ける。パラメータは共有のまま
            var publicGroups = groups.Where(g => OutfitSwitcherModel.MenuItemIndices(g, false, _quest).Count > 0).ToList();
            var restrictedGroups = groups.Where(g => OutfitSwitcherModel.MenuItemIndices(g, true, _quest).Count > 0).ToList();
            var separateRestricted = _settings.restrictedMenuTarget != null;

            Transform publicContainer = null;
            if (publicGroups.Count > 0 || (restrictedGroups.Count > 0 && !separateRestricted))
            {
                var root = CreateMenuRoot(generatedRoot, "Menu", _settings.menuTarget);
                if (OutfitSwitcherModel.FlattenPublic(_settings)) root.AddComponent<ModularAvatarMenuGroup>();
                else AddSubMenu(root, _settings.menuName, _settings.menuIcon);
                publicContainer = root.transform;

                foreach (var group in publicGroups) AddGroupMenu(publicContainer, group, false);
            }

            if (restrictedGroups.Count == 0) return;

            GameObject restrictedRoot;
            if (separateRestricted)
            {
                restrictedRoot = CreateMenuRoot(generatedRoot, "RestrictedMenu", _settings.restrictedMenuTarget);
            }
            else
            {
                restrictedRoot = CreateChild(publicContainer, "RestrictedMenu");
            }

            if (OutfitSwitcherModel.FlattenRestricted(_settings)) restrictedRoot.AddComponent<ModularAvatarMenuGroup>();
            else AddSubMenu(restrictedRoot, _settings.restrictedMenuName, _settings.restrictedMenuIcon);

            if (OutfitSwitcherModel.UsesLock(_settings))
            {
                var unlock = CreateChild(restrictedRoot.transform, "Unlock");
                AddControl(unlock, _settings.unlockLabel, _settings.unlockIcon, ControlType.Toggle, unlockParam, 1);
            }

            foreach (var group in restrictedGroups) AddGroupMenu(restrictedRoot.transform, group, true);
        }

        private void AddGroupMenu(Transform parent, OutfitGroup group, bool restrictedSide)
        {
            var container = parent;
            if (OutfitSwitcherModel.UsesSubmenu(group))
            {
                var sub = CreateChild(parent, group.name);
                AddSubMenu(sub, group.name, group.icon);
                container = sub.transform;
            }

            if (OutfitSwitcherModel.HasNoneButton(group) && OutfitSwitcherModel.NoneButtonOnRestrictedSide(group, _quest) == restrictedSide)
            {
                var none = CreateChild(container, NoneLabel);
                AddControl(none, NoneLabel, null, ControlType.Toggle, OutfitSwitcherModel.GroupParameter(_settings, group),
                    OutfitSwitcherModel.NoneValue(group));
            }

            foreach (var i in OutfitSwitcherModel.MenuItemIndices(group, restrictedSide, _quest))
            {
                var label = OutfitSwitcherModel.DisplayLabel(group, i);
                var icon = OutfitSwitcherModel.DisplayIcon(group, i);
                var item = group.items[i];

                // パーツがある衣装は、衣装のトグルとパーツのトグルを衣装名のサブメニューにまとめる
                var itemContainer = container;
                if (OutfitSwitcherModel.UsesItemSubmenu(item))
                {
                    var sub = CreateChild(container, label);
                    AddSubMenu(sub, label, icon);
                    itemContainer = sub.transform;
                }

                var go = CreateChild(itemContainer, label);
                if (OutfitSwitcherModel.IsExclusive(group))
                    AddControl(go, label, icon, ControlType.Toggle, OutfitSwitcherModel.GroupParameter(_settings, group),
                        OutfitSwitcherModel.ExclusiveValue(group, i));
                else
                    AddControl(go, label, icon, ControlType.Toggle, OutfitSwitcherModel.ItemParameter(_settings, group, i), 1);

                foreach (var p in OutfitSwitcherModel.MenuPartIndices(item))
                {
                    var partLabel = OutfitSwitcherModel.PartLabel(item.parts[p], p);
                    var partGo = CreateChild(itemContainer, partLabel);
                    AddControl(partGo, partLabel, item.parts[p].icon, ControlType.Toggle,
                        OutfitSwitcherModel.PartParameter(_settings, group, i, p), 1);
                }
            }
        }

        /// <summary>
        /// メニューのルートを作る。生成先が GameObject（MA のメニュー階層）ならその子として置き、
        /// MA が子オブジェクトとして拾う。それ以外は Menu Installer で VRCExpressionsMenu（null ならルート）に入れる。
        /// </summary>
        private static GameObject CreateMenuRoot(Transform generatedRoot, string name, UnityEngine.Object target)
        {
            if (target is GameObject container)
                return CreateChild(OutfitSwitcherModel.MenuChildrenRoot(container), "OutfitSwitcher_" + name);

            var root = CreateChild(generatedRoot, name);
            var installer = root.AddComponent<ModularAvatarMenuInstaller>();
            installer.installTargetMenu = target as VRCExpressionsMenu;
            return root;
        }

        private static void AddSubMenu(GameObject go, string label, Texture2D icon)
        {
            var item = AddControl(go, label, icon, ControlType.SubMenu, "", 0);
            item.MenuSource = SubmenuSource.Children;
        }

        private static ModularAvatarMenuItem AddControl(GameObject go, string label, Texture2D icon, ControlType type,
            string parameter, float value)
        {
            var item = go.AddComponent<ModularAvatarMenuItem>();
            item.label = label;
            item.automaticValue = false;
            item.Control = new VRCExpressionsMenu.Control
            {
                name = label,
                icon = icon,
                type = type,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                value = value,
                subParameters = Array.Empty<VRCExpressionsMenu.Control.Parameter>(),
                labels = Array.Empty<VRCExpressionsMenu.Control.Label>(),
            };
            return item;
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
