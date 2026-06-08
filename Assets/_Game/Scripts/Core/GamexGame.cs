using System;
using System.Collections.Generic;

namespace Gamex.Core
{
    // Pure-C# game logic. No Unity types. Presentation layer reads state every frame
    // and routes player intents (button clicks / step ingestion) back through this class.
    public class GamexGame
    {
        public AppPhase phase = AppPhase.Boot;
        public GameState state = new GameState();
        public System.Action onSave;

        // ---- step-based mechanic (M5a) ----
        // Linear XP: every 5000 effective steps = 1 level. Running counts 2x via
        // EffectiveSteps = totalSteps + totalRunSteps (running steps are double-counted).
        // No level cap. Visual body stages still split into the same 6 buckets, but
        // anything beyond Stage 3 (Lv 20) is the chosen race form — no further auto
        // body change.
        public const int STEPS_PER_LEVEL = 5000;

        // Daily quest thresholds + reward
        const int Q_WALK_1000     = 1000;
        const int Q_WALK_5000     = 5000;
        const int Q_WALK_10000    = 10000;
        const int Q_RUN_15_MIN_S  = 15 * 60;
        const int Q_RUN_30_MIN_S  = 30 * 60;
        // Per-quest rewards tuned 1/3/5/3/5 per Jackson's call after the
        // first pass — softens the gap so "show up + walk 1k" still feels
        // worthwhile while the 5/5 bonus rewards the full-clear day.
        public const int Q_REWARD_WALK_1000  = 1;
        public const int Q_REWARD_WALK_5000  = 3;
        public const int Q_REWARD_WALK_10000 = 5;
        public const int Q_REWARD_RUN_15     = 3;
        public const int Q_REWARD_RUN_30     = 5;
        public const int Q_REWARD_ALL_BONUS  = 10;
        public const int Q_REWARD_DAILY_MAX  = Q_REWARD_WALK_1000 + Q_REWARD_WALK_5000 + Q_REWARD_WALK_10000
                                             + Q_REWARD_RUN_15  + Q_REWARD_RUN_30 + Q_REWARD_ALL_BONUS;  // 27
        const int STREAK_ACTIVE_THRESHOLD = 500;   // 500 steps = "active day" for streak
        const int STREAK_WEEKLY_BONUS     = 5;     // every 7 streak days

        public long EffectiveSteps => state.totalSteps + state.totalRunSteps;

        // Stage transitions land EXACTLY on Lv 10 / 15 / 20 per Jackson —
        // the previous "(level - 1) / 5" gave the same brackets but offset
        // by one level (changes happened at Lv 6 / 11 / 16, which felt
        // mistuned to him). Caps at 3 = race-ready.
        //   Lv 1-9   stage 0
        //   Lv 10-14 stage 1
        //   Lv 15-19 stage 2
        //   Lv 20+   stage 3
        public int Stage => Math.Max(0, Math.Min(state.level / 5 - 1, 3));
        public int XpInCurrentLevel => (int)(EffectiveSteps % STEPS_PER_LEVEL);
        public int XpToNextLevel    => STEPS_PER_LEVEL;

        // ---- phase navigation ----

        // Each tap on an opening text screen advances to the next narrative beat.
        public void TapAdvanceOpening()
        {
            switch (phase)
            {
                case AppPhase.OpeningIntro:      phase = AppPhase.OpeningHeroShown;  break;
                case AppPhase.OpeningHeroShown:  phase = AppPhase.OpeningCurseLooms; break;
                // Gluttony retired (Jackson) — only Weakness exists, no curse
                // select. Auto-set the curse + go straight into the cinematic.
                case AppPhase.OpeningCurseLooms: state.curse = (int)Curse.Weakness; phase = AppPhase.CurseAnim; break;
                case AppPhase.OpeningAmnesia:    phase = AppPhase.FirstMirror;       break;
            }
            onSave?.Invoke();
        }

        public void SetCurse(Curse c)
        {
            state.curse = (int)c;
            phase = AppPhase.CurseAnim;
            onSave?.Invoke();
        }

        public void CompleteCurseAnim()
        {
            if (phase != AppPhase.CurseAnim) return;
            phase = AppPhase.OpeningAmnesia;
            onSave?.Invoke();
        }

        public void SetGender(Gender g)
        {
            state.gender = (int)g;
            onSave?.Invoke();
        }

        public void SetRaceAndGender(Race r, Gender g)
        {
            if (phase != AppPhase.RaceSelect) return;
            state.race   = (int)r;
            state.gender = (int)g;
            phase = AppPhase.RaceTransformAnim;
            onSave?.Invoke();
        }

        public void CompleteRaceAnim()
        {
            if (phase != AppPhase.RaceTransformAnim) return;
            phase = AppPhase.Home;
            onSave?.Invoke();
        }

        public void FinishFirstMirror()
        {
            state.firstMirrorDone = true;
            phase = AppPhase.Home;
            onSave?.Invoke();
        }

        public void GoHome()     => phase = AppPhase.Home;
        public void GoQuests()   => phase = AppPhase.Quests;
        public void GoShop()     { phase = AppPhase.Shop;       activeSetId = null; onSave?.Invoke(); }
        public void GoInventory(){ phase = AppPhase.Inventory;  onSave?.Invoke(); }
        public void GoSetDetail(string setId) { activeSetId = setId; phase = AppPhase.SetDetail; onSave?.Invoke(); }
        // Currently-open set on the SetDetail screen. Cleared whenever we
        // navigate back to plain Shop or anywhere else.
        public string activeSetId;

        // ---- step ingestion (M5a) ----

        // Single entry point for any activity. On iOS this is fed from HealthKit;
        // in Editor from the debug "+1000 steps" key. All three counters increment
        // both today and the lifetime totals.
        //   newSteps      — total new steps this tick (includes any running steps)
        //   newRunSteps   — running steps within newSteps (counted again for the 2x XP)
        //   newRunSeconds — running session time within this tick
        public void AddActivity(int newSteps, int newRunSteps, int newRunSeconds)
        {
            if (newSteps    < 0) newSteps    = 0;
            if (newRunSteps < 0) newRunSteps = 0;
            if (newRunSeconds < 0) newRunSeconds = 0;
            if (newRunSteps > newSteps) newRunSteps = newSteps;

            state.todaySteps      += newSteps;
            state.todayRunSteps   += newRunSteps;
            state.todayRunSeconds += newRunSeconds;
            state.totalSteps      += newSteps;
            state.totalRunSteps   += newRunSteps;
            state.totalRunSeconds += newRunSeconds;

            // Recompute level from effective steps (steps + run-steps, with running
            // double-counted). Linear, no cap.
            int prevLevel = state.level;
            int newLevel  = 1 + (int)(EffectiveSteps / STEPS_PER_LEVEL);
            state.level   = newLevel;

            // Race transformation trigger — fires the first time level crosses 20.
            if (state.level >= 20 && state.race == 0 &&
                phase != AppPhase.RaceSelect && phase != AppPhase.RaceTransformAnim)
            {
                phase = AppPhase.RaceSelect;
            }

            CheckQuests();
            onSave?.Invoke();
        }

        // Legacy debug entry: each "rep" = 1 walking step.
        public void DoRep(Exercise e) => AddActivity(1, 0, 0);

        void CheckQuests()
        {
            // Each completion grants a per-quest reward and flips the flag so
            // the same quest can't be re-claimed today. EndDay resets the
            // flags. Clearing all 5 in a single day fires a one-off bonus.
            bool justClearedAll = false;
            void Try(Quest q, bool met, int reward)
            {
                int idx = (int)q;
                if (!state.questDone[idx] && met)
                {
                    state.questDone[idx] = true;
                    state.coins += reward;
                }
            }
            Try(Quest.Walk1000,  state.todaySteps      >= Q_WALK_1000,    Q_REWARD_WALK_1000);
            Try(Quest.Walk5000,  state.todaySteps      >= Q_WALK_5000,    Q_REWARD_WALK_5000);
            Try(Quest.Walk10000, state.todaySteps      >= Q_WALK_10000,   Q_REWARD_WALK_10000);
            Try(Quest.Run15Min,  state.todayRunSeconds >= Q_RUN_15_MIN_S, Q_REWARD_RUN_15);
            Try(Quest.Run30Min,  state.todayRunSeconds >= Q_RUN_30_MIN_S, Q_REWARD_RUN_30);
            // All-clear bonus — checked AFTER the per-quest grants so the
            // 5th completion in a single sequence also fires the bonus.
            // state.questAllBonusToday guards against double-payout if the
            // player crosses thresholds in multiple AddActivity calls.
            bool allDone = state.questDone[(int)Quest.Walk1000]
                        && state.questDone[(int)Quest.Walk5000]
                        && state.questDone[(int)Quest.Walk10000]
                        && state.questDone[(int)Quest.Run15Min]
                        && state.questDone[(int)Quest.Run30Min];
            if (allDone && !state.questAllBonusToday)
            {
                state.questAllBonusToday = true;
                state.coins += Q_REWARD_ALL_BONUS;
            }
        }

        // Day rollover: advance streak (if today was active), reset daily counters
        // and per-day quest flags. No decay — the step model removes the punitive
        // "lose a level if you miss" mechanic Jackson asked to drop.
        public void EndDay()
        {
            bool activeToday = state.todaySteps >= STREAK_ACTIVE_THRESHOLD;
            if (activeToday)
            {
                state.streakDays += 1;
                // Weekly streak bonus
                if (state.streakDays > 0 && state.streakDays % 7 == 0)
                    state.coins += STREAK_WEEKLY_BONUS;
            }
            else
            {
                state.streakDays = 0;
            }

            // Knight Set chain — Jackson pivoted to outfits, so the chain
            // now grants the WHOLE 6-piece silver knight set in one shot
            // after 10 consecutive 5k days (was 10 days per piece x 6 pieces
            // = 60). Chain unlocks at Lv 20, ticks while not yet earned.
            // knightChainStage acts as "earned" flag: 0 = not earned,
            // KnightSet.Length = earned (so the < check below is the gate).
            if (state.level >= KNIGHT_CHAIN_UNLOCK_LEVEL && state.knightChainStage < KnightSet.Length)
            {
                if (state.todaySteps >= KNIGHT_CHAIN_DAILY_STEPS)
                {
                    state.knightChainProgress += 1;
                    if (state.knightChainProgress >= KNIGHT_CHAIN_DAYS)
                    {
                        foreach (var p in KnightSet)
                            if (!state.owned.Contains(p.id)) state.owned.Add(p.id);
                        state.knightChainStage    = KnightSet.Length;   // mark earned
                        state.knightChainProgress = 0;
                    }
                }
                else
                {
                    state.knightChainProgress = 0;
                }
            }

            state.todaySteps      = 0;
            state.todayRunSteps   = 0;
            state.todayRunSeconds = 0;
            if (state.questDone == null || state.questDone.Length != (int)Quest.Count)
                state.questDone = new bool[(int)Quest.Count];
            else
                Array.Clear(state.questDone, 0, state.questDone.Length);
            state.questAllBonusToday = false;

            state.lastDayEnd = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            onSave?.Invoke();
        }

        // Crosses any LOCAL-CALENDAR midnights between lastDayEnd and now.
        // (Was 24-hour elapsed seconds — that mis-fires the common "play late at
        // night, reopen early morning of the next day" case where <24 h has
        // elapsed but a real day boundary has been crossed.)
        // Called from GameRunner.Awake() on every cold start.
        public void CatchUpDays()
        {
            DateTime todayLocal = DateTime.Now.Date;

            if (state.lastDayEnd == 0)
            {
                state.lastDayEnd = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return;
            }

            DateTime lastLocal = DateTimeOffset.FromUnixTimeSeconds(state.lastDayEnd).LocalDateTime.Date;
            int daysBetween = (todayLocal - lastLocal).Days;
            for (int i = 0; i < daysBetween; i++) EndDay();
        }

        // ---- shop ----

        // Phase 2 cleanup: dropped the 4 sword + 4 armor tier items. The
        // SPUM library is being added in Phase 3-5 as full sets sold whole
        // (with mix-and-match piece purchases inside each set), and Jackson
        // wants those to be the shop content rather than the old generic
        // "iron sword / silver armor" placeholders. Knight Set still comes
        // from the M5d chain quest, not the shop, so it stays out of Catalog.
        public static readonly EquipmentDef[] Catalog = new EquipmentDef[0];

        // Knight Set — earned through the M5d chain quest, not via the shop.
        // 6 pieces total: chest -> helmet -> leggings -> gauntlets -> boots -> sword.
        // At 10 chain days per piece (M5d) this is a 60-day commitment for the
        // full silver-knight loadout, with the sword as the climax reward.
        public static readonly EquipmentDef[] KnightSet = new[]
        {
            new EquipmentDef { id = "knight_chest",     name = "Knight Chestplate", slot = EquipSlot.Chest  },
            new EquipmentDef { id = "knight_helmet",    name = "Knight Helmet",     slot = EquipSlot.Head   },
            new EquipmentDef { id = "knight_leggings",  name = "Knight Leggings",   slot = EquipSlot.Legs   },
            new EquipmentDef { id = "knight_gauntlets", name = "Knight Gauntlets",  slot = EquipSlot.Wrists },
            new EquipmentDef { id = "knight_boots",     name = "Knight Boots",      slot = EquipSlot.Feet   },
            new EquipmentDef { id = "knight_sword",     name = "Knight Longsword",  slot = EquipSlot.Weapon },
        };

        // Synthetic SetDef wrapping the Knight chain pieces. Lives outside
        // SetCatalog on purpose — SetCatalog is what the Shop iterates, and
        // Knight Set is a chain-quest reward, not a sellable bundle. FindSet
        // / ApplyOutfit / IsOutfitActive consult it as a fallback so the
        // outfit-only Inventory grid can show a "Knight Set" cell + apply
        // all 6 pieces on tap, the same way it handles champion outfits.
        public static readonly SetDef KnightOutfit = new SetDef
        {
            id            = "knight_silver_set",
            displayName   = "Silver Knight Set",
            previewSprite = "Sets/knight_silver_set",
            source        = "quest",
            pieces        = KnightSet,
        };

        // M5g (Phase 4) — purchasable full-body skins. Each entry is a SPUM /
        // Luiz Melo / Cyberpunk-style packaged portrait the player can swap to
        // instead of their race form. Phase 4 ships SPUM Bundle placeholders;
        // Luiz Melo + Cyberpunk roll in as Jackson imports them.
        public static readonly SkinDef[] SkinCatalog = new[]
        {
            // Source = "spum_bundle" (Phase 4 placeholder). Sprite paths under
            // Resources/Skins/. Picks are from /tmp/spum_previews/ — full-gear
            // bakes of BasicPack prefabs so each "skin" looks like an actual
            // adventurer rather than a bare body.
            // Phase 5e1 — Luiz Melo's 10 character packs. Each is an animated
            // sprite strip; frameCount lets Hud cycle through Skins/<id>_NN.png
            // at 0.12s/frame for the idle bob. All 300g per Jackson's pricing.
            // Launch cohort: Iron Knight (Western male warrior), Lich Lord
            // (Western male dark wizard), Samurai (Eastern male). Other 7 are
            // gated behind availableAtUnix = Catalogs.FUTURE.
            new SkinDef { id = "lm_iron_knight",    displayName = "Iron Knight",     price = 300, source = "legend", frameCount = 11 },
            new SkinDef { id = "lm_lich_lord",      displayName = "Lich Lord",       price = 300, source = "legend", frameCount = 10 },
            new SkinDef { id = "lm_monk",           displayName = "Samurai",         price = 300, source = "legend", frameCount =  8 },  // renamed Monk -> Samurai for Eastern coding
            new SkinDef { id = "lm_crusader",       displayName = "Crusader",        price = 300, source = "legend", frameCount = 11, availableAtUnix = Catalogs.FUTURE },
            new SkinDef { id = "lm_necromancer",    displayName = "Necromancer",     price = 300, source = "legend", frameCount =  8, availableAtUnix = Catalogs.FUTURE },
            new SkinDef { id = "lm_dark_sage",      displayName = "Dark Sage",       price = 300, source = "legend", frameCount =  8, availableAtUnix = Catalogs.FUTURE },
            new SkinDef { id = "lm_sovereign_king", displayName = "Sovereign King",  price = 300, source = "legend", frameCount =  6, availableAtUnix = Catalogs.FUTURE },
            new SkinDef { id = "lm_royal_guard",    displayName = "Royal Guard",     price = 300, source = "legend", frameCount = 11, availableAtUnix = Catalogs.FUTURE },
            new SkinDef { id = "lm_knight_captain", displayName = "Knight Captain",  price = 300, source = "legend", frameCount =  8, availableAtUnix = Catalogs.FUTURE },
            new SkinDef { id = "lm_veteran_knight", displayName = "Veteran Knight",  price = 300, source = "legend", frameCount =  8, availableAtUnix = Catalogs.FUTURE },

            // Phase 5d (trimmed) — 5 distinct Cyberpunk archetypes. Jackson
            // pruned the original 18 down to a curated 1-per-style set so the
            // Cyberpunk shop section stays focused. 150g each per his pricing.
            // Launch cohort: Bounty Hunter (Western female), Stealth Operative
            // (androgynous cyborg). Soldier / Punk / Netrunner deferred.
            new SkinDef { id = "cyber_04", displayName = "Bounty Hunter",     price = 150, source = "cyberpunk" },                                       // blonde with twin pistols
            new SkinDef { id = "cyber_06", displayName = "Stealth Operative", price = 150, source = "cyberpunk" },                                       // dark armor, no hair
            new SkinDef { id = "cyber_01", displayName = "Cyber Soldier",     price = 150, source = "cyberpunk", availableAtUnix = Catalogs.FUTURE },    // blue-hair cyborg armor
            new SkinDef { id = "cyber_10", displayName = "Punk Hacker",       price = 150, source = "cyberpunk", availableAtUnix = Catalogs.FUTURE },    // purple mohawk + twin weapons
            new SkinDef { id = "cyber_13", displayName = "Netrunner",         price = 150, source = "cyberpunk", availableAtUnix = Catalogs.FUTURE },    // blonde in blue casual outfit

            // Phase 5e2 (trimmed per Jackson) — 3 representative pets: one
            // dog, one cat, one fantasy creature. The other 17 PNG sets stay
            // on disk for the future-content expansion he flagged. Launch
            // cohort: friendly Golden Retriever + Tabby Kitten only. Blue
            // Slime + the rest sit behind availableAtUnix.
            new SkinDef { id = "pet_dog_golden", displayName = "Golden Retriever", price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_cat_01",     displayName = "Tabby Kitten",     price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_slime",      displayName = "Blue Slime",       price = 200, source = "pet", frameCount = 14, availableAtUnix = Catalogs.FUTURE },
        };
        public static SkinDef FindSkin(string id)
        {
            if (id == null) return null;
            foreach (var s in SkinCatalog) if (s.id == id) return s;
            return null;
        }
        // Pet-source skins live in a parallel slot (state.activePet) — the
        // skin Apply / Buy / Active routing branches off source, so a player
        // can run a Cyber Operative skin + a Goblin pet at the same time.
        static bool IsPet(SkinDef s) => s != null && s.source == "pet";

        public bool TryBuySkin(SkinDef skin)
        {
            if (skin == null) return false;
            if (state.coins < skin.price) return false;
            var owned = IsPet(skin) ? state.ownedPets : state.ownedSkins;
            if (owned.Contains(skin.id)) return false;
            state.coins -= skin.price;
            owned.Add(skin.id);
            onSave?.Invoke();
            return true;
        }
        public void ApplySkin(string id)
        {
            var skin = FindSkin(id);
            if (skin == null) return;
            if (IsPet(skin))
            {
                if (!state.ownedPets.Contains(id)) return;
                state.activePet = id;
            }
            else
            {
                if (!state.ownedSkins.Contains(id)) return;
                state.activeSkin = id;
            }
            onSave?.Invoke();
        }
        public void RemoveActiveSkin()
        {
            if (string.IsNullOrEmpty(state.activeSkin)) return;
            state.activeSkin = null;
            onSave?.Invoke();
        }
        public void RemoveActivePet()
        {
            if (string.IsNullOrEmpty(state.activePet)) return;
            state.activePet = null;
            onSave?.Invoke();
        }
        public bool IsSkinOwned(string id)
        {
            var skin = FindSkin(id);
            if (skin == null) return false;
            return IsPet(skin) ? state.ownedPets.Contains(id) : state.ownedSkins.Contains(id);
        }
        public bool IsSkinActive(string id)
        {
            var skin = FindSkin(id);
            if (skin == null) return false;
            return IsPet(skin) ? state.activePet == id : state.activeSkin == id;
        }

        // M5g (Phase 3a) — SPUM full-prefab sets sold in the shop. Pieces are
        // individually buyable; bundling the whole set saves 20% (SetDef logic).
        // First set: elf_paladin (sourced from SPUM elf_07 prefab — blonde
        // sword-maiden with silver longsword + chest + leggings + boots). The
        // Sets/<id>_preview sprite is the prefab's full-gear render.
        // Phase 5b — build a 6-piece SPUM Champion set from a single per-piece
        // price. Helper keeps the catalog readable when adding 8+ sets at once;
        // every set uses the standard 6-slot layout (helmet/chest/leggings/
        // gauntlets/boots/weapon) and Phase 5 bake will produce transparent
        // overlays for any slot the source prefab doesn't actually have art
        // for — harmless on the avatar, just an empty piece in the inventory.
        static EquipmentDef[] ChampionPieces(string idPrefix, string namePrefix, int pricePerPiece)
        {
            return new[]
            {
                new EquipmentDef { id = idPrefix + "_helmet",    name = namePrefix + " Helmet",    slot = EquipSlot.Head,   price = pricePerPiece },
                new EquipmentDef { id = idPrefix + "_chest",     name = namePrefix + " Chest",     slot = EquipSlot.Chest,  price = pricePerPiece },
                new EquipmentDef { id = idPrefix + "_leggings",  name = namePrefix + " Leggings",  slot = EquipSlot.Legs,   price = pricePerPiece },
                new EquipmentDef { id = idPrefix + "_gauntlets", name = namePrefix + " Gauntlets", slot = EquipSlot.Wrists, price = pricePerPiece },
                new EquipmentDef { id = idPrefix + "_boots",     name = namePrefix + " Boots",     slot = EquipSlot.Feet,   price = pricePerPiece },
                new EquipmentDef { id = idPrefix + "_weapon",    name = namePrefix + " Weapon",    slot = EquipSlot.Weapon, price = pricePerPiece },
            };
        }

        // Launch cohort (polish round 7) — Jackson's curated first impression.
        // 11 cosmetic items chosen for diversity across gender, region, and
        // gameplay archetype. Everything else lives in the catalog but is
        // gated behind availableAtUnix = Catalogs.FUTURE so the shop UI
        // skips it until we flip the flag in a content drop.
        public static readonly SetDef[] SetCatalog = new[]
        {
            // ----- LAUNCH -----
            // Elite: 1 (Dark Horned Knight — visual hook for big spenders)
            new SetDef { id = "champ_dark_knight",     displayName = "Dark Horned Knight", previewSprite = "champ_dark_knight",     source = "champion", pieces = ChampionPieces("champ_dark_knight", "Dark Knight", 30) },
            // Veteran: 1 (Silver Hooded Knight — classic mid-tier paladin)
            new SetDef { id = "champ_silver_hood",     displayName = "Silver Hooded Knight", previewSprite = "champ_silver_hood",  source = "champion", pieces = ChampionPieces("champ_silver_hood",  "Silver Hooded",  15) },
            // Recruit: 2 (entry tier; Squire = male-coded, Pink Archer = female-coded)
            new SetDef { id = "champ_squire",          displayName = "Squire",            previewSprite = "champ_squire",      source = "champion", pieces = ChampionPieces("champ_squire",      "Squire", 5) },
            new SetDef { id = "champ_pink_archer",     displayName = "Pink Archer",       previewSprite = "champ_pink_archer", source = "champion", pieces = ChampionPieces("champ_pink_archer", "Pink Archer", 5) },

            // ----- DEFERRED (visible after each entry's availableAtUnix) -----
            new SetDef { id = "elf_paladin",           displayName = "Elven Paladin",     previewSprite = "elf_paladin",       source = "champion", availableAtUnix = Catalogs.FUTURE,
                pieces = new[]
                {
                    new EquipmentDef { id = "elfpaladin_sword",    name = "Paladin Sword",    slot = EquipSlot.Weapon, price = 200 },
                    new EquipmentDef { id = "elfpaladin_chest",    name = "Paladin Plate",    slot = EquipSlot.Chest,  price = 250 },
                    new EquipmentDef { id = "elfpaladin_leggings", name = "Paladin Greaves",  slot = EquipSlot.Legs,   price = 150 },
                    new EquipmentDef { id = "elfpaladin_boots",    name = "Paladin Boots",    slot = EquipSlot.Feet,   price = 100 },
                },
            },
            new SetDef { id = "champ_skull_warrior",   displayName = "Skull Warrior",      previewSprite = "champ_skull_warrior",   source = "champion", availableAtUnix = Catalogs.FUTURE, pieces = ChampionPieces("champ_skull_warrior",   "Skull Warrior",   30) },
            new SetDef { id = "champ_crimson_warrior", displayName = "Crimson Warrior",    previewSprite = "champ_crimson_warrior", source = "champion", availableAtUnix = Catalogs.FUTURE, pieces = ChampionPieces("champ_crimson_warrior", "Crimson Warrior", 30) },
            new SetDef { id = "champ_greatsword",      displayName = "Greatsword Knight",  previewSprite = "champ_greatsword",      source = "champion", availableAtUnix = Catalogs.FUTURE, pieces = ChampionPieces("champ_greatsword",      "Greatsword",      15) },
            new SetDef { id = "champ_caped_noble",     displayName = "Caped Noble",        previewSprite = "champ_caped_noble",     source = "champion", availableAtUnix = Catalogs.FUTURE, pieces = ChampionPieces("champ_caped_noble",     "Noble",           15) },
            new SetDef { id = "champ_blue_mage",       displayName = "Azure Mage",         previewSprite = "champ_blue_mage",       source = "champion", availableAtUnix = Catalogs.FUTURE, pieces = ChampionPieces("champ_blue_mage",       "Azure Mage",      15) },
            new SetDef { id = "champ_purple_axe",      displayName = "Violet Axemaster",   previewSprite = "champ_purple_axe",      source = "champion", availableAtUnix = Catalogs.FUTURE, pieces = ChampionPieces("champ_purple_axe",      "Violet Axe",      15) },
            new SetDef { id = "champ_cloak_sword",     displayName = "Cloaked Swordsman",  previewSprite = "champ_cloak_sword",     source = "champion", availableAtUnix = Catalogs.FUTURE, pieces = ChampionPieces("champ_cloak_sword",     "Cloak",            5) },
            new SetDef { id = "champ_mohawk",          displayName = "Mohawk Striker",     previewSprite = "champ_mohawk",          source = "champion", availableAtUnix = Catalogs.FUTURE, pieces = ChampionPieces("champ_mohawk",          "Mohawk",           5) },
            new SetDef { id = "champ_apprentice",      displayName = "Apprentice",         previewSprite = "champ_apprentice",      source = "champion", availableAtUnix = Catalogs.FUTURE, pieces = ChampionPieces("champ_apprentice",      "Apprentice",       5) },
        };
        public const int KNIGHT_CHAIN_DAILY_STEPS = 5000;
        public const int KNIGHT_CHAIN_DAYS        = 10;
        public const int KNIGHT_CHAIN_UNLOCK_LEVEL = 20;

        public bool TryBuy(EquipmentDef def)
        {
            if (def == null) return false;
            if (state.coins < def.price) return false;
            if (state.owned.Contains(def.id)) return false;
            state.coins -= def.price;
            state.owned.Add(def.id);
            onSave?.Invoke();
            return true;
        }

        // Atomic set purchase — pays the discounted bundle price and grants
        // every piece + auto-equips the set so the player sees their new
        // outfit immediately (per Jackson's "Champions sold as outfits" pivot).
        // Activating clears state.activeSkin so the equipment overlay path
        // renders the set's pieces on top of the race-form portrait.
        public bool TryBuySet(SetDef set)
        {
            if (set == null || set.pieces == null) return false;
            int price = set.BundlePrice;
            if (state.coins < price) return false;
            bool anyMissing = false;
            foreach (var p in set.pieces)
                if (!state.owned.Contains(p.id)) { anyMissing = true; break; }
            if (!anyMissing) return false;
            state.coins -= price;
            foreach (var p in set.pieces)
                if (!state.owned.Contains(p.id)) state.owned.Add(p.id);
            ApplyOutfit(set.id);
            return true;
        }

        // Single-call "wear this outfit" used by the Inventory grid + the
        // post-purchase auto-equip. Champion sets fill state.equipped with
        // their 6 pieces; skins flip state.activeSkin and clear equipment so
        // overlays don't double-render. Pets stay separate (state.activePet).
        public void ApplyOutfit(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var set = FindSet(id);
            if (set != null)
            {
                state.equipped.Clear();
                foreach (var p in set.pieces) state.equipped.Add(p.id);
                state.activeSkin = null;
                onSave?.Invoke();
                return;
            }
            var skin = FindSkin(id);
            if (skin != null && !IsPet(skin))
            {
                if (!state.ownedSkins.Contains(id)) return;
                state.activeSkin = id;
                state.equipped.Clear();
                onSave?.Invoke();
            }
        }

        // Active means: this set is the one whose pieces currently fill
        // state.equipped AND no skin is overriding the portrait. Used by the
        // Inventory cell's "Active" badge so the player can see at a glance
        // which outfit is on.
        public bool IsOutfitActive(string setId)
        {
            if (!string.IsNullOrEmpty(state.activeSkin)) return false;
            var set = FindSet(setId);
            if (set == null) return false;
            if (state.equipped.Count != set.pieces.Length) return false;
            foreach (var p in set.pieces)
                if (!state.equipped.Contains(p.id)) return false;
            return true;
        }

        // Equipment slots — the Inventory paper-doll has exactly one cell per slot,
        // so equipping a new item replaces whatever else is in that slot. Catalog
        // IDs follow naming conventions so this routing can be done by string
        // prefix; the Knight Set is hand-mapped because it doesn't share prefixes.
        public enum EquipSlot { None, Weapon, Chest, Head, Legs, Wrists, Feet }
        public static readonly EquipSlot[] AllSlots = {
            EquipSlot.Head, EquipSlot.Chest, EquipSlot.Wrists,
            EquipSlot.Weapon, EquipSlot.Legs, EquipSlot.Feet,
        };
        // Slot lookup is dictionary-driven now. KnightSet + every SetCatalog
        // piece declares its slot explicitly; SlotOf just reads the map.
        // Building lazily on first access keeps the static constructor simple.
        static System.Collections.Generic.Dictionary<string, EquipSlot> _slotMap;
        static void BuildSlotMap()
        {
            _slotMap = new System.Collections.Generic.Dictionary<string, EquipSlot>();
            foreach (var p in KnightSet) _slotMap[p.id] = p.slot;
            foreach (var set in SetCatalog)
                foreach (var p in set.pieces) _slotMap[p.id] = p.slot;
        }
        public static EquipSlot SlotOf(string id)
        {
            if (id == null) return EquipSlot.None;
            if (_slotMap == null) BuildSlotMap();
            return _slotMap.TryGetValue(id, out var s) ? s : EquipSlot.None;
        }
        // Catalog walkers — used by Hud's Inventory grid + shop screens.
        public static EquipmentDef FindPiece(string id)
        {
            foreach (var p in KnightSet) if (p.id == id) return p;
            foreach (var set in SetCatalog)
                foreach (var p in set.pieces) if (p.id == id) return p;
            return null;
        }
        public static SetDef FindSet(string setId)
        {
            foreach (var set in SetCatalog) if (set.id == setId) return set;
            if (KnightOutfit.id == setId) return KnightOutfit;
            return null;
        }
        public string EquippedInSlot(EquipSlot slot)
        {
            for (int i = state.equipped.Count - 1; i >= 0; i--)
                if (SlotOf(state.equipped[i]) == slot) return state.equipped[i];
            return null;
        }

        // Equipping a new item evicts the previous occupant of the same slot so
        // the paper-doll never shows two of the same kind. Unequip is just remove.
        public void EquipItem(string id)
        {
            if (!state.owned.Contains(id)) return;
            var slot = SlotOf(id);
            if (slot == EquipSlot.None) return;
            state.equipped.RemoveAll(e => SlotOf(e) == slot);
            state.equipped.Add(id);
            onSave?.Invoke();
        }
        public void Unequip(string id)
        {
            if (state.equipped.Remove(id)) onSave?.Invoke();
        }
        // Kept for back-compat (shop list still toggles). Routes through the
        // slot-aware Equip path so multi-equip can't happen via shop either.
        public void ToggleEquip(string id)
        {
            if (state.equipped.Contains(id)) Unequip(id);
            else                              EquipItem(id);
        }

        public bool IsOwned(string id)    => state.owned.Contains(id);
        public bool IsEquipped(string id) => state.equipped.Contains(id);
    }
}
