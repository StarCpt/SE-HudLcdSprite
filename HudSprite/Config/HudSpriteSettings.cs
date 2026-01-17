using Newtonsoft.Json;
using System;
using System.IO;
using VRage.Utils;

namespace HudSprite.Config;

public class HudSpriteSettings
{
    public bool Enabled { get; set; } = true;
    public bool ScanAllText { get; set; } = false;

    private readonly string _filePath;

    public HudSpriteSettings(string filePath)
    {
        _filePath = filePath;
    }

    public static HudSpriteSettings LoadOrCreate(string filePath)
    {
        if (!File.Exists(filePath))
        {
            MyLog.Default.Info($"{typeof(HudSpriteSettings).FullName}: Config not found, initializing default values. path={filePath}.");
            return CreateDefaultAndSave(filePath);
        }

        try
        {
            JsonSerializer serializer = new JsonSerializer();
            using (var sr = new StreamReader(filePath))
            using (var jr = new JsonTextReader(sr))
            {
                var config = new HudSpriteSettings(filePath);
                serializer.Populate(jr, config);
                return config;
            }
        }
        catch (Exception e)
        {
            MyLog.Default.Info($"{typeof(HudSpriteSettings).FullName}: Could not load config, initializing default values. path={filePath}, {e}");
            return CreateDefaultAndSave(filePath);
        }
    }

    private static HudSpriteSettings CreateDefaultAndSave(string filePath)
    {
        var conf = new HudSpriteSettings(filePath);
        conf.Save();
        return conf;
    }

    public void Save()
    {
        try
        {
            JsonSerializer serializer = new JsonSerializer
            {
                Formatting = Formatting.Indented,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath));

            using (var sw = new StreamWriter(_filePath, false))
            {
                serializer.Serialize(sw, this);
            }
        }
        catch (Exception e)
        {
            MyLog.Default.Info($"{typeof(HudSpriteSettings).FullName}: Could not save config. {e}");
        }
    }
}
