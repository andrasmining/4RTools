using System.Drawing;

namespace _4RTools.Model.Vanilla
{
    internal static class VanillaProxyPattern
    {
        internal static bool IsUsable(Bitmap bitmap)
        {
            return bitmap != null && bitmap.Width >= 480 && bitmap.Height >= 320;
        }
    }
}
