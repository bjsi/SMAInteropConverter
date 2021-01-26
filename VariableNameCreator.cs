using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMAInteropConverter
{
    public class VariableNameCreator
    {
        private int Index { get; set; } = 0;
        private string Name { get; } = "var";
        public string GetName()
        {
            var name = Name + Index;
            Index++;
            return name;
        }
    }
}
