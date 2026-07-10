using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.TextCore.LowLevel;
using Gamex.Core;
using Gamex.Platform;

namespace Gamex.Game
{
    public partial class Hud
    {
        // ============================================================
        // Refresh
        // ============================================================
        public void Refresh(GamexGame g)
        {
            // Phase entry — reset per-phase animation state.
            if (g.phase != _lastPhase)
            {
                if (g.phase == AppPhase.CurseAnim)         _curseAnimT = 0f;
                if (g.phase == AppPhase.RaceTransformAnim) _raceAnimT  = 0f;
                if (g.phase == AppPhase.Title)
                {
                    _titleT       = 0f;
                    _titleExiting = false;
                    _titleExitT   = 0f;
                    if (_titleCanvasGroup != null)  _titleCanvasGroup.alpha = 1f;
                    if (_titleStartButton != null)  _titleStartButton.interactable = true;
                }
                if (g.phase == AppPhase.CasinoScratch)  OnEnterCasinoScratch();
                if (g.phase == AppPhase.CasinoPrestige) OnEnterCasinoPrestige();
                if (g.phase == AppPhase.CasinoFindCash) OnEnterCasinoFindCash();
                if (g.phase == AppPhase.CasinoGoldRush) OnEnterCasinoGoldRush();
                if (g.phase == AppPhase.CasinoMega)     OnEnterCasinoMega();
                _lastPhase = g.phase;
            }

            // Background music — calm drone on every screen the player lingers on.
            // Title + Settings included so the loop covers the first impression
            // and audio-toggle screen. Cinematics (Opening*, CurseAnim,
            // RaceSelect, RaceTransformAnim, FirstMirror) stay quiet so the
            // SFX hits and narrative beats land hard. PlayLoop is idempotent
            // (same-clip re-call is a no-op) so calling every Refresh is cheap.
            bool bgmOn = g.phase == AppPhase.Title
                      || g.phase == AppPhase.Home
                      || g.phase == AppPhase.Quests
                      || g.phase == AppPhase.Shop
                      || g.phase == AppPhase.SetDetail
                      || g.phase == AppPhase.SkinDetail
                      || g.phase == AppPhase.Inventory
                      || g.phase == AppPhase.Settings
                      || g.phase == AppPhase.CasinoLobby
                      || g.phase == AppPhase.CasinoScratch
                      || g.phase == AppPhase.CasinoUpgrades
                      || g.phase == AppPhase.CasinoPrestige
                      || g.phase == AppPhase.CasinoSlots
                      || g.phase == AppPhase.CasinoFindCash
                      || g.phase == AppPhase.CasinoGoldRush
                      || g.phase == AppPhase.CasinoLadder
                      || g.phase == AppPhase.CasinoHighStakes
                      || g.phase == AppPhase.CasinoMega;
            if (bgmOn) Bgm.PlayLoop("bgm_home");
            else       Bgm.Stop();

            // First-run tutorial. Process the pending-finish flag BEFORE the
            // trigger check — otherwise the trigger fires again on the same
            // Refresh that should be tearing the overlay down.
            if (_pendingTutorialFinish)
            {
                _pendingTutorialFinish = false;
                g.state.tutorialDone = true;
                g.onSave?.Invoke();
            }
            if (g.phase == AppPhase.Home && g.state.firstMirrorDone
                && !g.state.tutorialDone && _tutorialStep < 0
                && _tutorialOverlay != null && !_tutorialOverlay.activeSelf)
            {
                StartTutorial();
            }

            // Passive coin gain (quest reward / streak bonus) — purchases
            // decrease state.coins so any positive delta is always a gain
            // worth jingling. Skip if uninitialised (just-loaded save).
            if (_prevCoins >= 0 && g.state.coins > _prevCoins)
            {
                long delta = g.state.coins - _prevCoins;
                Sfx.Play("coin");
                // Aggregate inside an active float so a multi-quest burst lands
                // as a single "+N" rather than stacking overlapping floaters.
                _coinFloatAmount = (_coinFloatT > 0f) ? _coinFloatAmount + delta : delta;
                _coinFloatT      = COIN_FLOAT_DURATION;
                string txt = "+" + _coinFloatAmount;
                if (_homeCoinFloater      != null) _homeCoinFloater.text      = txt;
                if (_shopCoinFloater      != null) _shopCoinFloater.text      = txt;
                if (_setDetailCoinFloater != null) _setDetailCoinFloater.text = txt;
            }
            _prevCoins = g.state.coins;

            // Coin floater tick — runs every Refresh regardless of phase so the
            // active screen's "+N" animates wherever the player happens to be
            // when coins land. Each panel has its own floater Text; only the
            // active-phase one renders.
            if (_coinFloatT > 0f)
            {
                _coinFloatT -= Time.unscaledDeltaTime;
                float t01 = 1f - Mathf.Clamp01(_coinFloatT / COIN_FLOAT_DURATION);
                float a   = (t01 < 0.5f) ? 1f : Mathf.Clamp01(1f - (t01 - 0.5f) / 0.5f);
                TickCoinFloater(_homeCoinFloater,      t01, a, COIN_FLOAT_HOME_START_Y, COIN_FLOAT_HOME_END_Y);
                TickCoinFloater(_shopCoinFloater,      t01, a, COIN_FLOAT_SHOP_START_Y, COIN_FLOAT_SHOP_END_Y);
                TickCoinFloater(_setDetailCoinFloater, t01, a, COIN_FLOAT_SHOP_START_Y, COIN_FLOAT_SHOP_END_Y);
                if (_coinFloatT <= 0f) _coinFloatAmount = 0;
            }

            // Quest completion — detect any false -> true flip in questDone.
            if (g.state.questDone != null)
            {
                if (_prevQuestDone == null || _prevQuestDone.Length != g.state.questDone.Length)
                    _prevQuestDone = new bool[g.state.questDone.Length];
                for (int i = 0; i < g.state.questDone.Length; i++)
                {
                    if (g.state.questDone[i] && !_prevQuestDone[i])
                    {
                        Sfx.Play("quest_done");
                        if (i < _questPopT.Length) _questPopT[i] = TROPHY_POP_DURATION;
                    }
                    _prevQuestDone[i] = g.state.questDone[i];
                }
            }

            Set(_titlePanel,              g.phase == AppPhase.Title);
            if (g.phase == AppPhase.Title) UpdateTitle();
            Set(_openingIntroPanel,       g.phase == AppPhase.OpeningIntro);
            Set(_openingHeroShownPanel,   g.phase == AppPhase.OpeningHeroShown);
            Set(_openingCurseLoomsPanel,  g.phase == AppPhase.OpeningCurseLooms);
            Set(_curseAnimPanel,          g.phase == AppPhase.CurseAnim);
            Set(_openingAmnesiaPanel,     g.phase == AppPhase.OpeningAmnesia);
            Set(_firstMirrorPanel,        g.phase == AppPhase.FirstMirror);
            Set(_homePanel,               g.phase == AppPhase.Home);
            Set(_trainPanel,              g.phase == AppPhase.Quests);
            Set(_shopPanel,               g.phase == AppPhase.Shop);
            Set(_setDetailPanel,          g.phase == AppPhase.SetDetail);
            Set(_skinDetailPanel,         g.phase == AppPhase.SkinDetail);
            Set(_inventoryPanel,          g.phase == AppPhase.Inventory);
            Set(_raceSelectPanel,         g.phase == AppPhase.RaceSelect);
            Set(_raceAnimPanel,           g.phase == AppPhase.RaceTransformAnim);
            Set(_settingsPanel,           g.phase == AppPhase.Settings);
            // Modal is a child of _settingsPanel — when the player leaves
            // Settings the whole hierarchy goes inactive, but the modal's
            // own activeSelf stays true so it'd re-show on re-entry.
            // Force-close it on every transition out of Settings.
            if (g.phase != AppPhase.Settings && _hkReconnectModal != null)
                _hkReconnectModal.SetActive(false);
            if (g.phase == AppPhase.Settings) UpdateSettings(g);
            Set(_hkGatePanel,             g.phase == AppPhase.HealthKitGate);
            if (g.phase == AppPhase.HealthKitGate) UpdateHealthKitGate();
            Set(_casinoLobbyPanel,        g.phase == AppPhase.CasinoLobby);
            if (g.phase == AppPhase.CasinoLobby) UpdateCasinoLobby(g);
            Set(_casinoScratchPanel,      g.phase == AppPhase.CasinoScratch);
            if (g.phase == AppPhase.CasinoScratch) UpdateCasinoScratch();
            Set(_casinoUpgradesPanel,     g.phase == AppPhase.CasinoUpgrades);
            if (g.phase == AppPhase.CasinoUpgrades) UpdateCasinoUpgrades();
            Set(_casinoPrestigePanel,     g.phase == AppPhase.CasinoPrestige);
            if (g.phase == AppPhase.CasinoPrestige) UpdateCasinoPrestige();
            Set(_casinoSlotsPanel,        g.phase == AppPhase.CasinoSlots);
            if (g.phase == AppPhase.CasinoSlots) UpdateCasinoSlots();
            Set(_casinoFindCashPanel,     g.phase == AppPhase.CasinoFindCash);
            if (g.phase == AppPhase.CasinoFindCash) UpdateCasinoFindCash();
            Set(_casinoGoldRushPanel,     g.phase == AppPhase.CasinoGoldRush);
            if (g.phase == AppPhase.CasinoGoldRush) UpdateCasinoGoldRush();
            Set(_casinoLadderPanel,       g.phase == AppPhase.CasinoLadder);
            if (g.phase == AppPhase.CasinoLadder) UpdateCasinoLadder();
            Set(_casinoHighStakesPanel,   g.phase == AppPhase.CasinoHighStakes);
            if (g.phase == AppPhase.CasinoHighStakes) UpdateCasinoHighStakes();
            Set(_casinoMegaPanel,         g.phase == AppPhase.CasinoMega);
            if (g.phase == AppPhase.CasinoMega) UpdateCasinoMega();

            if (g.phase == AppPhase.RaceTransformAnim)
            {
                _raceAnimT += Time.unscaledDeltaTime;
                bool swapped = _raceAnimT >= RACE_ANIM_SWAP_AT;
                if (_raceAnimSilhouette != null) _raceAnimSilhouette.gameObject.SetActive(!swapped);
                if (_raceAnimAvatar != null)
                {
                    _raceAnimAvatar.root.SetActive(swapped);
                    if (swapped)
                        ApplyAvatarLook(_raceAnimAvatar,
                            (Gender)g.state.gender,
                            (Race)g.state.race, g.Stage, g.state.equipped);
                }
                if (_raceAnimT >= RACE_ANIM_DURATION) _onRaceAnimDone?.Invoke();
            }

            var gender = (Gender)g.state.gender;
            var safeGender = gender == Gender.Unset ? Gender.Male : gender;

            // Curse animation: shake hero + dim background, swap sprite at SWAP_AT,
            // auto-advance when duration elapses.
            if (g.phase == AppPhase.CurseAnim)
            {
                _curseAnimT += Time.unscaledDeltaTime;
                bool swapped = _curseAnimT >= CURSE_ANIM_SWAP_AT;

                // shake amplitude: ramp up 2 -> 14 px to swap, then decay 14 -> 0 px after
                float shakeAmp = swapped
                    ? Mathf.Lerp(14f, 0f, (_curseAnimT - CURSE_ANIM_SWAP_AT) / (CURSE_ANIM_DURATION - CURSE_ANIM_SWAP_AT))
                    : Mathf.Lerp(2f, 14f, _curseAnimT / CURSE_ANIM_SWAP_AT);
                float sx = Mathf.Round(UnityEngine.Random.Range(-shakeAmp, shakeAmp));
                float sy = Mathf.Round(UnityEngine.Random.Range(-shakeAmp, shakeAmp));
                ((RectTransform)_curseAnimAvatar.root.transform).anchoredPosition = new Vector2(sx, sy);

                // dim overlay alpha 0 -> 0.7
                var dc = _curseAnimDim.color;
                dc.a = Mathf.Lerp(0f, 0.7f, _curseAnimT / CURSE_ANIM_DURATION);
                _curseAnimDim.color = dc;

                // sprite: hero (dark_knight composite, full color) before swap,
                // cursed (stage 0 of chosen curse) after. Pre-swap bypasses
                // ApplyAvatarLook's race-form path because we want the
                // legendary hero rendered as the dark_knight set — same image
                // the OpeningHeroShown panel uses, so the curse "shatters" the
                // exact same hero the player just saw.
                if (swapped)
                {
                    ApplyAvatarLook(_curseAnimAvatar, safeGender, Race.Unset, 0);
                }
                else
                {
                    var heroSprite = Make.SetPreview("champ_dark_knight");
                    if (heroSprite != null)
                    {
                        _curseAnimAvatar.portrait.sprite = heroSprite;
                        float a = _curseAnimAvatar.portrait.color.a;
                        _curseAnimAvatar.portrait.color = new Color(1f, 1f, 1f, a);
                        SetOverlay(_curseAnimAvatar.sword,     null, a);
                        SetOverlay(_curseAnimAvatar.armor,     null, a);
                        SetOverlay(_curseAnimAvatar.helmet,    null, a);
                        SetOverlay(_curseAnimAvatar.leggings,  null, a);
                        SetOverlay(_curseAnimAvatar.gauntlets, null, a);
                        SetOverlay(_curseAnimAvatar.boots,     null, a);
                    }
                    else
                    {
                        // Fallback if composite isn't baked yet.
                        ApplyAvatarLook(_curseAnimAvatar, safeGender, Race.Unset, 5);
                    }
                }

                if (_curseAnimT >= CURSE_ANIM_DURATION) _onCurseAnimDone?.Invoke();
            }

            // (Curse-select avatars used to switch by gender; M3a/M3b made the
            // curse preview gender-neutral so no per-frame work is needed.)

            // First mirror reveals the cursed self (not the lost hero).
            if (_firstMirrorSelf != null)
                ApplyAvatarLook(_firstMirrorSelf, safeGender, Race.Unset, stage: 0);

            if (g.phase == AppPhase.Home || g.phase == AppPhase.Quests || g.phase == AppPhase.Shop)
            {
                _homeLevel.text   = $"Lv {g.state.level}";
                _homeCoins.text   = $"{g.state.coins}";
                LayoutCoinNextToText(_homeCoinIcon, _homeCoins, marginRight: 40f, coinYOffset: -54f - SAFE_AREA_TOP_INSET);
                _homeStreak.text  = $"{g.state.streakDays}-day streak";
                bool dailyGoalMet = g.state.todaySteps >= 5000;
                _homeProgress.text  = $"Today {g.state.todaySteps} steps";
                _homeProgress.color = dailyGoalMet ? AccentGold : TextDim;
                if (_homeProgressCheck != null)
                {
                    _homeProgressCheck.enabled = dailyGoalMet;
                    if (dailyGoalMet)
                    {
                        // preferredWidth needs a layout pass to be accurate; ForceMeshUpdate
                        // gives us the rendered width of the current string at FS_LABEL.
                        _homeProgress.ForceMeshUpdate();
                        float textHalfW = _homeProgress.preferredWidth * 0.5f;
                        _homeProgressCheck.rectTransform.anchoredPosition =
                            new Vector2(textHalfW + 32f, -240f);
                    }
                }
                float xpTarget = (float)g.XpInCurrentLevel / Mathf.Max(1, g.XpToNextLevel);
                if (_xpDisplayPct < 0f)
                {
                    _xpDisplayPct = xpTarget;   // first Refresh, snap silently
                    _xpPrevLevel  = g.state.level;
                }
                else if (_xpAnimState == XpAnim.Normal && g.state.level > _xpPrevLevel)
                {
                    // Level-up: enter the fill -> hold -> drop sequence so the
                    // bar finishes the lap before chasing the new XP value.
                    _xpAnimStartPct = _xpDisplayPct;
                    _xpAnimState    = XpAnim.LevelUpFill;
                    _xpAnimT        = XP_LEVELUP_FILL_DUR;
                }
                _xpPrevLevel = g.state.level;

                float dt = Time.unscaledDeltaTime;
                switch (_xpAnimState)
                {
                    case XpAnim.Normal:
                        // Exponential ease — fast initial chase, smooth settle.
                        _xpDisplayPct = Mathf.Lerp(_xpDisplayPct, xpTarget,
                            1f - Mathf.Exp(-XP_SMOOTH_SPEED * dt));
                        break;
                    case XpAnim.LevelUpFill:
                        _xpAnimT -= dt;
                        float tf = 1f - Mathf.Clamp01(_xpAnimT / XP_LEVELUP_FILL_DUR);
                        _xpDisplayPct = Mathf.Lerp(_xpAnimStartPct, 1f, tf);
                        if (_xpAnimT <= 0f)
                        {
                            _xpDisplayPct = 1f;
                            _xpAnimState  = XpAnim.LevelUpHold;
                            _xpAnimT      = XP_LEVELUP_HOLD_DUR;
                        }
                        break;
                    case XpAnim.LevelUpHold:
                        _xpAnimT -= dt;
                        _xpDisplayPct = 1f;
                        if (_xpAnimT <= 0f)
                        {
                            _xpAnimState = XpAnim.LevelUpDrop;
                            _xpAnimT     = XP_LEVELUP_DROP_DUR;
                        }
                        break;
                    case XpAnim.LevelUpDrop:
                        _xpAnimT -= dt;
                        float td = 1f - Mathf.Clamp01(_xpAnimT / XP_LEVELUP_DROP_DUR);
                        _xpDisplayPct = Mathf.Lerp(1f, 0f, td);
                        if (_xpAnimT <= 0f)
                        {
                            _xpDisplayPct = 0f;
                            _xpAnimState  = XpAnim.Normal;
                        }
                        break;
                }
                _xpBar.rectTransform.sizeDelta = new Vector2(
                    700f * Mathf.Clamp01(_xpDisplayPct), 28f);

                // Next-milestone hint — guides the player toward the Lv 10
                // form shift, Lv 15 fuller body, Lv 20 race transformation.
                int next = g.state.level < 10 ? 10
                         : g.state.level < 15 ? 15
                         : g.state.level < 20 ? 20
                         : -1;
                _homeNextHint.text = next > 0
                    ? $"Next form change at Lv {next}"
                    : ((Race)g.state.race != Race.Unset ? "" : "Race awakens at Lv 20");

                // Mirror is the player at the current stage. race == Unset -> skeleton growth,
                // race != Unset -> race form (post Lv 20 transformation).
                ApplyAvatarLook(_mirrorSelf, safeGender, (Race)g.state.race, g.Stage,
                                g.state.equipped, g.state.activeSkin);
                _mirrorSelf.SetAlpha(1f);

                // Stage / level transition detection (Home only). First Refresh after Home
                // appears initialises the tracker without firing a fake milestone.
                if (g.phase == AppPhase.Home)
                {
                    int currentStage = g.Stage;
                    if (_prevStage < 0)
                    {
                        _prevStage = currentStage;
                        _prevLevel = g.state.level;
                    }
                    else
                    {
                        if (currentStage > _prevStage)
                        {
                            _stageUpT = STAGEUP_DURATION;
                            int idx = Mathf.Clamp(currentStage - 1, 0, MILESTONE_LINES.Length - 1);
                            _milestoneText.text = MILESTONE_LINES[idx];
                            _milestoneT = MILESTONE_DURATION;
                            Sfx.Play("milestone");
                        }
                        else if (g.state.level > _prevLevel)
                        {
                            Sfx.Play("level_up");
                            _levelUpT = LEVELUP_DURATION;
                        }
                        // hitting max level for the first time fires the 6th line, even
                        // though Stage doesn't change (Lv 26-30 are all stage 5).
                        // Lv 30 milestone fires once when first hit (no level cap now).
                        if (_prevLevel < 30 && g.state.level >= 30)
                        {
                            _stageUpT = STAGEUP_DURATION;
                            _milestoneText.text = MILESTONE_LINES[5];
                            _milestoneT = MILESTONE_DURATION;
                        }
                        _prevStage = currentStage;
                        _prevLevel = g.state.level;
                    }
                }

                // Stage-up flash (scale pulse + white overlay) supersedes breathing.
                if (_stageUpT > 0f)
                {
                    _stageUpT -= Time.unscaledDeltaTime;
                    float t = 1f - Mathf.Clamp01(_stageUpT / STAGEUP_DURATION);
                    float pulse = Mathf.Sin(t * Mathf.PI);
                    float scale = 1f + STAGEUP_SCALE_AMP * pulse;
                    _mirrorSelf.root.transform.localScale = new Vector3(scale, scale, 1f);
                    var fc = _stageUpFlash.color;
                    fc.a = STAGEUP_FLASH_A * pulse;
                    _stageUpFlash.color = fc;
                    if (_stageUpT <= 0f) _stageUpFlash.color = new Color(1f, 1f, 1f, 0f);
                }
                else
                {
                    // Idle breathing — gentle sin-wave scale modulation.
                    float breath = 1f + BREATH_AMP * Mathf.Sin(Time.time * BREATH_FREQ);
                    _mirrorSelf.root.transform.localScale = new Vector3(breath, breath, 1f);
                }

                // Milestone text — visible for MILESTONE_DURATION, fades out over last second.
                if (_milestoneT > 0f)
                {
                    _milestoneT -= Time.unscaledDeltaTime;
                    var tc = _milestoneText.color;
                    tc.a = Mathf.Clamp01(_milestoneT / MILESTONE_FADE_OUT);
                    _milestoneText.color = tc;
                }

                // Level-up celebration — sine-pulse the Lv label scale + fade the
                // "LEVEL UP!" toast in/hold/out over LEVELUP_DURATION. The pulse
                // pivots from the label's top-left (anchor 0,1) so it grows toward
                // the mirror rather than off-screen.
                if (_levelUpT > 0f)
                {
                    _levelUpT -= Time.unscaledDeltaTime;
                    float t01  = 1f - Mathf.Clamp01(_levelUpT / LEVELUP_DURATION);
                    float bell = Mathf.Sin(t01 * Mathf.PI);
                    float s    = 1f + LEVELUP_PULSE_AMP * bell;
                    _homeLevel.transform.localScale = new Vector3(s, s, 1f);

                    // Toast alpha: fade in over first 15%, hold, fade out over last 35%.
                    float a;
                    if      (t01 < 0.15f) a = t01 / 0.15f;
                    else if (t01 > 0.65f) a = Mathf.Clamp01((1f - t01) / 0.35f);
                    else                  a = 1f;
                    var lc = _levelUpToast.color; lc.a = a; _levelUpToast.color = lc;

                    if (_levelUpT <= 0f)
                    {
                        _homeLevel.transform.localScale = Vector3.one;
                        _levelUpToast.color = new Color(1f, 0.84f, 0.42f, 0f);
                    }
                }


                // Daily ritual: 4 candles light up incrementally at 1.25k / 2.5k /
                // 3.75k / 5k steps. The crown switches to gold + bursts when all
                // four are lit. Each newly-lit candle scale-pops and chimes;
                // the 4th additionally fires the milestone sound + crown burst.
                int candlesLit = Mathf.Clamp(g.state.todaySteps / STEPS_PER_CANDLE, 0, 4);
                var litSprite   = Make.UI("candle_lit");
                var unlitSprite = Make.UI("candle_unlit");
                for (int i = 0; i < _candleImgs.Length; i++)
                {
                    if (_candleImgs[i] == null) continue;
                    _candleImgs[i].sprite = (i < candlesLit) ? litSprite : unlitSprite;
                }
                _crownImg.sprite = Make.UI(candlesLit >= 4 ? "crown_gold" : "crown_grey");

                // Transition detection — first Home Refresh just records the
                // baseline, no celebration fires for a save that loaded
                // mid-day with candles already lit. Decreases (day rollover)
                // also update the tracker so the next mid-day light-up fires.
                if (_prevCandlesLit < 0)
                {
                    _prevCandlesLit = candlesLit;
                }
                else if (candlesLit != _prevCandlesLit)
                {
                    if (candlesLit > _prevCandlesLit)
                    {
                        for (int i = _prevCandlesLit; i < candlesLit && i < _candleFlashT.Length; i++)
                        {
                            _candleFlashT[i] = CANDLE_FLASH_DURATION;
                            Sfx.Play("quest_done");
                        }
                        if (_prevCandlesLit < 4 && candlesLit >= 4)
                        {
                            _crownLandT = CROWN_LAND_DURATION;
                            Sfx.Play("milestone");
                        }
                    }
                    _prevCandlesLit = candlesLit;
                }

                // Per-frame ticks for candle pops + crown burst. Sine-bell scale.
                for (int i = 0; i < _candleImgs.Length; i++)
                {
                    if (_candleImgs[i] == null) continue;
                    if (_candleFlashT[i] > 0f)
                    {
                        _candleFlashT[i] -= Time.unscaledDeltaTime;
                        float t01  = 1f - Mathf.Clamp01(_candleFlashT[i] / CANDLE_FLASH_DURATION);
                        float s    = 1f + CANDLE_FLASH_SCALE_AMP * Mathf.Sin(t01 * Mathf.PI);
                        _candleImgs[i].rectTransform.localScale = new Vector3(s, s, 1f);
                        if (_candleFlashT[i] <= 0f)
                            _candleImgs[i].rectTransform.localScale = Vector3.one;
                    }
                }
                if (_crownLandT > 0f)
                {
                    _crownLandT -= Time.unscaledDeltaTime;
                    float t01 = 1f - Mathf.Clamp01(_crownLandT / CROWN_LAND_DURATION);
                    float s   = 1f + CROWN_LAND_SCALE_AMP * Mathf.Sin(t01 * Mathf.PI);
                    _crownImg.rectTransform.localScale = new Vector3(s, s, 1f);
                    if (_crownLandT <= 0f)
                        _crownImg.rectTransform.localScale = Vector3.one;
                }
            }

            if (g.phase == AppPhase.Quests)
            {
                UpdateQuests(g);
            }

            if (g.phase == AppPhase.Shop)
            {
                _shopCoins.text = $"{g.state.coins}";
                LayoutCoinNextToText(_shopCoinIcon, _shopCoins, marginRight: 40f, coinYOffset: -84f - SAFE_AREA_TOP_INSET);
                _ownedSnapshot.Clear();
                foreach (var id in g.state.owned) _ownedSnapshot.Add(id);

                // Per-card price/owned labels + gold-glow tint when owned.
                // ColorTint button transition multiplies these so the press
                // highlight still lifts on top of the owned tint.
                var setOwnedTint   = new Color(1.00f, 0.92f, 0.55f, 1f);
                var setDefaultTint = new Color(0.95f, 0.86f, 0.66f, 1f);
                foreach (var card in _shopSetCards)
                {
                    var set = GamexGame.FindSet(card.setId);
                    if (set == null) continue;
                    bool fullyOwned = true;
                    foreach (var p in set.pieces)
                        if (!g.IsOwned(p.id)) { fullyOwned = false; break; }
                    if (fullyOwned)
                        card.priceLabel.text = "Complete set owned";
                    else
                        card.priceLabel.text = $"{set.BundlePrice} gold";
                    var img = card.root.GetComponent<Image>();
                    if (img != null) img.color = fullyOwned ? setOwnedTint : setDefaultTint;
                }

                // Phase 4b: per-skin Buy / Apply / Remove + locked state.
                // Owned = warm gold tint; Active = brightest gold so the
                // currently-worn skin pops at a glance.
                var skinDefaultTint = new Color(0.92f, 0.84f, 0.62f, 1f);
                var skinOwnedTint   = new Color(0.98f, 0.88f, 0.55f, 1f);
                var skinActiveTint  = new Color(1.00f, 0.92f, 0.45f, 1f);
                foreach (var card in _shopSkinCards)
                {
                    var skin = GamexGame.FindSkin(card.skinId);
                    if (skin == null) continue;
                    bool owned = g.IsSkinOwned(skin.id);
                    bool active = g.IsSkinActive(skin.id);
                    if      (!owned)  card.stateLabel.text = $"{skin.price} gold";
                    else if (!active) card.stateLabel.text = "Owned";
                    else              card.stateLabel.text = "Active";
                    var img = card.root.GetComponent<Image>();
                    if (img != null)
                        img.color = active ? skinActiveTint
                                    : owned ? skinOwnedTint
                                            : skinDefaultTint;
                }
            }

            if (g.phase == AppPhase.SetDetail) UpdateSetDetail(g);
            if (g.phase == AppPhase.SkinDetail) UpdateSkinDetail(g);
            if (g.phase == AppPhase.Inventory) UpdateInventory(g);

            // Phase 5e3 — animate the active skin. Runs every Refresh; cheap
            // when no animated skin is applied (early return on frameCount<=1).
            TickActiveSkinAnimation(g);
            TickSkinDetailAnimation(g);
            TickActivePet(g);
        }

        // Pet companion — small sprite drawn beside the avatar. Same per-frame
        // sprite swap as TickActiveSkinAnimation, just driven off the separate
        // state.activePet slot so a character skin + a pet can coexist.
        void TickActivePet(GamexGame g)
        {
            string activePet = g.state.activePet;
            if (activePet != _petAnimLast)
            {
                _petAnimLast   = activePet;
                _petAnimFrame  = 0;
                _petAnimTimer  = 0f;
            }
            Image petImg = null;
            if (g.phase == AppPhase.Home || g.phase == AppPhase.Quests || g.phase == AppPhase.Shop || g.phase == AppPhase.SetDetail || g.phase == AppPhase.SkinDetail)
                petImg = _homePet;
            else if (g.phase == AppPhase.Inventory)
                petImg = _inventoryPet;
            if (petImg == null) return;
            if (string.IsNullOrEmpty(activePet))
            {
                if (petImg.gameObject.activeSelf) petImg.gameObject.SetActive(false);
                return;
            }
            var pet = GamexGame.FindSkin(activePet);
            if (pet == null) return;
            if (!petImg.gameObject.activeSelf) petImg.gameObject.SetActive(true);
            if (pet.frameCount > 1)
            {
                _petAnimTimer += Time.unscaledDeltaTime;
                float perFrame = pet.frameSeconds > 0f ? pet.frameSeconds : 0.12f;
                while (_petAnimTimer >= perFrame)
                {
                    _petAnimTimer -= perFrame;
                    _petAnimFrame  = (_petAnimFrame + 1) % pet.frameCount;
                }
                var spr = Resources.Load<Sprite>($"Skins/{activePet}_{_petAnimFrame:D2}");
                if (spr != null) petImg.sprite = spr;
            }
            else
            {
                var spr = Resources.Load<Sprite>($"Skins/{activePet}");
                if (spr != null) petImg.sprite = spr;
            }
        }

        // Advances the active-skin frame timer and swaps the avatar's portrait
        // sprite to the next frame when the per-frame interval elapses. Frame
        // state resets whenever the player applies a different skin (or none).
        void TickActiveSkinAnimation(GamexGame g)
        {
            string activeSkin = g.state.activeSkin;
            if (activeSkin != _animLastSkin)
            {
                _animLastSkin = activeSkin;
                _animFrame    = 0;
                _animTimer    = 0f;
            }
            var skin = GamexGame.FindSkin(activeSkin);
            if (skin == null || skin.frameCount <= 1) return;

            _animTimer += Time.unscaledDeltaTime;
            float perFrame = skin.frameSeconds > 0f ? skin.frameSeconds : 0.12f;
            while (_animTimer >= perFrame)
            {
                _animTimer -= perFrame;
                _animFrame  = (_animFrame + 1) % skin.frameCount;
            }
            // Pick whichever avatar is on screen this phase.
            AvatarSprite active = null;
            if (g.phase == AppPhase.Home || g.phase == AppPhase.Quests || g.phase == AppPhase.Shop || g.phase == AppPhase.SetDetail || g.phase == AppPhase.SkinDetail)
                active = _mirrorSelf;
            else if (g.phase == AppPhase.Inventory)
                active = _inventoryAvatar;
            if (active == null || active.portrait == null) return;
            var spr = Resources.Load<Sprite>($"Skins/{activeSkin}_{_animFrame:D2}");
            if (spr != null) active.portrait.sprite = spr;
        }

        // Cycles the skin-detail preview through Skins/<id>_NN.png frames
        // so the legend art breathes inside the detail page instead of
        // sitting as a flat sprite. Static skins (frameCount <= 1) just
        // show the base sprite and return.
        void TickSkinDetailAnimation(GamexGame g)
        {
            if (g.phase != AppPhase.SkinDetail || _skinDetailPreview == null) return;
            string id = g.activeSkinId;
            var skin = GamexGame.FindSkin(id);
            if (skin == null) return;
            if (id != _skinDetailAnimLast)
            {
                _skinDetailAnimLast  = id;
                _skinDetailAnimFrame = 0;
                _skinDetailAnimTimer = 0f;
            }
            if (skin.frameCount <= 1) return;
            _skinDetailAnimTimer += Time.unscaledDeltaTime;
            float perFrame = skin.frameSeconds > 0f ? skin.frameSeconds : 0.12f;
            while (_skinDetailAnimTimer >= perFrame)
            {
                _skinDetailAnimTimer -= perFrame;
                _skinDetailAnimFrame  = (_skinDetailAnimFrame + 1) % skin.frameCount;
            }
            var spr = Resources.Load<Sprite>($"Skins/{id}_{_skinDetailAnimFrame:D2}");
            if (spr != null) _skinDetailPreview.sprite = spr;
        }

        // ============================================================
        // Set detail refresh — flip per-set row groups to match
        // g.activeSetId, refresh price/owned labels + bundle CTA state.
        // ============================================================
        void UpdateSetDetail(GamexGame g)
        {
            _currentSetId = g.activeSetId;
            _ownedSnapshot.Clear();
            foreach (var id in g.state.owned) _ownedSnapshot.Add(id);
            _setDetailCoins.text = $"{g.state.coins}";
            LayoutCoinNextToText(_setDetailCoinIcon, _setDetailCoins, marginRight: 40f, coinYOffset: -84f - SAFE_AREA_TOP_INSET);

            var set = GamexGame.FindSet(_currentSetId);
            if (set == null) return;

            _setDetailTitle.text  = set.displayName;
            _setDetailPreview.sprite = Make.SetPreview(set.id);

            bool fullyOwned = true;
            foreach (var p in set.pieces)
                if (!g.IsOwned(p.id)) { fullyOwned = false; break; }

            int price = set.BundlePrice;
            if (fullyOwned)
            {
                _setDetailBundleLabel.text = "Complete set owned";
                _setDetailBundleBtn.interactable = false;
                _setDetailBundleBtn.GetComponentInChildren<Text>().text = "Already Owned";
            }
            else if (g.state.coins < price)
            {
                _setDetailBundleLabel.text = $"Buy whole set: {price} gold (you need more gold)";
                _setDetailBundleBtn.interactable = false;
                _setDetailBundleBtn.GetComponentInChildren<Text>().text = $"Buy Set ({price}g)";
            }
            else
            {
                _setDetailBundleLabel.text = $"Buy whole set: {price} gold";
                _setDetailBundleBtn.interactable = true;
                _setDetailBundleBtn.GetComponentInChildren<Text>().text = $"Buy Set ({price}g)";
            }
        }

        // Skin detail refresh — mirror of UpdateSetDetail, simpler because
        // skins are atomic (one preview, one price, one CTA whose label
        // depends on owned/active state).
        void UpdateSkinDetail(GamexGame g)
        {
            _currentSkinId = g.activeSkinId;
            _skinDetailCoins.text = $"{g.state.coins}";

            var skin = GamexGame.FindSkin(_currentSkinId);
            if (skin == null) return;

            _skinDetailTitle.text  = skin.displayName;
            _skinDetailPreview.sprite = Make.Skin(skin.id);

            bool owned  = g.IsSkinOwned(skin.id);
            bool active = g.IsSkinActive(skin.id);
            if (!owned)
            {
                _skinDetailStateLabel.text  = $"{skin.price} gold";
                _skinDetailActionLabel.text = "Buy";
                _skinDetailActionBtn.interactable = g.state.coins >= skin.price;
            }
            else if (!active)
            {
                _skinDetailStateLabel.text  = "Owned";
                _skinDetailActionLabel.text = "Apply";
                _skinDetailActionBtn.interactable = true;
            }
            else
            {
                _skinDetailStateLabel.text  = "Active";
                _skinDetailActionLabel.text = "Remove";
                _skinDetailActionBtn.interactable = true;
            }
        }

        // ============================================================
        // Inventory panel refresh — repaints the paper-doll avatar, the
        // 6 slot icons, and the storage grid (own / equipped state).
        // ============================================================
        void UpdateInventory(GamexGame g)
        {
            var gender = (Gender)g.state.gender;
            var safeGender = gender == Gender.Unset ? Gender.Male : gender;
            var race   = (Race)g.state.race;
            ApplyAvatarLook(_inventoryAvatar, safeGender, race, g.Stage, g.state.equipped, g.state.activeSkin);
            _inventoryAvatar.SetAlpha(1f);

            // Walk owned outfits into the grid: Champion sets first (when all
            // 6 pieces are owned -> one cell), then Skins (each owned Skin =
            // one cell). Active outfit gets the "Active" badge.
            int gridIdx = 0;
            foreach (var set in GamexGame.SetCatalog)
            {
                if (gridIdx >= INV_GRID_CAPACITY) break;
                bool fullyOwned = true;
                foreach (var p in set.pieces)
                    if (!g.IsOwned(p.id)) { fullyOwned = false; break; }
                if (!fullyOwned) continue;
                _invGridIds[gridIdx] = set.id;
                var spr = Make.SetPreview(set.id);
                if (spr != null) _invGridIcons[gridIdx].sprite = spr;
                bool active = g.IsOutfitActive(set.id);
                if (_invGridBadges[gridIdx].activeSelf != active)
                    _invGridBadges[gridIdx].SetActive(active);
                if (!_invGridRoots[gridIdx].activeSelf) _invGridRoots[gridIdx].SetActive(true);
                gridIdx++;
            }
            // Knight Set sits outside SetCatalog (it's a chain-quest reward,
            // not a shop bundle), so the SetCatalog loop above never sees it.
            // Render it as its own cell once the chain has granted all 6
            // pieces — tap routes through ApplyOutfit("knight_silver_set")
            // because FindSet picks up KnightOutfit as a fallback.
            if (gridIdx < INV_GRID_CAPACITY)
            {
                var knight = GamexGame.KnightOutfit;
                bool fullyOwned = true;
                foreach (var p in knight.pieces)
                    if (!g.IsOwned(p.id)) { fullyOwned = false; break; }
                if (fullyOwned)
                {
                    _invGridIds[gridIdx] = knight.id;
                    var spr = Make.SetPreview(knight.id);
                    if (spr != null) _invGridIcons[gridIdx].sprite = spr;
                    bool active = g.IsOutfitActive(knight.id);
                    if (_invGridBadges[gridIdx].activeSelf != active)
                        _invGridBadges[gridIdx].SetActive(active);
                    if (!_invGridRoots[gridIdx].activeSelf) _invGridRoots[gridIdx].SetActive(true);
                    gridIdx++;
                }
            }
            // Owned skins (Legends + Cyberpunk; pets are hidden for launch).
            foreach (var id in g.state.ownedSkins)
            {
                if (gridIdx >= INV_GRID_CAPACITY) break;
                _invGridIds[gridIdx] = id;
                var spr = Make.Skin(id);
                if (spr != null) _invGridIcons[gridIdx].sprite = spr;
                bool active = g.IsSkinActive(id);
                if (_invGridBadges[gridIdx].activeSelf != active)
                    _invGridBadges[gridIdx].SetActive(active);
                if (!_invGridRoots[gridIdx].activeSelf) _invGridRoots[gridIdx].SetActive(true);
                gridIdx++;
            }
            for (int i = gridIdx; i < INV_GRID_CAPACITY; i++)
            {
                _invGridIds[i] = null;
                if (_invGridRoots[i].activeSelf) _invGridRoots[i].SetActive(false);
            }
        }

        // Title screen tick — drives wordmark / tagline fade-in, gentle
        // continuous pulse on the Start Game button, independent candle
        // flicker, and the post-tap fade-out before the actual phase
        // transition. _titleT is reset to 0 on phase entry so each cold
        // start replays the intro from the beginning.
        void UpdateTitle()
        {
            _titleT += Time.unscaledDeltaTime;

            if (_titleWordmark != null)
            {
                float a = Mathf.Clamp01(_titleT / TITLE_FADEIN_DURATION);
                var c = _titleWordmark.color; c.a = a; _titleWordmark.color = c;
            }
            if (_titleTagline != null)
            {
                // Tagline starts a beat after the wordmark so the eye lands
                // on the title first, then the subtitle resolves.
                float a = Mathf.Clamp01((_titleT - TITLE_TAGLINE_DELAY) / TITLE_TAGLINE_DURATION);
                var c = _titleTagline.color; c.a = a; _titleTagline.color = c;
            }
            // Tap-to-Start gentle alpha pulse — breathes between 0.55 and
            // 1.0 over a 2-second cycle. Subtle enough not to dominate the
            // painted scene, but obvious enough to read as "this is the
            // interactive bit." Alpha-only (no scale) since scaling text
            // on a static painted background looks like a flicker bug;
            // alpha breathing is the standard mobile "tap to start" cue.
            if (_titleStartLabel != null)
            {
                float a = 0.775f + 0.225f * Mathf.Sin(_titleT * (Mathf.PI * 2f / 2.0f));
                var col = _titleStartLabel.color;
                col.a = a;
                _titleStartLabel.color = col;
            }

            // Crown halo breathing — slow ~3.4s cycle on the Kenney glow
            // sprite. Alpha rides between 0.28 and 0.42, scale rides ±2%,
            // both phase-offset from the candle flicker so the throne-
            // room "lights" don't pulse in lockstep.
            if (_titleCrownHalo != null)
            {
                float haloT  = _titleT * (Mathf.PI * 2f / 3.4f);
                float haloA  = 0.35f + 0.07f * Mathf.Sin(haloT);
                float haloS  = 1f    + 0.02f * Mathf.Sin(haloT + 0.6f);
                var col = _titleCrownHalo.color; col.a = haloA; _titleCrownHalo.color = col;
                _titleCrownHalo.rectTransform.localScale = new Vector3(haloS, haloS, 1f);
            }

            // Candle flicker — each lamp gets a hardcoded phase + frequency
            // offset so the four flames flicker out of sync. Two coupled
            // sin waves (one for alpha, one for scale) at slightly different
            // frequencies give an irregular, organic-looking flame instead
            // of a metronomic strobe. Alpha floor is 0.82 so the candle
            // shape stays readable even at the trough.
            for (int i = 0; i < _titleCandles.Length; i++)
            {
                var c = _titleCandles[i];
                if (c == null) continue;
                float phase  = i * 1.37f;                                // arbitrary, mutually-prime-ish
                float aFreq  = 2.1f + i * 0.27f;                         // 2.1..2.91 Hz per lamp
                float sFreq  = 1.6f + i * 0.18f;                         // 1.6..2.14 Hz per lamp
                float aMul   = CANDLE_FLICKER_ALPHA_MIN + (1f - CANDLE_FLICKER_ALPHA_MIN)
                               * (0.5f + 0.5f * Mathf.Sin(_titleT * aFreq + phase));
                float sMul   = 1f + 0.025f * Mathf.Sin(_titleT * sFreq + phase * 1.4f);
                var col = c.color; col.a = aMul; c.color = col;
                c.rectTransform.localScale = new Vector3(sMul, sMul, 1f);
            }

            // Exit transition — Start Game's onClick set _titleExiting and
            // disabled the button. Fade the whole panel via CanvasGroup,
            // then fire _onLeaveTitle once the fade completes so the next
            // phase swap (OpeningIntro / Home / RaceSelect) happens after
            // the player sees the title resolve, not snap-cut away.
            if (_titleExiting)
            {
                _titleExitT += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(_titleExitT / TITLE_EXIT_DURATION);
                if (_titleCanvasGroup != null) _titleCanvasGroup.alpha = 1f - t;
                if (t >= 1f)
                {
                    _titleExiting = false;   // arm again for any future re-entry (Reset Progress)
                    _onLeaveTitle?.Invoke();
                }
            }
        }

        // Settings panel refresh — repaints the 4 row labels every frame so
        // toggles + HK status + the reset-confirm countdown reflect live
        // state. Cheap (4 Text mutations) so running on every Refresh is fine.
        // Drives the HealthKit gate UI per Refresh tick. Three states:
        //   1. iOS device, HK not available — extremely rare device shape;
        //      we hide the action button and the body explains the dead end.
        //   2. iOS device, HK status Denied — modal won't re-trigger, so the
        //      button switches to "Open iOS Settings" (deep-link).
        //   3. iOS device, HK status NotDetermined — first-time prompt path.
        // The Authorized case never appears here (gate would have closed).
        void UpdateHealthKitGate()
        {
            if (_hkGateBody == null || _hkGateActionLabel == null || _hkGateActionBtn == null) return;

            bool hkAvailable = HealthKitBridge.IsAvailable();
            if (!hkAvailable)
            {
                _hkGateBody.text = "This device doesn't support HealthKit, which Gamexercise needs to track your steps. The game can't run on this device.";
                _hkGateActionBtn.SetActive(false);
                return;
            }

            _hkGateActionBtn.SetActive(true);
            if (HealthKitBridge.CurrentStatus() == HealthKitBridge.AuthStatus.Denied)
            {
                _hkGateBody.text = "HealthKit access is currently denied. Open iOS Settings, enable Steps for Gamexercise, and return to continue.";
                _hkGateActionLabel.text = "Open iOS Settings";
            }
            else
            {
                _hkGateBody.text = "Gamexercise tracks your real-world steps via HealthKit. Your character only grows when you walk — without HealthKit there's nothing to power the game.";
                _hkGateActionLabel.text = "Connect HealthKit";
            }
        }

        void UpdateSettings(GamexGame g)
        {
            if (_settingsSfxLabel != null)
                _settingsSfxLabel.text = "Sound effects: " + (g.state.sfxMuted ? "OFF" : "ON");
            if (_settingsBgmLabel != null)
                _settingsBgmLabel.text = "Music: " + (g.state.bgmMuted ? "OFF" : "ON");

            if (_settingsHKLabel != null)
            {
                // Static "Reconnect HealthKit" label regardless of cached
                // status — Apple's read-only auth API is unreliable enough
                // (always returns sharingDenied for read requests) that
                // the previous Connected/Denied state labels were often
                // misleading. The single Reconnect action handles every
                // recovery case (originally-denied, later-revoked, just
                // want to retry).
                _settingsHKLabel.text = HealthKitBridge.IsAvailable()
                    ? "Reconnect HealthKit"
                    : "Not available on this device";
            }

            if (_settingsResetLabel != null)
            {
                float remaining = _resetArmedUntil - Time.unscaledTime;
                if (remaining > 0f)
                    _settingsResetLabel.text = $"Tap again to confirm ({Mathf.CeilToInt(remaining)}s)";
                else
                {
                    _resetArmedUntil = 0f;
                    _settingsResetLabel.text = "Reset progress";
                }
            }
        }

        static void Set(GameObject go, bool active)
        {
            if (go == null) return;
            if (go.activeSelf != active) go.SetActive(active);
        }

        // Position the coin sprite immediately to the left of the gold number
        // with a 10px gap. Reading text.preferredWidth after the text is set
        // lets the coin track varying digit counts (e.g. "0" vs "9500").
        // anchor (1,1) pivot (1,1) means pos.x is measured leftward from the
        // panel's right edge: coin's right edge sits at
        //    marginRight + textVisualWidth + gap
        // away from the panel right.
        const float COIN_GAP = 10f;
        static void LayoutCoinNextToText(Image coin, TMP_Text text, float marginRight, float coinYOffset)
        {
            if (coin == null || text == null) return;
            float textW = text.preferredWidth;
            var rt = coin.rectTransform;
            rt.anchoredPosition = new Vector2(-(marginRight + textW + COIN_GAP), coinYOffset);
        }

        // ============================================================
        // Quests panel — per-row checkmark + total counters refresh.
        // ============================================================
        void UpdateQuests(GamexGame g)
        {
            for (int i = 0; i < QUEST_SPEC.Length; i++)
            {
                var spec = QUEST_SPEC[i];
                int progress = spec.runMinutes ? g.state.todayRunSeconds / 60 : g.state.todaySteps;
                bool done = g.state.questDone != null
                            && spec.quest < (Quest)g.state.questDone.Length
                            && g.state.questDone[(int)spec.quest];

                if (_questRowLabels[i] != null)
                {
                    string status = done
                        ? "done"
                        : (spec.runMinutes
                            ? $"{progress} / {spec.goal} min"
                            : $"{progress} / {spec.goal} steps");
                    _questRowLabels[i].text = $"{spec.label}\n{status}";
                }
                if (_questCheckmarks[i] != null)
                {
                    _questCheckmarks[i].gameObject.SetActive(done);
                    // Scale-pop: overshoot to 1+AMP at midpoint, settle to 1.0.
                    // Idle trophies stay at 1.0; the pop only runs for the
                    // freshly-completed one.
                    if (_questPopT[i] > 0f)
                    {
                        _questPopT[i] -= Time.unscaledDeltaTime;
                        float t01  = 1f - Mathf.Clamp01(_questPopT[i] / TROPHY_POP_DURATION);
                        float bell = Mathf.Sin(t01 * Mathf.PI);
                        float s    = 1f + TROPHY_POP_AMP * bell;
                        _questCheckmarks[i].rectTransform.localScale = new Vector3(s, s, 1f);
                        if (_questPopT[i] <= 0f)
                            _questCheckmarks[i].rectTransform.localScale = Vector3.one;
                    }
                }
                // Coin chip vs trophy — mutually exclusive on the right side.
                if (_questChipRoots[i] != null && _questChipRoots[i].activeSelf == done)
                    _questChipRoots[i].SetActive(!done);
            }

            if (_questsTotalSteps != null) _questsTotalSteps.text = $"Total steps: {g.state.totalSteps:N0}";
            if (_questsTotalRun != null)
            {
                int totalMin = (int)(g.state.totalRunSeconds / 60);
                _questsTotalRun.text = $"Total running: {totalMin / 60}h {totalMin % 60}m";
            }
            if (_questsStreak != null) _questsStreak.text = $"{g.state.streakDays}-day streak";

            // Knight Set chain row: only visible once Lv 20 is reached. Shows the
            // next piece + day progress, or a celebratory line once all earned.
            // Bar fills 0..1 across the 10-day chain so the player sees grind progress.
            if (_questsKnightRow != null)
            {
                bool show = g.state.level >= GamexGame.KNIGHT_CHAIN_UNLOCK_LEVEL;
                _questsKnightRow.SetActive(show);
                if (show && _questsKnight != null)
                {
                    bool earned = g.state.knightChainStage >= GamexGame.KnightSet.Length;
                    if (earned)
                    {
                        _questsKnight.text = "Knight Set earned!";
                    }
                    else
                    {
                        int days = g.state.knightChainProgress;
                        int needed = GamexGame.KNIGHT_CHAIN_DAYS;
                        _questsKnight.text = $"Knight Set — {days}/{needed} days (5k+ steps each)";
                    }
                    if (_questsKnightBarBg != null) _questsKnightBarBg.SetActive(!earned);
                    if (_questsKnightBarFill != null && !earned)
                    {
                        float pct = Mathf.Clamp01((float)g.state.knightChainProgress / GamexGame.KNIGHT_CHAIN_DAYS);
                        _questsKnightBarFill.rectTransform.sizeDelta = new Vector2(840f * pct, 24f);
                    }
                }
            }
        }

    }
}
