using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimpleForth
{
    public static partial class Extensions
    {
        public static T AssertNotNull<T>(this T? item) where T : class
        {
            if (item == null) throw new NullReferenceException();
            return item;
        }
    }
}
