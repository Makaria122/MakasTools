using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace OutfitSwitcher.Editor
{
    public enum IssueSeverity
    {
        Info,
        Warning,
        Error,
    }

    public readonly struct ValidationIssue
    {
        public readonly IssueSeverity Severity;
        public readonly string Message;
        public readonly Object Context;

        public ValidationIssue(IssueSeverity severity, string message, Object context = null)
        {
            Severity = severity;
            Message = message;
            Context = context;
        }
    }

    /// <summary>
    /// 設定からパラメータ名・ラベル等を決める処理と検証。ウィンドウとビルド処理で共用する。
    /// </summary>
    public static class OutfitSwitcherModel
    {
        public const int MaxSyncedBits = 256;

        public static string GroupParameter(OutfitSwitcherSettings settings, OutfitGroup group)
        {
            return string.IsNullOrWhiteSpace(group.parameterName)
                ? settings.parameterPrefix + group.name
                : group.parameterName;
        }

        public static string ItemParameter(OutfitSwitcherSettings settings, OutfitGroup group, int itemIndex)
        {
            if (IsButton(group)) return GroupParameter(settings, group);

            // 対象オブジェクト名に頼ると、Quest 用にオブジェクトを消した時にパラメータ名が変わってしまう
            var label = group.items[itemIndex].label;
            return GroupParameter(settings, group) + "/" + (string.IsNullOrWhiteSpace(label) ? $"Item {itemIndex + 1}" : label);
        }

        /// <summary>パーツの Bool パラメータ。衣装のパラメータ名の下に置く（ボタンはボタンのパラメータの下）</summary>
        public static string PartParameter(OutfitSwitcherSettings settings, OutfitGroup group, int itemIndex, int partIndex)
        {
            // 衣装と同じく、対象オブジェクト名には頼らない
            var label = group.items[itemIndex].parts[partIndex].label;
            return ItemParameter(settings, group, itemIndex) + "/" + (string.IsNullOrWhiteSpace(label) ? $"Part {partIndex + 1}" : label);
        }

        public static string PartLabel(OutfitPart part, int index)
        {
            if (!string.IsNullOrWhiteSpace(part.label)) return part.label;
            var first = PartTargets(part).FirstOrDefault();
            return first != null ? first.name : $"Part {index + 1}";
        }

        /// <summary>パーツの対象オブジェクト（未設定を除く）</summary>
        public static IEnumerable<GameObject> PartTargets(OutfitPart part)
        {
            return part.objects.Where(o => o != null && o.target != null).Select(o => o.target);
        }

        /// <summary>メニューに出すパーツ。衣装自体がメニューに出る時だけ使う</summary>
        public static List<int> MenuPartIndices(OutfitItem item)
        {
            return Enumerable.Range(0, item.parts.Count)
                .Where(p => PartTargets(item.parts[p]).Any(IsUsableTarget))
                .ToList();
        }

        /// <summary>衣装名のサブメニューにまとめるか（メニューに出るパーツがある）</summary>
        public static bool UsesItemSubmenu(OutfitItem item)
        {
            return MenuPartIndices(item).Count > 0;
        }

        public static string UnlockParameter(OutfitSwitcherSettings settings)
        {
            return settings.parameterPrefix + "Unlock";
        }

        public static bool IsButton(OutfitGroup group)
        {
            return group.kind == OutfitEntryKind.Button;
        }

        /// <summary>ボタンは常に個別 ON/OFF として扱う</summary>
        public static bool IsExclusive(OutfitGroup group)
        {
            return !IsButton(group) && group.mode == OutfitGroupMode.Exclusive;
        }

        public static bool UsesSubmenu(OutfitGroup group)
        {
            return !IsButton(group) && group.useSubmenu;
        }

        /// <summary>メニューに出す表示名。ボタンは項目の名前を使う</summary>
        public static string DisplayLabel(OutfitGroup group, int itemIndex)
        {
            if (IsButton(group) && !string.IsNullOrWhiteSpace(group.name)) return group.name;
            return ItemLabel(group.items[itemIndex], itemIndex);
        }

        public static Texture2D DisplayIcon(OutfitGroup group, int itemIndex)
        {
            return IsButton(group) ? group.icon : group.items[itemIndex].icon;
        }

        public static string ItemLabel(OutfitItem item, int index)
        {
            if (!string.IsNullOrWhiteSpace(item.label)) return item.label;
            var first = item.targets.FirstOrDefault(t => t != null);
            return first != null ? first.name : $"Item {index + 1}";
        }

        /// <summary>ボタンを制限メニューに置くか。グループ全体が Restricted か、衣装ごとに指定した場合</summary>
        public static bool IsItemRestricted(OutfitGroup group, int itemIndex)
        {
            return group.category == OutfitCategory.Restricted || (!IsButton(group) && group.items[itemIndex].restricted);
        }

        public static bool IsItemLocked(OutfitSwitcherSettings settings, OutfitGroup group, int itemIndex)
        {
            return settings.useRestrictedLock && IsItemRestricted(group, itemIndex);
        }

        /// <summary>対象オブジェクトが1つでも残っているか。削除済み・EditorOnly はアップロード時に残らないので数えない</summary>
        public static bool IsValidItem(OutfitItem item)
        {
            return item.targets.Any(IsUsableTarget);
        }

        public static bool IsUsableTarget(GameObject target)
        {
            if (target == null) return false;
            for (var t = target.transform; t != null; t = t.parent)
                if (t.CompareTag("EditorOnly")) return false;
            return true;
        }

        /// <summary>対象が削除・EditorOnly になっていて、自動で使えない扱いになっている衣装か</summary>
        public static bool IsAutoUnavailable(OutfitItem item)
        {
            return item.targets.Count > 0 && !IsValidItem(item);
        }

        /// <summary>Android / iOS 向けのビルドか</summary>
        public static bool IsQuestBuild()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            return target == BuildTarget.Android || target == BuildTarget.iOS;
        }

        /// <summary>このプラットフォームで表示できる衣装か。使えない衣装も値は欠番として残る</summary>
        public static bool IsItemAvailable(OutfitGroup group, int itemIndex, bool quest)
        {
            var item = group.items[itemIndex];
            return IsValidItem(item) && !(quest && item.pcOnly);
        }

        /// <summary>使えない衣装の値が来た時に表示する衣装。-1 は「なし」</summary>
        public static int FallbackItemIndex(OutfitGroup group, int itemIndex, bool quest)
        {
            var f = group.items[itemIndex].fallbackIndex;
            if (f >= 0 && f < group.items.Count && f != itemIndex && IsItemAvailable(group, f, quest)) return f;

            var d = DefaultItemIndex(group);
            return d >= 0 && d != itemIndex && IsItemAvailable(group, d, quest) ? d : -1;
        }

        /// <summary>通常メニュー側（restrictedSide = false）または制限メニュー側に置く衣装のインデックス</summary>
        public static List<int> MenuItemIndices(OutfitGroup group, bool restrictedSide, bool quest = false)
        {
            return Enumerable.Range(0, group.items.Count)
                .Where(i => IsItemAvailable(group, i, quest) && IsItemRestricted(group, i) == restrictedSide)
                .ToList();
        }

        /// <summary>「なし」ボタンを置く側。通常メニューに衣装があれば通常側</summary>
        public static bool NoneButtonOnRestrictedSide(OutfitGroup group, bool quest = false)
        {
            return MenuItemIndices(group, false, quest).Count == 0;
        }

        public static bool FlattenPublic(OutfitSwitcherSettings settings)
        {
            return settings.menuTarget != null && settings.flattenPublicMenu;
        }

        // パラメータは設定だけから決める。対象オブジェクトの有無やプラットフォームで変えると、
        // PC 版と Quest 版でパラメータがずれて同期が壊れる

        public static IEnumerable<OutfitGroup> ActiveGroups(OutfitSwitcherSettings settings)
        {
            return settings.groups.Where(g => g.items.Count > 0);
        }

        public static bool UsesLock(OutfitSwitcherSettings settings)
        {
            return ActiveGroups(settings).Any(g =>
                Enumerable.Range(0, g.items.Count).Any(i => IsItemLocked(settings, g, i)));
        }

        /// <summary>
        /// Quest ビルドで削除する「PC のみ」衣装のオブジェクト。Quest でも使う衣装と共有しているもの、
        /// パラメータを持つギミックや OutfitSwitcher 自身・メニュー生成先を含むものは残して非表示にする。
        /// </summary>
        public static List<GameObject> QuestRemovals(OutfitSwitcherSettings settings)
        {
            var result = new List<GameObject>();
            if (!settings.removePcOnlyObjects) return result;

            var keep = new HashSet<GameObject>();
            var candidates = new List<GameObject>();
            foreach (var group in ActiveGroups(settings))
            foreach (var item in group.items)
            foreach (var target in item.targets.Where(IsUsableTarget))
            {
                if (item.pcOnly) candidates.Add(target);
                else keep.Add(target);
            }

            foreach (var c in candidates.Distinct())
            {
                if (keep.Contains(c) || keep.Any(k => k.transform.IsChildOf(c.transform))) continue;
                if (ContainsProtected(settings, c)) continue;
                result.Add(c);
            }

            return result;
        }

        private static bool ContainsProtected(OutfitSwitcherSettings settings, GameObject go)
        {
            var t = go.transform;
            if (settings.transform.IsChildOf(t)) return true;
            if (settings.menuTarget is GameObject m && m.transform.IsChildOf(t)) return true;
            if (settings.restrictedMenuTarget is GameObject r && r.transform.IsChildOf(t)) return true;
            return HasParameterGimmick(go);
        }

        /// <summary>消すと PC 版とパラメータがずれるコンポーネントを含むか</summary>
        public static bool HasParameterGimmick(GameObject go)
        {
            return go.GetComponentsInChildren<ModularAvatarParameters>(true).Length > 0
                   || go.GetComponentsInChildren<ModularAvatarMenuItem>(true).Any(m =>
                       m.Control?.parameter != null && !string.IsNullOrEmpty(m.Control.parameter.name));
        }

        // Exclusive グループの Int パラメータの割り当て:
        //   0 は常にデフォルト（初期衣装、または「なし」）。トグルを OFF にすると 0 に戻るので、
        //   どのボタンを解除してもデフォルトに戻る。残りの衣装は 1 から順に、「なし」は最後の値。

        /// <summary>Exclusive グループの初期衣装（-1 は「なし」）</summary>
        public static int DefaultItemIndex(OutfitGroup group)
        {
            if (group.defaultIndex >= 0 && group.defaultIndex < group.items.Count) return group.defaultIndex;
            return -1;
        }

        public static int ExclusiveValue(OutfitGroup group, int itemIndex)
        {
            var d = DefaultItemIndex(group);
            if (d < 0) return itemIndex + 1;
            if (itemIndex == d) return 0;
            return itemIndex < d ? itemIndex + 1 : itemIndex;
        }

        /// <summary>「なし」の値</summary>
        public static int NoneValue(OutfitGroup group)
        {
            return DefaultItemIndex(group) < 0 ? 0 : group.items.Count;
        }

        /// <summary>初期衣装がある排他グループで「なし」を許可している場合は、メニューに「なし」ボタンを出す</summary>
        public static bool HasNoneButton(OutfitGroup group)
        {
            return IsExclusive(group) && group.showNoneButton && DefaultItemIndex(group) >= 0;
        }

        /// <summary>制限メニューをサブメニューにせず、生成先に直接並べるか</summary>
        public static bool FlattenRestricted(OutfitSwitcherSettings settings)
        {
            return settings.restrictedMenuTarget != null && settings.flattenRestrictedMenu;
        }

        /// <summary>この設定が使う同期ビット数</summary>
        public static int SyncedBits(OutfitSwitcherSettings settings)
        {
            var bits = 0;
            foreach (var group in ActiveGroups(settings))
            {
                if (!group.synced) continue;
                bits += IsExclusive(group) ? 8 : group.items.Count;
                bits += group.items.Sum(i => i.parts.Count);
            }

            if (UsesLock(settings)) bits += 1;
            return bits;
        }

        public static List<ValidationIssue> Validate(OutfitSwitcherSettings settings, Transform avatarRoot)
        {
            var issues = new List<ValidationIssue>();

            if (avatarRoot == null)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error,
                    "OutfitSwitcher がアバター（VRC Avatar Descriptor）の配下にありません。", settings));
                return issues;
            }

            ValidateMenuTarget(settings.menuTarget, "メニュー生成先", settings, avatarRoot, issues);
            ValidateMenuTarget(settings.restrictedMenuTarget, "制限メニュー生成先", settings, avatarRoot, issues);

            var paramOwners = new Dictionary<string, string>();
            var objectOwners = new Dictionary<GameObject, string>();

            for (var gi = 0; gi < settings.groups.Count; gi++)
            {
                var group = settings.groups[gi];
                var kindLabel = IsButton(group) ? "ボタン" : "グループ";
                var groupLabel = string.IsNullOrWhiteSpace(group.name) ? $"{kindLabel} {gi + 1}" : group.name;

                if (string.IsNullOrWhiteSpace(group.name) && string.IsNullOrWhiteSpace(group.parameterName))
                    issues.Add(new ValidationIssue(IssueSeverity.Error, $"{groupLabel}: {kindLabel}名が空です。", settings));

                if (group.items.Count == 0)
                {
                    issues.Add(new ValidationIssue(IssueSeverity.Warning,
                        $"{groupLabel}: 衣装が無いため、生成をスキップします。", settings));
                    continue;
                }

                if (IsButton(group) && group.items.Count != 1)
                    issues.Add(new ValidationIssue(IssueSeverity.Error, $"{groupLabel}: ボタンの衣装データが {group.items.Count} 個あります（1個である必要があります）。", settings));

                if (IsExclusive(group) && group.items.Count > 255)
                    issues.Add(new ValidationIssue(IssueSeverity.Error, $"{groupLabel}: 排他グループの衣装は255個までです。", settings));

                var paramNames = (IsExclusive(group)
                        ? new[] { GroupParameter(settings, group) }
                        : group.items.Select((_, i) => ItemParameter(settings, group, i)))
                    .Concat(group.items.SelectMany((item, i) =>
                        item.parts.Select((_, p) => PartParameter(settings, group, i, p))))
                    .ToArray();
                foreach (var p in paramNames)
                {
                    if (paramOwners.TryGetValue(p, out var owner))
                        issues.Add(new ValidationIssue(IssueSeverity.Error,
                            $"パラメータ名「{p}」が {owner} と {groupLabel} で重複しています。", settings));
                    else
                        paramOwners[p] = groupLabel;
                }

                for (var ii = 0; ii < group.items.Count; ii++)
                {
                    var item = group.items[ii];
                    var itemLabel = $"{groupLabel} / {ItemLabel(item, ii)}";

                    if (item.targets.Count == 0)
                        issues.Add(new ValidationIssue(IssueSeverity.Warning,
                            $"{itemLabel}: 対象オブジェクトが未設定です。メニューには出さず、パラメータの値だけ残します。", settings));
                    else if (!IsValidItem(item))
                        issues.Add(new ValidationIssue(IssueSeverity.Info,
                            $"{itemLabel}: 対象が削除済みか EditorOnly なので、このアバターでは使えない衣装として扱います（メニューに出さず、選ばれたら代わりの衣装を表示）。", settings));

                    if ((item.pcOnly || !IsValidItem(item)) && IsExclusive(group) && FallbackItemIndex(group, ii, true) < 0)
                        issues.Add(new ValidationIssue(IssueSeverity.Warning,
                            $"{itemLabel}: 代わりに表示する衣装がありません。この衣装が使えない環境（Quest など）では、選ばれると何も着ていない状態に見えます。", settings));

                    if (item.pcOnly && settings.removePcOnlyObjects)
                        foreach (var target in item.targets.Where(t => t != null && HasParameterGimmick(t)))
                            issues.Add(new ValidationIssue(IssueSeverity.Warning,
                                $"{itemLabel}: 「{target.name}」の中にパラメータを使うギミックがあるため、Quest でも削除せず非表示にします（削除すると PC 版とパラメータがずれるため）。", target));

                    foreach (var target in item.targets.Where(t => t != null))
                    {
                        if (target.transform == avatarRoot)
                        {
                            issues.Add(new ValidationIssue(IssueSeverity.Error, $"{itemLabel}: アバターのルートは対象にできません。", target));
                            continue;
                        }

                        if (!target.transform.IsChildOf(avatarRoot))
                        {
                            issues.Add(new ValidationIssue(IssueSeverity.Error,
                                $"{itemLabel}: 「{target.name}」がアバターの外にあります。", target));
                            continue;
                        }

                        // 排他グループ内の重複は同じレイヤーで扱えるので問題ない。レイヤーを跨ぐ重複だけ警告する
                        var layerKey = IsExclusive(group) ? groupLabel : itemLabel;
                        if (objectOwners.TryGetValue(target, out var other) && other != layerKey)
                            issues.Add(new ValidationIssue(IssueSeverity.Warning,
                                $"「{target.name}」が {other} と {layerKey} の両方で使われています。後のレイヤーが優先されます。", target));
                        else
                            objectOwners[target] = layerKey;
                    }

                    ValidateParts(item, itemLabel, avatarRoot, objectOwners, issues);
                }
            }

            OutfitSwitcherLinks.Validate(settings, avatarRoot, objectOwners, issues);

            var bits = SyncedBits(settings);
            if (bits > MaxSyncedBits)
                issues.Add(new ValidationIssue(IssueSeverity.Error,
                    $"同期パラメータが {bits} bit で、上限の {MaxSyncedBits} bit を超えています。", settings));

            return issues;
        }

        private static void ValidateParts(OutfitItem item, string itemLabel, Transform avatarRoot,
            Dictionary<GameObject, string> objectOwners, List<ValidationIssue> issues)
        {
            var itemTargets = item.targets.Where(t => t != null).ToList();
            for (var pi = 0; pi < item.parts.Count; pi++)
            {
                var part = item.parts[pi];
                var partLabel = $"{itemLabel} / {PartLabel(part, pi)}";

                if (!PartTargets(part).Any())
                    issues.Add(new ValidationIssue(IssueSeverity.Warning,
                        $"{partLabel}: パーツの対象オブジェクトが未設定です。メニューには出さず、パラメータだけ残します。"));

                foreach (var conflict in part.objects.Where(o => o != null && o.target != null)
                             .GroupBy(o => o.target).Where(g => g.Select(o => o.active).Distinct().Count() > 1))
                    issues.Add(new ValidationIssue(IssueSeverity.Error,
                        $"{partLabel}: 「{conflict.Key.name}」が「表示」と「隠す」の両方に入っています。どちらか1つにしてください。", conflict.Key));

                foreach (var target in PartTargets(part).Distinct())
                {
                    if (target.transform == avatarRoot || !target.transform.IsChildOf(avatarRoot))
                    {
                        issues.Add(new ValidationIssue(IssueSeverity.Error,
                            $"{partLabel}: 「{target.name}」がアバターの中（ルート以外）にありません。", target));
                        continue;
                    }

                    // 衣装のレイヤーとパーツのレイヤーが同じオブジェクトを奪い合う
                    if (itemTargets.Contains(target))
                    {
                        issues.Add(new ValidationIssue(IssueSeverity.Error,
                            $"{partLabel}: 「{target.name}」は衣装自体の対象です。パーツには、衣装の対象オブジェクトの子を指定してください。", target));
                        continue;
                    }

                    if (!itemTargets.Any(t => target.transform.IsChildOf(t.transform)))
                        issues.Add(new ValidationIssue(IssueSeverity.Warning,
                            $"{partLabel}: 「{target.name}」が衣装の対象オブジェクトの中にありません。衣装を切り替えても、このパーツは表示されたままになります。", target));

                    if (objectOwners.TryGetValue(target, out var other) && other != partLabel)
                        issues.Add(new ValidationIssue(IssueSeverity.Warning,
                            $"「{target.name}」が {other} と {partLabel} の両方で使われています。後のレイヤーが優先されます。", target));
                    else
                        objectOwners[target] = partLabel;
                }
            }
        }

        private static void ValidateMenuTarget(Object target, string label, OutfitSwitcherSettings settings,
            Transform avatarRoot, List<ValidationIssue> issues)
        {
            // 未設定の Object フィールドは Unity の「偽の null」になることがあるので、== で判定する
            if (target == null) return;

            switch (target)
            {
                case VRCExpressionsMenu _:
                    return;
                case GameObject go:
                    if (!go.transform.IsChildOf(avatarRoot))
                        issues.Add(new ValidationIssue(IssueSeverity.Error, $"{label}: 「{go.name}」がアバターの外にあります。", go));
                    else if (!IsMenuContainer(go))
                        issues.Add(new ValidationIssue(IssueSeverity.Error,
                            $"{label}: 「{go.name}」には SubMenu（子オブジェクト）型の MA Menu Item か MA Menu Group が必要です。", go));
                    return;
                default:
                    issues.Add(new ValidationIssue(IssueSeverity.Error,
                        $"{label}: VRCExpressionsMenu か GameObject を指定してください。", settings));
                    return;
            }
        }

        public static bool IsMenuContainer(GameObject go)
        {
            if (go.GetComponent<ModularAvatarMenuGroup>() != null) return true;
            var item = go.GetComponent<ModularAvatarMenuItem>();
            return item != null
                   && item.Control != null
                   && item.Control.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                   && item.MenuSource == SubmenuSource.Children;
        }

        /// <summary>
        /// 生成先 GameObject の中で、実際に子メニュー項目を置くべき親 Transform。
        /// </summary>
        public static Transform MenuChildrenRoot(GameObject container)
        {
            var group = container.GetComponent<ModularAvatarMenuGroup>();
            if (group != null) return group.targetObject != null ? group.targetObject.transform : container.transform;

            var item = container.GetComponent<ModularAvatarMenuItem>();
            if (item != null && item.menuSource_otherObjectChildren != null)
                return item.menuSource_otherObjectChildren.transform;
            return container.transform;
        }
    }
}
