using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VRage.Game.Definitions;
using VRage.Game.GUI.TextPanel;
using VRage.Plugins;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace HudSprite;

public struct SurfaceConfig
{
    public Vector2 TopLeft;
    public float Scale;
}

public class Plugin : IPlugin
{
    const string HUDLCD_TAG = "hudlcd";
    public const string OFFSCREEN_TEX_PREFIX = "HUDSPRITE_";
    static readonly char[] _newline = ['\n'];
    static readonly char[] _configSeparator = [':'];
    static readonly Vector2D _defaultPos = new Vector2D(-0.98, -0.2);

    static readonly Dictionary<string, Color> _colorByName =
        typeof(Color).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.CanRead && p.PropertyType == typeof(Color))
            .ToDictionary(p => p.Name.ToLower(), p => (Color)p.GetValue(null));

    public void Init(object gameInstance)
    {
        new Harmony(GetType().FullName).PatchAll(Assembly.GetExecutingAssembly());
    }

    private int _lastLcdGather = -1;
    private readonly List<MyTextPanelComponent> _lcdsToRemove = [];

    public void Update()
    {
        if (MySession.Static is null || !MySession.Static.Ready)
        {
            ClearLcds();
            _lastLcdGather = -1;
            return;
        }

        if (MySession.Static.ControlledEntity is MyCockpit cockpit)
        {
            if (MySession.Static.GameplayFrameCounter - 100 > _lastLcdGather)
            {
                GatherNewLcds(cockpit.CubeGrid);
                _lastLcdGather = MySession.Static.GameplayFrameCounter;
            }
        }
        else
        {
            ClearLcds();
            _lastLcdGather = -1;
        }

        // update surfaces
        bool update10 = MySession.Static.GameplayFrameCounter % 10 == 0;
        foreach (var data in Surfaces.Values)
        {
            if (!data.Update(update10))
            {
                _lcdsToRemove.Add(data.Comp);
            }
        }

        // remove invalid lcds
        for (int i = _lcdsToRemove.Count - 1; i >= 0; i--)
        {
            if (Surfaces.TryRemove(_lcdsToRemove[i], out var data))
            {
                data.Dispose();
            }
            _lcdsToRemove.RemoveAt(i);
        }
    }

    public static readonly ConcurrentDictionary<MyTextPanelComponent, HudSpriteData> Surfaces = [];
    public static readonly HashSet<string> CreatedTextures = [];

    private static void GatherNewLcds(MyCubeGrid grid)
    {
        foreach (var block in grid.GetFatBlocks<MyTerminalBlock>())
        {
            if (block.CustomData == null || block is not Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider surfaceProvider)
            {
                continue;
            }

            string[] lines = block.CustomData.ToLower().Split(_newline, StringSplitOptions.RemoveEmptyEntries);
            int surfaceIndex = 0;
            foreach (var line in lines)
            {
                if (surfaceIndex >= Math.Max(1, surfaceProvider.SurfaceCount))
                    break;

                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith(HUDLCD_TAG))
                    continue;

                if (surfaceProvider.GetSurface(surfaceIndex) is not MyTextPanelComponent surface)
                    continue;

                if (Surfaces.ContainsKey(surface))
                    continue;

                var data = new HudSpriteData(surfaceProvider, surface, surfaceIndex);
                Surfaces.TryAdd(surface, data);
                data.UpdateSettings();

                surfaceIndex++;
            }
        }
    }

    private static void ClearLcds()
    {
        Surfaces.Values.ForEach(i => i.Dispose());
        Surfaces.Clear();
    }

    static readonly MyStringHash _fontMonospace = MyStringHash.GetOrCompute("Monospace");

    public class HudSpriteData : IDisposable
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

        public HudSpriteData(Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider block, MyTextPanelComponent comp, int surfaceIndex)
        {
            _surfaceProvider = block;
            Comp = comp;
            SurfaceIndex = surfaceIndex;
        }

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
                var screenSize = MyGuiManager.GetFullscreenRectangle();
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

                    var line = lines[l];
                    const string COLOR_TAG_START = "<color=";
                    const char COLOR_TAG_END = '>';

                    float xOffset = 0;

                    string[] candidates = line.Split([COLOR_TAG_START], StringSplitOptions.None);
                    Color prevColor = TextColor;
                    foreach (var candidate in candidates)
                    {
                        int end = candidate.IndexOf(COLOR_TAG_END);
                        Color color = default;
                        bool colorValid = false;
                        if (end >= 0)
                        {
                            string colorName = candidate.Substring(0, end);
                            if (_colorByName.TryGetValue(colorName, out color))
                            {
                                colorValid = true;
                            }
                            else
                            {
                                string[] args = colorName.Split([','], 4);
                                if (args.Length == 3)
                                {
                                    // only rgb
                                    if (byte.TryParse(args[0], out byte r) &&
                                        byte.TryParse(args[1], out byte g) &&
                                        byte.TryParse(args[2], out byte b))
                                    {
                                        color = new Color(r, g, b);
                                        colorValid = true;
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
                                        colorValid = true;
                                    }
                                }
                            }
                        }

                        if (!colorValid)
                        {
                            color = prevColor;
                        }
                        prevColor = color;

                        string textToDraw;
                        if (!colorValid || end <= 0)
                        {
                            // invalid or malformed color tag
                            textToDraw = xOffset is 0 ? candidate : $"{COLOR_TAG_START}{candidate}";
                        }
                        else
                        {
                            if (candidate.Length - (end + 1) <= 0)
                            {
                                // valid color tag but no text to draw after the closing char
                                continue;
                            }

                            textToDraw = candidate.Substring(end + 1);
                        }

                        Vector2 pos = TopLeft + offset + new Vector2(xOffset, 0);
                        DrawString(textToDraw, pos, scale, color);
                        xOffset += GetStringWidth(textToDraw, font, scale, screenSize.Width);
                    }
                }
            }

            return true;
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

    public void Dispose()
    {
        ClearLcds();
    }
}
