using System;
using System.IO;
using System.Reflection;
using System.Linq;

namespace InspectAssembly
{
    class Program
    {
        static void Main(string[] args)
        {
            var assembly = Assembly.LoadFrom(@"C:\Program Files (x86)\Steam\steamapps\common\OxygenNotIncluded\OxygenNotIncluded_Data\Managed\Assembly-CSharp.dll");
            var firstpass = Assembly.LoadFrom(@"C:\Program Files (x86)\Steam\steamapps\common\OxygenNotIncluded\OxygenNotIncluded_Data\Managed\Assembly-CSharp-firstpass.dll");

            void InspectType(string typeName)
            {
                Console.WriteLine($"\n================== TYPE: {typeName} ==================");
                Type? t = assembly.GetType(typeName) ?? firstpass.GetType(typeName);
                if (t == null)
                {
                    t = assembly.GetTypes().FirstOrDefault(x => x.Name == typeName);
                }
                if (t == null)
                {
                    Console.WriteLine("Type not found.");
                    return;
                }

                Console.WriteLine("--- Fields ---");
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    Console.WriteLine($"{f.FieldType} {f.Name}");
                }

                Console.WriteLine("--- Properties ---");
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    Console.WriteLine($"{p.PropertyType} {p.Name}");
                }
            }

            InspectType("EdiblesManager");
            InspectType("Edible");
        }
    }
}
