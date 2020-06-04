using UnityEditor;
using UnityEditor.Rendering.Universal;
using UnityEngine;

namespace Rendering.RenderPipeline.Editor
{
    [CustomEditor(typeof(ClusterForwardRendererData), true)]
    public class ClusterForwardRendererDataEditor : ForwardRendererDataEditor
    {
        private static class Styles
        {
            public static readonly GUIContent ClusterLabel = new GUIContent("Cluster", "Controls how to split the view-frustum into clusters for this renderer.");
            public static readonly GUIContent MaxZFar = new GUIContent("Max Cluster Z Far", "The max z of clusters.");
            public static readonly GUIContent GridSizeX = new GUIContent("Grid Size X", "Pixel width of cluster in x-y plane.");
            public static readonly GUIContent GridSizeY = new GUIContent("Grid Size Y", "Pixel height of cluster in x-y plane.");
            public static readonly GUIContent ZCount = new GUIContent("Z slices count", "Slices count in z axis");
        }
        
        private SerializedProperty m_MaxClusterZFar;
        private SerializedProperty m_ClusterGridSizeX;
        private SerializedProperty m_ClusterGridSizeY;
        private SerializedProperty m_ClusterZCount;

        private bool m_HasInit = false;
        
        protected void Init()
        {
            m_MaxClusterZFar = serializedObject.FindProperty("m_MaxClusterZFar");
            m_ClusterGridSizeX = serializedObject.FindProperty("m_ClusterGridSizeX");
            m_ClusterGridSizeY = serializedObject.FindProperty("m_ClusterGridSizeY");
            m_ClusterZCount = serializedObject.FindProperty("m_ClusterZCount");
        }
        
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            
            if(!m_HasInit) Init();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(Styles.ClusterLabel, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(m_MaxClusterZFar, Styles.MaxZFar);
            EditorGUILayout.PropertyField(m_ClusterGridSizeX, Styles.GridSizeX);
            EditorGUILayout.PropertyField(m_ClusterGridSizeY, Styles.GridSizeY);
            EditorGUILayout.PropertyField(m_ClusterZCount, Styles.ZCount);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

            serializedObject.ApplyModifiedProperties();
        }
    }
}