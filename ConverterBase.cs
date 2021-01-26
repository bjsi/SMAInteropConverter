using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SMAInteropConverter
{
    public abstract class ConverterBase
    {
        protected CodeCompileUnit Unit { get; }
        protected CodeTypeDeclaration Klass { get; }
        protected CodeNamespace Namespace { get; }

        public ConverterBase(string klass)
        {
            Unit = new CodeCompileUnit();
            Klass = new CodeTypeDeclaration(klass);
            Namespace = new CodeNamespace(klass + "Namespace");
            Klass.IsClass = true;
            Klass.TypeAttributes = TypeAttributes.Public;
            Namespace.Types.Add(Klass);
            Unit.Namespaces.Add(Namespace);
        }
    }
}
