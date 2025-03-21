using System.Runtime.InteropServices;

namespace Illustra.Helpers
{
    public static class NaturalSortHelper
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        public static int Compare(string x, string y)
        {
            return StrCmpLogicalW(x, y);
        }
    }
}
