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
        const int QUEST_COIN_REWARD = 1;
        const int STREAK_ACTIVE_THRESHOLD = 500;   // 500 steps = "active day" for streak
        const int STREAK_WEEKLY_BONUS     = 5;     // every 7 streak days

        public long EffectiveSteps => state.totalSteps + state.totalRunSteps;

        // Stage caps at 3 (skeleton end / pre-race). After race choice the visual
        // is race form regardless — Make.Portrait ignores stage when race != Unset.
        public int Stage => Math.Min((state.level - 1) / 5, 3);
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
                case AppPhase.OpeningCurseLooms: phase = AppPhase.CurseSelect;       break;
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
            // Each completion grants QUEST_COIN_REWARD coins and flips the flag so
            // the same quest can't be re-claimed today. EndDay resets the flags.
            void Try(Quest q, bool met)
            {
                int idx = (int)q;
                if (!state.questDone[idx] && met)
                {
                    state.questDone[idx] = true;
                    state.coins += QUEST_COIN_REWARD;
                }
            }
            Try(Quest.Walk1000,  state.todaySteps      >= Q_WALK_1000);
            Try(Quest.Walk5000,  state.todaySteps      >= Q_WALK_5000);
            Try(Quest.Walk10000, state.todaySteps      >= Q_WALK_10000);
            Try(Quest.Run15Min,  state.todayRunSeconds >= Q_RUN_15_MIN_S);
            Try(Quest.Run30Min,  state.todayRunSeconds >= Q_RUN_30_MIN_S);
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

            // Knight Set chain — only ticks once Lv 20 is reached and chain not complete.
            // Hit 5000 today -> +1 progress day; miss -> reset to 0. At 10 days the
            // current piece is granted and the next slot opens.
            if (state.level >= KNIGHT_CHAIN_UNLOCK_LEVEL && state.knightChainStage < KnightSet.Length)
            {
                if (state.todaySteps >= KNIGHT_CHAIN_DAILY_STEPS)
                {
                    state.knightChainProgress += 1;
                    if (state.knightChainProgress >= KNIGHT_CHAIN_DAYS)
                    {
                        string pieceId = KnightSet[state.knightChainStage].id;
                        if (!state.owned.Contains(pieceId)) state.owned.Add(pieceId);
                        state.knightChainStage    += 1;
                        state.knightChainProgress  = 0;
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
            new SkinDef { id = "lm_iron_knight",    displayName = "Iron Knight",     price = 300, source = "legend", frameCount = 11 },
            new SkinDef { id = "lm_crusader",       displayName = "Crusader",        price = 300, source = "legend", frameCount = 11 },
            new SkinDef { id = "lm_necromancer",    displayName = "Necromancer",     price = 300, source = "legend", frameCount =  8 },
            new SkinDef { id = "lm_dark_sage",      displayName = "Dark Sage",       price = 300, source = "legend", frameCount =  8 },
            new SkinDef { id = "lm_lich_lord",      displayName = "Lich Lord",       price = 300, source = "legend", frameCount = 10 },
            new SkinDef { id = "lm_monk",           displayName = "Monk",            price = 300, source = "legend", frameCount =  8 },
            new SkinDef { id = "lm_sovereign_king", displayName = "Sovereign King",  price = 300, source = "legend", frameCount =  6 },
            new SkinDef { id = "lm_royal_guard",    displayName = "Royal Guard",     price = 300, source = "legend", frameCount = 11 },
            new SkinDef { id = "lm_knight_captain", displayName = "Knight Captain",  price = 300, source = "legend", frameCount =  8 },
            new SkinDef { id = "lm_veteran_knight", displayName = "Veteran Knight",  price = 300, source = "legend", frameCount =  8 },

            // Phase 5d — 18 Cyberpunk full-body skins. All 150g per Jackson's
            // pricing ("Cyberpunk 的因为也是一整套但是没有动画所以150金币一套").
            // Names are intentionally generic — Bartek's pack ships them as
            // "character_N" without lore, so the player gets a numbered roster.
            new SkinDef { id = "cyber_01", displayName = "Cyber Operative 01", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_02", displayName = "Cyber Operative 02", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_03", displayName = "Cyber Operative 03", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_04", displayName = "Cyber Operative 04", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_05", displayName = "Cyber Operative 05", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_06", displayName = "Cyber Operative 06", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_07", displayName = "Cyber Operative 07", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_08", displayName = "Cyber Operative 08", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_09", displayName = "Cyber Operative 09", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_10", displayName = "Cyber Operative 10", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_11", displayName = "Cyber Operative 11", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_12", displayName = "Cyber Operative 12", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_13", displayName = "Cyber Operative 13", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_14", displayName = "Cyber Operative 14", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_15", displayName = "Cyber Operative 15", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_16", displayName = "Cyber Operative 16", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_17", displayName = "Cyber Operative 17", price = 150, source = "cyberpunk" },
            new SkinDef { id = "cyber_18", displayName = "Cyber Operative 18", price = 150, source = "cyberpunk" },

            // Phase 5e2 — Luiz Melo pet packs, 200g each per Jackson's pricing.
            // 6 cats + 6 dogs + 4 fantasy monsters + 4 fantasy 2 monsters = 20.
            // Pet skins flip a separate state slot (state.activePet) so they
            // can run alongside a character skin without overriding it.
            new SkinDef { id = "pet_cat_01", displayName = "Tabby Kitten",       price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_cat_02", displayName = "Calico Kitten",      price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_cat_03", displayName = "Black Cat",          price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_cat_04", displayName = "Tuxedo Cat",         price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_cat_05", displayName = "Tortoiseshell",      price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_cat_06", displayName = "Persian",            price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_dog_golden",    displayName = "Golden Retriever", price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_dog_akita",     displayName = "Akita",            price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_dog_dane",      displayName = "Great Dane",       price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_dog_schnauzer", displayName = "Schnauzer",        price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_dog_bernard",   displayName = "Saint Bernard",    price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_dog_husky",     displayName = "Siberian Husky",   price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_eye",      displayName = "Flying Eye",   price = 200, source = "pet", frameCount =  8 },
            new SkinDef { id = "pet_goblin",   displayName = "Goblin Sprite",price = 200, source = "pet", frameCount =  4 },
            new SkinDef { id = "pet_mushroom", displayName = "Mushroom",     price = 200, source = "pet", frameCount =  4 },
            new SkinDef { id = "pet_skeleton", displayName = "Skeleton Pup", price = 200, source = "pet", frameCount =  4 },
            new SkinDef { id = "pet_bat",      displayName = "Vampire Bat",  price = 200, source = "pet", frameCount = 11 },
            new SkinDef { id = "pet_mimic",    displayName = "Mimic Chest",  price = 200, source = "pet", frameCount =  9 },
            new SkinDef { id = "pet_rat",      displayName = "Pet Rat",      price = 200, source = "pet", frameCount = 10 },
            new SkinDef { id = "pet_slime",    displayName = "Blue Slime",   price = 200, source = "pet", frameCount = 14 },
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

        public static readonly SetDef[] SetCatalog = new[]
        {
            new SetDef
            {
                id = "elf_paladin",
                displayName = "Elven Paladin",
                previewSprite = "elf_paladin",
                source = "champion",
                pieces = new[]
                {
                    new EquipmentDef { id = "elfpaladin_sword",    name = "Paladin Sword",    slot = EquipSlot.Weapon, price = 200 },
                    new EquipmentDef { id = "elfpaladin_chest",    name = "Paladin Plate",    slot = EquipSlot.Chest,  price = 250 },
                    new EquipmentDef { id = "elfpaladin_leggings", name = "Paladin Greaves",  slot = EquipSlot.Legs,   price = 150 },
                    new EquipmentDef { id = "elfpaladin_boots",    name = "Paladin Boots",    slot = EquipSlot.Feet,   price = 100 },
                },
            },

            // ---- Elite — Jackson's "最帅" tier (30g/piece, bundle ≈ 144g) ----
            new SetDef { id = "champ_dark_knight",     displayName = "Dark Horned Knight", previewSprite = "champ_dark_knight",     source = "champion", pieces = ChampionPieces("champ_dark_knight", "Dark Knight", 30) },
            new SetDef { id = "champ_skull_warrior",   displayName = "Skull Warrior",      previewSprite = "champ_skull_warrior",   source = "champion", pieces = ChampionPieces("champ_skull_warrior", "Skull Warrior", 30) },
            new SetDef { id = "champ_crimson_warrior", displayName = "Crimson Warrior",    previewSprite = "champ_crimson_warrior", source = "champion", pieces = ChampionPieces("champ_crimson_warrior", "Crimson Warrior", 30) },

            // ---- Veteran — "中等帅" tier (15g/piece, bundle ≈ 72g) ----
            new SetDef { id = "champ_silver_hood",  displayName = "Silver Hooded Knight", previewSprite = "champ_silver_hood",  source = "champion", pieces = ChampionPieces("champ_silver_hood",  "Silver Hooded",  15) },
            new SetDef { id = "champ_greatsword",   displayName = "Greatsword Knight",    previewSprite = "champ_greatsword",   source = "champion", pieces = ChampionPieces("champ_greatsword",   "Greatsword",     15) },
            new SetDef { id = "champ_caped_noble",  displayName = "Caped Noble",          previewSprite = "champ_caped_noble",  source = "champion", pieces = ChampionPieces("champ_caped_noble",  "Noble",          15) },
            new SetDef { id = "champ_blue_mage",    displayName = "Azure Mage",           previewSprite = "champ_blue_mage",    source = "champion", pieces = ChampionPieces("champ_blue_mage",    "Azure Mage",     15) },
            new SetDef { id = "champ_purple_axe",   displayName = "Violet Axemaster",     previewSprite = "champ_purple_axe",   source = "champion", pieces = ChampionPieces("champ_purple_axe",   "Violet Axe",     15) },
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
        // every piece the player doesn't already own. Pieces already in
        // state.owned are NOT discounted-out of the price (the set price is
        // a fixed bundle); we simply skip re-adding them.
        public bool TryBuySet(SetDef set)
        {
            if (set == null || set.pieces == null) return false;
            int price = set.BundlePrice;
            if (state.coins < price) return false;
            // Don't sell a set the player has already fully completed.
            bool anyMissing = false;
            foreach (var p in set.pieces)
                if (!state.owned.Contains(p.id)) { anyMissing = true; break; }
            if (!anyMissing) return false;
            state.coins -= price;
            foreach (var p in set.pieces)
                if (!state.owned.Contains(p.id)) state.owned.Add(p.id);
            onSave?.Invoke();
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
