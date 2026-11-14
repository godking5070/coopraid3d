using UnityEngine;
using UnityEngine.UI;
namespace RaidRogue
{
    public static class RaidUIFill
    {
        public static void Set(RectTransform rect,float ratio)
        {
            if(rect==null)return;
            var image=rect.GetComponent<Image>();
            if(image!=null&&image.type==Image.Type.Filled)image.fillAmount=Mathf.Clamp01(ratio);
            else rect.anchorMax=new Vector2(1,Mathf.Clamp01(ratio));
        }
    }
    public static class RaidUIOrder
    {
        public static void Raise(Transform target)
        {
            if(target==null)return;
            target.SetAsLastSibling();
            while(target.parent!=null&&target.parent.GetComponent<Canvas>()==null)
            {target=target.parent;target.SetAsLastSibling();}
        }
    }
}
