using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using static UnityEngine.Rendering.RenderGraphModule.Util.RenderGraphUtils;

namespace ExoFlight.Volumes
{
    [DisallowMultipleRendererFeature("Enter Volume Effect")]
    public sealed class EnterVolumeEffectFeature : ScriptableRendererFeature
    {
        public const string DefaultShaderName = "Hidden/ExoFlight/EnterVolumeEffect";

        [Header("Shader")]
        [Tooltip("Твой шейдер или Shader Graph. Пусто — берётся Hidden/ExoFlight/EnterVolumeEffect")]
        [SerializeField] Shader effectShader;
        [Tooltip("Номер прохода в шейдере")]
        [SerializeField] int shaderPassIndex;

        [Header("Injection")]
        [Tooltip("AfterRenderingPostProcessing — поверх готового кадра; BeforeRenderingPostProcessing — до постобработки")]
        [SerializeField] RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

        Material material;
        EnterVolumeEffectPass pass;

        public override void Create()
        {
            if (effectShader == null)
                effectShader = Shader.Find(DefaultShaderName);

            material = effectShader != null ? CoreUtils.CreateEngineMaterial(effectShader) : null;
            pass = new EnterVolumeEffectPass { renderPassEvent = injectionPoint };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null)
                return;

            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            if (stack == null)
                return;

            EnterVolumeEffectVolume volume = stack.GetComponent<EnterVolumeEffectVolume>();
            if (volume == null || !volume.IsActive())
                return;

            pass.Setup(material, shaderPassIndex, volume);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        sealed class EnterVolumeEffectPass : ScriptableRenderPass
        {
            static readonly int IntensityId = Shader.PropertyToID("_Intensity");

            Material material;
            int shaderPassIndex;
            EnterVolumeEffectVolume volume;

            public void Setup(Material material, int shaderPassIndex, EnterVolumeEffectVolume volume)
            {
                this.material = material;
                this.shaderPassIndex = shaderPassIndex;
                this.volume = volume;

                // без этого blit может уйти в backbuffer и эффект не отрисуется
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (material == null || volume == null || !volume.IsActive())
                    return;

                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer)
                    return;

                material.SetFloat(IntensityId, volume.intensity.value);

                TextureHandle source = resourceData.activeColorTexture;

                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "Enter Volume Effect Color";
                desc.clearBuffer = false;

                TextureHandle destination = renderGraph.CreateTexture(desc);

                var blitParams = new BlitMaterialParameters(source, destination, material, shaderPassIndex);
                renderGraph.AddBlitPass(blitParams, passName: "Enter Volume Effect");

                resourceData.cameraColor = destination;
            }
        }
    }
}
