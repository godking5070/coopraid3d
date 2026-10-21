using UnityEngine;

namespace RaidRogue
{
    /// <summary>
    /// 보스 머리 위에 떠오르는 3D 데미지/판정 텍스트 팝업 컴포넌트.
    /// 카메라를 향해 빌보드 회전하며 위로 부드럽게 떠올랐다가 사라집니다.
    /// </summary>
    public class FloatingTextPopup : MonoBehaviour
    {
        public float floatSpeed = 1.8f;
        public float lifetime = 1.25f;

        private TextMesh _mainText;
        private TextMesh _shadowText;
        private Color _baseColor;
        private float _elapsed;
        private Vector3 _initialScale;

        public static FloatingTextPopup Spawn(Vector3 worldPosition, string message, Color color, bool isCrit)
        {
            GameObject root = new GameObject($"Popup_{message}");
            // 연속 타격 시 겹치지 않도록 약간의 수평 랜덤 오프셋 부여
            Vector3 jitter = new Vector3(Random.Range(-0.45f, 0.45f), Random.Range(-0.1f, 0.25f), Random.Range(-0.2f, 0.2f));
            root.transform.position = worldPosition + jitter;

            FloatingTextPopup popup = root.AddComponent<FloatingTextPopup>();
            popup.Initialize(message, color, isCrit);
            return popup;
        }

        private void Initialize(string message, Color color, bool isCrit)
        {
            _baseColor = color;
            float scale = isCrit ? 0.16f : 0.125f;
            _initialScale = Vector3.one * scale;
            transform.localScale = _initialScale * (isCrit ? 1.35f : 1.1f);

            // 그림자 텍스트 (가독성 향상)
            GameObject shadowObj = new GameObject("Shadow");
            shadowObj.transform.SetParent(transform, false);
            shadowObj.transform.localPosition = new Vector3(0.18f, -0.18f, 0.05f);

            _shadowText = shadowObj.AddComponent<TextMesh>();
            _shadowText.text = message;
            _shadowText.fontSize = isCrit ? 72 : 60;
            _shadowText.fontStyle = FontStyle.Bold;
            _shadowText.anchor = TextAnchor.MiddleCenter;
            _shadowText.alignment = TextAlignment.Center;
            _shadowText.color = new Color(0f, 0f, 0f, 0.9f);

            // 메인 텍스트
            _mainText = gameObject.AddComponent<TextMesh>();
            _mainText.text = message;
            _mainText.fontSize = isCrit ? 72 : 60;
            _mainText.fontStyle = FontStyle.Bold;
            _mainText.anchor = TextAnchor.MiddleCenter;
            _mainText.alignment = TextAlignment.Center;
            _mainText.color = color;

            MeshRenderer mainRenderer = GetComponent<MeshRenderer>();
            if (mainRenderer != null)
            {
                mainRenderer.sortingOrder = 50;
            }
            MeshRenderer shadowRenderer = shadowObj.GetComponent<MeshRenderer>();
            if (shadowRenderer != null)
            {
                shadowRenderer.sortingOrder = 49;
            }
        }

        private void LateUpdate()
        {
            _elapsed += Time.deltaTime;

            // 카메라 정면 빌보드 정렬
            Camera cam = Camera.main;
            if (cam != null)
            {
                transform.rotation = cam.transform.rotation;
            }

            // 위로 부유 및 스케일 탄성 복원
            transform.position += Vector3.up * (floatSpeed * Time.deltaTime);
            transform.localScale = Vector3.Lerp(transform.localScale, _initialScale, Time.deltaTime * 10f);

            // 후반부 알파 페이드 아웃
            float normalizedTime = Mathf.Clamp01(_elapsed / lifetime);
            if (normalizedTime > 0.55f)
            {
                float alpha = Mathf.InverseLerp(1f, 0.55f, normalizedTime);
                if (_mainText != null)
                {
                    Color c = _baseColor;
                    c.a = alpha;
                    _mainText.color = c;
                }
                if (_shadowText != null)
                {
                    _shadowText.color = new Color(0f, 0f, 0f, alpha * 0.9f);
                }
            }

            if (_elapsed >= lifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}
