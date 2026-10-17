// Only the WPF image boundary is stubbed. All model, parsing, validation, identity,
// backup, and file-writing methods under test are the linked production sources.
namespace System.Windows.Media
{
    public abstract class ImageSource { }
}
namespace ShortcutList.Services
{
    public static class FileIconService
    {
        public static System.Windows.Media.ImageSource? GetIcon(ShortcutList.Models.ShortcutItem item) => null;
    }
}
