using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Deform
{
    /// <summary>
    ///     Like TouchPushDeformer, but instead of pushing every affected vertex in one fixed
    ///     direction, each vertex is pushed away from the Axis's origin point, radially -
    ///     so the surface bulges outward from a center (a balloon/vocal-sac look), rather
    ///     than being shifted uniformly in one direction (a poke/dent look).
    /// </summary>
    [Deformer(Name = "Radial Push", Type = typeof(RadialPushDeformer))]
    public class RadialPushDeformer : Deformer
    {
        public Transform Axis;
        public float Radius = 0.05f;
        public float Factor = 0f;

        public override DataFlags DataFlags => DataFlags.Vertices;

        public override JobHandle Process(MeshData data, JobHandle dependency = default)
        {
            if (Axis == null) return dependency;

            // Radius半径の判定は、TouchPushDeformerと同じくAxisのローカル空間で行う。
            var meshToAxis = Axis.worldToLocalMatrix * transform.localToWorldMatrix;

            // 押し出しの中心点だけメッシュのローカル空間へ変換しておく(方向は頂点ごとに変わる)。
            float3 axisOriginInMeshSpace = transform.InverseTransformPoint(Axis.position);

            return new RadialPushJob
            {
                meshToAxis = meshToAxis,
                axisOriginInMeshSpace = axisOriginInMeshSpace,
                radius = Radius,
                factor = Factor,
                vertices = data.DynamicNative.VertexBuffer
            }.Schedule(data.Length, 64, dependency);
        }

        [BurstCompile]
        private struct RadialPushJob : IJobParallelFor
        {
            public float4x4 meshToAxis;
            public float3 axisOriginInMeshSpace;
            public float radius;
            public float factor;
            public NativeArray<float3> vertices;

            public void Execute(int index)
            {
                float3 posInAxisSpace = math.transform(meshToAxis, vertices[index]);
                float dist = math.length(posInAxisSpace);

                if (dist < radius && dist > 0.0001f)
                {
                    float t = 1f - (dist / radius);
                    t = t * t * (3f - 2f * t);

                    // 中心点から見た頂点の方向(メッシュ空間)へ、そのまま外向きに押す。
                    float3 radialDir = math.normalize(vertices[index] - axisOriginInMeshSpace);
                    vertices[index] += radialDir * factor * t;
                }
            }
        }
    }
}
