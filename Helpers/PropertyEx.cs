namespace SMAInteropConverter.Helpers
{
    public static class PropertyEx
    {
        // For getters and setters
        // get_Name -> GetName
        public static string GetNormalMethodName(this string name)
        {
            return string.Concat(char.ToUpper(name[0]), name.Substring(1, 2), name.Substring(4));
        }

        // For getters and setters
        // get_Name = > Name
        public static string GetPropertyName(this string name)
        {
            return name.Substring(4);
        }
    }
}
