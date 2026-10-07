using HarmonyLib;
using KMod;

namespace DataNotIncluded
{
    public class ModInfo : UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            Debug.Log("DataNotIncluded: Mod loaded successfully.");
        }
    }
}
