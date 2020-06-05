using System;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Rendering.RenderPipeline
{
    public class ClusterForwardRendererData : ForwardRendererData
    {
#if UNITY_EDITOR
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812")]
        internal class CreateClusterForwardRendererAsset : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                var instance = CreateInstance<ClusterForwardRendererData>();
                AssetDatabase.CreateAsset(instance, pathName);
                ResourceReloader.ReloadAllNullIn(instance, UniversalRenderPipelineAsset.packagePath);
                Selection.activeObject = instance;
            }
        }

        [MenuItem("Assets/Create/Rendering/My Render Pipeline/Cluster Forward Renderer", priority = CoreUtils.assetCreateMenuPriority2)]
        static void CreateClusterForwardRendererData()
        {
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, CreateInstance<CreateClusterForwardRendererAsset>(), "ClusterForwardRendererData.asset", null, null);
        }
#endif

        [SerializeField] float m_MaxClusterZFar;
        
        [SerializeField] int m_ClusterGridSizeX;
        [SerializeField] int m_ClusterGridSizeY;
        [SerializeField] int m_ClusterZCount;

        [SerializeField] bool m_ZPriority;
        
        protected override ScriptableRenderer Create()
        {
            return new ClusterForwardRenderer(this);
        }
        
        public float maxClusterZFar
        {
            get => m_MaxClusterZFar;
        }

        public int clusterGridSizeX
        {
            get => m_ClusterGridSizeX;
        }

        public int clusterGridSizeY
        {
            get => m_ClusterGridSizeY;
        }

        public int clusterZCount
        {
            get => m_ClusterZCount;
        }

        public bool zPriority
        {
            get => m_ZPriority;
        }
    }
}