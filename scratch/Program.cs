using System;
using System.Reflection;
using System.Linq;

class Program
{
    static void Main()
    {
        var asm = Assembly.LoadFrom(@"C:\Program Files (x86)\Steam\steamapps\common\OxygenNotIncluded\OxygenNotIncluded_Data\Managed\Assembly-CSharp.dll");
        
        var type = asm.GetType("GameTags");
        if (type != null)
        {
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.Name.IndexOf("Seed", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine($"{f.Name} = {f.GetValue(null)}");
                }
            }
        }
    }
}














