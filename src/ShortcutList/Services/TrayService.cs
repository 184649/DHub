using System.Drawing;
using System.Windows.Forms;

namespace ShortcutList.Services;

public class TrayService
{
    private NotifyIcon? _icon;

    public void Initialize()
    {
        _icon = new NotifyIcon();
        _icon.Icon = SystemIcons.Application;
        _icon.Visible = true;
        _icon.Text = "DHub";
    }
}
