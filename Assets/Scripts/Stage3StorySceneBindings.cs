using UnityEngine;

namespace RaidRogue
{
    // Attach in the actual sanctuary scene. A confirmed Stage 3 receipt drives presentation.
    // The optional companion reference does not create a new party slot or invent character data.
    public sealed class Stage3StorySceneBindings : MonoBehaviour
    {
        [SerializeField] private GameObject xCoffinClosed;
        [SerializeField] private GameObject xCoffinOpen;
        [SerializeField] private GameObject awakenedCompanionVisual;
        private bool? _applied;
        private void OnEnable(){_applied=null;Apply();}
        private void Update(){Apply();}
        private void Apply()
        {
            bool cleared=CampaignRuntime.Instance?.Stage3BellSilenced==true;
            if(_applied==cleared)return;_applied=cleared;
            if(xCoffinClosed!=null)xCoffinClosed.SetActive(!cleared);
            if(xCoffinOpen!=null)xCoffinOpen.SetActive(cleared);
            if(awakenedCompanionVisual!=null)awakenedCompanionVisual.SetActive(cleared);
        }
    }
}
