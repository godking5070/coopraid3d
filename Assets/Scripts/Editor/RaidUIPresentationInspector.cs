#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace RaidRogue.Editor
{
    [CustomEditor(typeof(RaidUIPresentation))]
    public sealed class RaidUIPresentationInspector : UnityEditor.Editor
    {
        private string _filter="";
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject,"m_Script","icons");
            EditorGUILayout.Space();EditorGUILayout.LabelField("아이콘 · 데이터 ID 연결",EditorStyles.boldLabel);
            _filter=EditorGUILayout.TextField("ID / 스킬 이름 검색",_filter);
            var icons=serializedObject.FindProperty("icons");int displayed=0;
            for(int i=0;i<icons.arraySize;i++)
            {
                var entry=icons.GetArrayElementAtIndex(i);string id=entry.FindPropertyRelative("id").stringValue;
                var skill=CharacterCatalog.All.SelectMany(j=>j.skills).FirstOrDefault(s=>s.id==id);
                string caption=(skill?.name??CharacterCatalog.All.FirstOrDefault(j=>j.id==id)?.name??id)+" · "+id;
                if(_filter.Length>0&&caption.IndexOf(_filter,System.StringComparison.OrdinalIgnoreCase)<0)continue;
                if(displayed++>=40)continue;
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("sprite"),new GUIContent(caption));
            }
            if(displayed>40)EditorGUILayout.HelpBox("40개까지 표시합니다. 검색으로 범위를 좁히세요.",MessageType.Info);
            EditorGUILayout.HelpBox("빈 아이콘은 기본 이미지 또는 텍스트로 표시됩니다. 프리팹의 Raid UI Icon > Override Sprite가 이 목록보다 우선합니다.",MessageType.None);
            serializedObject.ApplyModifiedProperties();
            if(GUILayout.Button("참조 검사"))RaidUIAuthoring.Validate((RaidUIPresentation)target);
        }
    }
    [CustomEditor(typeof(RaidUIView))]
    public sealed class RaidUIViewInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("배치/이름/부모는 자유롭게 수정하세요. 아래 ID와 Path는 게임 연결 계약입니다. 참조된 필수 텍스트/버튼을 삭제할 때는 대체 요소를 연결해야 합니다.",MessageType.Info);
            DrawDefaultInspector();var view=(RaidUIView)target;
            foreach(var reference in view.references)
                if(reference.count<0&&reference.target==null)EditorGUILayout.HelpBox("참조 누락: "+reference.path,MessageType.Error);
            foreach(var action in view.actions)
                if(action.button==null)EditorGUILayout.HelpBox("버튼 누락: "+action.id,MessageType.Error);
        }
    }

    [CustomEditor(typeof(RaidUIRoot))]
    public sealed class RaidUIRootInspector : UnityEditor.Editor
    {
        private bool _bindings;
        public override void OnInspectorGUI()
        {
            var root = (RaidUIRoot)target;
            var nodes = root.GetComponentsInChildren<Transform>(true);
            EditorGUILayout.HelpBox("여기에는 메뉴·전투 HUD·결과 창의 화면 오브젝트가 함께 있습니다. 실제 HP, 파티, 방 상태는 게임 시스템이 관리합니다. 아래 버튼으로 편집할 부분을 선택하세요.", MessageType.Info);
            Section(root, nodes, "전투 기본 HUD", "01 - Combat HUD", "MeleeRange_Feedback", "Top_StageHeader_Badge", "Top_BossHP_Frame", "Top_BossCastBar_Frame", "Center_MechanicAlert_Text", "Bottom_SkillHotbar_Panel", "HUD_TopLeft_Panel", "BossClear_CenterBanner");
            Section(root, nodes, "대상·파티·적", "PartyList_LeftHUD", "EnemyList_LeftHUD", "TargetInfoHUD_FFXIV", "TargetFloatingArrowUI", "EnemyListUI");
            Section(root, nodes, "메뉴·선택·결과", "connection", "origin", "skill-reward", "settings", "wipe", "metrics", "AegisOriginSelection", "RoguelikeCard_ModalRoot", "AllStagesClear_VictoryModalRoot", "RoleSelection_ModalOverlay");
            Section(root, nodes, "그 밖의 전투 표시", "character", "resurrection", "potion", "dash", "Minimap", "TopRight_ViewModeToggle_Button", "RoleSwitchButton_TopRight");
            EditorGUILayout.Space();
            if (GUILayout.Button("활성 프리팹 참조 검사")) RaidUIAuthoring.ValidateActive();
            _bindings = EditorGUILayout.Foldout(_bindings, "고급: Presenter 참조 (배치 편집 시 변경 불필요)", true);
            if (_bindings)
            {
                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("presenters"), true);
                serializedObject.ApplyModifiedProperties();
            }
        }
        private static void Section(RaidUIRoot root, Transform[] nodes, string title, params string[] names)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            foreach (var name in names)
            {
                var found = nodes.FirstOrDefault(t => t != root.transform && t.name == name);
                if (found != null && GUILayout.Button(name, EditorStyles.miniButton))
                {
                    Selection.activeGameObject = found.gameObject;
                    EditorGUIUtility.PingObject(found.gameObject);
                }
            }
        }
    }
}
#endif
