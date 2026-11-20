using System;
using System.Collections.Generic;
using System.Linq;
using Astella.PrismV4;
using RaidRogue;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Astella.TwoD.Editor
{
    internal static class Astella2DPrismAuthoring
    {
        private const string Folder = "Assets/Astella2D/Presentation";
        private const string PrefabPath = Folder + "/PrismCombatHUD.prefab";
        private const string SkillArt = "Assets/UI/AstellaOrnate/Art/";

        internal static bool AssignIfMissing(Astella2DGame game)
        {
            if (game.prismHudPrefab != null) return false;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)?.GetComponent<Astella2DPrismView>();
            if (prefab == null) prefab = CreatePrefab();
            game.prismHudPrefab = prefab;
            EditorUtility.SetDirty(game);
            return true;
        }

        private static Astella2DPrismView CreatePrefab()
        {
            if (AssetDatabase.LoadMainAssetAtPath(PrefabPath) != null)
                throw new InvalidOperationException("The existing 2D HUD prefab must contain Astella2DPrismView.");
            var source = RaidUIPresentation.Settings?.hudPrefab;
            var bridge = source != null ? source.GetComponentInChildren<AstellaPrismHUDBridge>(true) : null;
            if (bridge == null || bridge.presentationRoot == null || bridge.slots.Length != 8 || bridge.partyRows.Length != 4)
                throw new InvalidOperationException("The active Raid UI profile requires the authored Prism HUD with eight slots and four party rows.");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Astella2D", "Presentation");

            var original = bridge.presentationRoot.transform;
            var clone = Object.Instantiate(bridge.presentationRoot);
            clone.name = "Prism Combat HUD";
            clone.SetActive(false);
            try
            {
                var view = clone.AddComponent<Astella2DPrismView>();
                view.player = Map(original, clone.transform, bridge.playerView);
                view.boss = Map(original, clone.transform, bridge.bossView);
                view.center = Map(original, clone.transform, bridge.centerView);
                view.slots = bridge.slots.Select(slot => Map(original, clone.transform, slot.view)).ToArray();
                var rows = bridge.partyRows.OrderBy(row => row.sourceIndex).ToArray();
                view.party = rows.Select(row => Map(original, clone.transform, row.view)).ToArray();
                view.partyShields = rows.Select(row => Map(original, clone.transform, row.shieldFill)).ToArray();
                view.partyAggro = rows.Select(row => MapTransform(original, clone.transform, row.aggroOverlay?.transform)?.gameObject).ToArray();
                view.tooltipRoot = MapTransform(original, clone.transform, bridge.hud.tooltipRoot?.transform)?.gameObject;
                view.tooltipText = Map(original, clone.transform, bridge.hud.tooltipText);
                view.slotIcons = new[] { Icon("01_GoldenSlash"), Icon("02_Sunburst"), null, null, Icon("04_ConstellationShield"), null, null, Icon("08_CyanDash") };
                if (view.player == null || view.boss == null || view.center == null || view.slots.Any(slot => slot == null) || view.party.Any(row => row == null))
                    throw new InvalidOperationException("One of the Prism modules is outside the authored combat composition.");

                Disable(MapTransform(original, clone.transform, bridge.hud.partyRevivalRoot?.transform));
                foreach (var slot in bridge.hud.slots)
                    if (slot != null && (slot.slot == CharacterSlot.Potion || slot.sharedResurrection))
                        Disable(MapTransform(original, clone.transform, slot.button?.transform));
                var bossPresenter = source.GetComponentInChildren<AstellaBossHUDV4>(true);
                Disable(MapTransform(original, clone.transform, bossPresenter?.targetRoot?.transform));

                PrepareControls(clone);
                PrefabUtility.SaveAsPrefabAsset(clone, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Could not save the authored 2D Prism HUD.");
            }
            finally { Object.DestroyImmediate(clone); }

            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<Astella2DPrismView>();
        }

        private static Sprite Icon(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(SkillArt + name + ".png");

        private static void PrepareControls(GameObject clone)
        {
            foreach (var button in clone.GetComponentsInChildren<Button>(true)) button.onClick = new Button.ButtonClickedEvent();
            foreach (var skin in clone.GetComponentsInChildren<AstellaSkillSlotSkin>(true))
            {
                if (skin.button == null || skin.frame == null) continue;
                skin.button.targetGraphic = skin.frame;
                skin.button.transition = Selectable.Transition.SpriteSwap;
                skin.button.spriteState = new SpriteState {
                    highlightedSprite = skin.hoverFrame, pressedSprite = skin.pressedFrame,
                    selectedSprite = skin.focusedFrame, disabledSprite = skin.disabledFrame
                };
            }
            foreach (var gauge in clone.GetComponentsInChildren<PrismV4Gauge>(true)) gauge.Bind(null);
            foreach (var component in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                // Keep the authored uGUI composition, but only the 2D adapter may drive its data.
                if (component is Graphic or Button or BaseMeshEffect or Mask or RectMask2D or LayoutGroup or LayoutElement or
                    ContentSizeFitter or AspectRatioFitter or PrismV4Bindings or AstellaPrismCanvasFit or RaidUIStyle or Astella2DPrismView)
                    continue;
                if (component != null) Object.DestroyImmediate(component);
            }
        }

        private static T Map<T>(Transform original, Transform copy, T source) where T : Component =>
            source != null ? MapTransform(original, copy, source.transform)?.GetComponent<T>() : null;

        private static Transform MapTransform(Transform original, Transform copy, Transform source)
        {
            if (source == null || (source != original && !source.IsChildOf(original))) return null;
            var path = new Stack<int>();
            for (var current = source; current != original; current = current.parent) path.Push(current.GetSiblingIndex());
            while (path.Count > 0) copy = copy.GetChild(path.Pop());
            return copy;
        }

        private static void Disable(Transform target)
        {
            if (target != null) target.gameObject.SetActive(false);
        }
    }
}
