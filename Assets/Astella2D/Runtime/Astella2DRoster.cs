using System;
using System.Collections.Generic;
using UnityEngine;

namespace Astella.TwoD
{
    public sealed class PixelCharacter
    {
        public string Id;
        public string Name;
        public PixelRole Role;
    }

    /// <summary>
    /// The existing concept branch's eight-direction idle art, used on an XY playfield.
    /// The roster owns generated sprites, while Resources owns the loaded texture assets.
    /// </summary>
    public static class Astella2DRoster
    {
        private const int FrameWidth = 128;
        private const int FrameHeight = 144;
        private const int Columns = 4;
        private const int Rows = 2;
        private const float PixelsPerUnit = 96f;

        private static readonly string[] DirectionNames =
        {
            "s", "se", "e", "ne", "n", "nw", "w", "sw"
        };

        private static readonly Dictionary<int, Sprite[]> CachedFrames =
            new Dictionary<int, Sprite[]>();

        public static readonly IReadOnlyList<PixelCharacter> All = Array.AsReadOnly(new[]
        {
            new PixelCharacter { Id = "guardian", Name = "수호기사", Role = PixelRole.Tank },
            new PixelCharacter { Id = "lance-knight", Name = "랜스 창기사", Role = PixelRole.Tank },
            new PixelCharacter { Id = "hammer-knight", Name = "성전 망치 기사", Role = PixelRole.Tank },
            new PixelCharacter { Id = "chronomancer", Name = "시간술사", Role = PixelRole.Healer },
            new PixelCharacter { Id = "bard", Name = "음유시인", Role = PixelRole.Healer },
            new PixelCharacter { Id = "alchemist", Name = "연금술사", Role = PixelRole.Healer },
            new PixelCharacter { Id = "dual-blade", Name = "쌍검 암살자", Role = PixelRole.Melee },
            new PixelCharacter { Id = "dragon-lancer", Name = "용창사", Role = PixelRole.Melee },
            new PixelCharacter { Id = "forest-archer", Name = "숲의 궁수", Role = PixelRole.Ranged },
            new PixelCharacter { Id = "machinist", Name = "기공사", Role = PixelRole.Ranged },
            new PixelCharacter { Id = "nebula-mage", Name = "성운술사", Role = PixelRole.Caster },
            new PixelCharacter { Id = "causality-mage", Name = "인과술사", Role = PixelRole.Caster },
            new PixelCharacter { Id = "iris", Name = "이리스", Role = PixelRole.Caster }
        });

        /// <summary>
        /// Returns one directional idle pose. Up is north, down is south, and a zero
        /// direction uses south. Walking and attack animation are not in these atlases.
        /// </summary>
        public static Sprite Frame(int characterIndex, Vector2 direction)
        {
            if (characterIndex < 0 || characterIndex >= All.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(characterIndex), characterIndex,
                    "Character index must refer to an entry in Astella2DRoster.All.");
            }

            if (float.IsNaN(direction.x) || float.IsInfinity(direction.x) ||
                float.IsNaN(direction.y) || float.IsInfinity(direction.y))
            {
                throw new ArgumentException("Facing direction must contain finite numbers.",
                    nameof(direction));
            }

            int directionIndex = 0;
            if (direction.x != 0f || direction.y != 0f)
            {
                float angle = Mathf.Atan2(direction.x, -direction.y) * Mathf.Rad2Deg;
                directionIndex = (Mathf.RoundToInt(angle / 45f) + 8) % 8;
            }

            if (!CachedFrames.TryGetValue(characterIndex, out Sprite[] frames) ||
                frames[directionIndex] == null)
            {
                // A frame can become a Unity-null object after leaving Play mode with
                // domain reload disabled. Drop any surviving generated frames first.
                if (frames != null)
                {
                    DestroyFrames(frames);
                }

                frames = CreateFrames(characterIndex);
                CachedFrames[characterIndex] = frames;
            }

            return frames[directionIndex];
        }

        private static Sprite[] CreateFrames(int characterIndex)
        {
            string id = All[characterIndex].Id;
            string resourcePath = "Astella2D/Characters/" + id;
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                throw new InvalidOperationException("Missing 2D character atlas: " + resourcePath);
            }

            if (texture.width != FrameWidth * Columns || texture.height != FrameHeight * Rows)
            {
                throw new InvalidOperationException(
                    "The 2D character atlas must be 512 x 288 pixels: " + resourcePath);
            }

            var frames = new Sprite[DirectionNames.Length];
            var pivot = new Vector2(0.5f, 16f / FrameHeight);
            for (int i = 0; i < frames.Length; i++)
            {
                int x = (i % Columns) * FrameWidth;
                int y = (Rows - 1 - i / Columns) * FrameHeight;
                frames[i] = Sprite.Create(texture, new Rect(x, y, FrameWidth, FrameHeight),
                    pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
                frames[i].name = "Astella2D_" + id + "_" + DirectionNames[i];
            }

            return frames;
        }

        public static void Release()
        {
            foreach (Sprite[] frames in CachedFrames.Values)
            {
                DestroyFrames(frames);
            }

            CachedFrames.Clear();
        }

        private static void DestroyFrames(Sprite[] frames)
        {
            foreach (Sprite sprite in frames)
            {
                if (sprite == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(sprite);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(sprite);
                }
            }
        }
    }
}
