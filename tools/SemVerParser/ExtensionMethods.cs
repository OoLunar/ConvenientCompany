using System;
using System.Diagnostics.CodeAnalysis;

namespace OoLunar.ConvenientCompany.Tools.SemVerParser
{
    public static class ExtensionMethods
    {
        [return: NotNull]
        public static T ExpectNullable<T>(this T? value, string message) where T : class
        {
            if (value is null)
            {
                Console.WriteLine(message);
                Environment.Exit(1);
            }

            return value;
        }
    }
}