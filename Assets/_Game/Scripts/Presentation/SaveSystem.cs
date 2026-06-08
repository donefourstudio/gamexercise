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
            try
            {
                // Stamp the current schema version on every write so loads of
                // this file don't run unnecessary migrations next session.
                if (s != null) s.schemaVersion = Migrations.CurrentVersion;
                File.WriteAllText(Path, JsonUtility.ToJson(s));
            }
            catch (System.Exception e) { Debug.LogWarning("[Gamex] save failed: " + e.Message); }
        }

        public static GameState Load()
        {
            try
            {
                if (File.Exists(Path))
                {
                    var s = JsonUtility.FromJson<GameState>(File.ReadAllText(Path));
                    // Run the migration ladder before handing the state back —
                    // old saves get fields backfilled, future saves no-op.
                    Migrations.Apply(s);
                    return s;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Gamex] load failed: " + e.Message); }
            return null;
        }
    }
}
