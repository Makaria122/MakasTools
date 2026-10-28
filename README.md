# Maka's Tools

VRChat アバター改変用の Unity エディタ拡張ツール集です。

| ツール | メニュー | 内容 |
|---|---|---|
| OutfitSwitcher | `Maka's Tools/OutfitSwitcher` | 衣装切り替えの FX レイヤー・アニメーション・メニューを、専用ウィンドウの設定から非破壊で生成します。衣装内パーツ、連動オブジェクト、Quest 対応、PC 版 → Quest 版への設定コピーに対応。 |
| Animator Controller Remover | `Maka's Tools/Settings/Animator Controller Remover` | アップロード時に、アバター直下の Animator の Controller を非破壊で None にします（チェックで ON/OFF）。 |

## 導入（VCC / ALCOM）

1. 次のボタンを押すか、URL を VCC / ALCOM の「Packages → Add Repository」に登録します。

   [VCC に追加](vcc://vpm/addRepo?url=https://makaria122.github.io/MakasTools/vpm.json)

   ```
   https://makaria122.github.io/MakasTools/vpm.json
   ```

2. プロジェクトの「Manage Project」から **Maka's Tools** を追加します。
   NDMF と Modular Avatar も自動で入ります。

### `.unitypackage` 版から移行する場合

VCC / ALCOM で追加すると、古い `Assets/OutfitSwitcher` と `Assets/AnimatorControllerRemover` は自動で削除されます。
スクリプトの ID は同じなので、アバターに付けた設定はそのまま使えます。
（自動で消えなかった場合は、この2つのフォルダを手で削除してください。両方あると重複エラーになります）

## 動作環境

- Unity 2022.3
- VRChat SDK Avatars 3.7 以上
- NDMF 1.14 以上 / Modular Avatar 1.18 以上

## リリースの作り方（開発者向け）

1. `Packages/jp.makaria.makas-tools/package.json` の `version` を上げてコミット・push する。
2. GitHub の Actions → **Build Release** → Run workflow。
   zip を作って Releases に載せ、続けて `vpm.json`（GitHub Pages）を更新します。

## ライセンス

All rights reserved. VRChat のアバター制作に使うのは自由ですが、改変・再配布・販売は禁止です。詳しくは [LICENSE](LICENSE) を見てください。
