using System;
namespace Shinobi.WebSockets.Extensions
{
    public static class Net47Extensions
    {
        public static bool Contains(this string source, string value, StringComparison comparisonType)
        {
            return source?.IndexOf(value, comparisonType) >= 0;
        }
    }
}