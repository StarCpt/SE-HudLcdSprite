using Sandbox.Game.Entities.Blocks;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VRage.Game.Definitions;
using VRage.Game.GUI.TextPanel;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace HudSprite;

public partial class Plugin
{
    static readonly Dictionary<string, Color> _colorByName =
        typeof(Color).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.CanRead && p.PropertyType == typeof(Color))
            .ToDictionary(p => p.Name.ToLower(), p => (Color)p.GetValue(null));

    public class HudSpriteSurface : IDisposable
    {
        public MyTextPanelComponent Comp { get; private set; }
        public int SurfaceIndex { get; }
        public Vector2 TopLeft { get; private set; }
        public float Scale { get; private set; }
        public Color TextColor { get; private set; }

        public string? OffscreenTextureName { get; private set; }

        private readonly Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider _surfaceProvider;
        private bool _textureCreated = false;
        private string? _prevCustomDataText = null;
        private float _prevCompScale;
        private Color _prevCompColor;

        public HudSpriteSurface(Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider block, MyTextPanelComponent comp, int surfaceIndex)
        {
            _surfaceProvider = block;
            Comp = comp;
            SurfaceIndex = surfaceIndex;
        }

        static readonly char[] _configSeparator = [':'];
        static readonly Vector2D _defaultPos = new Vector2D(-0.98, -0.2);

        /// <summary>
        /// 
        /// </summary>
        /// <returns>False if hudlcd config is not found.</returns>
        public bool UpdateSettings()
        {
            string? customData = Comp.m_block.CustomData;
            if (customData == null)
            {
                return false;
            }

            bool configChanged = _prevCustomDataText != customData || _prevCompScale != Comp.FontSize || _prevCompColor != Comp.FontColor;
            if (!configChanged)
            {
                return true;
            }
            _prevCustomDataText = customData;
            _prevCompScale = Comp.FontSize;
            _prevCompColor = Comp.FontColor;

            string[] lines = customData.ToLower().Split(_newline, StringSplitOptions.RemoveEmptyEntries);
            int surfaceIndex = 0;
            for (int l = 0; l < lines.Length; l++)
            {
                string line = lines[l];
                if (surfaceIndex > SurfaceIndex)
                    return false; // config for this surface not found

                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith(HUDLCD_TAG))
                    continue;

                if (surfaceIndex == SurfaceIndex)
                {
                    Vector2D pos = _defaultPos;
                    double scale = Comp.FontSize;
                    Color textColor = Comp.FontColor;

                    string[] args = line.Trim().Split(_configSeparator, 6);
                    for (int i = 1; i < Math.Min(6, args.Length); i++)
                    {
                        if (string.IsNullOrWhiteSpace(args[i]))
                            continue;

                        switch (i)
                        {
                            case 1: // x position
                                pos.X = TryParseOrDefault(args[i], pos.X);
                                break;
                            case 2: // y position
                                pos.Y = TryParseOrDefault(args[i], pos.Y);
                                break;
                            case 3:
                                scale = TryParseOrDefault(args[i], scale);
                                break;
                            case 4:
                                textColor = _colorByName.GetValueOrDefault(args[i].Trim().ToLower(), textColor);
                                break;
                            case 5:
                                // text shadow; not used
                                break;
                        }
                    }

                    // change [-1,1] top left, [1,-1] bottom right used by hudlcd to [0,0], [1,1] used by rendering
                    pos.X = (pos.X + 1) * 0.5;
                    pos.Y = (-pos.Y + 1) * 0.5;
                    scale *= 0.682; // match with original hudlcd

                    TopLeft = (Vector2)pos;
                    Scale = (float)scale;
                    TextColor = textColor;
                    return true;
                }
                surfaceIndex++;
            }
            return false;
        }

        private static double TryParseOrDefault(string str, double defaultValue)
        {
            return double.TryParse(str, out var val) ? val : defaultValue;
        }

        private enum TagType
        {
            LineStart = 0,
            ColorStart = 1,
            ColorEnd = 2,
            Reset = 3,
        }

        static readonly MyStringHash _fontMonospace = MyStringHash.GetOrCompute("Monospace");
        const string COLOR_TAG_START = "<color=";
        const char COLOR_TAG_END = '>';
        const string RESET_TAG = "<reset>";

        private struct TagToken(TagType type, int startIndex)
        {
            public TagType Type = type;
            public int StartIndex = startIndex;
            public readonly int EndIndex => StartIndex + Type switch
            {
                TagType.ColorStart => COLOR_TAG_START.Length,
                TagType.ColorEnd => 1,
                TagType.Reset => RESET_TAG.Length,
                _ => throw new Exception(),
            };
        }

        // reused globally to reduce allocations
        private static readonly List<TagToken> _tempTokens = [];

        /// <summary>
        /// 
        /// </summary>
        /// <param name="update10"></param>
        /// <returns>False if this instance is invalid and should be disposed.</returns>
        public bool Update(bool update10)
        {
            if (update10)
            {
                if (_surfaceProvider.GetSurface(SurfaceIndex) is not MyTextPanelComponent newComp)
                {
                    // unsure if the surface is ever null or not MyTextPanelComponent
                    return false;
                }

                if (newComp != Comp)
                {
                    // surface comp changed, likely due to a surface rotation change
                    DestroyTexture();
                    Comp = newComp;
                }

                if (!UpdateSettings())
                {
                    return false;
                }

                if (Comp.m_textureGenerated && Comp.ContentType is ContentType.SCRIPT && Comp.m_block.IsWorking)
                {
                    CreateTexture();
                }
                else
                {
                    DestroyTexture();
                }
            }

            if (Comp.ContentType is ContentType.TEXT_AND_IMAGE)
            {
                var screenWidth = MyGuiManager.GetFullscreenRectangle().Width;
                var font = MyFontDefinition.GetFont(Comp.Font.SubtypeId);

                string[] lines = Comp.Text.Split('\n');
                for (int l = 0; l < lines.Length; l++)
                {
                    // match original hudlcd position/line spacing
                    Vector2 offset;
                    float scale = Scale;
                    if (Comp.Font.SubtypeId == _fontMonospace)
                    {
                        offset = new Vector2(0, -0.0045f + (0.02825f * l)) * Scale;
                        scale *= 1.018f;
                    }
                    else
                    {
                        offset = new Vector2(0.0001f, -0.005f + (0.0229f * l)) * Scale;
                    }

                    if (TopLeft.X + offset.X >= 1 || TopLeft.Y + offset.Y >= 1)
                    {
                        break; // early out for nonvisible text
                    }

                    var line = lines[l];

                    // identify tags
                    int i = 0;
                    _tempTokens.Clear();
                    List<TagToken> tokens = _tempTokens;
                    tokens.Add(new TagToken(TagType.LineStart, 0));
                    while (i < line.Length)
                    {
                        if (line[i] is '<')
                        {
                            if (line.Length >= i + COLOR_TAG_START.Length && line.IndexOf(COLOR_TAG_START, i, COLOR_TAG_START.Length) == i)
                            {
                                tokens.Add(new TagToken(TagType.ColorStart, i));
                                i += COLOR_TAG_START.Length;
                            }
                            else if (line.Length >= i + RESET_TAG.Length && line.IndexOf(RESET_TAG, i, RESET_TAG.Length) == i)
                            {
                                tokens.Add(new TagToken(TagType.Reset, i));
                                i += RESET_TAG.Length;
                            }
                            else
                            {
                                i++;
                            }
                        }
                        else if (line[i] is '>' && tokens.Count > 0 && tokens[tokens.Count - 1].Type is TagType.ColorStart)
                        {
                            tokens.Add(new TagToken(TagType.ColorEnd, i));
                            i++;
                        }
                        else
                        {
                            i++;
                        }
                    }

                    if (tokens.Count > 1) // always has at least 1 LineStart token
                    {
                        float xOffset = 0;
                        Color color = TextColor;

                        int prevSegmentEndIndex = 0;
                        for (int t = 0; t < tokens.Count; t++)
                        {
                            TagToken token = tokens[t];
                            int segmentStart = prevSegmentEndIndex;
                            int segmentEnd = (t + 1) < tokens.Count ? tokens[t + 1].StartIndex : line.Length;
                            if (token.Type is TagType.ColorStart && t + 1 < tokens.Count)
                            {
                                TagToken nextToken = tokens[t + 1];
                                if (nextToken.Type is TagType.ColorEnd)
                                {
                                    if (TryParseColor(line.Substring(token.EndIndex, nextToken.StartIndex - token.EndIndex), out Color newColor))
                                    {
                                        color = newColor;
                                        segmentStart = nextToken.EndIndex;
                                    }
                                    t++;
                                    segmentEnd = (t + 1) < tokens.Count ? tokens[t + 1].StartIndex : line.Length;
                                }
                            }
                            else if (token.Type is TagType.Reset)
                            {
                                color = TextColor;
                                segmentStart = token.EndIndex;
                            }

                            string text = line.Substring(segmentStart, segmentEnd - segmentStart);
                            if (text.Length > 0)
                            {
                                Vector2 pos = TopLeft + offset + new Vector2(xOffset, 0);
                                DrawString(text, pos, scale, color);
                                xOffset += GetStringWidth(text, font, scale, screenWidth);
                            }

                            prevSegmentEndIndex = segmentEnd;
                        }
                    }
                    else
                    {
                        DrawString(line, TopLeft + offset, scale, TextColor);
                    }
                }
            }

            return true;
        }

        private static bool TryParseColor(string str, out Color color)
        {
            if (_colorByName.TryGetValue(str, out color))
            {
                return true;
            }
            else
            {
                string[] args = str.Split([','], 4);
                if (args.Length == 3)
                {
                    // only rgb
                    if (byte.TryParse(args[0], out byte r) &&
                        byte.TryParse(args[1], out byte g) &&
                        byte.TryParse(args[2], out byte b))
                    {
                        color = new Color(r, g, b);
                        return true;
                    }
                }
                else if (args.Length == 4)
                {
                    // rgba
                    if (byte.TryParse(args[0], out byte r) &&
                        byte.TryParse(args[1], out byte g) &&
                        byte.TryParse(args[2], out byte b) &&
                        byte.TryParse(args[3], out byte a))
                    {
                        color = new Color(r, g, b, a);
                        return true;
                    }
                }
            }
            return false;
        }

        // use this instead of MyGuiManager.DrawString() to avoid snapping positions to pixels
        private void DrawString(string text, Vector2 pos, float scale, Color color)
        {
            Vector2 normalizedPos = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(pos, true);
            MyRenderProxy.DrawString((int)Comp.Font.SubtypeId, normalizedPos, color, text, scale, float.PositiveInfinity, true);
        }

        private float GetStringWidth(string text, MyFont font, float scale, float screenWidth)
        {
            return (font.MeasureString(text, scale, false).X + scale) * MyGuiConstants.FONT_SCALE / screenWidth;
        }

        private void CreateTexture()
        {
            if (!_textureCreated)
            {
                _textureCreated = true;
                string name = OFFSCREEN_TEX_PREFIX + Comp.GetRenderTextureName();
                MyRenderProxy.CreateGeneratedTexture(name, Comp.m_textureSize.X, Comp.m_textureSize.Y, MyGeneratedTextureType.RGBA, 1, null, generateMipmaps: true, immediatelyReady: false);
                Comp.m_lastRenderLayers.Clear();
                CreatedTextures.Add(name);
                OffscreenTextureName = name;
            }
        }

        private void DestroyTexture()
        {
            if (_textureCreated)
            {
                _textureCreated = false;
                string name = OFFSCREEN_TEX_PREFIX + Comp.GetRenderTextureName();
                MyRenderProxy.DestroyGeneratedTexture(name);
                CreatedTextures.Remove(name);
                OffscreenTextureName = null;
            }
        }

        public IUserGeneratedTexture? TryGetRenderTexture()
        {
            if (OffscreenTextureName is not null && MyManagers.FileTextures.TryGetTexture(OffscreenTextureName, out IUserGeneratedTexture texture) && texture.IsLoaded)
            {
                return texture;
            }
            return null;
        }

        public void Dispose()
        {
            DestroyTexture();
        }
    }
}
