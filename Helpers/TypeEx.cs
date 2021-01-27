using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SMAInteropConverter.Helpers
{
    public static class TypeEx
    {
        public static bool IsRegMember(this Type type, List<RegistryType> regTypes, out RegistryType regType)
        {
            regType = regTypes.Where(x => x.Member == type).FirstOrDefault();
            return regType != null;
        }

        public static string GetSvcName(this Type type)
        {
            return type.Name.AsAlphabetic() + "Svc";
        }

        public static string GetSvcNamespace(this Type type)
        {
            return type.Name.AsAlphabetic() + "Namespace";
        }

        public static List<MethodInfo> GetConvertableMethods(this Type type)
        {
            // Skips out params
            // Skips special methods
            // Includes the most specialized methods of inherited interface types

            var methods = type
                .GetMethods()
                .Where(x => !x.IsSpecialName)
                .Where(x => !x.GetParameters().Any(p => p.IsOut))
                .ToList();

            if (type.IsInterface)
            {
                var implemented = type.GetInterfaces();

                foreach (var i in implemented)
                {
                    var toAdd = i.GetConvertableMethods().Where(x => !methods.Any(m => m.Name == x.Name));
                    methods.AddRange(toAdd);
                }
            }

            return methods;
        }

        public static List<MethodInfo> GetSetters(this Type type)
        {
            return type.GetMethods()
                       .Where(x => x.IsSpecialName && x.Name.StartsWith("set_"))
                       .ToList();
        }

        public static List<MethodInfo> GetGetters(this Type type)
        {
            return type.GetMethods()
                       .Where(x => x.IsSpecialName && x.Name.StartsWith("get_"))
                       .ToList();
        }
    }
}
