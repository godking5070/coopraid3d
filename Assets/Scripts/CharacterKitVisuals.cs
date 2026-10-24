using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class CharacterKit
    {
        private Transform _jumpBody;
        private Vector3 _jumpBase;
        private bool _lifted;
        private GameObject _gear;
        private string _gearJob;
        private Material _gearMaterial;
        private readonly Dictionary<string, LineRenderer> _shapes = new Dictionary<string, LineRenderer>();
        private void HideGear()
        {
            if (_lifted && _jumpBody != null)
                _jumpBody.localPosition = _jumpBase;
            _lifted = false;
            if (_gear != null)
                _gear.SetActive(false);
            foreach (var line in _shapes.Values)
                if (line != null)
                    line.enabled = false;
        }

        private void Part(string name, PrimitiveType primitive, Vector3 position, Vector3 scale, Vector3 rotation)
        {
            var obj = GameObject.CreatePrimitive(primitive);
            obj.name = name;
            obj.transform.SetParent(_gear.transform, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            obj.transform.localEulerAngles = rotation;
            var col = obj.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
            obj.GetComponent<Renderer>().sharedMaterial = _gearMaterial;
        }

        private void DrawGear()
        {
            if (_gearJob != Job.Id)
            {
                if (_gear != null)
                    Destroy(_gear);
                if (_gearMaterial != null)
                    Destroy(_gearMaterial);
                _gearJob = Job.Id;
                _gear = new GameObject(Job.slug + "_NaturalWeapon");
                _gear.transform.SetParent(transform, false);
                _gearMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                _gearMaterial.color = Tint;
                if (_player != null)
                    _player.ApplyCharacterAppearance(Color.Lerp(Tint, Color.white, .35f), Tint);
                switch (Job.index)
                {
                    case 1:
                        Part("Greatsword", PrimitiveType.Cube, new Vector3(.65f, .2f, .35f), new Vector3(.35f, 1.9f, .12f), new Vector3(20, 0, -20));
                        break;
                    case 2:
                        Part("DragonBoneLance", PrimitiveType.Capsule, new Vector3(.65f, .1f, .6f), new Vector3(.28f, 1.1f, .28f), new Vector3(70, 0, 0));
                        Part("DragonfireGauntlet", PrimitiveType.Sphere, new Vector3(-.6f, .1f, .4f), Vector3.one * .4f, Vector3.zero);
                        break;
                    case 3:
                        Part("LivingStaff", PrimitiveType.Cylinder, new Vector3(.6f, .2f, 0), new Vector3(.09f, 1, .09f), Vector3.zero);
                        Part("WorldTreeBud", PrimitiveType.Sphere, new Vector3(.6f, 1.25f, 0), new Vector3(.5f, .7f, .35f), Vector3.zero);
                        break;
                    case 4:
                        Part("HarpLeft", PrimitiveType.Cube, new Vector3(-.4f, 0, .6f), new Vector3(.1f, 1, .1f), new Vector3(0, 0, -15));
                        Part("HarpRight", PrimitiveType.Cube, new Vector3(.4f, 0, .6f), new Vector3(.1f, 1, .1f), new Vector3(0, 0, 15));
                        for (int i = 0; i < 5; i++)
                            Part("LightString", PrimitiveType.Cube, new Vector3((i - 2) * .14f, 0, .6f), new Vector3(.02f, .8f, .02f), Vector3.zero);
                        break;
                    case 5:
                        for (int i = 0; i < 3; i++)
                            Part("SilverNeedle", PrimitiveType.Cylinder, new Vector3(.5f + i * .1f, .3f, .5f), new Vector3(.025f, .4f, .025f), new Vector3(50, 0, 0));
                        Part("HerbalFlask", PrimitiveType.Sphere, new Vector3(-.6f, .1f, .4f), Vector3.one * .3f, Vector3.zero);
                        break;
                    case 6:
                        for (int i = -1; i <= 1; i += 2)
                            Part("MoonDagger", PrimitiveType.Cube, new Vector3(i * .6f, 0, .4f), new Vector3(.12f, .65f, .05f), new Vector3(50, 0, i * 25));
                        break;
                    case 7:
                        Part("LightningLance", PrimitiveType.Cylinder, new Vector3(.6f, .2f, .7f), new Vector3(.08f, 1.6f, .08f), new Vector3(55, 0, 0));
                        break;
                    case 8:
                        for (int i = -1; i <= 1; i += 2)
                            Part("SilverPistol", PrimitiveType.Cube, new Vector3(i * .6f, .2f, .4f), new Vector3(.13f, .14f, .55f), Vector3.zero);
                        Part("HuntingRifle", PrimitiveType.Cube, new Vector3(.25f, .3f, -.35f), new Vector3(.14f, 1.5f, .18f), new Vector3(0, 0, -20));
                        break;
                    case 9:
                        for (int i = -1; i <= 1; i += 2)
                            Part("WoodenLongbow", PrimitiveType.Cube, new Vector3(.6f, i * .35f, .4f), new Vector3(.07f, .85f, .08f), new Vector3(i * 20, 0, 0));
                        break;
                    case 10:
                        Part("CelestialCrystal", PrimitiveType.Sphere, new Vector3(.7f, .6f, .5f), Vector3.one * .5f, Vector3.zero);
                        for (int i = 0; i < 4; i++)
                            Part("Star", PrimitiveType.Cube, new Vector3(Mathf.Cos(i * 1.57f), 1.1f, Mathf.Sin(i * 1.57f)) * .5f, Vector3.one * .12f, new Vector3(45, 45, 0));
                        break;
                    case 11:
                        Part("PhantomScythe", PrimitiveType.Cylinder, new Vector3(.65f, .2f, .3f), new Vector3(.07f, 1, .07f), Vector3.zero);
                        Part("GlassBlade", PrimitiveType.Cube, new Vector3(.3f, 1.1f, .3f), new Vector3(.8f, .1f, .08f), new Vector3(0, 0, 15));
                        for (int i = -1; i <= 1; i += 2)
                            Part("CrystalMirror", PrimitiveType.Cube, new Vector3(i * .9f, .8f, -.2f), new Vector3(.35f, .5f, .05f), new Vector3(0, 0, 45));
                        break;
                }
            }

            _gear.SetActive(CombatRange.LivingPartyMember(transform));
            var air = Effect("air");
            if (air != null)
            {
                if (!_lifted)
                {
                    var body = GetComponent<DualVisualController>()?.visual3DRoot;
                    _jumpBody = body != null ? body.transform : null;
                    if (_jumpBody != null)
                        _jumpBase = _jumpBody.localPosition;
                    _lifted = true;
                }

                float progress = _actor.Snapshot.castTotal > 0 ? 1 - _actor.Snapshot.castRemaining / _actor.Snapshot.castTotal : 1;
                float height = Mathf.Sin(progress * Mathf.PI) * 2;
                _gear.transform.localPosition = Vector3.up * height;
                if (_jumpBody != null)
                    _jumpBody.localPosition = _jumpBase + Vector3.up * height;
            }
            else
            {
                _gear.transform.localPosition = Vector3.zero;
                if (_lifted && _jumpBody != null)
                    _jumpBody.localPosition = _jumpBase;
                _lifted = false;
            }

            if (Has("aimLock"))
                transform.rotation = Quaternion.Euler(0, Effect("aimLock").yaw, 0);
            _gear.transform.localScale = Vector3.one * (Job.index == 11 && Route == 2 && Has("identity") ? 1.3f : 1);
        }

        private void DrawEffects()
        {
            foreach (var line in _shapes.Values)
                line.enabled = false;
            foreach (var e in Data.effects)
            {
                if (e.end <= Now || !(e.kind == "ground" || e.kind == "channel" || e.id == "polaris" || e.id == "portal" || e.id == "wall" || e.id == "clonePosition"))
                    continue;
                if (!_shapes.TryGetValue(e.id, out var line))
                {
                    var go = new GameObject(e.id + "_Range");
                    go.transform.SetParent(transform, false);
                    line = go.AddComponent<LineRenderer>();
                    line.useWorldSpace = true;
                    line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
                    line.startWidth = line.endWidth = .07f;
                    _shapes[e.id] = line;
                }

                line.enabled = true;
                line.positionCount = 49;
                line.startColor = line.endColor = Tint;
                if (e.id == "wall")
                {
                    var point = new Vector3(e.x, .3f, e.z);
                    var right = Quaternion.Euler(0, e.yaw, 0) * Vector3.right;
                    line.positionCount = 5;
                    line.SetPosition(0, point - right * 3);
                    line.SetPosition(1, point - right * 3 + Vector3.up * 2);
                    line.SetPosition(2, point + right * 3 + Vector3.up * 2);
                    line.SetPosition(3, point + right * 3);
                    line.SetPosition(4, point - right * 3);
                    continue;
                }

                if (e.id == "clonePosition")
                {
                    var point = new Vector3(e.x, 1, e.z);
                    line.positionCount = 5;
                    line.SetPosition(0, point + Vector3.up);
                    line.SetPosition(1, point + Vector3.right * .5f);
                    line.SetPosition(2, point - Vector3.up * .7f);
                    line.SetPosition(3, point - Vector3.right * .5f);
                    line.SetPosition(4, point + Vector3.up);
                    continue;
                }

                var ability = Job.Ability(e.skill);
                if (e.kind == "channel" && ability?.shape == "cone")
                {
                    Vector3 origin = transform.position;
                    origin.y = .15f;
                    line.SetPosition(0, origin);
                    for (int i = 1; i < 48; i++)
                    {
                        float angle = (e.yaw - ability.angle * .5f + ability.angle * (i - 1) / 46) * Mathf.Deg2Rad;
                        line.SetPosition(i, origin + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * ability.range);
                    }

                    line.SetPosition(48, origin);
                    continue;
                }

                Vector3 center = e.kind == "channel" ? transform.position : new Vector3(e.x, .15f, e.z);
                center.y = .15f;
                float radius = e.radius > 0 ? e.radius : 4;
                for (int i = 0; i < 49; i++)
                {
                    float angle = i * Mathf.PI * 2 / 48;
                    line.SetPosition(i, center + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius);
                }
            }
        }

        private void OnDestroy()
        {
            if (_gearMaterial != null)
                Destroy(_gearMaterial);
            foreach (var line in _shapes.Values)
                if (line != null)
                    Destroy(line.sharedMaterial);
        }
    }
}
