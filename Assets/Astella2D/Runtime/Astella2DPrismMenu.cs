using System;
using RaidRogue;
using UnityEngine;
using UnityEngine.UI;

namespace Astella.TwoD
{
    internal static class Astella2DPrismMenu
    {
        // Keep the 2D roster and actions while borrowing the live Prism form presentation.
        public static void Apply(GameObject menu, GameObject pause)
        {
            var presentation = RaidUIPresentation.Settings;
            var template = presentation?.formButtonTemplate;
            if (presentation?.theme == null || template == null || template.button == null ||
                !(template.button.targetGraphic is Image templateImage) || template.label == null)
                throw new InvalidOperationException("The active Prism menu theme or form button is missing.");

            foreach (var panel in new[] { menu, pause })
            {
                panel.GetComponent<Image>().color = presentation.theme.panel;
                foreach (var button in panel.GetComponentsInChildren<Button>(true))
                {
                    var image = (Image)button.targetGraphic;
                    image.sprite = templateImage.sprite;
                    image.type = templateImage.type;
                    image.color = templateImage.color;
                    image.material = templateImage.material;
                    image.pixelsPerUnitMultiplier = templateImage.pixelsPerUnitMultiplier;
                    button.colors = template.button.colors;

                    var caption = button.GetComponentInChildren<Text>(true);
                    if (caption == null || string.IsNullOrEmpty(caption.text)) continue;
                    caption.font = template.label.font;
                    caption.fontStyle = template.label.fontStyle;
                    caption.color = template.label.color;
                }
            }
        }

        public static void SetSelected(Button button, bool selected)
        {
            var presentation = RaidUIPresentation.Settings;
            var baseColor = presentation.formButtonTemplate.button.targetGraphic.color;
            button.targetGraphic.color = selected ? Color.Lerp(baseColor, presentation.theme.selected, .3f) : baseColor;
        }

        public static Astella2DPrismOutcome CreateOutcome(Transform parent, Action<int> reward, Action retry, Action menu)
        {
            var presentation = RaidUIPresentation.Settings;
            var rewardView = InstantiateView(presentation, "skill-reward", parent);
            var resultView = InstantiateView(presentation, "wipe", parent);

            // Read the authored binding keys, then connect their controls directly to the 2D game.
            string[] blessings =
            {
                "별빛의 칼날\n\n공격력 +25%",
                "은빛 생명\n\n최대 체력 +30%",
                "공명의 발걸음\n\n대시 대기시간 -25%"
            };
            for (int i = 0; i < blessings.Length; i++)
            {
                int index = i;
                Reference<Text>(rewardView, "_labels/[" + i + "]").text = blessings[i];
                Bind(ActionButton(rewardView, "choice." + i), () => reward(index));
            }
            Reference<Text>(rewardView, "_header").text = "공명 수호자 격파";
            Reference<Text>(rewardView, "_status").text = "다음 원정을 위한 축복을 고르세요.";
            var rewardMenu = ActionButton(rewardView, "menu");
            Label(rewardMenu, "캐릭터 선택");
            Bind(rewardMenu, menu);

            var rewardRetry = UnityEngine.Object.Instantiate(rewardMenu, rewardMenu.transform.parent, false);
            rewardRetry.name = "Retry";
            var retryRect = (RectTransform)rewardRetry.transform;
            retryRect.anchoredPosition -= Vector2.right * (retryRect.sizeDelta.x + 12);
            Label(rewardRetry, "다시 도전");
            Bind(rewardRetry, retry);

            var resultRetry = Reference<Button>(resultView, "_retry");
            Reference<Text>(resultView, "_retryCaption").text = "다시 도전";
            Bind(resultRetry, retry);
            var resultMenu = ActionButton(resultView, "메인 메뉴로");
            Label(resultMenu, "캐릭터 선택");
            Bind(resultMenu, menu);
            ActionButton(resultView, "이번 시도 전투 기록").gameObject.SetActive(false);
            Reference<GameObject>(resultView, "_buttons").SetActive(true);
            Reference<GameObject>(resultView, "_connection").SetActive(true);

            return new Astella2DPrismOutcome(rewardView.gameObject, Reference<RectTransform>(rewardView, "_root").gameObject,
                resultView.gameObject, Reference<GameObject>(resultView, "_root"),
                Reference<Text>(resultView, "_title"), Reference<Text>(resultView, "_details"));
        }

        private static RaidUIView InstantiateView(RaidUIPresentation presentation, string id, Transform parent)
        {
            var prefab = presentation?.View(id);
            if (prefab == null) throw new InvalidOperationException("Missing authored Prism view: " + id);
            var instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.gameObject.SetActive(false);
            return instance;
        }

        private static T Reference<T>(RaidUIView view, string path) where T : UnityEngine.Object
        {
            var target = view.references.Find(reference => reference.path == path)?.target as T;
            return target != null ? target : throw new InvalidOperationException("Missing Prism binding: " + view.viewId + "/" + path);
        }

        private static Button ActionButton(RaidUIView view, string id)
        {
            var button = view.actions.Find(action => action.id == id)?.button;
            return button != null ? button : throw new InvalidOperationException("Missing Prism action: " + view.viewId + "/" + id);
        }

        private static void Bind(Button button, Action callback)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => callback());
        }

        private static void Label(Button button, string value)
        {
            var text = button.GetComponentInChildren<Text>(true);
            if (text != null) text.text = value;
        }
    }

    internal sealed class Astella2DPrismOutcome
    {
        private readonly GameObject rewardView, rewardRoot, resultView, resultRoot;
        private readonly Text title, details;

        public Astella2DPrismOutcome(GameObject rewardView, GameObject rewardRoot, GameObject resultView, GameObject resultRoot, Text title, Text details)
        {
            this.rewardView = rewardView;
            this.rewardRoot = rewardRoot;
            this.resultView = resultView;
            this.resultRoot = resultRoot;
            this.title = title;
            this.details = details;
        }

        public void Show(bool won, bool hasReward)
        {
            Hide();
            if (won && hasReward)
            {
                Open(rewardView, rewardRoot);
                return;
            }

            title.text = won ? "원정 완료" : "원정 실패";
            details.text = won ? "세 번의 공명을 모두 잠재웠습니다." : "수호자의 장판을 읽고 다시 도전하세요.";
            Open(resultView, resultRoot);
        }

        public void Hide()
        {
            rewardView.SetActive(false);
            resultView.SetActive(false);
        }

        private static void Open(GameObject view, GameObject content)
        {
            view.transform.SetAsLastSibling();
            content.SetActive(true);
            view.SetActive(true);
        }
    }
}
