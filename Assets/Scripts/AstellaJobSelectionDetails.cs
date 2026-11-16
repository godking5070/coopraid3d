using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // Opt-in presentation for the authored list/detail picker. It never commits a job.
    public sealed class AstellaJobSelectionDetails : MonoBehaviour
    {
        public GameObject detailsRoot;
        public Text titleText, roleText, descriptionText;
        public GameObject[] jobSelectedMarkers = new GameObject[12];
        public GameObject[] routeSelectedMarkers = new GameObject[3];

        public static string RoleLabel(CharacterDefinition definition) => definition == null ? "" :
            definition.role == CharacterRole.Tank ? "탱커" : definition.role == CharacterRole.Healer ? "힐러" :
            definition.classification == AttackClassification.Melee ? "근접 딜러" :
            definition.classification == AttackClassification.RangedPhysical ? "원거리 딜러" : "캐스터";

        public void ShowJob(CharacterDefinition definition)
        {
            if (titleText != null) titleText.text = definition?.name ?? "직업을 선택하세요";
            if (roleText != null) roleText.text = RoleLabel(definition);
            if (descriptionText != null) descriptionText.text = definition?.description ?? "왼쪽 목록에서 직업의 특징을 살펴볼 수 있습니다.";
        }

        public void SetJobsVisible(bool jobs)
        {
            if (detailsRoot != null) detailsRoot.SetActive(jobs);
        }

        public void UpdateSelection(int jobIndex, int routeIndex)
        {
            SetMarker(jobSelectedMarkers, jobIndex);
            SetMarker(routeSelectedMarkers, routeIndex);
        }

        private static void SetMarker(GameObject[] markers, int selected)
        {
            if (markers == null) return;
            for (int index = 0; index < markers.Length; index++)
                if (markers[index] != null && markers[index].activeSelf != (index == selected))
                    markers[index].SetActive(index == selected);
        }
    }
}
