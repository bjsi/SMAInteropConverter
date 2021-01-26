using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SMAInteropConverter
{
    public class InteropTypeExtractor
    {
        private Assembly SMAAssembly { get; }
        private Type UI { get; }
        private Type Registry { get; }

        public InteropTypeExtractor(string dllPath)
        {
            SMAAssembly = Assembly.LoadFile(dllPath);
            var iSuperMemo = SMAAssembly.GetType("SuperMemoAssistant.Interop.SuperMemo.ISuperMemo");
            UI = iSuperMemo.GetProperty("UI").PropertyType;
            Registry = iSuperMemo.GetProperty("Registry").PropertyType;
        }

        public List<RegistryType> GetRegistries()
        {
            var ret = new List<RegistryType>();
            var props = Registry.GetProperties();
            var types = props?.Select(x => x.PropertyType).ToList();
            foreach (var regType in types)
            {
                if (regType.Name.Contains("Component")) // TODO
                    continue;

                var memberType = GetMemberType(regType);
                ret.Add(new RegistryType(regType, memberType));
            }
            return ret;
        }

        private Type GetMemberType(Type regType)
        {
            // Get the generic parameter of the IRegistry interface
            var inter = regType.GetInterface("IRegistry`1");
            return inter.GetGenericArguments().First();
        }

        public List<Type> GetUITypes()
        {
            var props = UI.GetProperties();
            return props?.Select(x => x.PropertyType).ToList();
        }
    }
}
