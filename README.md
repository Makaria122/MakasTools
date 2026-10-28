# Maka's Tools

A collection of Unity editor extensions for customizing VRChat avatars.

| Tool | Menu | Description |
|---|---|---|
| OutfitSwitcher | `Maka's Tools/OutfitSwitcher` | Non-destructively generates FX layers, animations and Expression Menus for switching outfits, all from a dedicated window. Supports per-outfit parts, linked objects (IF / AND / OR / NOT conditions), Quest builds, and copying settings from a PC avatar to a Quest avatar. |
| Animator Controller Remover | `Maka's Tools/Settings/Animator Controller Remover` | Non-destructively sets the Controller of the avatar root's Animator to None on upload. Toggle it with the menu checkmark. |

## Installation (VCC / ALCOM)

1. Click the button below, or add the URL in VCC / ALCOM under **Packages → Add Repository**.

   [Add to VCC](vcc://vpm/addRepo?url=https://makaria122.github.io/MakasTools/vpm.json)

   ```
   https://makaria122.github.io/MakasTools/vpm.json
   ```

2. Open **Manage Project** and add **Maka's Tools**.
   NDMF and Modular Avatar are installed automatically as dependencies.

### Migrating from the `.unitypackage` version

When you add the package through VCC / ALCOM, the old `Assets/OutfitSwitcher` and `Assets/AnimatorControllerRemover` folders are removed automatically.
The script IDs are unchanged, so the settings on your avatars keep working.
(If the folders are not removed, delete them manually. Having both installed causes duplicate errors.)

## Requirements

- Unity 2022.3
- VRChat SDK Avatars 3.7 or later
- NDMF 1.14 or later / Modular Avatar 1.18 or later

## Making a release (for developers)

1. Bump `version` in `Packages/jp.makaria.makas-tools/package.json`, then commit and push.
2. Run GitHub Actions → **Build Release** → Run workflow.
   It publishes the zip to Releases and then updates `vpm.json` on GitHub Pages.

## License

All rights reserved. You are free to use this software to create avatars for VRChat, but modification, redistribution and resale are prohibited. See [LICENSE](LICENSE) for details.
