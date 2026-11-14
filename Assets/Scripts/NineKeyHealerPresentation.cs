using UnityEngine;

namespace RaidRogue
{
    public sealed partial class NineKeyHealerKit
    {
        private readonly LineRenderer[] _flowerRings = new LineRenderer[2];
        private LineRenderer _flowerPreview;
        private Material _flowerMaterial;
        private void FlowerRing(ref LineRenderer line, string name, Vector3 center, float radius, Color color)
        {
            if (_flowerMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default"); if (shader == null) return;
                _flowerMaterial = new Material(shader);
            }
            if (line == null)
            {
                var go = new GameObject(name); go.transform.SetParent(transform, false);
                line = go.AddComponent<LineRenderer>(); line.useWorldSpace = true; line.loop = true;
                line.positionCount = 40; line.startWidth = line.endWidth = .06f; line.sharedMaterial = _flowerMaterial;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            }
            line.enabled = true; line.startColor = line.endColor = color;
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2 / line.positionCount;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, .06f, Mathf.Sin(angle) * radius));
            }
        }
        private void UpdateFlowerPresentation()
        {
            foreach (var line in _flowerRings) if (line != null) line.enabled = false;
            if (_flowerPreview != null) _flowerPreview.enabled = false;
            if (!UsesGround || !CombatRange.LivingPartyMember(transform)) return;
            Bind(); var flowers = Flowers;
            bool preview = RaidCombatAuthority.Local(_actor) && !RaidCombatAuthority.InputBlocked(_actor) &&
                !LocalGameplayInput.PointerOverUI(LocalGameplayInput.MousePosition) && Camera.main != null;
            bool replace = preview && flowers.Length >= (Core(2, 0) ? 1 : 2);
            for (int i = 0; i < flowers.Length && i < 2; i++)
            {
                var flower = flowers[i];
                FlowerRing(ref _flowerRings[i], "HealerSeasonFlower", new Vector3(flower.x, flower.y, flower.z), flower.radius,
                    replace && i == 0 ? new Color(1, .65f, .15f, .85f) : new Color(.25f, 1, .65f, .65f));
            }
            if (!preview) return;
            var plane = new Plane(Vector3.up, Vector3.zero); var ray = Camera.main.ScreenPointToRay(LocalGameplayInput.MousePosition);
            if (!plane.Raycast(ray, out float distance)) return;
            Vector3 point = ray.GetPoint(distance);
            FlowerRing(ref _flowerPreview, "HealerSeasonPreview", point, Core(2, 0) ? 10 : 7,
                CanPlaceFlower(point) ? new Color(.3f, .9f, 1, .45f) : new Color(1, .3f, .3f, .45f));
        }
        private void OnDisable()
        {
            foreach (var line in _flowerRings) if (line != null) line.enabled = false;
            if (_flowerPreview != null) _flowerPreview.enabled = false;
            if(_actor!=null)Clear();
        }
        private void OnDestroy()
        {
            foreach (var line in _flowerRings) if (line != null) Destroy(line.gameObject);
            if (_flowerPreview != null) Destroy(_flowerPreview.gameObject);
            if (_flowerMaterial != null) Destroy(_flowerMaterial);
        }
    }
}
