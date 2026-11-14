using UnityEngine;

namespace RaidRogue
{
    // Attach in the actual sanctuary scene. Existing eligible clear receipts drive XI presentation.
    public sealed class Stage4StorySceneBindings : MonoBehaviour
    {
        [SerializeField] private GameObject xiCoffinClosed;
        [SerializeField] private GameObject xiCoffinOpen;
        [SerializeField] private GameObject awakenedCompanionVisual;
        private bool? _applied;
        private void OnEnable(){_applied=null;Apply();}
        private void Update(){Apply();}
        private void Apply()
        {
            bool cleared=CampaignRuntime.Instance?.Stage4PlayStopped==true;
            if(_applied==cleared)return;_applied=cleared;
            if(xiCoffinClosed!=null)xiCoffinClosed.SetActive(!cleared);
            if(xiCoffinOpen!=null)xiCoffinOpen.SetActive(cleared);
            if(awakenedCompanionVisual!=null)awakenedCompanionVisual.SetActive(cleared);
        }
    }
}
