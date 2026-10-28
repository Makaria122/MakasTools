using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;
using UnityEngine;

[assembly: ExportsPlugin(typeof(OutfitSwitcher.Editor.OutfitSwitcherPlugin))]

namespace OutfitSwitcher.Editor
{
    public class OutfitSwitcherPlugin : Plugin<OutfitSwitcherPlugin>
    {
        public override string QualifiedName => "outfitswitcher";
        public override string DisplayName => "OutfitSwitcher";

        private const string IssueKey = "outfitswitcher.issue";

        // メッセージは検証側で組み立て済みなので、キーをそのまま "{0}" に展開するだけの Localizer
        private static readonly Localizer Localizer = new Localizer("ja-jp", () => new List<(string, Func<string, string>)>
        {
            ("ja-jp", key => key == IssueKey ? "{0}" : null),
            ("en-us", key => key == IssueKey ? "{0}" : null),
        });

        protected override void Configure()
        {
            // MA のコンポーネントを生成するので、MA の処理より前に走らせる
            InPhase(BuildPhase.Generating)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Generate outfit switcher", ctx =>
                {
                    var avatarRoot = ctx.AvatarRootTransform;
                    foreach (var settings in avatarRoot.GetComponentsInChildren<OutfitSwitcherSettings>(true))
                    {
                        // 先に処理した設定が Quest 用に削除したオブジェクトの中にあった場合
                        if (settings == null) continue;
                        if (settings.gameObject.activeInHierarchy && !settings.CompareTag("EditorOnly"))
                            Process(settings, avatarRoot);
                        UnityEngine.Object.DestroyImmediate(settings);
                    }
                });
        }

        private static void Process(OutfitSwitcherSettings settings, Transform avatarRoot)
        {
            var issues = OutfitSwitcherModel.Validate(settings, avatarRoot);
            foreach (var issue in issues.Where(i => i.Severity != IssueSeverity.Info))
            {
                var severity = issue.Severity == IssueSeverity.Error ? ErrorSeverity.Error : ErrorSeverity.NonFatal;
                ErrorReport.ReportError(Localizer, severity, IssueKey, issue.Message, issue.Context);
            }

            if (issues.Any(i => i.Severity == IssueSeverity.Error)) return;

            var quest = OutfitSwitcherModel.IsQuestBuild();
            var removals = quest ? OutfitSwitcherModel.QuestRemovals(settings) : new List<GameObject>();
            new OutfitSwitcherGenerator(settings, avatarRoot, quest, removals).Generate();

            // ビルド用のクローンなので元のアバターには影響しない
            foreach (var go in removals.Where(go => go != null))
                UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
