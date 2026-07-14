using UnityEngine;

namespace Gamex.Core
{
    // Save-file schema migration ladder.
    //
    // SaveSystem.Load runs each loaded GameState through Migrations.Apply so
    // old saves keep working when fields are renamed, types change, or new
    // computed defaults need backfilling. SaveSystem.Save stamps the current
    // version onto every write so newly-saved files never trigger migration
    // on the next load.
    //
    // **Adding a new schema version**:
    //   1. Make the data change in Model.cs (rename field, change type, drop
    //      a field, etc.).
    //   2. Add a new `if (s.schemaVersion < N) { ... transform ... ;
    //      s.schemaVersion = N; }` block here in version order.
    //   3. Bump CurrentVersion to N.
    //   4. Add a Check in SmokeTest.LogicTest.Run that constructs a save at
    //      version N-1 and asserts Apply yields the expected v_N output.
    //
    // The migrations form a strict ladder — each step runs in version order
    // and bumps schemaVersion exactly once, so a save many versions behind
    // walks through every intermediate transform. NEVER skip a version in
    // the ladder; old saves still in the wild depend on every step running.
    public static class Migrations
    {
        // Bump every time a new migration block is added below.
        public const int CurrentVersion = 3;

        public static void Apply(GameState s)
        {
            if (s == null) return;

            // v0 -> v1: baseline. Saves written before schemaVersion existed
            // deserialise with the field at 0 (JsonUtility default). No data
            // change is needed — we just stamp the version so subsequent
            // saves write CurrentVersion and skip this branch.
            if (s.schemaVersion < 1)
            {
                s.schemaVersion = 1;
            }

            // v1 -> v2: Casino scaffold. Guarantees the nested CasinoState
            // exists on saves written before the field was added. All-zero
            // fields are a valid "never entered the casino" state, so no
            // value backfill is needed. (Historical note: v2 originally
            // added this as `fate`/FateState; renamed wholesale to casino
            // in Phase R before the feature ever shipped, so no production
            // save carries the old key.)
            if (s.schemaVersion < 2)
            {
                if (s.casino == null) s.casino = new CasinoState();
                s.schemaVersion = 2;
            }

            // v2 -> v3: The Desk (Pivot 3). Guarantees the nested DeskState
            // exists; all-zero fields are a valid "never sat at the desk"
            // state. (Also heals array sizes if a future catalog outgrows
            // the serialized arrays.)
            if (s.schemaVersion < 3)
            {
                if (s.desk == null) s.desk = new DeskState();
                s.schemaVersion = 3;
            }

            // Future migrations go here, e.g.:
            //   if (s.schemaVersion < 4) { ...split foo into foo_a + foo_b...; s.schemaVersion = 4; }
            //   if (s.schemaVersion < 5) { ...backfill new default...;          s.schemaVersion = 5; }

            // Forward-compat guard: a save from a newer build than the one
            // running (downgrade scenario) — we can't reverse-migrate, so
            // just preserve the bytes and let JsonUtility silently drop the
            // unknown fields. Log so a developer running an older build
            // notices the mismatch.
            if (s.schemaVersion > CurrentVersion)
            {
                Debug.LogWarning($"[Migrations] save schemaVersion {s.schemaVersion} is newer than known {CurrentVersion}; preserving fields the running build understands.");
            }
        }
    }
}
