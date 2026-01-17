using Sandbox;
using Sandbox.Graphics.GUI;
using System;
using System.Linq;
using VRage.Utils;
using VRageMath;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
namespace HudSprite.Config;

public class GuiScreenHudSpriteConfig : MyGuiScreenBase
{
    private MyGuiControlCheckbox _enabledCheckbox, _scanAllTextCheckbox;

    private readonly HudSpriteSettings _settings;

    public GuiScreenHudSpriteConfig(HudSpriteSettings settings)
        : base(new Vector2(0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.4f, 0.3f), false, null, MySandboxGame.Config.UIBkOpacity, MySandboxGame.Config.UIOpacity)
    {
        _settings = settings;

        EnabledBackgroundFade = true;
        m_closeOnEsc = true;
        m_drawEvenWithoutFocus = true;
        CanHideOthers = true;
        CanBeHidden = true;
        CloseButtonEnabled = true;
    }

    public override string GetFriendlyName() => GetType().FullName;

    public override void LoadContent()
    {
        base.LoadContent();
        RecreateControls(false);
    }

    public override void RecreateControls(bool constructor)
    {
        base.RecreateControls(constructor);

        AddCaption("SSGI Settings");

        float rowHeight = 0.05f;

        float posY = -rowHeight;
        AddControl(new MyGuiControlLabel(new Vector2(0, posY), text: "Enabled", originAlign: MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER));
        _enabledCheckbox = new MyGuiControlCheckbox(new Vector2(0.01f, posY), toolTip: "Enable Plugin", isChecked: _settings.Enabled, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        AddControl(_enabledCheckbox);

        posY += rowHeight;
        AddControl(new MyGuiControlLabel(new Vector2(0, posY), text: "Scan all text", originAlign: MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER));
        _scanAllTextCheckbox = new MyGuiControlCheckbox(new Vector2(0.01f, posY), toolTip: "Scan all text for the hudlcd tag\ninstead of only the start of each line.", isChecked: _settings.ScanAllText, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
        AddControl(_scanAllTextCheckbox);

        // add footer buttons
        float yPos = (Size!.Value.Y * 0.5f) - (MyGuiConstants.SCREEN_CAPTION_DELTA_Y / 2f);
        var button = new MyGuiControlButton(onButtonClick: OnSaveButtonClick)
        {
            Position = new Vector2(0, yPos),
            Text = "Save",
            OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM,
        };
        AddControl(button);
    }

    private void OnSaveButtonClick(MyGuiControlButton btn)
    {
        _settings.Enabled = _enabledCheckbox.IsChecked;
        _settings.ScanAllText = _scanAllTextCheckbox.IsChecked;
        _settings.Save();
        Plugin.Surfaces.Values.ForEach(i => i.UpdateSettings(true));
    }
}
