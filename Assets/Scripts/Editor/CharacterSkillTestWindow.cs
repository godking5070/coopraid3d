using System;
using UnityEditor;
using UnityEngine;

namespace RaidRogue.EditorTools
{
    // Editor only: no new RPC, saved preference, scene reference, or player-build cheat input.
    public sealed class CharacterSkillTestWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _message;
        private bool _failed;
        private double _nextRepaint;

        [MenuItem("Raid Rogue/스킬 테스트")]
        public static void Open()
        {
            var window=GetWindow<CharacterSkillTestWindow>("스킬 테스트");
            window.minSize=new Vector2(430,400);
            window.Show();
        }

        private void OnInspectorUpdate()
        {
            if(EditorApplication.timeSinceStartup<_nextRepaint)return;
            _nextRepaint=EditorApplication.timeSinceStartup+.25;
            Repaint();
        }

        private static CharacterActor LocalActor()
        {
            if(!EditorApplication.isPlaying)return null;
            var hotbar=UnityEngine.Object.FindFirstObjectByType<SkillHotbarUI>();
            return hotbar?.combatController?.GetComponent<CharacterActor>();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Play → 직업과 기원 선택 → 전투 시작 후 사용하세요.\n"+
                "R/F/V 후보를 테스트 장착할 수 있습니다. 이미 런 보상으로 확정한 슬롯은 교체할 수 없습니다.\n"+
                "멀티플레이는 호스트 자신의 캐릭터만 지원합니다. 설정 파일이나 직업 원본에는 저장하지 않습니다.",MessageType.Info);
            var actor=LocalActor();
            string reason="Play 모드에서 캐릭터가 생성되면 사용할 수 있습니다.";
            bool allowed=actor!=null&&actor.CanEditSkillTest(out reason);
            if(actor==null||actor.Definition==null)
            {EditorGUILayout.HelpBox(reason,MessageType.Info);return;}
            EditorGUILayout.LabelField(actor.Definition.name,EditorStyles.boldLabel);
            EditorGUILayout.LabelField("현재 기원",Array.Find(actor.Definition.routes,r=>r.id==actor.RouteId)?.name??"기원 없음");
            if(!allowed)EditorGUILayout.HelpBox(reason,MessageType.Warning);
            using(new EditorGUI.DisabledScope(!allowed))
            {
                if(GUILayout.Button("구현된 현재 슬롯 잠금 해제",GUILayout.Height(30)))Apply(actor,SkillTestOperation.UnlockImplemented);
                if(GUILayout.Button("성광 / 직업 자원 최대치 채우기",GUILayout.Height(30)))Apply(actor,SkillTestOperation.FillResources);
                using(new EditorGUI.DisabledScope(actor.Definition.legacyRole>0))
                    if(GUILayout.Button("직업 스킬 쿨타임 + GCD 초기화",GUILayout.Height(30)))Apply(actor,SkillTestOperation.RefreshJobCooldowns);
                if(GUILayout.Button("선택한 적의 카운터 테스트 창 열기 (3초)",GUILayout.Height(30)))
                {
                    var target=TargetingManager.Instance?.CurrentTarget;
                    if(!actor.CanEditSkillTest(out _message)||!CombatRange.LivingEnemy(target))
                    {_message="살아 있는 적을 선택하세요. 호스트 자신의 캐릭터만 테스트할 수 있습니다.";_failed=true;}
                    else
                    {CombatCounterTarget.Ensure(target).OpenWindow(3);_message="적 정면에서 카운터를 적중시키면 파티 이동기 충전이 모두 복구됩니다. 이 창은 테스트 전용입니다.";_failed=false;}
                    ReleaseGameInput();
                }
            }
            EditorGUILayout.HelpBox("쿨타임 초기화는 대쉬·포션·콤보·현재 변신/가드에 영향을 주지 않습니다. 시전 중에는 거부됩니다.\n"+
                "Z의 성광 비용, 사거리, 생존 조건과 효과 중 재사용 제한은 그대로 적용됩니다.\n"+
                "재도전 시 자원/쿨타임은 기존 규칙으로 초기화되며, 테스트 해금은 새 런에서 초기화됩니다.",MessageType.None);
            if(!string.IsNullOrEmpty(_message))EditorGUILayout.HelpBox(_message,_failed?MessageType.Warning:MessageType.Info);

            var snapshot=actor.Snapshot;
            if(snapshot==null)return;
            foreach(var resource in snapshot.resources)
            {
                var definition=Array.Find(actor.Definition.resources,r=>r.id==resource.id);
                EditorGUILayout.LabelField(definition.name,$"{resource.value:0.#} / {actor.ResourceMaximum(definition.id):0.#}");
            }
            _scroll=EditorGUILayout.BeginScrollView(_scroll);
            if(actor.Definition.legacyRole==0)
            {
                EditorGUILayout.LabelField("R/F/V 후보 테스트 (실제 해금 보상과 별도)",EditorStyles.boldLabel);
                using(new EditorGUI.DisabledScope(!allowed))
                    foreach(var rule in actor.Definition.unlocks)
                        foreach(var id in rule.choices)
                            if(GUILayout.Button(CharacterSlots.Name(rule.slot)+" · "+actor.Definition.Skill(id).name))
                            {_failed=!actor.PreviewExtension(id,out _message);ReleaseGameInput();}
            }
            if(actor.Definition.legacyRole==0)
            {
                EditorGUILayout.LabelField("직업 진화 카드 효과 미리 보기",EditorStyles.boldLabel);
                using(new EditorGUI.DisabledScope(!allowed))
                {
                    if(GUILayout.Button("극복 카드 효과 적용")){actor.RecordBuildEffect("evolution.overcome",1);actor.RecordBuildEffect("evolution.maximize",0);ReleaseGameInput();}
                    if(GUILayout.Button("극대화 카드 효과 적용")){actor.RecordBuildEffect("evolution.overcome",0);actor.RecordBuildEffect("evolution.maximize",1);ReleaseGameInput();}
                    if(GUILayout.Button("진화 카드 미리 보기 제거")){actor.RecordBuildEffect("evolution.overcome",0);actor.RecordBuildEffect("evolution.maximize",0);ReleaseGameInput();}
                }
            }
            foreach(var slot in CharacterSlots.All)
            {
                var skill=actor.Selected(slot);
                if(skill==null)continue;
                bool unlocked=Array.IndexOf(snapshot.unlocked,skill.id)>=0;
                var timing=Array.Find(snapshot.skills,s=>s.id==skill.id);
                string state=!skill.implemented?"미구현 (해금 불가)":!unlocked?"잠김":timing?.charges==0?$"대기 {timing.recharge:0.0}초":"해금됨";
                EditorGUILayout.LabelField($"[{LocalInputBindings.Label(CharacterSlots.Binding(slot))}] {skill.name}",state);
            }
            EditorGUILayout.EndScrollView();
        }

        private void Apply(CharacterActor actor,SkillTestOperation operation)
        {
            _failed=!actor.ApplyEditorSkillTest(operation,out _message);
            ReleaseGameInput();
        }
        private static void ReleaseGameInput()
        {
            // Clicking back into Game must not resume a held skill or camera drag.
            QuarterViewCamera.LocalCamera?.ReleaseOrbit();
            LocalInputBindings.QuarantineHeld();
            LocalGameplayInput.SuppressThisFrame();
            LocalGameplayInput.SuppressPointerThisFrame();
        }
    }
}
