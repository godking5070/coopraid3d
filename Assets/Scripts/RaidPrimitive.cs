using UnityEngine;
using UnityEngine.Rendering;

namespace RaidRogue
{
    // URP's defaultMaterial is editor-only. Player-created primitives otherwise use
    // the built-in Standard material, which renders magenta under URP.
    public sealed class RaidPrimitive : MonoBehaviour
    {
        private Material _ownedMaterial;

        public static GameObject Create(PrimitiveType type)
        {
            var primitive = GameObject.CreatePrimitive(type);
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null) return primitive;
            var shader = pipeline.defaultShader;
            if (shader == null || !shader.isSupported)
            {
                Debug.LogError("Runtime primitive has no supported render-pipeline shader: " + type);
                return primitive;
            }
            var owner = primitive.AddComponent<RaidPrimitive>();
            // Acquire this renderer's instance rather than editing a shared or authored asset.
            owner._ownedMaterial = primitive.GetComponent<Renderer>().material;
            owner._ownedMaterial.shader = shader;
            owner._ownedMaterial.color = Color.white;
            return primitive;
        }

        private void OnDestroy()
        {
            if (_ownedMaterial != null) Destroy(_ownedMaterial);
        }
    }
}
