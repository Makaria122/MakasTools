using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitSwitcher.Editor
{
    /// <summary>
    /// 連動オブジェクトの条件（IF / AND / OR / NOT）を解決し、アニメーターの遷移条件に変換する。
    /// 条件は既存のパラメータの値だけで判定するので、パラメータは増えない。
    /// </summary>
    public static class OutfitSwitcherLinks
    {
        /// <summary>1つのルールで作る遷移の上限。条件を反転すると組み合わせが増えるため</summary>
        public const int MaxTransitions = 256;

        public static string NewId() => Guid.NewGuid().ToString("N");

        /// <summary>パラメータ1つへの条件。Bool は ON か、Int は値が一致するか（Not で反転）</summary>
        public readonly struct Literal
        {
            public readonly string Parameter;
            public readonly bool IsInt;
            public readonly int Value;
            public readonly bool Not;

            /// <summary>パラメータの初期値（初期状態の判定に使う）</summary>
            public readonly int DefaultValue;

            public Literal(string parameter, bool isInt, int value, bool not, int defaultValue)
            {
                Parameter = parameter;
                IsInt = isInt;
                Value = value;
                Not = not;
                DefaultValue = defaultValue;
            }

            public Literal Negate() => new Literal(Parameter, IsInt, Value, !Not, DefaultValue);

            public bool EvaluateDefault() => (IsInt ? DefaultValue == Value : DefaultValue != 0) != Not;

            public string Key => $"{Parameter}|{IsInt}|{Value}|{Not}";
        }

        // ---------------------------------------------------------------- 参照

        public static bool TryFindItem(OutfitSwitcherSettings settings, string id, out OutfitGroup group, out int itemIndex)
        {
            group = null;
            itemIndex = -1;
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var g in settings.groups)
            {
                var i = g.items.FindIndex(item => item.id == id);
                if (i < 0) continue;
                group = g;
                itemIndex = i;
                return true;
            }

            return false;
        }

        public static bool TryFindPart(OutfitSwitcherSettings settings, string id, out OutfitGroup group, out int itemIndex, out int partIndex)
        {
            group = null;
            itemIndex = -1;
            partIndex = -1;
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var g in settings.groups)
            for (var i = 0; i < g.items.Count; i++)
            {
                var p = g.items[i].parts.FindIndex(part => part.id == id);
                if (p < 0) continue;
                group = g;
                itemIndex = i;
                partIndex = p;
                return true;
            }

            return false;
        }

        public static bool TryResolve(OutfitSwitcherSettings settings, LinkCondition c, out Literal literal)
        {
            literal = default;
            switch (c.kind)
            {
                case LinkSourceKind.Item:
                {
                    if (!TryFindItem(settings, c.sourceId, out var group, out var i)) return false;
                    literal = OutfitSwitcherModel.IsExclusive(group)
                        ? new Literal(OutfitSwitcherModel.GroupParameter(settings, group), true,
                            OutfitSwitcherModel.ExclusiveValue(group, i), c.not, 0)
                        : new Literal(OutfitSwitcherModel.ItemParameter(settings, group, i), false, 0, c.not,
                            group.items[i].defaultOn ? 1 : 0);
                    return true;
                }
                case LinkSourceKind.Part:
                {
                    if (!TryFindPart(settings, c.sourceId, out var group, out var i, out var p)) return false;
                    literal = new Literal(OutfitSwitcherModel.PartParameter(settings, group, i, p), false, 0, c.not,
                        group.items[i].parts[p].defaultOn ? 1 : 0);
                    return true;
                }
                case LinkSourceKind.Unlock:
                    if (!OutfitSwitcherModel.UsesLock(settings)) return false;
                    literal = new Literal(OutfitSwitcherModel.UnlockParameter(settings), false, 0, c.not, 0);
                    return true;
                default:
                    return false;
            }
        }

        // ---------------------------------------------------------------- 表示

        /// <summary>条件の参照先の名前。見つからなければ null</summary>
        public static string SourceLabel(OutfitSwitcherSettings settings, LinkSourceKind kind, string id)
        {
            switch (kind)
            {
                case LinkSourceKind.Item:
                    return TryFindItem(settings, id, out var g, out var i) ? ItemPath(g, i) : null;
                case LinkSourceKind.Part:
                    return TryFindPart(settings, id, out var pg, out var pi, out var pp)
                        ? ItemPath(pg, pi) + " / " + OutfitSwitcherModel.PartLabel(pg.items[pi].parts[pp], pp)
                        : null;
                case LinkSourceKind.Unlock:
                    return settings.unlockLabel;
                default:
                    return null;
            }
        }

        public static string ItemPath(OutfitGroup group, int itemIndex)
        {
            var label = OutfitSwitcherModel.DisplayLabel(group, itemIndex);
            return OutfitSwitcherModel.IsButton(group) ? label : $"{group.name} / {label}";
        }

        public static string ConditionText(OutfitSwitcherSettings settings, LinkCondition c)
        {
            var label = SourceLabel(settings, c.kind, c.sourceId);
            if (label == null) return "（参照先が見つかりません）";
            if (c.kind == LinkSourceKind.Item && TryFindItem(settings, c.sourceId, out var g, out _) && OutfitSwitcherModel.IsExclusive(g))
                return c.not ? $"「{label}」を選択していない" : $"「{label}」を選択中";
            return $"「{label}」が {(c.not ? "OFF" : "ON")}";
        }

        /// <summary>例: (A が ON かつ B が OFF) または X を選択中 → C を ON（満たさない時は逆）</summary>
        public static string Describe(OutfitSwitcherSettings settings, LinkRule rule)
        {
            if (rule.conditions.Count == 0) return "（条件がありません）";

            var terms = SplitTerms(rule.conditions);
            var text = string.Join(" または ", terms.Select(t =>
            {
                var s = string.Join(" かつ ", t.Select(c => ConditionText(settings, c)));
                return terms.Count > 1 && t.Count > 1 ? $"({s})" : s;
            }));

            var objects = rule.objects.Where(o => o != null && o.target != null)
                .Select(o => $"{o.target.name} を {(o.active ? "ON" : "OFF")}").ToList();
            return $"{text}\n→ {(objects.Count > 0 ? string.Join("、", objects) : "（対象オブジェクトなし）")}（満たさない時は逆）";
        }

        /// <summary>OR で区切って、AND でつながった条件のまとまりに分ける（AND が OR より先に結びつく）</summary>
        public static List<List<LinkCondition>> SplitTerms(List<LinkCondition> conditions)
        {
            var terms = new List<List<LinkCondition>>();
            for (var i = 0; i < conditions.Count; i++)
            {
                if (i == 0 || conditions[i].connector == LinkConnector.Or) terms.Add(new List<LinkCondition>());
                terms[terms.Count - 1].Add(conditions[i]);
            }

            return terms;
        }

        // ---------------------------------------------------------------- 遷移条件への変換

        /// <summary>
        /// 条件を「AND のまとまりの OR」（遷移の一覧）に変換する。whenTrue は条件を満たす遷移、whenFalse は満たさない遷移。
        /// どちらも Any State からの遷移1本 = AND のまとまり1つになる。
        /// </summary>
        public static bool TryBuild(OutfitSwitcherSettings settings, LinkRule rule,
            out List<List<Literal>> whenTrue, out List<List<Literal>> whenFalse, out string error)
        {
            whenTrue = new List<List<Literal>>();
            whenFalse = new List<List<Literal>>();
            error = null;

            if (rule.conditions.Count == 0)
            {
                error = "条件がありません。";
                return false;
            }

            var terms = new List<List<Literal>>();
            foreach (var term in SplitTerms(rule.conditions))
            {
                var literals = new List<Literal>();
                foreach (var c in term)
                {
                    if (!TryResolve(settings, c, out var literal))
                    {
                        error = c.kind == LinkSourceKind.Unlock
                            ? "ロックを使っていないので、ロック解除は条件にできません。"
                            : "条件の参照先（衣装・パーツ）が見つかりません。削除された可能性があります。";
                        return false;
                    }

                    literals.Add(literal);
                }

                terms.Add(literals);
            }

            whenTrue = Dedupe(terms.Select(Simplify).Where(t => t != null));

            // NOT (T1 OR T2 ...) = NOT T1 AND NOT T2 ...。各まとまりから反転した条件を1つずつ選ぶ組み合わせを作る
            var product = new List<List<Literal>> { new List<Literal>() };
            foreach (var term in terms)
            {
                var next = new List<List<Literal>>();
                foreach (var partial in product)
                foreach (var literal in term)
                {
                    var combined = Simplify(partial.Append(literal.Negate()));
                    if (combined != null) next.Add(combined);
                }

                product = Dedupe(next);
                if (product.Count > MaxTransitions)
                {
                    error = $"条件の組み合わせが多すぎます（{MaxTransitions} を超えます）。条件を減らすか、ルールを分けてください。";
                    return false;
                }
            }

            whenFalse = product;
            return true;
        }

        /// <summary>重複を除き、同時に満たせない組み合わせなら null を返す</summary>
        private static List<Literal> Simplify(IEnumerable<Literal> literals)
        {
            var list = literals.GroupBy(l => l.Key).Select(g => g.First()).ToList();
            foreach (var byParam in list.GroupBy(l => l.Parameter))
            {
                var ls = byParam.ToList();
                if (ls[0].IsInt)
                {
                    var equals = ls.Where(l => !l.Not).Select(l => l.Value).Distinct().ToList();
                    if (equals.Count > 1) return null;
                    if (equals.Count == 1 && ls.Any(l => l.Not && l.Value == equals[0])) return null;
                    // 値が決まっていれば「〜ではない」は不要
                    if (equals.Count == 1) list.RemoveAll(l => l.Parameter == byParam.Key && l.Not);
                }
                else if (ls.Select(l => l.Not).Distinct().Count() > 1)
                {
                    return null;
                }
            }

            return list;
        }

        private static List<List<Literal>> Dedupe(IEnumerable<List<Literal>> terms)
        {
            return terms.GroupBy(t => string.Join(";", t.Select(l => l.Key).OrderBy(k => k, StringComparer.Ordinal)))
                .Select(g => g.First()).ToList();
        }

        /// <summary>パラメータが初期値の時に条件を満たすか（レイヤーの初期ステートに使う）</summary>
        public static bool EvaluateDefault(List<List<Literal>> whenTrue)
        {
            return whenTrue.Any(term => term.All(l => l.EvaluateDefault()));
        }

        public static string RuleLabel(LinkRule rule, int index)
        {
            return string.IsNullOrWhiteSpace(rule.name) ? $"連動 {index + 1}" : rule.name;
        }

        // ---------------------------------------------------------------- 検証

        public static void Validate(OutfitSwitcherSettings settings, Transform avatarRoot,
            Dictionary<GameObject, string> objectOwners, List<ValidationIssue> issues)
        {
            for (var ri = 0; ri < settings.linkRules.Count; ri++)
            {
                var rule = settings.linkRules[ri];
                var label = "連動オブジェクト / " + RuleLabel(rule, ri);

                if (!TryBuild(settings, rule, out var whenTrue, out var whenFalse, out var error))
                    issues.Add(new ValidationIssue(IssueSeverity.Error, $"{label}: {error}", settings));
                else if (whenTrue.Count == 0)
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, $"{label}: 条件を満たすことがありません（常に「満たさない時」の状態になります）。", settings));
                else if (whenFalse.Count == 0)
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, $"{label}: 条件を常に満たします（常に指定の状態になります）。", settings));

                if (!rule.objects.Any(o => o != null && o.target != null))
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, $"{label}: 対象オブジェクトがありません。", settings));

                foreach (var conflict in rule.objects.Where(o => o != null && o.target != null)
                             .GroupBy(o => o.target).Where(g => g.Select(o => o.active).Distinct().Count() > 1))
                    issues.Add(new ValidationIssue(IssueSeverity.Error,
                        $"{label}: 「{conflict.Key.name}」が ON と OFF の両方に入っています。どちらか1つにしてください。", conflict.Key));

                foreach (var target in rule.objects.Where(o => o != null && o.target != null).Select(o => o.target).Distinct())
                {
                    if (target.transform == avatarRoot || !target.transform.IsChildOf(avatarRoot))
                    {
                        issues.Add(new ValidationIssue(IssueSeverity.Error,
                            $"{label}: 「{target.name}」がアバターの中（ルート以外）にありません。", target));
                        continue;
                    }

                    if (objectOwners.TryGetValue(target, out var other) && other != label)
                        issues.Add(new ValidationIssue(IssueSeverity.Warning,
                            $"「{target.name}」が {other} と {label} の両方で使われています。後のレイヤー（連動オブジェクト）が優先されます。", target));
                    else
                        objectOwners[target] = label;
                }
            }
        }
    }
}
