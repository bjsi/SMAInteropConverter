using System.Linq;

namespace SMAInteropConverter.Helpers
{
    public static class StringEx
    {
        public static string AsAlphabetic(this string s)
        {
            return string.Concat(s.Where(c => char.IsLetter(c)));
        }
    }
}
