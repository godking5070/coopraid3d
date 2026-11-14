using UnityEngine;

namespace RaidRogue
{
    // Attach in the sanctuary scene. Existing eligible Stage 5 receipts drive XII presentation.
    public sealed class Stage5StorySceneBindings : MonoBehaviour
    {
        [SerializeField] private GameObject xiiCoffinClosed;
        [SerializeField] private GameObject xiiCoffinOpen;
        [SerializeField] private GameObject awakenedCompanionVisual;
        private bool? _applied;
        private void OnEnable(){_applied=null;Apply();}
        private void Update(){Apply();}
        private void Apply()
        {
            bool cleared=CampaignRuntime.Instance?.Stage5TrialEnded==true;
            if(_applied==cleared)return;_applied=cleared;
            if(xiiCoffinClosed!=null)xiiCoffinClosed.SetActive(!cleared);
            if(xiiCoffinOpen!=null)xiiCoffinOpen.SetActive(cleared);
            if(awakenedCompanionVisual!=null)awakenedCompanionVisual.SetActive(cleared);
        }
    }
}
