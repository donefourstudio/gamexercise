using System;
using System.Collections.Generic;

namespace Gamex.Core
{
    public enum AppPhase
    {
        Boot,
        // Opening sequence (first run only). Order:
        // OpeningIntro       — black, "你曾是这个时代最强的勇士……"   (tap)
        // OpeningHeroShown   — hero standing, bright                  (tap)
        // OpeningCurseLooms  — black, "……直到诅咒降临。"             (tap)
        // CurseSelect        — pick weakness / gluttony
        // CurseAnim          — shake + dim + sprite swap, auto-advance
        // OpeningAmnesia     — black, "你忘了自己是谁。"             (tap)
        // GenderSelect       — pick male / female
        // FirstMirror        — "「……这就是现在的我。」" + first rep
        OpeningIntro,
        OpeningHeroShown,
        OpeningCurseLooms,
        CurseSelect,
        CurseAnim,
        OpeningAmnesia,
        GenderSelect,
        FirstMirror,
        Home,
        Training,
        Shop,
    }

    public enum Gender { Unset = 0, Male = 1, Female = 2 }
    public enum Curse  { Unset = 0, Weakness = 1, Gluttony = 2 }
    public enum Exercise { Pushup, Situp, Squat }

    [Serializable]
    public class EquipmentDef
    {
        public string id;
        public string name;
        public int tier;       // 1-4 visual tier
        public int minLevel;   // can't buy below this
        public int price;
    }

    // Player progression — all of this serializes to disk via JsonUtility.
    [Serializable]
    public class GameState
    {
        public int gender;             // 0=unset, 1=male, 2=female
        public int curse;              // 0=unset, 1=weakness, 2=gluttony
        public int level = 1;          // 1..30
        public int xp;                 // toward next level, resets on level-up
        public long coins;
        public int repsToday;          // any of the 3 exercises, resets at day end
        public int streakDays;
        public int missedDays;         // consecutive days under maintenance
        public long lastDayEnd;        // unix seconds of last day-end rollover
        public List<string> owned = new();
        public List<string> equipped = new();
        public bool firstMirrorDone;
    }
}
