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
            public static readonly GUIContent ZPriority = new GUIContent("Z Prior", "Arrange clusters with Z Prior");

            public static readonly GUIContent LightingLabel = new GUIContent("Linghting", "Controls how to deal the additional lights for this renderer.");
            public static readonly GUIContent PointLightAttenRange = new GUIContent("Point Light Attenuation Range", "Range of point light starts to take attenuation");
            public static readonly GUIContent PerClusterLimit = new GUIContent("Per Cluster LImit", "Max count of lights per cluster");
            public static readonly GUIContent LightsSorting = new GUIContent("Lights Sorting", "Indicate whether sorts the lights to optimize rendering.");
        }
        
        private SerializedProperty m_MaxClusterZFar;
        private SerializedProperty m_ClusterGridSizeX;
        private SerializedProperty m_ClusterGridSizeY;
        private SerializedProperty m_ClusterZCount;
        private SerializedProperty m_ZPriority;
        private SerializedProperty m_PointLightAttenRange;
        private SerializedProperty m_LightsCountPerCluster;
        private SerializedProperty m_LightsSorting;

        private bool m_HasInit = false;
        
        protected void Init()
        {
            m_MaxClusterZFar = serializedObject.FindProperty("m_MaxClusterZFar");
            m_ClusterGridSizeX = serializedObject.FindProperty("m_ClusterGridSizeX");
            m_ClusterGridSizeY = serializedObject.FindProperty("m_ClusterGridSizeY");
            m_ClusterZCount = serializedObject.FindProperty("m_ClusterZCount");
            m_ZPriority = serializedObject.FindProperty("m_ZPriority");
            m_PointLightAttenRange = serializedObject.FindProperty("m_PointLightAttenRange");
            m_LightsCountPerCluster = serializedObject.FindProperty("m_LightsCountPerCluster");
            m_LightsSorting = serializedObject.FindProperty("m_LightsSorting");
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
            EditorGUILayout.PropertyField(m_ZPriority, Styles.ZPriority);
            EditorGUI.indentLevel--;
            EditorGUILayout.LabelField(Styles.LightingLabel, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.IntSlider(m_LightsCountPerCluster, 0, 16, Styles.PerClusterLimit);
            EditorGUILayout.Slider(m_PointLightAttenRange, 0.0f, 1.0f, Styles.PointLightAttenRange);
            EditorGUILayout.PropertyField(m_LightsSorting, Styles.LightsSorting);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();

            serializedObject.ApplyModifiedProperties();
        }
    }
}