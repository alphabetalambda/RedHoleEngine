using System;
using System.Numerics;

namespace RedHoleEngine.Rendering.UI;

public static class UiFontAtlas
{
    public const int GlyphSize = 8;
    public const int Grid = 16;
    public const int AtlasSize = GlyphSize * Grid;
    public const int WhiteGlyphIndex = 255;

    public static Vector4 GetGlyphUv(int glyphIndex)
    {
        var clamped = Math.Clamp(glyphIndex, 0, 255);
        int col = clamped % Grid;
        int row = clamped / Grid;

        float u0 = (col * GlyphSize) / (float)AtlasSize;
        float v0 = (row * GlyphSize) / (float)AtlasSize;
        float u1 = ((col + 1) * GlyphSize) / (float)AtlasSize;
        float v1 = ((row + 1) * GlyphSize) / (float)AtlasSize;

        return new Vector4(u0, v0, u1, v1);
    }

    public static byte[] BuildAtlas()
    {
        var atlas = new byte[AtlasSize * AtlasSize];

        int glyphCount = Math.Min(Font8x8Basic.Length, 128);
        for (int glyph = 0; glyph < glyphCount; glyph++)
        {
            var rows = Font8x8Basic[glyph];
            int col = glyph % Grid;
            int row = glyph / Grid;
            int baseX = col * GlyphSize;
            int baseY = row * GlyphSize;

            for (int y = 0; y < GlyphSize; y++)
            {
                byte rowBits = rows[y];
                for (int x = 0; x < GlyphSize; x++)
                {
                    bool on = (rowBits & (1 << x)) != 0;
                    int px = baseX + x;
                    int py = baseY + y;
                    atlas[py * AtlasSize + px] = on ? (byte)255 : (byte)0;
                }
            }
        }

        // Solid white glyph for rectangles
        {
            int glyph = WhiteGlyphIndex;
            int col = glyph % Grid;
            int row = glyph / Grid;
            int baseX = col * GlyphSize;
            int baseY = row * GlyphSize;
            for (int y = 0; y < GlyphSize; y++)
            {
                for (int x = 0; x < GlyphSize; x++)
                {
                    int px = baseX + x;
                    int py = baseY + y;
                    atlas[py * AtlasSize + px] = 255;
                }
            }
        }

        return atlas;
    }

    private static readonly byte[][] Font8x8Basic =
    {
        new byte[] { 0,0,0,0,0,0,0,0 },
        new byte[] { 24,60,60,24,24,0,24,0 },
        new byte[] { 54,54,20,0,0,0,0,0 },
        new byte[] { 54,54,127,54,127,54,54,0 },
        new byte[] { 24,62,3,30,48,31,24,0 },
        new byte[] { 0,99,103,14,28,56,115,99 },
        new byte[] { 28,54,28,59,102,102,59,0 },
        new byte[] { 6,6,12,0,0,0,0,0 },
        new byte[] { 12,6,3,3,3,6,12,0 },
        new byte[] { 3,6,12,12,12,6,3,0 },
        new byte[] { 0,102,60,255,60,102,0,0 },
        new byte[] { 0,12,12,63,12,12,0,0 },
        new byte[] { 0,0,0,0,0,12,12,6 },
        new byte[] { 0,0,0,63,0,0,0,0 },
        new byte[] { 0,0,0,0,0,12,12,0 },
        new byte[] { 96,48,24,12,6,3,1,0 },
        new byte[] { 62,99,115,123,111,103,62,0 },
        new byte[] { 12,14,12,12,12,12,63,0 },
        new byte[] { 62,99,96,48,24,12,127,0 },
        new byte[] { 62,99,96,56,96,99,62,0 },
        new byte[] { 48,56,60,54,127,48,120,0 },
        new byte[] { 127,3,63,96,96,99,62,0 },
        new byte[] { 60,6,3,63,99,99,62,0 },
        new byte[] { 127,99,48,24,12,12,12,0 },
        new byte[] { 62,99,99,62,99,99,62,0 },
        new byte[] { 62,99,99,126,96,48,30,0 },
        new byte[] { 0,12,12,0,0,12,12,0 },
        new byte[] { 0,12,12,0,0,12,12,6 },
        new byte[] { 48,24,12,6,12,24,48,0 },
        new byte[] { 0,0,63,0,0,63,0,0 },
        new byte[] { 6,12,24,48,24,12,6,0 },
        new byte[] { 62,99,96,48,24,0,24,0 },
        new byte[] { 62,99,123,123,123,3,62,0 },
        new byte[] { 24,60,102,102,126,102,102,0 },
        new byte[] { 63,102,102,62,102,102,63,0 },
        new byte[] { 60,102,3,3,3,102,60,0 },
        new byte[] { 31,54,102,102,102,54,31,0 },
        new byte[] { 127,70,22,30,22,70,127,0 },
        new byte[] { 127,70,22,30,22,6,15,0 },
        new byte[] { 60,102,3,3,115,102,124,0 },
        new byte[] { 102,102,102,126,102,102,102,0 },
        new byte[] { 63,12,12,12,12,12,63,0 },
        new byte[] { 120,48,48,48,51,51,30,0 },
        new byte[] { 103,102,54,30,54,102,103,0 },
        new byte[] { 15,6,6,6,70,102,127,0 },
        new byte[] { 99,119,127,107,99,99,99,0 },
        new byte[] { 99,103,111,123,115,99,99,0 },
        new byte[] { 62,99,99,99,99,99,62,0 },
        new byte[] { 63,102,102,62,6,6,15,0 },
        new byte[] { 62,99,99,99,107,59,110,0 },
        new byte[] { 63,102,102,62,54,102,103,0 },
        new byte[] { 62,99,7,62,112,99,62,0 },
        new byte[] { 63,45,12,12,12,12,30,0 },
        new byte[] { 99,99,99,99,99,99,62,0 },
        new byte[] { 99,99,99,99,99,54,28,0 },
        new byte[] { 99,99,99,107,127,119,99,0 },
        new byte[] { 99,99,54,28,54,99,99,0 },
        new byte[] { 51,51,51,30,12,12,30,0 },
        new byte[] { 127,99,49,24,76,102,127,0 },
        new byte[] { 15,3,3,3,3,3,15,0 },
        new byte[] { 3,6,12,24,48,96,64,0 },
        new byte[] { 15,12,12,12,12,12,15,0 },
        new byte[] { 8,28,54,99,0,0,0,0 },
        new byte[] { 0,0,0,0,0,0,0,255 },
        new byte[] { 12,12,24,0,0,0,0,0 },
        new byte[] { 0,0,30,48,62,51,110,0 },
        new byte[] { 7,6,6,62,102,102,59,0 },
        new byte[] { 0,0,62,99,3,99,62,0 },
        new byte[] { 56,48,48,62,51,51,110,0 },
        new byte[] { 0,0,62,99,127,3,62,0 },
        new byte[] { 28,54,6,15,6,6,15,0 },
        new byte[] { 0,0,110,51,51,62,48,31 },
        new byte[] { 7,6,54,110,102,102,103,0 },
        new byte[] { 12,0,14,12,12,12,30,0 },
        new byte[] { 24,0,28,24,24,24,27,14 },
        new byte[] { 7,6,102,54,30,54,103,0 },
        new byte[] { 14,12,12,12,12,12,30,0 },
        new byte[] { 0,0,55,127,107,99,99,0 },
        new byte[] { 0,0,59,103,99,99,99,0 },
        new byte[] { 0,0,62,99,99,99,62,0 },
        new byte[] { 0,0,59,102,102,62,6,15 },
        new byte[] { 0,0,110,51,51,62,48,120 },
        new byte[] { 0,0,59,103,3,3,7,0 },
        new byte[] { 0,0,126,3,62,96,63,0 },
        new byte[] { 8,12,63,12,12,44,24,0 },
        new byte[] { 0,0,99,99,99,99,110,0 },
        new byte[] { 0,0,99,99,99,54,28,0 },
        new byte[] { 0,0,99,99,107,127,54,0 },
        new byte[] { 0,0,99,54,28,54,99,0 },
        new byte[] { 0,0,99,99,99,62,48,31 },
        new byte[] { 0,0,127,25,12,38,127,0 },
        new byte[] { 56,12,12,7,12,12,56,0 },
        new byte[] { 12,12,12,0,12,12,12,0 },
        new byte[] { 7,12,12,56,12,12,7,0 },
        new byte[] { 110,59,0,0,0,0,0,0 },
        new byte[] { 0,8,28,54,99,99,127,0 }
    };
}
