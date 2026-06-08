using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using VoxelSystem;

namespace VolumetricSmoke
{
    public class OptimizedGPUVoxelizer
    {
        private List<Vector3> _vertices = new();
        private List<Vector2> _uvs = new();
        private List<int> _triangles = new();
        private ComputeBuffer _vertBuffer;
        private ComputeBuffer _uvBuffer;
        private ComputeBuffer _triBuffer;
        private ComputeBuffer _voxelBuffer;
        private Voxel_t[] _clearArray;

        protected const string kVolumeKernelKey = "Volume",
            kSurfaceFrontKernelKey = "SurfaceFront",
            kSurfaceBackKernelKey = "SurfaceBack";

        protected const string kStartKey = "_Start", kEndKey = "_End", kSizeKey = "_Size";
        protected const string kUnitKey = "_Unit", kInvUnitKey = "_InvUnit", kHalfUnitKey = "_HalfUnit";
        protected const string kWidthKey = "_Width", kHeightKey = "_Height", kDepthKey = "_Depth";
        protected const string kTriCountKey = "_TrianglesCount", kTriIndexesKey = "_TriangleIndexes";
        protected const string kVertBufferKey = "_VertBuffer", kUVBufferKey = "_UVBuffer", kTriBufferKey = "_TriBuffer";
        protected const string kVoxelBufferKey = "_VoxelBuffer";

        public GPUVoxelData Voxelize(ComputeShader voxelizer, Mesh mesh, int resolution = 32, bool volume = true)
        {
            mesh.RecalculateBounds();
            return Voxelize(voxelizer, mesh, mesh.bounds, resolution, volume);
        }

        public GPUVoxelData Voxelize(ComputeShader voxelizer, Mesh mesh, Bounds bounds, int resolution = 32,
            bool volume = true)
        {
            mesh.GetVertices(_vertices);
            int vertCount = _vertices.Count;

            // reallocate buffers only if count is changed
            if (_vertBuffer == null || _vertBuffer.count != vertCount)
            {
                _vertBuffer?.Release();
                _vertBuffer = new ComputeBuffer(vertCount, Marshal.SizeOf(typeof(Vector3)));
            }

            _vertBuffer.SetData(_vertices);

            mesh.GetUVs(0, _uvs);
            if (_uvBuffer == null || _uvBuffer.count != vertCount)
            {
                _uvBuffer?.Release();
                int count = vertCount > 0 ? vertCount : 1;
                _uvBuffer = new ComputeBuffer(count, Marshal.SizeOf(typeof(Vector2)));
            }

            if (_uvs.Count > 0)
                _uvBuffer.SetData(_uvs);

            mesh.GetTriangles(_triangles, 0);
            int triCount = _triangles.Count;
            if (_triBuffer == null || _triBuffer.count != triCount)
            {
                _triBuffer?.Release();
                _triBuffer = new ComputeBuffer(triCount, Marshal.SizeOf(typeof(int)));
            }

            _triBuffer.SetData(_triangles);

            var maxLength = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            var unit = maxLength / resolution;
            var hunit = unit * 0.5f;

            // since we use a cube, we need to extend the bounds evenly
            var center = bounds.center;
            var start = center - new Vector3(maxLength * 0.5f - hunit, maxLength * 0.5f - hunit, maxLength * 0.5f - hunit);
            var end   = center + new Vector3(maxLength * 0.5f - hunit, maxLength * 0.5f - hunit, maxLength * 0.5f - hunit);
            var size = end - start;

            int w, h, d;
            w = h = d = resolution;

            int voxelCount = w * h * d;

            if (_voxelBuffer == null || _voxelBuffer.count != voxelCount)
            {
                _voxelBuffer?.Release();
                _voxelBuffer = new ComputeBuffer(voxelCount, Marshal.SizeOf(typeof(Voxel_t)));
                _clearArray = new Voxel_t[voxelCount];
            }

            _voxelBuffer.SetData(_clearArray);

            // send bounds
            voxelizer.SetVector(kStartKey, start);
            voxelizer.SetVector(kEndKey, end);
            voxelizer.SetVector(kSizeKey, size);

            voxelizer.SetFloat(kUnitKey, unit);
            voxelizer.SetFloat(kInvUnitKey, 1f / unit);
            voxelizer.SetFloat(kHalfUnitKey, hunit);
            voxelizer.SetInt(kWidthKey, w);
            voxelizer.SetInt(kHeightKey, h);
            voxelizer.SetInt(kDepthKey, d);

            // send mesh data
            voxelizer.SetInt(kTriCountKey, triCount);
            var indexes = triCount / 3;
            voxelizer.SetInt(kTriIndexesKey, indexes);

            // surface front
            var surfaceFrontKer = new Kernel(voxelizer, kSurfaceFrontKernelKey);
            voxelizer.SetBuffer(surfaceFrontKer.Index, kVertBufferKey, _vertBuffer);
            voxelizer.SetBuffer(surfaceFrontKer.Index, kUVBufferKey, _uvBuffer);
            voxelizer.SetBuffer(surfaceFrontKer.Index, kTriBufferKey, _triBuffer);
            voxelizer.SetBuffer(surfaceFrontKer.Index, kVoxelBufferKey, _voxelBuffer);
            voxelizer.Dispatch(surfaceFrontKer.Index, indexes / (int)surfaceFrontKer.ThreadX + 1,
                (int)surfaceFrontKer.ThreadY, (int)surfaceFrontKer.ThreadZ);

            // surface back
            var surfaceBackKer = new Kernel(voxelizer, kSurfaceBackKernelKey);
            voxelizer.SetBuffer(surfaceBackKer.Index, kVertBufferKey, _vertBuffer);
            voxelizer.SetBuffer(surfaceBackKer.Index, kUVBufferKey, _uvBuffer);
            voxelizer.SetBuffer(surfaceBackKer.Index, kTriBufferKey, _triBuffer);
            voxelizer.SetBuffer(surfaceBackKer.Index, kVoxelBufferKey, _voxelBuffer);
            voxelizer.Dispatch(surfaceBackKer.Index, indexes / (int)surfaceBackKer.ThreadX + 1,
                (int)surfaceBackKer.ThreadY, (int)surfaceBackKer.ThreadZ);

            if (volume)
            {
                var volumeKer = new Kernel(voxelizer, kVolumeKernelKey);
                voxelizer.SetBuffer(volumeKer.Index, kVoxelBufferKey, _voxelBuffer);
                voxelizer.Dispatch(volumeKer.Index, w / (int)volumeKer.ThreadX + 1, h / (int)volumeKer.ThreadY + 1,
                    d / (int)volumeKer.ThreadZ + 1);
            }

            return new GPUVoxelData(_voxelBuffer, w, h, d, unit);
        }

        public void Dispose()
        {
            _vertBuffer?.Release();
            _uvBuffer?.Release();
            _triBuffer?.Release();
            _voxelBuffer?.Release();
        }
    }
}