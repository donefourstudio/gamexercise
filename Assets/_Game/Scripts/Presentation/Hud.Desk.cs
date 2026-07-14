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
        // The Desk (Pivot 3 — docs/casino-mvp-plan.md). One scene:
        //   top     — wallet (red in debt) + lifetime-earnings unlock bar
        //   desk    — the paycheck envelope (stride-roll collection) +
        //             pile summary; the scratch MAT with real per-spot
        //             scratching arrives in R2-3
        //   rows    — the catalog (6 cards: cost / Lv / owned / BUY /
        //             PLAY) + the 3 money upgrades
        // R2-2 ships a BASIC play path (instant full-reveal via
        // ScratchAll) so the whole loop is playable; R2-3 replaces it
        // with the foil mat, printed odds panels and peek-and-bail.
        // DeskGame is held directly (results feed animations
        // synchronously); nav goes through _onDeskNav.
        // ============================================================

        GameObject _deskPanel;
        TMP_Text _dkCoins, _dkLoanLine, _dkBarLabel, _dkStepsLine, _dkPileLine, _dkResultLine;
        RectTransform _dkBarFill;
        GameObject _dkEnvelopeBtn;
        TMP_Text   _dkEnvelopeLabel;
        string _dkResult;
        float _dkPopT, _dkShakeT, _dkCoinsShown = -1f;
        GameObject _dkDesk;
        const float DK_BAR_W = 720f;
        static readonly Vector2 DK_DESK_POS = new Vector2(0f, 430f);

        (TMP_Text name, TMP_Text info, Button buy, TMP_Text buyLbl, Button play)[] _dkCardRows;
        (TMP_Text name, TMP_Text info, Button buy, TMP_Text buyLbl)[] _dkUpRows;

        // ---- coin burst pool (shared juice, migrated from the casino) ----
        const int BURST_POOL = 26;
        Image[]   _burstCoins;
        Vector2[] _burstVel;
        float[]   _burstSpin;
        bool      _burstAny;
        Sprite[]  _coinFrames;
        float     _coinFlipT;

        void BuildDesk(Transform root)
        {
            _deskPanel = MkFullPanel("Desk", root);

            // ---- header ----
            _dkCoins = MkText("Coins", _deskPanel.transform, new Vector2(0f, 1f),
                new Vector2(50f, -75f - SAFE_AREA_TOP_INSET), new Vector2(640f, 70f),
                FS_TITLE, TextAnchor.UpperLeft, TextWhite);
            MkButton("Back", _deskPanel.transform, new Vector2(1f, 1f),
                new Vector2(-50f, -70f - SAFE_AREA_TOP_INSET), new Vector2(220f, 76f), "HOME",
                () => _onDeskNav((int)AppPhase.Home), "btn_grey", "btn_grey_down", "back");
            _dkLoanLine = MkText("Loan", _deskPanel.transform, new Vector2(0f, 1f),
                new Vector2(50f, -150f - SAFE_AREA_TOP_INSET), new Vector2(640f, 44f),
                FS_BODY, TextAnchor.UpperLeft, new Color(1f, 0.45f, 0.4f));

            var barBg = MkPanel("BarBg", _deskPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -210f - SAFE_AREA_TOP_INSET), new Vector2(DK_BAR_W, 30f),
                new Color(0.10f, 0.09f, 0.16f, 1f));
            var fill = MkPanel("Fill", barBg.transform, new Vector2(0f, 0.5f),
                new Vector2(3f, 0f), new Vector2(0f, 24f), AccentGold);
            _dkBarFill = fill.GetComponent<RectTransform>();
            _dkBarLabel = MkText("BarLabel", _deskPanel.transform, new Vector2(0.5f, 1f),
                new Vector2(0f, -244f - SAFE_AREA_TOP_INSET), new Vector2(900f, 40f),
                FS_BODY, TextAnchor.UpperCenter, TextDim);

            // ---- the desk surface ----
            _dkDesk = MkSpritePanel("Surface", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                DK_DESK_POS, new Vector2(1000f, 460f), "panel", new Color(0.62f, 0.45f, 0.30f, 1f));
            _dkDesk.GetComponent<Image>().raycastTarget = false;

            _dkEnvelopeBtn = MkButton("Envelope", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(-270f, 470f), new Vector2(330f, 190f), "PAYCHECK",
                DeskTearEnvelope);
            _dkEnvelopeLabel = _dkEnvelopeBtn.GetComponentInChildren<TMP_Text>();
            _dkStepsLine = MkText("Steps", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(-270f, 340f), new Vector2(420f, 44f), FS_BODY, TextAnchor.MiddleCenter, TextDim);

            _dkPileLine = MkText("Pile", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(210f, 470f), new Vector2(460f, 140f), FS_LABEL, TextAnchor.MiddleCenter, TextDim);
            MkText("MatNote", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(210f, 350f), new Vector2(480f, 40f), FS_BODY, TextAnchor.MiddleCenter, TextDim)
                .text = "PLAY scratches instantly — the foil mat lands in R2-3";

            _dkResultLine = MkText("Result", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, 150f), new Vector2(1050f, 64f), FS_BTN, TextAnchor.MiddleCenter, AccentGold);

            // ---- catalog rows ----
            int n = DeskGame.CATALOG.Length;
            _dkCardRows = new (TMP_Text, TMP_Text, Button, TMP_Text, Button)[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                float y = 60f - 112f * i;
                var row = MkSpritePanel("Card_" + i, _deskPanel.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(0f, y), new Vector2(1000f, 104f),
                    "panel_light", new Color(0.16f, 0.18f, 0.28f, 1f));
                row.GetComponent<Image>().raycastTarget = false;
                var nameL = MkText("Name", row.transform, new Vector2(0f, 1f), new Vector2(26f, -10f),
                    new Vector2(480f, 46f), FS_LABEL, TextAnchor.UpperLeft, TextWhite);
                var infoL = MkText("Info", row.transform, new Vector2(0f, 0f), new Vector2(26f, 10f),
                    new Vector2(560f, 38f), FS_BODY, TextAnchor.LowerLeft, TextDim);
                var buyGo = MkButton("Buy", row.transform, new Vector2(1f, 0.5f), new Vector2(-190f, 0f),
                    new Vector2(160f, 84f), "BUY", () => DeskBuy(idx));
                var buyLbl = buyGo.GetComponentInChildren<TMP_Text>();
                buyLbl.fontSize = FS_LABEL;
                var playGo = MkButton("Play", row.transform, new Vector2(1f, 0.5f), new Vector2(-16f, 0f),
                    new Vector2(160f, 84f), "PLAY", () => DeskBasicPlay(idx), "btn_grey", "btn_grey_down");
                playGo.GetComponentInChildren<TMP_Text>().fontSize = FS_LABEL;
                _dkCardRows[i] = (nameL, infoL, buyGo.GetComponent<Button>(), buyLbl, playGo.GetComponent<Button>());
            }

            // ---- money upgrades ----
            MkText("UpHeader", _deskPanel.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -620f), new Vector2(900f, 40f), FS_BODY, TextAnchor.MiddleCenter, TextDim)
                .text = "— UPGRADES —";
            var ups = new[]
            {
                (DeskGame.Upgrade.Luck, "Scratch Luck", "fewer blank spots on every card"),
                (DeskGame.Upgrade.Size, "Scratch Size", "a fatter thumb (bigger brush)"),
                (DeskGame.Upgrade.Coin, "Lucky Coin",   "stronger coin vs tough foil"),
            };
            _dkUpRows = new (TMP_Text, TMP_Text, Button, TMP_Text)[3];
            for (int i = 0; i < 3; i++)
            {
                var u = ups[i];
                float y = -692f - 88f * i;
                var row = MkSpritePanel("Up_" + i, _deskPanel.transform, new Vector2(0.5f, 0.5f),
                    new Vector2(0f, y), new Vector2(1000f, 80f),
                    "panel_light", new Color(0.14f, 0.16f, 0.24f, 1f));
                row.GetComponent<Image>().raycastTarget = false;
                var nameL = MkText("Name", row.transform, new Vector2(0f, 0.5f), new Vector2(26f, 8f),
                    new Vector2(520f, 42f), FS_LABEL, TextAnchor.MiddleLeft, TextWhite);
                nameL.text = u.Item2;
                var infoL = MkText("Info", row.transform, new Vector2(0f, 0.5f), new Vector2(26f, -22f),
                    new Vector2(600f, 32f), FS_BODY, TextAnchor.MiddleLeft, TextDim);
                infoL.text = u.Item3;
                var kind = u.Item1;
                var buyGo = MkButton("Buy", row.transform, new Vector2(1f, 0.5f), new Vector2(-16f, 0f),
                    new Vector2(200f, 66f), "—",
                    () => Sfx.Play(_desk.TryBuyUpgrade(kind) ? "purchase" : "error"));
                var buyLbl = buyGo.GetComponentInChildren<TMP_Text>();
                buyLbl.fontSize = FS_BODY;
                _dkUpRows[i] = (nameL, infoL, buyGo.GetComponent<Button>(), buyLbl);
            }
        }

        void DeskBuy(int i)
        {
            Sfx.Play(_desk.TryBuyCard(i) ? "purchase" : "error");
        }

        // R2-2 basic play: instant full reveal. R2-3 replaces this with the
        // foil mat + printed odds + peek-and-bail.
        void DeskBasicPlay(int i)
        {
            if (_desk.state.cardsOwned[i] <= 0) { Sfx.Play("error"); return; }
            var r = _desk.ScratchAll(i);
            var c = DeskGame.CATALOG[i];
            string t;
            if (r.payout > 0)
                t = $"{c.name}: +{r.payout:N0}" + (r.penalty > 0 ? $"  · traps −{r.penalty:N0}" : "");
            else if (r.penalty > 0)
                t = $"{c.name}: traps −{r.penalty:N0}. Ouch.";
            else
                t = $"{c.name}: nothing. Zilch.";
            if (r.bigWin)
            {
                t = "★ BIG WIN ★  " + t;
                Sfx.Play("milestone"); Sfx.Play("level_up");
                _dkShakeT = 0.5f;
                SpawnCoinBurst(_deskPanel.transform, DK_DESK_POS, BURST_POOL);
            }
            else if (r.payout >= c.cost) Sfx.Play("coin");
            else Sfx.Play("tap");
            _dkResult = t;
            _dkPopT = 0.35f;
        }

        void DeskTearEnvelope()
        {
            int rolls = _desk.state.rollsPending;
            if (rolls <= 0) { Sfx.Play("error"); return; }
            long gross = _desk.TearEnvelope();
            _dkResult = $"Paycheck: {rolls} strides → +{gross:N0} coins";
            _dkPopT = 0.35f;
            Sfx.Play("coin");
            SpawnCoinBurst(_deskPanel.transform, new Vector2(-270f, 470f), Mathf.Min(BURST_POOL, 8 + rolls / 4));
        }

        void UpdateDesk(GamexGame g)
        {
            float dt = Time.unscaledDeltaTime;
            TickCoinBurst(dt);
            TickShake(_dkDesk, DK_DESK_POS, ref _dkShakeT, dt);
            TickPop(_dkResultLine.rectTransform, ref _dkPopT, dt);

            var d = _desk.state;
            long coins = _desk.host.coins;
            long shown = TickCountUp(ref _dkCoinsShown, coins, dt);
            _dkCoins.text  = $"{shown:N0} COINS";
            _dkCoins.color = coins < 0 ? new Color(1f, 0.4f, 0.35f) : TextWhite;
            _dkLoanLine.text = d.loanOwed > 0 ? $"loan shark is owed {d.loanOwed:N0}" : "";

            // earnings bar toward the next locked card
            long next = -1; string nextName = null;
            foreach (var c in DeskGame.CATALOG)
                if (d.earnedThisRun < c.unlockAt) { next = c.unlockAt; nextName = c.name; break; }
            if (next > 0)
            {
                float pct = Mathf.Clamp01(d.earnedThisRun / (float)next);
                _dkBarFill.sizeDelta = new Vector2((DK_BAR_W - 6f) * pct, 24f);
                _dkBarLabel.text = $"{nextName} unlocks at {next:N0} earned  ·  {d.earnedThisRun:N0}";
            }
            else
            {
                _dkBarFill.sizeDelta = new Vector2(DK_BAR_W - 6f, 24f);
                _dkBarLabel.text = $"catalog complete  ·  {d.earnedThisRun:N0} earned";
            }

            // envelope + steps
            bool hasPay = d.rollsPending > 0;
            _dkEnvelopeBtn.SetActive(hasPay);
            if (hasPay) _dkEnvelopeLabel.text = $"PAYCHECK ×{d.rollsPending}";
            _dkStepsLine.text = hasPay
                ? $"next stride: {d.stepAccumulator}/{DeskGame.STEPS_PER_ROLL} steps"
                : $"walk {DeskGame.STEPS_PER_ROLL - d.stepAccumulator} more steps for a stride";

            int pile = 0;
            for (int i = 0; i < DeskGame.CATALOG.Length; i++) pile += d.cardsOwned[i];
            _dkPileLine.text = pile > 0 ? $"the pile: {pile} unscratched card{(pile == 1 ? "" : "s")}" : "the desk is clear";

            _dkResultLine.text = _dkResult ?? "";

            // catalog rows
            for (int i = 0; i < _dkCardRows.Length; i++)
            {
                var c = DeskGame.CATALOG[i];
                var row = _dkCardRows[i];
                bool unlocked = _desk.Unlocked(i);
                if (!unlocked)
                {
                    row.name.text = c.name;
                    row.name.color = TextDim;
                    row.info.text = $"unlocks at {c.unlockAt:N0} earned";
                    row.buy.gameObject.SetActive(false);
                    row.play.gameObject.SetActive(false);
                    continue;
                }
                row.name.text = $"{c.name}   Lv {d.cardLevel[i]}";
                row.name.color = TextWhite;
                row.info.text = $"cost {c.cost:N0}   ·   owned {d.cardsOwned[i]}";
                row.buy.gameObject.SetActive(true);
                row.buy.interactable = _desk.CanBuy(i);
                row.buyLbl.text = c.cost >= 1000 ? $"{c.cost / 1000}k" : c.cost.ToString();
                row.play.gameObject.SetActive(d.cardsOwned[i] > 0);
            }

            // upgrade rows
            for (int i = 0; i < 3; i++)
            {
                var kind = i == 0 ? DeskGame.Upgrade.Luck : i == 1 ? DeskGame.Upgrade.Size : DeskGame.Upgrade.Coin;
                var row = _dkUpRows[i];
                int lvl = _desk.UpgradeLevel(kind);
                bool capped = lvl >= _desk.UpgradeCap(kind);
                row.name.text = (i == 0 ? "Scratch Luck" : i == 1 ? "Scratch Size" : "Lucky Coin") + $"   Lv {lvl}";
                row.buyLbl.text = capped ? "MAX" : _desk.UpgradeCost(kind).ToString("N0");
                row.buy.interactable = !capped && coins >= _desk.UpgradeCost(kind);
            }
        }

        // ============================================================
        // Shared juice (migrated from the retired casino rooms)
        // ============================================================
        long TickCountUp(ref float shown, long target, float dt)
        {
            if (shown < 0f && target >= 0) shown = target;
            shown = Mathf.Abs(shown - target) < 1f
                ? target : Mathf.Lerp(shown, target, 1f - Mathf.Exp(-8f * dt));
            return (long)shown;
        }

        static void TickPop(RectTransform rt, ref float t, float dt)
        {
            float s = 1f;
            if (t > 0f)
            {
                t -= dt;
                float t01 = 1f - Mathf.Clamp01(t / 0.35f);
                s = Mathf.Lerp(1.35f, 1f, t01 * t01 * (3f - 2f * t01));
            }
            rt.localScale = new Vector3(s, s, 1f);
        }

        static void TickShake(GameObject go, Vector2 basePos, ref float t, float dt)
        {
            var rt = (RectTransform)go.transform;
            if (t <= 0f) return;
            t -= dt;
            float amp = Mathf.Lerp(0f, 14f, Mathf.Clamp01(t / 0.6f));
            rt.anchoredPosition = basePos + new Vector2(
                UnityEngine.Random.Range(-amp, amp), UnityEngine.Random.Range(-amp, amp));
            if (t <= 0f) rt.anchoredPosition = basePos;
        }

        void EnsureBurstPool()
        {
            if (_burstCoins != null) return;
            _coinFrames = new Sprite[6];
            bool any = false;
            for (int i = 0; i < 6; i++)
            {
                _coinFrames[i] = Make.Casino("coin_gold_" + (i + 1));
                any |= _coinFrames[i] != null;
            }
            if (!any) _coinFrames = null;

            _burstCoins = new Image[BURST_POOL];
            _burstVel   = new Vector2[BURST_POOL];
            _burstSpin  = new float[BURST_POOL];
            for (int i = 0; i < BURST_POOL; i++)
            {
                var go = _coinFrames != null
                    ? MkSpriteIcon("BurstCoin" + i, _deskPanel.transform,
                        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 53f), _coinFrames[0], Color.white)
                    : MkSpriteIcon("BurstCoin" + i, _deskPanel.transform,
                        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f), "coin", Color.white);
                go.SetActive(false);
                _burstCoins[i] = go.GetComponent<Image>();
            }
        }

        void SpawnCoinBurst(Transform parent, Vector2 origin, int n)
        {
            EnsureBurstPool();
            int spawned = 0;
            for (int i = 0; i < BURST_POOL && spawned < n; i++)
            {
                var img = _burstCoins[i];
                if (img.gameObject.activeSelf) continue;
                img.transform.SetParent(parent, false);
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = origin + new Vector2(
                    UnityEngine.Random.Range(-130f, 130f), UnityEngine.Random.Range(-40f, 40f));
                rt.localRotation = Quaternion.identity;
                _burstVel[i]  = new Vector2(UnityEngine.Random.Range(-420f, 420f),
                                            UnityEngine.Random.Range(500f, 1050f));
                _burstSpin[i] = UnityEngine.Random.Range(-540f, 540f);
                img.gameObject.SetActive(true);
                spawned++;
            }
            _burstAny = true;
        }

        void TickCoinBurst(float dt)
        {
            if (!_burstAny || _burstCoins == null) return;
            Sprite frame = null;
            if (_coinFrames != null)
            {
                _coinFlipT += dt;
                frame = _coinFrames[(int)(_coinFlipT / 0.08f) % 6];
            }
            bool any = false;
            for (int i = 0; i < BURST_POOL; i++)
            {
                var img = _burstCoins[i];
                if (!img.gameObject.activeSelf) continue;
                if (frame != null) img.sprite = frame;
                var rt = img.rectTransform;
                _burstVel[i].y -= 2400f * dt;
                rt.anchoredPosition += _burstVel[i] * dt;
                rt.Rotate(0f, 0f, _burstSpin[i] * dt);
                if (rt.anchoredPosition.y < -1400f) img.gameObject.SetActive(false);
                else any = true;
            }
            _burstAny = any;
        }
    }
}
