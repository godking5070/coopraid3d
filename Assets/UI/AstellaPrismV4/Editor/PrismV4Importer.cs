#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Astella.PrismV4.Editor
{
    /// <summary>Explicit, pack-local import settings. No initialize-on-load hook or active HUD writes.</summary>
    public static class PrismV4Importer
    {
        public const string Root = "Assets/UI/AstellaPrismV4";

        [MenuItem("Tools/Astella/Prism V4/Import Sprites")]
        public static void ImportSprites()
        {
            EnsureEditMode();
            string folder = Root + "/Sprites";
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
            string[] files = Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                string assetPath = file.Replace('\\', '/');
                RequirePackPath(assetPath);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Expected a PNG TextureImporter: " + assetPath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.sRGBTexture = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 2048;
                importer.spritePixelsPerUnit = 100;
                importer.spriteBorder = Vector4.zero;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spritePivot = new Vector2(.5f, .5f);
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            Debug.Log("Prism V4: configured " + files.Length + " sprites inside " + folder + ". Active scenes and presentation profiles were not changed.");
        }

        internal static void EnsureEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Run this authoring menu after compilation, outside Play mode.");
        }

        internal static void RequirePackPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith(Root + "/", StringComparison.Ordinal) ||
                assetPath.Contains("..") || assetPath.Contains("\\"))
                throw new InvalidOperationException("Write/import target must remain inside " + Root + ": " + assetPath);
        }
    }
}
#endif
