using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;

[assembly: ExportsPlugin(typeof(AnimatorControllerRemover.Editor.AnimatorControllerRemoverPlugin))]

namespace AnimatorControllerRemover.Editor
{
    public class AnimatorControllerRemoverPlugin : Plugin<AnimatorControllerRemoverPlugin>
    {
        public override string QualifiedName => "makastools.animatorcontrollerremover";
        public override string DisplayName => "Animator Controller Remover";

        private const string MenuPath = "Maka's Tools/Settings/Animator Controller Remover";
        private const string EnabledKey = "MakasTools.AnimatorControllerRemover.Enabled";

        // EditorPrefs に保存するので、この PC のすべてのプロジェクトで共通
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, true);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        [MenuItem(MenuPath)]
        private static void ToggleEnabled() => Enabled = !Enabled;

        [MenuItem(MenuPath, true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        protected override void Configure()
        {
            // ほかのツールの処理がすべて終わってから外す。Optimizing だと、あとから動くツール
            // （AAO など）の処理で元に戻ってしまうので、最後のフェーズで行う
            //
            // NDMF の Animator 管理は、一度読み込んだ Controller を覚えていて、無効化のたびに Animator へ
            // 書き戻す（あとのパスが再度有効にした場合も含む）。有効にした状態で管理対象から外してから None にする
            InPhase(BuildPhase.PlatformFinish)
                .WithRequiredExtension(typeof(AnimatorServicesContext), seq =>
                {
                    seq.Run("Remove root animator controller", ctx =>
                    {
                        if (!Enabled) return;
                        // Play モード（エミュレーターでの確認など）では外さない。アップロード時だけ
                        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

                        // ビルド用のクローンなので元のアバターには影響しない。子の Animator には触らない
                        var animator = ctx.AvatarRootObject.GetComponent<Animator>();
                        if (animator == null) return;

                        ctx.Extension<AnimatorServicesContext>().ControllerContext.ForgetController(animator);
                        animator.runtimeAnimatorController = null;
                    });
                });
        }
    }
}
