using System;
using UnityEngine;
using UnityEngine.Rendering;

// https://github.com/mattatz/unity-voxel/tree/master
using VoxelSystem;

namespace VolumetricSmoke
{
    public class SmokeManager : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Size of the voxel grid")] [Range(4, 256)]
        public int resolution = 64;

        [Tooltip("Scale of the detail noise")] [Range(1, 100)]
        public int detailNoiseScale = 10;

        [Tooltip("Scale of the erosion noise")] [Range(1, 100)]
        public int erosionNoiseScale = 10;

        [Tooltip("Enable Gaussian blur smoothing")]
        public bool enableSmoothing = true;

        [Tooltip("How many times to apply the blur")] [Range(1, 10)]
        public int blurStrength = 1;

        [Tooltip("Update voxels every frame (for animated meshes)")]
        public bool updateVoxels = true;

        [Tooltip("Whether voxels should fill the volume or just outline the surface")]
        public bool fillVoxels = true;

        [Header("References")]
        public ComputeShader voxelizerShader;
        public ComputeShader gaussianBlurShader;
        public ComputeShader noiseShader;

        [Tooltip("Objects that use this material will render the smoke")]
        public Material smokeMaterial;

        [Tooltip("The SkinnedMeshRenderer of the object to voxelize")]
        public SkinnedMeshRenderer skinnedMeshToVoxelize;

        private OptimizedGPUVoxelizer _voxelizer;
        private RenderTexture _noiseTexture;
        private RenderTexture _volumeTexture;
        private RenderTexture _blurTempTexture;
        private Mesh _bakedMesh;
        private float _currentVoronoiScale;
        private float _currentErosionScale;
        private int _blurKernel;
        private int _noiseKernel;

        protected GPUVoxelData data;

        // smoke shader
        private static readonly int TextureId = Shader.PropertyToID("_VolumeTexture");
        private static readonly int NoiseTextureId = Shader.PropertyToID("_NoiseTexture");

        // blur shader
        private static readonly int InputTextureId = Shader.PropertyToID("InputTexture");
        private static readonly int OutputTextureId = Shader.PropertyToID("OutputTexture");
        private static readonly int ResolutionId = Shader.PropertyToID("Resolution");

        // noise shader
        private static readonly int NoiseResultTextureId = Shader.PropertyToID("Result");
        private static readonly int VoronoiScaleId = Shader.PropertyToID("Scale");
        private static readonly int ErosionScaleId = Shader.PropertyToID("ErosionScale");



        void Start()
        {
            _voxelizer = new OptimizedGPUVoxelizer();
            _blurKernel = gaussianBlurShader.FindKernel("CSMain");
            _noiseKernel = noiseShader.FindKernel("CSMain");

            _bakedMesh = new Mesh();

            GenerateVolumeTexture();
            GenerateNoiseTexture();
        }

        void Update()
        {
            if (updateVoxels)
            {
                GenerateVolumeTexture();
            }
        }

        // editor only
        private void OnValidate()
        {
            if (!voxelizerShader)
            {
                throw new NullReferenceException("[SmokeManager] Voxelizer compute shader not assigned in the inspector");
            }

            if (!gaussianBlurShader)
            {
                throw new NullReferenceException("[SmokeManager] Blur compute shader not assigned in the inspector");
            }

            if (!noiseShader)
            {
                throw new NullReferenceException("[SmokeManager] Noise compute shader not assigned in the inspector");
            }

            if (!skinnedMeshToVoxelize)
            {
                throw new NullReferenceException("[SmokeManager] Object Mesh not assigned in the inspector");
            }

            if (!smokeMaterial)
            {
                throw new NullReferenceException("[SmokeManager] Smoke material not assigned in the inspector");
            }

            if ((int)_currentVoronoiScale != detailNoiseScale || (int)_currentErosionScale != erosionNoiseScale)
            {
                GenerateNoiseTexture();
                _currentVoronoiScale = detailNoiseScale;
                _currentErosionScale = erosionNoiseScale;
            }
        }

        private void GenerateVolumeTexture()
        {
            if (!voxelizerShader || !gaussianBlurShader || !noiseShader)
                return;
            if (!skinnedMeshToVoxelize)
                return;
            if (!smokeMaterial)
                return;
            if (_voxelizer == null)
                return;
            if (!_bakedMesh)
                return;

            smokeMaterial.SetTexture(TextureId, null);

            // sample mesh
            skinnedMeshToVoxelize.BakeMesh(_bakedMesh);

            // generate voxels
            data = _voxelizer.Voxelize(voxelizerShader, _bakedMesh, resolution, fillVoxels);

            // create 3d texture from voxels
            if (_volumeTexture && _volumeTexture.IsCreated())
            {
                _volumeTexture.Release();
            }

            var tempReference = _volumeTexture;
            _volumeTexture = GPUVoxelizer.BuildTexture3D(
                voxelizerShader,
                data,
                RenderTextureFormat.ARGBFloat,
                FilterMode.Bilinear
            );
            DestroyImmediate(tempReference, true);

            // gaussian blur
            if (enableSmoothing)
            {
                ApplyBlur();
            }

            smokeMaterial.SetTexture(TextureId, _volumeTexture);
        }

        private void GenerateNoiseTexture()
        {
            if (!noiseShader)
                return;
            if (!smokeMaterial)
                return;

            if (!_noiseTexture || !_noiseTexture.IsCreated())
            {
                _noiseTexture = CreateRenderTexture(resolution, TextureWrapMode.Repeat);
            }

            smokeMaterial.SetTexture(NoiseTextureId, null);

            int threadGroups = Mathf.CeilToInt(resolution / 8.0f);
            noiseShader.SetTexture(_noiseKernel, NoiseResultTextureId, _noiseTexture);
            noiseShader.SetInt(ResolutionId, resolution);
            noiseShader.SetFloat(VoronoiScaleId, detailNoiseScale);
            noiseShader.SetFloat(ErosionScaleId, erosionNoiseScale);

            noiseShader.Dispatch(_noiseKernel, threadGroups, threadGroups, threadGroups);

            smokeMaterial.SetTexture(NoiseTextureId, _noiseTexture);
        }

        private void ApplyBlur()
        {
            if (!_volumeTexture || !gaussianBlurShader)
                return;

            gaussianBlurShader.SetInt(ResolutionId, resolution);

            if (!_blurTempTexture || !_blurTempTexture.IsCreated())
            {
                _blurTempTexture = CreateRenderTexture(resolution);
            }

            // otherwise we have data race
            RenderTexture source = _volumeTexture;
            RenderTexture dest = _blurTempTexture;

            int threadGroups = Mathf.CeilToInt(resolution / 8.0f);

            for (int i = 0; i < blurStrength; i++)
            {
                gaussianBlurShader.SetTexture(_blurKernel, InputTextureId, source);
                gaussianBlurShader.SetTexture(_blurKernel, OutputTextureId, dest);
                gaussianBlurShader.SetInt(ResolutionId, resolution);

                gaussianBlurShader.Dispatch(_blurKernel, threadGroups, threadGroups, threadGroups);

                (source, dest) = (dest, source);
            }

            if (blurStrength % 2 == 1)
            {
                _volumeTexture.Release();
                Graphics.CopyTexture(_blurTempTexture, _volumeTexture);
            }
        }

        private static RenderTexture CreateRenderTexture(int res, TextureWrapMode wrapMode = TextureWrapMode.Clamp)
        {
            var tex = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = res,
                enableRandomWrite = true,
                wrapMode = wrapMode,
                filterMode = FilterMode.Bilinear
            };

            tex.Create();
            return tex;
        }

        void OnDestroy()
        {
            _voxelizer?.Dispose();

            if (_blurTempTexture && _blurTempTexture.IsCreated())
            {
                Destroy(_blurTempTexture);
                _blurTempTexture.Release();
            }

            if (_volumeTexture && _volumeTexture.IsCreated())
            {
                Destroy(_volumeTexture);
                _volumeTexture.Release();
            }

            if (_noiseTexture && _noiseTexture.IsCreated())
            {
                Destroy(_noiseTexture);
                _noiseTexture.Release();
            }

            if (_bakedMesh)
            {
                Destroy(_bakedMesh);
            }

            if (data != null)
            {
                data.Dispose();
                data = null;
            }
        }
    }
}
