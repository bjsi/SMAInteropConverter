using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SMAInteropConverter.Helpers
{
    public static class TypeEx
    {
        public static bool IsIEnumerableType(this Type type)
        {
            var iFaces = type.GetInterfaces();
            foreach (Type interfaceType in iFaces)
            {
                if (interfaceType.IsGenericType &&
                    interfaceType.GetGenericTypeDefinition()
                    == typeof(IEnumerable<>))
                {
                    return true;
                }
            }
            return false;
        }
        // From: https://stackoverflow.com/questions/2448800/given-a-type-instance-how-to-get-generic-type-name-in-c
        // Other solutions available if this doesn't work
        public static string ToGenericTypeString(this Type t)
        {
            if (!t.IsGenericType)
                return t.Name;
            string genericTypeName = t.GetGenericTypeDefinition().Name;
            genericTypeName = genericTypeName.Substring(0,
                genericTypeName.IndexOf('`'));
            string genericArgs = string.Join(",",
                t.GetGenericArguments()
                    .Select(ta => ToGenericTypeString(ta)).ToArray());
            return genericTypeName + "<" + genericArgs + ">";
        }

        public static HashSet<Type> GetIEnumerableTypeArgs(this Type type)
        {
            foreach (Type interfaceType in type.GetInterfaces())
            {
                if (interfaceType.IsGenericType &&
                    interfaceType.GetGenericTypeDefinition()
                    == typeof(IEnumerable<>))
                {
                    return type.GetGenericArguments().ToHashSet();
                }
            }
            return new HashSet<Type>();
        }

        public static bool IsRegMember(this Type type, List<RegistryType> regTypes, out RegistryType regType)
        {
            regType = regTypes.Where(x => x.Member == type).FirstOrDefault();
            return regType != null;
        }

        public static bool IsReg(this Type type, List<RegistryType> regTypes, out RegistryType regType)
        {
            regType = regTypes.Where(x => x.Registry == type).FirstOrDefault();
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
            // Skips special methods
            // Includes the most specialized methods of inherited interface types

            var methods = type
                .GetMethods()
                .Where(x => !x.IsSpecialName)
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
