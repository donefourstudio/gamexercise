using System.IO;
using UnityEngine;
using Gamex.Core;

namespace Gamex.Game
{
    public static class SaveSystem
    {
        static string Path => System.IO.Path.Combine(Application.persistentDataPath, "gamex_save.json");

        public static void Wipe()
        {
            try { if (File.Exists(Path)) File.Delete(Path); }
            catch (System.Exception e) { Debug.LogWarning("[Gamex] wipe failed: " + e.Message); }
        }

        public static void Save(GameState s)
        {
            try { File.WriteAllText(Path, JsonUtility.ToJson(s)); }
            catch (System.Exception e) { Debug.LogWarning("[Gamex] save failed: " + e.Message); }
        }

        public static GameState Load()
        {
            try
            {
                if (File.Exists(Path))
                    return JsonUtility.FromJson<GameState>(File.ReadAllText(Path));
            }
            catch (System.Exception e) { Debug.LogWarning("[Gamex] load failed: " + e.Message); }
            return null;
        }
    }
}
