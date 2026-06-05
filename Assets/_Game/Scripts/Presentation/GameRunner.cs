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

            // route to the right starting phase based on persisted state
            if (_game.state.gender == 0)              _game.phase = AppPhase.GenderSelect;
            else if (_game.state.curse == 0)          _game.phase = AppPhase.CurseSelect;
            else if (!_game.state.firstMirrorDone)    _game.phase = AppPhase.FirstMirror;
            else                                       _game.phase = AppPhase.Home;

            var camGO = new GameObject("MainCamera") { tag = "MainCamera" };
            var cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.05f, 0.10f);

            _hud = new Hud(
                onSelectGender:      g => _game.SetGender((Gender)g),
                onSelectCurse:       c => _game.SetCurse((Curse)c),
                onFinishFirstMirror: () => { _game.DoRep(Exercise.Pushup); _game.FinishFirstMirror(); },
                onGoTraining:        () => _game.GoTraining(),
                onGoShop:            () => _game.GoShop(),
                onGoHome:            () => _game.GoHome(),
                onFakeRep:           () => _game.DoRep(Exercise.Pushup),
                onBuy:               id => _game.TryBuy(FindDef(id)),
                onToggleEquip:       id => _game.ToggleEquip(id));
        }

        void Update()
        {
            _hud?.Refresh(_game);
            // debug keys for fast iteration
            if (Input.GetKeyDown(KeyCode.E)) _game.EndDay();                  // advance one day
            if (Input.GetKeyDown(KeyCode.R) && _game.phase == AppPhase.Training) _game.DoRep(Exercise.Pushup); // alt fake rep
        }

        static EquipmentDef FindDef(string id)
        {
            foreach (var d in GamexGame.Catalog) if (d.id == id) return d;
            return null;
        }

        static void DisableDefaultSceneObjects()
        {
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))      c.gameObject.SetActive(false);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))       l.gameObject.SetActive(false);
            foreach (var a in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) a.enabled = false;
        }
    }
}
