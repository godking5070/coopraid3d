using UnityEngine;

namespace RaidRogue
{
    // Attach in the actual sanctuary scene. The confirmed personal receipt is the source of truth.
    // This component changes presentation only; it never grants a party slot or reward.
    public sealed class Stage2StorySceneBindings : MonoBehaviour
    {
        [SerializeField] private GameObject[] blueFlowersBeforeClear = new GameObject[4];
        [SerializeField] private GameObject[] blackAshAfterClear = new GameObject[4];
        [SerializeField] private GameObject ixCoffinClosed;
        [SerializeField] private GameObject ixCoffinOpen;
        [SerializeField] private GameObject awakenedCompanionVisual;
        private bool? _applied;
        private void OnEnable() { _applied=null; Apply(); }
        private void Update() { Apply(); }
        private void Apply()
        {
            bool cleared=CampaignRuntime.Instance?.Stage2GardenCleared == true;
            if(_applied==cleared)return;
            _applied=cleared;
            foreach(var flower in blueFlowersBeforeClear)if(flower!=null)flower.SetActive(!cleared);
            foreach(var ash in blackAshAfterClear)if(ash!=null)ash.SetActive(cleared);
            if(ixCoffinClosed!=null)ixCoffinClosed.SetActive(!cleared);
            if(ixCoffinOpen!=null)ixCoffinOpen.SetActive(cleared);
            if(awakenedCompanionVisual!=null)awakenedCompanionVisual.SetActive(cleared);
        }
    }
}
