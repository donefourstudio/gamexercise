using UnityEngine;
using Gamex.Core;

namespace Gamex.Game
{
    // Bootstraps the whole app from a runtime hook so pressing Play in any scene
    // just works. Same pattern as RouJiaMoKing.
    public class GameRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<GameRunner>() != null) return;
            new GameObject("Gamex_Root").AddComponent<GameRunner>();
        }

        public GamexGame Game => _game;
        public Hud Hud => _hud;

        GamexGame _game;
        Hud _hud;

        void Awake()
        {
            DisableDefaultSceneObjects();
            // lock to portrait — this app is one-handed phone/iPad only
            Screen.orientation = ScreenOrientation.Portrait;

            _game = new GamexGame();
            var loaded = SaveSystem.Load();
            if (loaded != null) _game.state = loaded;
            _game.onSave = () => SaveSystem.Save(_game.state);
            _game.CatchUpDays();

            // Route based on persisted state. Opening (Intro -> HeroShown -> CurseLooms ->
            // CurseSelect -> CurseAnim -> Amnesia -> FirstMirror) runs once for new players.
            // Gender + race chosen together at the Lv 20 RaceSelect, so we only need to
            // detect "post-opening but pre-race" as a resume case.
            if (_game.state.firstMirrorDone)
            {
                if (_game.state.race == 0 && _game.state.level >= 20)
                    _game.phase = AppPhase.RaceSelect;
                else
                    _game.phase = AppPhase.Home;
            }
            else if (_game.state.curse != 0) _game.phase = AppPhase.OpeningAmnesia;
            else                              _game.phase = AppPhase.OpeningIntro;

            var camGO = new GameObject("MainCamera") { tag = "MainCamera" };
            var cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.05f, 0.10f);

            _hud = new Hud(
                onTapAdvanceOpening:  () => _game.TapAdvanceOpening(),
                onCurseAnimDone:      () => _game.CompleteCurseAnim(),
                onSelectCurse:        c => _game.SetCurse((Curse)c),
                onSelectRaceAndGender:(r, g) => _game.SetRaceAndGender((Race)r, (Gender)g),
                onRaceAnimDone:       () => _game.CompleteRaceAnim(),
                onFinishFirstMirror:  () => { _game.DoRep(Exercise.Pushup); _game.FinishFirstMirror(); },
                onGoQuests:           () => _game.GoQuests(),
                onGoShop:             () => _game.GoShop(),
                onGoHome:             () => _game.GoHome(),
                onGoInventory:        () => _game.GoInventory(),
                onFakeRep:            () => _game.DoRep(Exercise.Pushup),
                onBuy:                id => _game.TryBuy(GamexGame.FindPiece(id)),
                onToggleEquip:        id => _game.ToggleEquip(id),
                onGoSetDetail:        id => _game.GoSetDetail(id),
                onBuySet:             id => _game.TryBuySet(GamexGame.FindSet(id)),
                onSkinAction:         id =>
                {
                    var skin = GamexGame.FindSkin(id);
                    if (skin == null) return;
                    if      (!_game.IsSkinOwned(id))  _game.TryBuySkin(skin);
                    else if (!_game.IsSkinActive(id)) _game.ApplySkin(id);
                    else if (skin.source == "pet")    _game.RemoveActivePet();
                    else                              _game.RemoveActiveSkin();
                },
                onApplyOutfit:       id =>
                {
                    // Inventory taps. Owned-set + already-active set -> race
                    // form (un-equip everything); otherwise just apply.
                    if (GamexGame.FindSet(id) != null && _game.IsOutfitActive(id))
                    {
                        _game.state.equipped.Clear();
                        return;
                    }
                    if (GamexGame.FindSkin(id) != null && _game.IsSkinActive(id))
                    {
                        _game.RemoveActiveSkin();
                        return;
                    }
                    _game.ApplyOutfit(id);
                });
        }

        void Update()
        {
            _hud?.Refresh(_game);
            // debug keys for fast iteration
            if (Input.GetKeyDown(KeyCode.E)) _game.EndDay();                  // advance one day
            if (Input.GetKeyDown(KeyCode.R) &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                // Jackson asked: R for fast shop testing should drop a usable
                // chunk of gold instead of nudging step count by 1k. 10000
                // gold per press buys a Legend + a few Champion sets so the
                // shop / inventory flows are easy to exercise.
                _game.state.coins += 10000;
            }
            // T = +1000 steps. Brought back as a separate key after the R
            // remap took step injection away from shop testing — Jackson
            // still needs both during a single play session.
            if (Input.GetKeyDown(KeyCode.T)) _game.AddActivity(1000, 0, 0);
            // Shift+R from anywhere: nuke save + replay opening (dev / QA only).
            if (Input.GetKeyDown(KeyCode.R) &&
                (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                SaveSystem.Wipe();
                _game.state = new GameState();
                _game.phase = AppPhase.OpeningIntro;
                Debug.Log("[Gamex] save wiped, replaying opening");
            }
        }

        static void DisableDefaultSceneObjects()
        {
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))      c.gameObject.SetActive(false);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))       l.gameObject.SetActive(false);
            foreach (var a in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) a.enabled = false;
        }
    }
}
