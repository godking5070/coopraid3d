using UnityEngine;

namespace RaidRogue
{
    public sealed partial class NineKeyDamageKit
    {
        // Actor-owned presentation only. No serialized references or authored UI edits.
        // Eight route lines plus two independent F markers; F never replaces R visuals.
        private readonly LineRenderer[] _prismLines=new LineRenderer[10];
        private Material _prismMaterial;
        private string _prismHint="";
        private readonly Vector3[] _fieldRingPoints=new Vector3[33];
        private void FieldRing(int index,DamagePoint point,float radius,Color color,float fraction=1)
        {
            Vector3 center=World(point)+Vector3.up*.08f;
            for(int n=0;n<_fieldRingPoints.Length;n++)
            {
                float angle=2*Mathf.PI*n/(_fieldRingPoints.Length-1)*fraction;
                _fieldRingPoints[n]=center+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
            }
            PrismLine(index,_fieldRingPoints,color);
        }
        private void ShowSpearFields()
        {
            var spears=_spears.Snapshot.spears;
            for(int i=0;i<spears.Length&&i<2;i++)
            {
                var spear=spears[i];Vector3 center=World(spear.point)+Vector3.up*.1f;
                Color color=spear.compressed?new Color(.75f,.45f,1,.7f):new Color(.25f,.85f,1,.7f);
                if(!spear.compressed)FieldRing(i*4,spear.point,4,new Color(color.r,color.g,color.b,.45f));
                PrismLine(i*4+1,new[]{center,center+Vector3.up*1.6f,center+new Vector3(-.2f,1.2f,0),center+Vector3.up*1.6f,center+new Vector3(.2f,1.2f,0)},color);
                if(spear.charge>0)FieldRing(i*4+2,spear.point,.7f,color,spear.charge/3f);
                FieldRing(i*4+3,spear.point,5,new Color(color.r,color.g,color.b,.2f));
            }
        }
        private void ShowGravityField()
        {
            if(!_gravity.Active)return;
            var field=_gravity.Snapshot;
            float radius=field.expanded?(Core(3,0)?9:7):(Core(3,1)?2:3);
            Color color=field.expanded?new Color(.5f,.7f,1,.6f):new Color(.8f,.45f,1,.8f);
            FieldRing(0,field.point,radius,color);
            FieldRing(1,field.point,.4f,color);
        }
        private void ShowTacticalField()
        {
            if(Job.index==7&&_seed.Active)
            {
                var seed=_seed.Snapshot;
                Color color=seed.compressed?new Color(1,.65f,.9f,.75f):new Color(1,.85f,.35f,.75f);
                FieldRing(8,seed.point,4,new Color(color.r,color.g,color.b,.4f));
                Vector3 center=World(seed.point)+Vector3.up*.12f;
                PrismLine(9,new[]{center+new Vector3(-.4f,0,-.4f),center+new Vector3(.4f,0,-.4f),
                    center+new Vector3(.4f,0,.4f),center+new Vector3(-.4f,0,.4f)},color,true);
            }
            if(Job.index==11&&_return.Active)
            {
                // This marks the stored destination, not a promise that the
                // movement/collision validator will allow returning there.
                Vector3 center=World(_return.Snapshot.point)+Vector3.up*.12f;
                Color color=new Color(.8f,.8f,1,.75f);
                PrismLine(8,new[]{center+Vector3.forward*.6f,center+Vector3.right*.6f,
                    center-Vector3.forward*.6f,center-Vector3.right*.6f},color,true);
                PrismLine(9,new[]{center-Vector3.right*.2f,center+Vector3.up*.7f,center+Vector3.right*.2f},color);
            }
        }
        private void HidePrismPresentation()
        {
            foreach(var line in _prismLines)if(line!=null)line.enabled=false;
            _prismHint="";
        }
        private void PrismLine(int index,Vector3[] points,Color color,bool loop=false)
        {
            if(points.Length<2)return;
            if(_prismMaterial==null)
            {
                var shader=Shader.Find("Sprites/Default");if(shader==null)return;
                _prismMaterial=new Material(shader){name="DPS Prism Preview"};
            }
            var line=_prismLines[index];
            if(line==null)
            {
                var go=new GameObject("DPSPrismLine"+index);go.transform.SetParent(transform,false);
                line=go.AddComponent<LineRenderer>();_prismLines[index]=line;
                line.useWorldSpace=true;line.sharedMaterial=_prismMaterial;
                line.startWidth=line.endWidth=.045f;
                line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
            }
            line.enabled=true;line.loop=loop;line.startColor=line.endColor=color;
            line.positionCount=points.Length;line.SetPositions(points);
        }
        private void PrismMirror(int outline,int normal,DamagePoint point,DamagePoint facing,Color color)
        {
            Vector3 center=World(point)+Vector3.up;
            Vector3 tangent=new Vector3(-facing.z,0,facing.x),up=Vector3.up*.85f;
            PrismLine(outline,new[]{center-tangent-up,center+tangent-up,center+tangent+up,center-tangent+up},color,true);
            Vector3 direction=new Vector3(facing.x,0,facing.z),tip=center+direction*.8f;
            PrismLine(normal,new[]{center,tip,tip-direction*.2f+tangent*.15f,tip,tip-direction*.2f-tangent*.15f},color);
        }
        private void PrismBeam(int index,DamagePrismState state,Color color)
        {
            var path=DamagePrismState.Clip(state.Trace(Point(transform.position),30),PrismObstruction);
            var points=new Vector3[path.Length];
            for(int i=0;i<path.Length;i++)points[i]=World(path[i])+Vector3.up;
            PrismLine(index,points,color);
        }
        private void LateUpdate()
        {
            HidePrismPresentation();
            if(!Active||!RaidCombatAuthority.Local(_actor)||!CombatRange.LivingPartyMember(transform)||StageManager.PlayerSkillsSuspended)return;
            Bind();
            ShowTacticalField();
            if(Job.index==7&&Route==3){ShowSpearFields();return;}
            if(Job.index==10&&Route==3){ShowGravityField();return;}
            if(!UsesPrismInput)return;
            var current=_prism.Snapshot;
            DamagePrismCommand command=null;DamagePrismState preview=null;
            bool legal=false;int replaced=0;
            if(_prismPressed&&!RaidCombatAuthority.InputBlocked(_actor)&&PrismCursor(out var end))
            {
                command=PrismGesture(end);
                legal=CanUsePrism(command)&&(command.mirrorId!=0||_runtime.Resource(Job.Resource)>=1);
                preview=_prism.Preview(command,Core(3,0)?1:2,20);
                if(command.mirrorId==0)replaced=_prism.ReplacedId(Core(3,0)?1:2);
                _prismHint=!legal?" · 설치/조정 불가":command.mirrorId!=0?" · 조정 #"+command.mirrorId:
                    replaced!=0?" · 교체 예정 #"+replaced:" · 새 거울 배치";
            }
            for(int i=0;i<current.mirrors.Length&&i<2;i++)
            {
                var mirror=current.mirrors[i];
                Color color=mirror.id==replaced?new Color(1,.55f,.1f,.9f):
                    mirror.id==current.firstId?new Color(.3f,1,1,.85f):new Color(.65f,.55f,1,.65f);
                PrismMirror(i*2,i*2+1,mirror.point,mirror.normal,color);
            }
            PrismBeam(4,_prism,new Color(.3f,.8f,1,.35f));
            if(command==null)return;
            var ghostColor=legal?new Color(.3f,1,.55f,.85f):new Color(1,.25f,.25f,.8f);
            PrismMirror(5,6,command.point,command.normal,ghostColor);
            if(preview!=null)PrismBeam(7,preview,ghostColor);
        }
        private void OnDisable()
        {
            ResetInput();HidePrismPresentation();
        }
        private void OnDestroy()
        {
            foreach(var line in _prismLines)if(line!=null)Destroy(line.gameObject);
            if(_prismMaterial!=null)Destroy(_prismMaterial);
        }
    }
}
